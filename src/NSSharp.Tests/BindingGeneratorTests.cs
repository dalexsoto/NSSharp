using NSSharp.Binding;
using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Ast;

namespace NSSharp.Tests;

public class BindingGeneratorTests
{
    private static string GenerateBinding(string objcSource)
    {
        var lexer = new ObjCLexer(objcSource);
        var tokens = lexer.Tokenize();
        var parser = new ObjCParser(tokens);
        var header = parser.Parse("test.h");
        var generator = new CSharpBindingGenerator();
        return generator.Generate(header);
    }

    #region Interface bindings

    [Fact]
    public void Generates_BaseType_For_Superclass()
    {
        var cs = GenerateBinding("@interface Foo : NSObject @end");
        Assert.Contains("[BaseType (typeof (NSObject))]", cs);
        Assert.Contains("interface Foo", cs);
    }

    [Fact]
    public void Generates_Protocol_Conformance_As_Interface_Inheritance()
    {
        var cs = GenerateBinding("""
            @protocol MyProto @end
            @interface Foo : NSObject <MyProto> @end
            """);
        Assert.Contains("interface Foo : IMyProto", cs);
    }

    [Fact]
    public void Generates_Category_With_BaseType()
    {
        var cs = GenerateBinding("""
            @interface NSString @end
            @interface NSString (MyCategory) @end
            """);
        Assert.Contains("[Category]", cs);
        Assert.Contains("[BaseType (typeof (NSString))]", cs);
        Assert.Contains("interface NSString_MyCategory", cs);
    }

    #endregion

    #region Method bindings

