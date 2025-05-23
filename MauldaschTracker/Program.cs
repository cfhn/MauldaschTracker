using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var trackerService = new MauldaschTrackerService("Server=127.0.0.1,1433;Database=MauldaschTracker;User Id=sa;Password=!1Start1!;TrustServerCertificate=True;");

app.MapGet("/api/Item/Track", async (Guid item) =>
{
    // return new TrackingResult(new Item(Guid.NewGuid(), "Max Mustermann", "300kg Stahlofen", "1m³ Platz und 300kg bitte Vale ich brauch den"),
    //     [
    //         new TrackingResultItem(DateTime.UtcNow, "LKW", 49, 9)
    //     ]);

    return await trackerService.GetTrackingResult(item);
})
.WithName("GetTrackingInfo");

app.MapPost("/api/Item/Add", async (AddItemsRequest request) =>
{
    await trackerService.AddItems(request);
    // Return PDF(?)
    return Results.Ok();
})
.WithName("AddItem");

app.MapGet("/api/Item/GetList", async () =>
{
    return await trackerService.GetItems();
})
.WithName("GetItems");

app.MapGet("/api/Collection/GetList", async () =>
{
    // var id1 = Guid.NewGuid();
    // var id2 = Guid.NewGuid();
    // var id3 = Guid.NewGuid();
    // var id4 = Guid.NewGuid();
    // return new List<GetCollectionsResultItem>{
    //     new GetCollectionsResultItem(id2, "Palette 1", id1, 10),
    //     new GetCollectionsResultItem(id3, "Palette 2", id1, 10),
    //     new GetCollectionsResultItem(id1, "LKW", null, 0),
    //     new GetCollectionsResultItem(id4, "MSE", null, 0)
    // };

    return await trackerService.GetCollections();
})
.WithName("GetCollections");

app.MapPost("/api/Collection/Add", async (AddCollectionRequest request) =>
{
    await trackerService.AddCollection(request.Name);
    return Results.Ok();
})
.WithName("AddCollection");

app.MapPost("/api/Collection/Delete", async (DeleteCollectionRequest request) =>
{
    await trackerService.DeleteCollection(request.Id);
    return Results.Ok();
})
.WithName("DeleteCollection");

app.MapPost("/api/SetCollection", async (SetCollectionRequest request) =>
{
    await trackerService.UpdateLocation(request.Items, request.Collections, request.ParentCollection, null, null, null);
    return Results.Ok();
})
.WithName("SetCollection");

app.MapPost("/api/SetPosition", async (SetPositionRequest request) =>
{
    await trackerService.UpdateLocation(request.Items, request.Collections, null, request.Latitude, request.Longitude, request.Accuracy);
    return Results.Ok();
})
.WithName("SetPosition");

app.UseStaticFiles(new StaticFileOptions()
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot")),
    RequestPath = ""
});

app.Run("http://+:8080");

public record TrackingResult(Item Item, IList<TrackingResultItem> ResultItems);
public record TrackingResultItem(DateTime Time, string? Collection, decimal? Latitude, decimal? Longitude, decimal? Accuracy);
public record GetCollectionsResultItem(Guid Id, string Name, Guid? Parent, int Items);

public record GetItemsResultItem(Guid Id, string Owner, string Name);
public record GetItemsResultCollection(Guid Id, string Name, IList<GetItemsResultCollection> Collections, IList<GetItemsResultItem> Items);


public record Collection(Guid Id, string Name, Guid? ParentCollectionId);
public record Item(Guid Id, string Owner, string Name, string Description, Guid? ParentCollectionId);
public record TrackingPosition(Guid ItemId, DateTime Time, Guid? CollectionId, decimal? Latitude, decimal? Longitude, decimal? Accuracy);


public record SetCollectionRequest(IList<Guid> Items, IList<Guid> Collections, Guid? ParentCollection);
public record SetPositionRequest(IList<Guid> Items, IList<Guid> Collections, decimal? Latitude, decimal? Longitude, decimal? Accuracy);

public record AddItemsRequest(string Owner, IList<AddItemRequestItem> Items);

public record AddItemRequestItem(string Name, string Description);
public record DeleteCollectionRequest(Guid Id);
public record AddCollectionRequest(string Name);