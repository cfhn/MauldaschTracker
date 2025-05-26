using System.Security.Claims;
using idunno.Authentication.Basic;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAuthentication(BasicAuthenticationDefaults.AuthenticationScheme)
    .AddBasic(options =>
    {
        options.Realm = "Basic Authentication";
        options.Events = new BasicAuthenticationEvents
        {
            OnValidateCredentials = context =>
            {
                var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var password = config.GetValue<string>("AdminPassword");
                if (!string.IsNullOrWhiteSpace(password) && password == context.Password)
                {
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, context.Username, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                        new Claim(ClaimTypes.Name, context.Username, ClaimValueTypes.String, context.Options.ClaimsIssuer)
                    };

                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
                    context.Success();
                }

                return Task.CompletedTask;
            }
        };
        options.AllowInsecureProtocol = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var trackerService = new MauldaschTrackerService(app.Configuration.GetConnectionString("Sql") ?? throw new ArgumentException("Missing SQL connection string"));

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
.WithName("AddItem")
.RequireAuthorization();

app.MapGet("/api/Item/GetList", async () =>
{
    return await trackerService.GetItems();
})
.WithName("GetItems")
.RequireAuthorization();

app.MapGet("/api/Collection/GetList", async () =>
{
    return await trackerService.GetCollections();
})
.WithName("GetCollections")
.RequireAuthorization();

app.MapPost("/api/Collection/Add", async (AddCollectionRequest request) =>
{
    await trackerService.AddCollection(request.Name);
    return Results.Ok();
})
.WithName("AddCollection")
.RequireAuthorization();

app.MapPost("/api/Collection/Delete", async (DeleteCollectionRequest request) =>
{
    await trackerService.DeleteCollection(request.Id);
    return Results.Ok();
})
.WithName("DeleteCollection")
.RequireAuthorization();

app.MapPost("/api/SetCollection", async (SetCollectionRequest request) =>
{
    await trackerService.UpdateLocation(request.Items, request.Collections, request.ParentCollection, null, null, null);
    return Results.Ok();
})
.WithName("SetCollection")
.RequireAuthorization();

app.MapPost("/api/SetPosition", async (SetPositionRequest request) =>
{
    await trackerService.UpdateLocation(request.Items, request.Collections, null, request.Latitude, request.Longitude, request.Accuracy);
    return Results.Ok();
})
.WithName("SetPosition")
.RequireAuthorization();

app.UseDefaultFiles();
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