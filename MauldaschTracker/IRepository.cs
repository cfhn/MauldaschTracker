using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;

public class MauldaschTrackerService
{
    private readonly string _connectionString;

    public MauldaschTrackerService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task AddCollection(string name)
    {
        var sql = "INSERT INTO Collection (Id, Name) VALUES (@Id, @Name)";
        using var db = new SqlConnection(_connectionString);
        db.Open();

        await db.ExecuteAsync(sql, new { Id = Guid.NewGuid(), Name = name });
    }

    public async Task DeleteCollection(Guid id)
    {
        var sql = "DELETE FROM Collection WHERE Id = @Id";
        using var db = new SqlConnection(_connectionString);
        db.Open();

        await db.ExecuteAsync(sql, new { Id = id });
    }

    public async Task AddItems(AddItemsRequest request)
    {
        var sql = "INSERT INTO Item (Id, Owner, Name, Description) VALUES (@Id, @Owner, @Name, @Description)";
        using var db = new SqlConnection(_connectionString);
        db.Open();

        using (var tran = await db.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            foreach (var item in request.Items)
            {
                var itemId = item.Id.ToUpperInvariant();
                if (itemId.Length != 32)
                    throw new ArgumentException("Ids must be 32 chars long!");
                await db.ExecuteAsync(sql, new { Id = itemId, Owner = request.Owner, Name = item.Name, Description = item.Description }, tran);
            }
            await tran.CommitAsync();
        }
    }

    public async Task UpdateOrAddItems(IList<LuggageItem> items)
    {
        var insertSql = "INSERT INTO Item (Id, Owner, Name, Description) VALUES (@Id, @Owner, @Name, @Description)";
        var updateSql = "UPDATE Item SET Owner = @Owner, Name = @Name, Description = @Description WHERE Id = @Id";
        using var db = new SqlConnection(_connectionString);
        db.Open();

        using (var tran = await db.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            foreach (var item in items)
            {
                var itemId = item.ItemId.ToUpperInvariant();
                if (itemId.Length != 32)
                    throw new ArgumentException("Ids must be 32 chars long!");
                var updated = await db.ExecuteAsync(updateSql, new { Id = itemId, Owner = item.Nickname, Name = item.Name, Description = item.Description }, tran);
                if (updated == 0)
                {
                    await db.ExecuteAsync(insertSql, new { Id = itemId, Owner = item.Nickname, Name = item.Name, Description = item.Description }, tran);
                }
            }
            await tran.CommitAsync();
        }
    }

    public async Task<GetItemsResultCollection> GetItems()
    {
        using var db = new SqlConnection(_connectionString);
        db.Open();

        var topItems = await db.QueryAsync<GetItemsResultItem>("SELECT Id, Owner, Name FROM Item WHERE ParentCollectionId IS NULL");
        var topCollections = await db.QueryAsync<(Guid Id, string Name)>("SELECT Id, Name FROM Collection WHERE ParentCollectionId IS NULL");

        var topCollectionResults = new List<GetItemsResultCollection>();
        foreach (var collection in topCollections)
        {
            topCollectionResults.Add(await GetItemsInternal(db, collection.Id, collection.Name));
        }

        return new GetItemsResultCollection(Guid.Empty, "Irgendwo", topCollectionResults, topItems.ToList());
    }

    private async Task<GetItemsResultCollection> GetItemsInternal(SqlConnection db, Guid id, string name)
    {
        var items = await db.QueryAsync<GetItemsResultItem>("SELECT Id, Owner, Name FROM Item WHERE ParentCollectionId = @Id", new { Id = id });
        var collections = await db.QueryAsync<(Guid Id, string Name)>("SELECT Id, Name FROM Collection WHERE ParentCollectionId = @Id", new { Id = id });

        var collectionResults = new List<GetItemsResultCollection>();

        foreach (var collection in collections)
        {
            collectionResults.Add(await GetItemsInternal(db, collection.Id, collection.Name));
        }

        return new GetItemsResultCollection(id, name, collectionResults, items.ToList());
    }

