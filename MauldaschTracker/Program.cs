using Microsoft.AspNetCore.Mvc;
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

app.MapGet("/api/Item/Track", (Guid item) =>
{
    return new TrackingResult(new Item(Guid.NewGuid(), "Max Mustermann", "300kg Stahlofen"),
        [
            new TrackingResultItem(DateTime.UtcNow, "LKW", 49, 9)
        ]);
})
.WithName("GetTrackingInfo");

app.MapPost("/api/Item/Add", (AddItemsRequest request) =>
{
    // Return PDF(?)
    return Results.Ok();
})
.WithName("AddItem");

app.MapGet("/api/Collection/GetList", () =>
{
    // TODO
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    return new List<GetCollectionsResultItem>{
        new GetCollectionsResultItem(id2, "Palette 1", 10, id1),
        new GetCollectionsResultItem(id1, "LKW", 0, null)
    };
})
.WithName("GetCollections");

app.MapPost("/api/Collection/Add", (AddCollectionRequest request) =>
{
    // TODO
    return Results.Ok();
})
.WithName("AddCollection");

app.MapPost("/api/SetLocation", (SetLocationRequest request) =>
{
    // TODO
    //TODO: validieren dass location nich in sich selbst sein kann (rekursiv)
    return Results.Ok();
})
.WithName("SetLocation");

app.UseStaticFiles(new StaticFileOptions()
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot")),
    RequestPath = ""
});

app.Run();

record TrackingResult(Item Item, List<TrackingResultItem> ResultItems);
record TrackingResultItem(DateTime Time, string? Collection, decimal? Latitude, decimal? Longitude);
record GetCollectionsResultItem(Guid Id, string Name, int Items, Guid? Parent);


record ItemCollection(Guid Id, string Name, Guid? Collection);
record TrackingPosition(Guid Item, DateTime Time, Guid? Collection, decimal? Latitude, decimal? Longitude);
record Item(Guid Id, string Owner, string Description);


record SetLocationRequest(IList<Guid> Items, IList<Guid> Collections, Guid? Location, decimal? Latitude, decimal? Longitude);

record AddItemsRequest(string Owner, IList<AddItemRequestItem> Items);
record AddItemRequestItem(string Name, string Description);

record AddCollectionRequest(string Name);