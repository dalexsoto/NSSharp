using System.Text.Json;
using NSSharp.Ast;
using NSSharp.Json;

namespace NSSharp.Tests;

public class JsonSerializerTests
{
    [Fact]
    public void Serializes_Header_With_CamelCase_Properties()
    {
        var header = new ObjCHeader
        {
            File = "test.h",
            Interfaces =
            [
                new ObjCInterface
                {
                    Name = "Foo",
                    Superclass = "NSObject",
                }
            ],
        };

        var json = ObjCJsonSerializer.Serialize(header);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("test.h", root.GetProperty("file").GetString());
        Assert.Equal("Foo", root.GetProperty("interfaces")[0].GetProperty("name").GetString());
        Assert.Equal("NSObject", root.GetProperty("interfaces")[0].GetProperty("superclass").GetString());
    }

    [Fact]
    public void Compact_Mode_Produces_Single_Line()
    {
        var header = new ObjCHeader { File = "test.h" };
        var json = ObjCJsonSerializer.Serialize(header, pretty: false);
        Assert.DoesNotContain("\n", json);
    }

    [Fact]
    public void Null_Values_Are_Omitted()
    {
        var header = new ObjCHeader
        {
            File = "test.h",
            Interfaces = [new ObjCInterface { Name = "Foo" }],
        };

        var json = ObjCJsonSerializer.Serialize(header);
        using var doc = JsonDocument.Parse(json);
        var iface = doc.RootElement.GetProperty("interfaces")[0];
        // superclass is null, should not appear
        Assert.False(iface.TryGetProperty("superclass", out _));
    }
}
