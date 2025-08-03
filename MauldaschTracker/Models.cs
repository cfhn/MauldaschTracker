namespace MauldaschTracker;

public record TrackingResult(Item Item, IList<TrackingResultItem> ResultItems);
public record TrackingResultItem(DateTime Time, string? Collection, decimal? Latitude, decimal? Longitude, decimal? Accuracy);

public record MultiTrackingResultItem(string ItemId, string ItemName, DateTime? Time, string? CollectionPath, decimal? Latitude, decimal? Longitude, decimal? Accuracy);

public record GetCollectionsResultItem(Guid Id, string Name, Guid? Parent, int Items);
public record GetCollectionResult(Guid Id, string Name);

public record GetItemsResultItem(string Id, string Owner, string Name);
public record GetItemsResultCollection(Guid Id, string Name, IList<GetItemsResultCollection> Collections, IList<GetItemsResultItem> Items);


public record Collection(Guid Id, string Name, Guid? ParentCollectionId);
public record Item(string Id, string Owner, string Name, string Description, Guid? ParentCollectionId);
public record TrackingPosition(string ItemId, DateTime Time, Guid? CollectionId, decimal? Latitude, decimal? Longitude, decimal? Accuracy);


public record SetCollectionRequest(IList<string> Items, IList<Guid> Collections, Guid? ParentCollection);
public record SetPositionRequest(IList<string> Items, IList<Guid> Collections, decimal? Latitude, decimal? Longitude, decimal? Accuracy);

public record AddItemsRequest(string Owner, IList<AddItemRequestItem> Items);
public record UpdateItems(IList<AddItemRequestItem> Items);

public record AddItemRequestItem(string Id, string Name, string Description);
public record DeleteCollectionRequest(Guid Id);
public record AddCollectionRequest(string Name);
public record GetCollectionRequest(Guid Id);

public record LuggageItem(string ItemId, string Nickname, string Name, string Description);