    public async Task<List<GetCollectionsResultItem>> GetCollections()
    {
        using var db = new SqlConnection(_connectionString);
        db.Open();
        var sql = "SELECT Id, Name, ParentCollectionId FROM Collection";
        var res = (await db.QueryAsync<(Guid Id, string Name, Guid? ParentCollectionId)>(sql)).ToList();
        var itemCollections = (await GetItemCollections(db))
            .GroupBy(x => x.Collection)
            .ToDictionary(x => x.Key, x => x.Count());

        return res
            .Select(x => new GetCollectionsResultItem(x.Id, x.Name, x.ParentCollectionId, itemCollections.TryGetValue(x.Id, out var count) ? count : 0))
            .ToList();
    }

    public async Task<TrackingResult?> GetTrackingResult(string itemId)
    {
        using var db = new SqlConnection(_connectionString);
        db.Open();
        var sql = "SELECT * FROM Item WHERE Id = @Id";

        var item = await db.QueryFirstOrDefaultAsync<Item?>(sql, new { Id = itemId });
        if (item == null)
            return null;

        var listSql = @"
        SELECT
            TrackingPosition.Time,
            TrackingPosition.CollectionPath,
            TrackingPosition.Latitude,
            TrackingPosition.Longitude,
            TrackingPosition.Accuracy
        FROM TrackingPosition
        WHERE TrackingPosition.ItemId = @ItemId
        ORDER BY Time DESC";

        var items = await db.QueryAsync<(DateTime Time, string? CollectionPath, decimal? Latitude, decimal? Longitude, decimal? Accuracy)>(listSql, new { ItemId = itemId });

        var collectionSql = "SELECT Id, Name FROM Collection"; // TODO: filter
        var collections = (await db.QueryAsync<(Guid Id, string Name)>(collectionSql)).ToDictionary(x => x.Id, x => x.Name);

        var trackingItems = items.Select(x => new TrackingResultItem(
            DateTime.SpecifyKind(x.Time, DateTimeKind.Utc),
            string.Join('/',
                (x.CollectionPath ?? "")
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(y => Guid.Parse(y))
                    .Select(y => collections.TryGetValue(y, out var collName) ? collName : "???")),
            x.Latitude,
            x.Longitude,
            x.Accuracy
            ));

        return new TrackingResult(item, trackingItems.ToList());
    }

    private async Task<IList<(string ItemId, Guid Collection)>> GetItemCollections(IDbConnection db, IDbTransaction? tran = null)
    {
        var collectionPerItemSql = @"SELECT Id AS ItemId, ParentCollectionId AS Collection FROM Item WHERE ParentCollectionId IS NOT NULL";

        return (await db.QueryAsync<(string ItemId, Guid Collection)>(collectionPerItemSql, transaction: tran)).ToList();
    }

    private async Task<ISet<Guid>> GetChildCollectionsRecursive(IDbConnection db, IDbTransaction? tran, IEnumerable<Guid> collections)
    {
        var result = new HashSet<Guid>();

        var potentialParents = collections.ToList();

        var getChildrenSql = @"SELECT DISTINCT Id FROM Collection WHERE ParentCollectionId IN (@ParentIds)";

        var i = 0;
        do
        {
            var children = (await db.QueryAsync<Guid>(getChildrenSql, new { ParentIds = potentialParents }, tran)).ToList();
            foreach (var child in children)
            {
                result.Add(child);
            }
            potentialParents = children;
            i++;
            if (i > 100)
                throw new InvalidDataException("Endless recursion detected!");
        } while (potentialParents.Count > 0);

        return result;
    }

    private async Task CheckTreeConsistency(IDbConnection db, IDbTransaction? tran, IEnumerable<Guid> collections)
    {
        var getParentSql = @"SELECT ParentCollectionId FROM Collection WHERE Id = (@Id)";

        foreach (var collection in collections)
        {
            var seenIds = new HashSet<Guid>();
            seenIds.Add(collection);
            var currentItem = collection;

            while (true)
            {
                var parent = await db.QueryFirstOrDefaultAsync<Guid?>(getParentSql, new { Id = currentItem }, tran);
                if (parent == null)
                    break;
                if (!seenIds.Add(parent.Value))
                    throw new InvalidDataException("Endless recursion detected!");
                currentItem = parent.Value;
            }
        }
    }

