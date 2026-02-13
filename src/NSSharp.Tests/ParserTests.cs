using NSSharp.Lexer;
using NSSharp.Parser;

namespace NSSharp.Tests;

public class ParserTests
{
    private static Ast.ObjCHeader Parse(string source)
    {
        var lexer = new ObjCLexer(source);
        var tokens = lexer.Tokenize();
        var parser = new ObjCParser(tokens);
        return parser.Parse("test.h");
    }

    [Fact]
    public void Parses_Interface_With_Superclass_And_Protocols()
    {
        var header = Parse("@interface Foo : NSObject <BarProtocol> @end");
        Assert.Single(header.Interfaces);
        var iface = header.Interfaces[0];
        Assert.Equal("Foo", iface.Name);
        Assert.Equal("NSObject", iface.Superclass);
        Assert.Single(iface.Protocols);
        Assert.Equal("BarProtocol", iface.Protocols[0]);
    }

    [Fact]
    public void Parses_Category()
    {
        var header = Parse("@interface NSString (MyCategory) - (void)myMethod; @end");
        var iface = header.Interfaces[0];
        Assert.Equal("NSString", iface.Name);
        Assert.Equal("MyCategory", iface.Category);
        Assert.Single(iface.InstanceMethods);
    }

    [Fact]
    public void Parses_Property_With_Attributes()
    {
        var header = Parse("@interface Foo @property (nonatomic, strong, nullable) NSString *name; @end");
        var prop = header.Interfaces[0].Properties[0];
        Assert.Equal("name", prop.Name);
        Assert.Equal("NSString *", prop.Type);
        Assert.Contains("nonatomic", prop.Attributes);
        Assert.Contains("strong", prop.Attributes);
        Assert.Contains("nullable", prop.Attributes);
        Assert.True(prop.IsNullable);
    }

    [Fact]
    public void Parses_Method_With_Parameters()
    {
        var header = Parse("@interface Foo - (BOOL)doThing:(NSString *)a withValue:(NSInteger)b; @end");
        var method = header.Interfaces[0].InstanceMethods[0];
        Assert.Equal("doThing:withValue:", method.Selector);
        Assert.Equal("BOOL", method.ReturnType);
        Assert.Equal(2, method.Parameters.Count);
        Assert.Equal("a", method.Parameters[0].Name);
        Assert.Equal("NSString *", method.Parameters[0].Type);
    }

    [Fact]
    public void Parses_Class_Method()
    {
        var header = Parse("@interface Foo + (instancetype)sharedInstance; @end");
        Assert.Single(header.Interfaces[0].ClassMethods);
        Assert.Equal("sharedInstance", header.Interfaces[0].ClassMethods[0].Selector);
    }

    [Fact]
    public void Parses_Protocol_With_Optional()
    {
        var source = """
            @protocol MyProto <NSObject>
            @required
            - (void)required1;
            @optional
            - (void)optional1;
            @end
            """;
        var header = Parse(source);
        var proto = header.Protocols[0];
        Assert.Equal("MyProto", proto.Name);
        Assert.Single(proto.RequiredInstanceMethods);
        Assert.Single(proto.OptionalInstanceMethods);
        Assert.True(proto.OptionalInstanceMethods[0].IsOptional);
    }

    [Fact]
    public void Parses_NS_ENUM()
    {
        var source = """
            typedef NS_ENUM(NSInteger, MyStatus) {
                MyStatusOK = 0,
                MyStatusFail,
            };
            """;
        var header = Parse(source);
        Assert.Single(header.Enums);
        var e = header.Enums[0];
        Assert.Equal("MyStatus", e.Name);
        Assert.Equal("NSInteger", e.BackingType);
        Assert.False(e.IsOptions);
        Assert.Equal(2, e.Values.Count);
        Assert.Equal("0", e.Values[0].Value);
    }

