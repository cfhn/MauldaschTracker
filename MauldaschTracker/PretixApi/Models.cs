using System.Text.Json.Serialization;

namespace MauldaschTracker.PretixApi;

public class PretixConfig
{
    public required string Url { get; set; }
    public required string Token { get; set; }
    public required string Organizer { get; set; }
    public required string Event { get; set; }
    public int ItemId { get; set; }
    public required string QuestionIdNickname { get; set; }
    public required string QuestionIdLuggageName { get; set; }
    public required string QuestionIdLuggageDescription { get; set; }
}

public class PretixPage<TData>
{
    public int Count { get; set; }
    public required string Next { get; set; }
    public required string Previous { get; set; }
    public required IList<TData> Results { get; set; }
}

public class PretixOrder
{
    public required IList<PretixOrderPosition> Positions { get; set; }
}

public class PretixOrderPosition
{
    public int Item { get; set; }
    public required string Secret { get; set; }
    public required IList<PretixOrderPositionAnswer> Answers { get; set; }
}
public class PretixOrderPositionAnswer
{
    [JsonPropertyName("question_identifier")]
    public required string QuestionIdentifier { get; set; }
    public required string Answer { get; set; }
}