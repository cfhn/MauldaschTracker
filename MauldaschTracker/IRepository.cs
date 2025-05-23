using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public interface IMauldaschTrackerService
{
    Task AddItems(AddItemsRequest request);
    Task AddCollection(string name);
    Task DeleteCollection(Guid id);
    Task<List<GetCollectionsResultItem>> GetCollections();
    Task UpdateLocation(IList<Guid> items, IList<Guid> collections, Guid? targetCollection, decimal? latitude, decimal? longitude);
    Task<TrackingResult?> GetTrackingResult(Guid itemId);
}

public class MauldaschTrackerService : IMauldaschTrackerService
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
                await db.ExecuteAsync(sql, new { Id = Guid.NewGuid(), Owner = request.Owner, Name = item.Name, Description = item.Description }, tran);
            }
            await tran.CommitAsync();
        }
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

    public async Task<TrackingResult?> GetTrackingResult(Guid itemId)
    {
        using var db = new SqlConnection(_connectionString);
        db.Open();
        var sql = "SELECT * FROM Item";

        var item = await db.QueryFirstOrDefaultAsync<Item?>(sql);
        if (item == null)
            return null;

        var listSql = @"
        SELECT
            TrackingPosition.Time,
            Collection.Name AS Collection,
            TrackingPosition.Latitude,
            TrackingPosition.Longitude
        FROM TrackingPosition
        LEFT JOIN Collection ON Collection.Id = TrackingPosition.CollectionId
        WHERE TrackingPosition.ItemId = @ItemId";

        var items = await db.QueryAsync<TrackingResultItem>(listSql, new { ItemId = itemId });

        return new TrackingResult(item, items.ToList());
    }

    private async Task<IList<(Guid ItemId, Guid Collection)>> GetItemCollections(IDbConnection db, IDbTransaction? tran = null)
    {
        var collectionPerItemSql = @"SELECT Id AS ItemId, ParentCollectionId AS Collection FROM Item WHERE ParentCollectionId IS NOT NULL";

        return (await db.QueryAsync<(Guid ItemId, Guid Collection)>(collectionPerItemSql, transaction: tran)).ToList();
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

    public async Task UpdateLocation(IList<Guid> items, IList<Guid> collections, Guid? targetCollection, decimal? latitude, decimal? longitude)
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
            var affectedItems = new HashSet<Guid>();

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

            var itemsPerCollection = (await GetItemCollections(db, tran))
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
                var locationSql = "SELECT TOP 1 * FROM TrackingPosition WHERE CollectionId = @Collection ORDER BY Time DESC";

                var targetLocation = await db.QueryFirstOrDefaultAsync<TrackingPosition>(locationSql, new { Collection = targetCollection }, tran);
                latitude = targetLocation?.Latitude;
                longitude = targetLocation?.Longitude;
            }

            var insertItemSql = @"
                INSERT INTO TrackingPosition (ItemId, Time, CollectionId, Latitude, Longitude)
                SELECT @Id, @Time, ISNULL(@CollectionId, Item.ParentCollectionId), @Latitude, @Longitude
                FROM Item
                WHERE Id = @Id";

            // update position of all affected items
            foreach (var item in affectedItems)
            {
                await db.ExecuteAsync(insertItemSql, new
                {
                    Id = item,
                    Time = now,
                    CollectionId = targetCollection,
                    Latitude = latitude,
                    Longitude = longitude
                }, tran);
            }

            await tran.CommitAsync();
        }
    }
}