    [Fact]
    public void Parses_NS_OPTIONS()
    {
        var source = """
            typedef NS_OPTIONS(NSUInteger, MyOpts) {
                MyOptsNone = 0,
                MyOptsA    = 1 << 0,
            };
            """;
        var header = Parse(source);
        var e = header.Enums[0];
        Assert.True(e.IsOptions);
        Assert.Equal("NSUInteger", e.BackingType);
    }

    [Fact]
    public void Parses_Struct()
    {
        var source = """
            typedef struct {
                CGFloat x;
                CGFloat y;
            } MyPoint;
            """;
        var header = Parse(source);
        Assert.Single(header.Structs);
        var s = header.Structs[0];
        Assert.Equal("MyPoint", s.Name);
        Assert.Equal(2, s.Fields.Count);
    }

    [Fact]
    public void Parses_Forward_Class_Declaration()
    {
        var header = Parse("@class Foo, Bar, Baz;");
        Assert.Equal(3, header.ForwardDeclarations.Classes.Count);
        Assert.Contains("Bar", header.ForwardDeclarations.Classes);
    }

    [Fact]
    public void Parses_Forward_Protocol_Declaration()
    {
        var header = Parse("@protocol Foo, Bar;");
        Assert.Equal(2, header.ForwardDeclarations.Protocols.Count);
    }

    [Fact]
    public void Parses_Extern_Function()
    {
        var header = Parse("extern NSString *NSStringFromFoo(NSInteger value);");
        Assert.Single(header.Functions);
        var f = header.Functions[0];
        Assert.Equal("NSStringFromFoo", f.Name);
        Assert.Equal("NSString *", f.ReturnType);
        Assert.Single(f.Parameters);
    }

    [Fact]
    public void Parses_Typedef_Block()
    {
        var header = Parse("typedef void (^CompletionBlock)(BOOL success);");
        Assert.Single(header.Typedefs);
        Assert.Equal("CompletionBlock", header.Typedefs[0].Name);
        Assert.Contains("void", header.Typedefs[0].UnderlyingType);
    }

    [Fact]
    public void Parses_Generics_In_Property_Type()
    {
        var header = Parse("@interface Foo @property (nonatomic) NSArray<NSString *> *items; @end");
        var prop = header.Interfaces[0].Properties[0];
        Assert.Contains("NSArray", prop.Type);
        Assert.Contains("NSString", prop.Type);
    }

    [Fact]
    public void Parses_Block_Type_Property()
    {
        var header = Parse("@interface Foo : NSObject @property (nonatomic, copy, nullable) void (^completionBlock)(BOOL success); @end");
        var prop = header.Interfaces[0].Properties[0];
        Assert.Equal("completionBlock", prop.Name);
        Assert.Contains("(^)", prop.Type);
        Assert.Contains("BOOL", prop.Type);
        Assert.True(prop.IsNullable);
    }

    [Fact]
    public void Parses_Block_Property_With_Object_Return_Type()
    {
        var header = Parse("@interface Foo : NSObject @property (nonatomic, copy) NSArray * (^choices)(NSString *arg); @end");
        var prop = header.Interfaces[0].Properties[0];
        Assert.Equal("choices", prop.Name);
        Assert.Contains("NSArray", prop.Type);
        Assert.Contains("(^)", prop.Type);
    }

    [Fact]
    public void Parses_NS_DESIGNATED_INITIALIZER()
    {
        var header = Parse("@interface Foo : NSObject - (instancetype)initWithTitle:(NSString *)title NS_DESIGNATED_INITIALIZER; @end");
        var method = header.Interfaces[0].InstanceMethods[0];
        Assert.Equal("initWithTitle:", method.Selector);
        Assert.True(method.IsDesignatedInitializer);
    }

    [Fact]
    public void NS_REQUIRES_SUPER_Does_Not_Appear_In_Selector()
    {
        var header = Parse("@interface Foo : NSObject - (void)viewDidLoad NS_REQUIRES_SUPER; @end");
        var method = header.Interfaces[0].InstanceMethods[0];
        Assert.Equal("viewDidLoad", method.Selector);
        Assert.DoesNotContain("NS_REQUIRES_SUPER", method.Selector);
    }
}
