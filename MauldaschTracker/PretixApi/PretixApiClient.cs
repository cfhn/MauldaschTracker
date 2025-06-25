using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MauldaschTracker.PretixApi;

public class PretixApiClient
{
    private readonly PretixConfig _config;
    private readonly HttpClient _httpClient;

    public PretixApiClient(IOptions<PretixConfig> config)
    {
        _config = config.Value;
        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri(_config.Url)
        };
    }

    public async Task<IList<LuggageItem>> GetLuggageItems()
    {
        var url = $"/api/v1/organizers/{_config.Organizer}/events/{_config.Event}/orders/?testmode=false&item={_config.ItemId}"; //TODO: only include necessary fields

        var toReturn = new List<LuggageItem>();

        while (url != null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", _config.Token);

            var response = await _httpClient.SendAsync(request);

            //var stringResponse = await response.Content.ReadAsStringAsync();
            //var orders = JsonSerializer.Deserialize<PretixPage<PretixOrder>>(stringResponse) ?? throw new Exception("Result is null");

            var orders = await response.Content.ReadFromJsonAsync<PretixPage<PretixOrder>>() ?? throw new Exception("Result is null");
            url = orders.Next;

            foreach (var order in orders.Results)
            {
                foreach (var position in order.Positions)
                {
                    if (position.Item == _config.ItemId)
                    {
                        toReturn.Add(new LuggageItem
                            (
                                position.Secret,
                                position.Answers.Single(x => x.QuestionIdentifier == _config.QuestionIdNickname).Answer,
                                position.Answers.Single(x => x.QuestionIdentifier == _config.QuestionIdLuggageName).Answer,
                                position.Answers.Single(x => x.QuestionIdentifier == _config.QuestionIdLuggageDescription).Answer
                            ));
                    }
                }
            }
        }

        return toReturn;
    }
}