    [Fact]
    public void Generates_Export_For_Instance_Method()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(void)doSomething;
            @end
            """);
        Assert.Contains("[Export (\"doSomething\")]", cs);
        Assert.Contains("void DoSomething ()", cs);
    }

    [Fact]
    public void Generates_Static_Export_For_Class_Method()
    {
        var cs = GenerateBinding("""
            @interface Foo
            +(void)classMethod;
            @end
            """);
        Assert.Contains("[Static]", cs);
        Assert.Contains("[Export (\"classMethod\")]", cs);
    }

    [Fact]
    public void Generates_Constructor_For_Init_Method()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(instancetype)initWithValue:(int)value;
            @end
            """);
        Assert.Contains("[Export (\"initWithValue:\")]", cs);
        Assert.Contains("NativeHandle Constructor (int value)", cs);
    }

    [Fact]
    public void Generates_Selector_With_Multiple_Parts()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(void)doThing:(int)a withValue:(NSString *)b;
            @end
            """);
        Assert.Contains("[Export (\"doThing:withValue:\")]", cs);
        Assert.Contains("void DoThingWithValue (int a, string b)", cs);
    }

    #endregion

    #region Property bindings

    [Fact]
    public void Generates_ReadWrite_Property()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property int value;
            @end
            """);
        Assert.Contains("[Export (\"value\")]", cs);
        Assert.Contains("int Value { get; set; }", cs);
    }

    [Fact]
    public void Generates_Readonly_Property()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property (readonly) int count;
            @end
            """);
        Assert.Contains("int Count { get; }", cs);
        Assert.DoesNotContain("set;", cs.Substring(cs.IndexOf("Count")));
    }

    [Fact]
    public void Generates_Copy_ArgumentSemantic()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property (copy) NSString *name;
            @end
            """);
        Assert.Contains("ArgumentSemantic.Copy", cs);
    }

    [Fact]
    public void Generates_NullAllowed_For_Nullable_Property()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property (nullable) NSString *name;
            @end
            """);
        Assert.Contains("NullAllowed", cs);
    }

    [Fact]
    public void Generates_Static_Property()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property (class) int sharedValue;
            @end
            """);
        Assert.Contains("[Static]", cs);
    }

    #endregion

    #region Protocol bindings

    [Fact]
    public void Generates_Protocol_Attribute()
    {
        var cs = GenerateBinding("@protocol MyProto @end");
        Assert.Contains("[Protocol]", cs);
        Assert.Contains("interface MyProto", cs);
    }

    [Fact]
    public void Generates_Abstract_For_Required_Methods()
    {
        var cs = GenerateBinding("""
            @protocol MyProto
            @required
            -(void)requiredMethod;
            @end
            """);
        Assert.Contains("[Abstract]", cs);
        Assert.Contains("[Export (\"requiredMethod\")]", cs);
    }

    [Fact]
    public void Generates_No_Abstract_For_Optional_Methods()
    {
        var cs = GenerateBinding("""
            @protocol MyProto
            @optional
            -(void)optionalMethod;
            @end
            """);
        Assert.Contains("[Export (\"optionalMethod\")]", cs);
        // The [Abstract] should not appear next to optionalMethod
        var optIdx = cs.IndexOf("optionalMethod");
        var preceding = cs[..optIdx];
        var lastAbstract = preceding.LastIndexOf("[Abstract]");
        // If Abstract exists, it should be far before (not directly preceding)
        if (lastAbstract >= 0)
        {
            var between = preceding[lastAbstract..];
            Assert.Contains("Export", between); // another method's export between them
        }
    }

    #endregion

    #region Enum bindings

    [Fact]
    public void Generates_Native_Attribute_For_NSInteger_Enum()
    {
        var cs = GenerateBinding("""
            typedef NS_ENUM(NSInteger, MyStatus) {
                MyStatusOK = 0,
                MyStatusFail = 1
            };
            """);
        Assert.Contains("[Native]", cs);
        Assert.Contains("public enum MyStatus : long", cs);
    }

    [Fact]
    public void Generates_Flags_For_NS_OPTIONS()
    {
        var cs = GenerateBinding("""
            typedef NS_OPTIONS(NSUInteger, MyFlags) {
                MyFlagsNone = 0,
                MyFlagsA = 1,
            };
            """);
        Assert.Contains("[Flags]", cs);
    }

    [Fact]
    public void Strips_Enum_Name_Prefix_From_Members()
    {
        var cs = GenerateBinding("""
            typedef NS_ENUM(NSInteger, MyStatus) {
                MyStatusOK = 0,
                MyStatusFail = 1
            };
            """);
        Assert.Contains("OK = 0", cs);
        Assert.Contains("Fail = 1", cs);
    }

    #endregion

    #region Struct bindings

    [Fact]
    public void Generates_StructLayout_Attribute()
    {
        var cs = GenerateBinding("""
            typedef struct {
                int x;
                int y;
            } MyPoint;
            """);
        Assert.Contains("[StructLayout (LayoutKind.Sequential)]", cs);
        Assert.Contains("public struct MyPoint", cs);
        Assert.Contains("public int x;", cs);
    }

    #endregion

    #region Function bindings (P/Invoke)

    [Fact]
    public void Generates_DllImport_For_Extern_Function()
    {
        var cs = GenerateBinding("extern void DoSomething(int value);");
        Assert.Contains("[DllImport (\"__Internal\")]", cs);
        Assert.Contains("static extern void DoSomething (int value)", cs);
    }

    [Fact]
    public void Generates_Variadic_As_IntPtr()
    {
        var cs = GenerateBinding("extern int myprintf(const char *fmt, ...);");
        Assert.Contains("IntPtr varArgs", cs);
    }

    #endregion

    #region Type mapping

    [Fact]
    public void Maps_NSString_To_String()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property NSString *name;
            @end
            """);
        Assert.Contains("string Name", cs);
    }

    [Fact]
    public void Maps_Id_To_NSObject()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(id)getValue;
            @end
            """);
        Assert.Contains("NSObject GetValue", cs);
    }

    [Fact]
    public void Maps_SEL_To_Selector()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property SEL action;
            @end
            """);
        Assert.Contains("Selector Action", cs);
    }

    #endregion
}
