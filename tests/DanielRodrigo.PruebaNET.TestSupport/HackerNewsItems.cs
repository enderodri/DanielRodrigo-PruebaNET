using System.Text.Json;
using System.Text.Json.Nodes;

namespace DanielRodrigo.PruebaNET.TestSupport;

/// <summary>Canned Hacker News item documents. The fixed ones come from the test brief and the public API README.</summary>
public static class HackerNewsItems
{
    public const long SpecSampleId = 21233041;

    /// <summary>The story used as the example in the test brief (time 1570887781 is 2019-10-12T13:43:01+00:00).</summary>
    public static string SpecSample => Story(
        SpecSampleId,
        "A uBlock Origin update was rejected from the Chrome Web Store",
        score: 1716,
        by: "ismaildonmez",
        time: 1570887781,
        url: "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
        descendants: 572);

    /// <summary>Story example from the Hacker News API README.</summary>
    public static string ReadmeStory => Story(
        8863,
        "My YC app: Dropbox - Throw away your USB drive",
        score: 104,
        by: "dhouston",
        time: 1175714200,
        url: "http://www.getdropbox.com/u/2/screencast.html",
        descendants: 71);

    /// <summary>Job example from the Hacker News API README: no comments and an empty url.</summary>
    public static string ReadmeJob => Item(new JsonObject
    {
        ["by"] = "justin",
        ["id"] = 192327,
        ["score"] = 6,
        ["text"] = "Justin.tv is the biggest live video site online...",
        ["time"] = 1210981217,
        ["title"] = "Justin.tv is looking for a Lead Flash Engineer!",
        ["type"] = "job",
        ["url"] = "",
    });

    public static string Story(
        long id,
        string title,
        int score,
        string by = "someone",
        long time = 1570887781,
        string? url = "https://example.com/story",
        int? descendants = 0)
    {
        var item = new JsonObject
        {
            ["by"] = by,
            ["id"] = id,
            ["score"] = score,
            ["time"] = time,
            ["title"] = title,
            ["type"] = "story",
        };

        if (url is not null)
        {
            item["url"] = url;
        }

        if (descendants is not null)
        {
            item["descendants"] = descendants;
        }

        return Item(item);
    }

    public static string Deleted(long id) => Item(new JsonObject
    {
        ["deleted"] = true,
        ["id"] = id,
        ["time"] = 1570887781,
        ["type"] = "story",
    });

    public static string Dead(long id, string title, int score) =>
        Item(JsonNode.Parse(Story(id, title, score))!.AsObject().With("dead", true));

    public static string OfType(long id, string type, string title = "Not a story", int score = 1) =>
        Item(JsonNode.Parse(Story(id, title, score))!.AsObject().With("type", type));

    public static string Item(JsonObject item) => item.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static JsonObject With(this JsonObject item, string property, JsonNode? value)
    {
        item[property] = value;
        return item;
    }
}
