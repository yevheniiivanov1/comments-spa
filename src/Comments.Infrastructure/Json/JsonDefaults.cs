using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comments.Infrastructure.Json;

/// <summary>One JSON shape everywhere: the HTTP API, the cache and the messages on the broker.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
