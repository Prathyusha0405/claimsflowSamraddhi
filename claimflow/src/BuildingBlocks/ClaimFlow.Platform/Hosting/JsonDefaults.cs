using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaimFlow.Platform.Hosting;

/// <summary>One JSON convention everywhere: camelCase, enums as strings, case-insensitive reads.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