    private async Task<string> GetCollectionPath(SqlConnection db, DbTransaction tran, Guid collection)
    {
        var getParentSql = @"SELECT ParentCollectionId FROM Collection WHERE Id = (@Id)";

        var currentItem = collection;

        IList<Guid> collections = new List<Guid> { collection };

        while (true)
        {
            var parent = await db.QueryFirstOrDefaultAsync<Guid?>(getParentSql, new { Id = currentItem }, tran);
            if (parent == null)
                break;
            collections.Add(parent.Value);
            currentItem = parent.Value;
        }

        return string.Join('/', collections.Reverse().Select(x => x.ToString("D")));
    }

    public async Task UpdateLocation(IList<string> items, IList<Guid> collections, Guid? targetCollection, decimal? latitude, decimal? longitude, decimal? accuracy)
    {
        if (targetCollection != null && (latitude != null && longitude != null))
        {
            throw new ArgumentException("this should either be used to set a collection _or_ to update a collection position, not both!");
        }

        using var db = new SqlConnection(_connectionString);
        db.Open();

        var now = DateTime.UtcNow;

        using (var tran = await db.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var affectedItems = new HashSet<string>();

            // update ParentCollection of Item(s)
            var updateItemSql = "UPDATE Item SET ParentCollectionId = @CollectionId WHERE Id = @Id";
            foreach (var item in items)
            {
                if ((await db.ExecuteAsync(updateItemSql, new { Id = item, CollectionId = targetCollection }, tran)) > 0)
                {
                    affectedItems.Add(item);
                }
            }

            // update ParentCollection of Collection(s)
            var affectedCollections = new HashSet<Guid>();
            var updateCollectionSql = "UPDATE Collection SET ParentCollectionId = @CollectionId WHERE Id = @Id";
            foreach (var collection in collections)
            {
                if ((await db.ExecuteAsync(updateCollectionSql, new { Id = collection, CollectionId = targetCollection }, tran)) > 0)
                {
                    affectedCollections.Add(collection);
                }
            }

            await CheckTreeConsistency(db, tran, affectedCollections);

            var itemCollections = await GetItemCollections(db, tran);
            var collectionPerItem = itemCollections.ToDictionary(x => x.ItemId, x => x.Collection);
            var itemsPerCollection = itemCollections
                .GroupBy(x => x.Collection)
                .ToDictionary(x => x.Key, x => x.Select(y => y.ItemId).ToList());

            // add all items of collections and child collections as affected as well
            foreach (var collection in affectedCollections.Concat(await GetChildCollectionsRecursive(db, tran, affectedCollections)))
            {
                foreach (var item in itemsPerCollection.TryGetValue(collection, out var collectionItems) ? collectionItems : [])
                {
                    affectedItems.Add(item);
                }
            }

            if (latitude == null || longitude == null)
            {
                var locationSql = "SELECT TOP 1 Latitude, Longitude, Accuracy FROM TrackingPosition WHERE CollectionId = @Collection ORDER BY Time DESC";

                var targetLocation = await db.QueryFirstOrDefaultAsync<(decimal? Latitude, decimal? Longitude, decimal? Accuracy)?>(locationSql, new { Collection = targetCollection }, tran);
                latitude = targetLocation?.Latitude;
                longitude = targetLocation?.Longitude;
                accuracy = targetLocation?.Accuracy;
            }

            var insertItemSql = @"
                INSERT INTO TrackingPosition (ItemId, Time, CollectionId, Latitude, Longitude, Accuracy, CollectionPath)
                SELECT @Id, @Time, Item.ParentCollectionId, @Latitude, @Longitude, @Accuracy, @CollectionPath
                FROM Item
                WHERE Id = @Id";

            // update position of all affected items
            foreach (var item in affectedItems)
            {
                await db.ExecuteAsync(insertItemSql, new
                {
                    Id = item,
                    Time = now,
                    Latitude = latitude,
                    Longitude = longitude,
                    Accuracy = accuracy,
                    CollectionPath = collectionPerItem.TryGetValue(item, out var collection) ? await GetCollectionPath(db, tran, collection) : null
                }, tran);
            }

            await tran.CommitAsync();
        }
    }
}