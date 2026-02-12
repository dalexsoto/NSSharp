using System.Text.Json;
using System.Text.Json.Serialization;
using NSSharp.Ast;

namespace NSSharp.Json;

public static class ObjCJsonSerializer
{
    private static readonly JsonSerializerOptions s_prettyOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions s_compactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ObjCHeader header, bool pretty = true)
    {
        var options = pretty ? s_prettyOptions : s_compactOptions;
        return JsonSerializer.Serialize(header, options);
    }

    public static string Serialize(IEnumerable<ObjCHeader> headers, bool pretty = true)
    {
        var options = pretty ? s_prettyOptions : s_compactOptions;
        return JsonSerializer.Serialize(headers, options);
    }
}
