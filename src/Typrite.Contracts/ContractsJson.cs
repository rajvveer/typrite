using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typrite.Contracts;

/// <summary>One serializer configuration for every wire and on-disk contract.</summary>
public static class ContractsJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
