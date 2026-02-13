using NSSharp.Binding;
using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Ast;

namespace NSSharp.Tests;

public class BindingGeneratorTests
{
    private static string GenerateBinding(string objcSource, bool emitCBindings = false)
    {
        var lexer = new ObjCLexer(objcSource);
        var tokens = lexer.Tokenize();
        var parser = new ObjCParser(tokens);
        var header = parser.Parse("test.h");
        var generator = new CSharpBindingGenerator();
        return generator.Generate(header, emitCBindings);
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
    public void Generates_Category_Merged_Into_Parent()
    {
        var cs = GenerateBinding("""
            @interface NSString
            -(void)originalMethod;
            @end
            @interface NSString (MyCategory)
            -(void)categoryMethod;
            @end
            """);
        // Category methods should be merged into the parent class
        Assert.DoesNotContain("[Category]", cs);
        Assert.DoesNotContain("NSString_MyCategory", cs);
        Assert.Contains("[Export (\"originalMethod\")]", cs);
        Assert.Contains("[Export (\"categoryMethod\")]", cs);
    }

    [Fact]
    public void Generates_Category_Standalone_When_No_Parent()
    {
        var cs = GenerateBinding("""
            @interface NSString (MyCategory)
            -(void)categoryMethod;
            @end
            """);
        // No parent class in same header → emitted as [Category]
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
            NS_ASSUME_NONNULL_BEGIN
            @interface Foo
            -(void)doThing:(int)a withValue:(NSString *)b;
            @end
            NS_ASSUME_NONNULL_END
            """);
        Assert.Contains("[Export (\"doThing:withValue:\")]", cs);
        Assert.Contains("void DoThing (int a, string b)", cs);
    }

    [Fact]
    public void Protocol_Delegate_Method_Strips_Sender_Prefix()
    {
        var cs = GenerateBinding("""
            @protocol MyDelegate
            -(void)myController:(id)sender didSelectItem:(NSString *)item;
            @end
            """);
        Assert.Contains("[Export (\"myController:didSelectItem:\")]", cs);
        Assert.Contains("void DidSelectItem", cs);
    }

    [Fact]
    public void Method_Name_Strips_Trailing_Preposition()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(void)configureWithDocument:(NSString *)doc title:(NSString *)title;
            @end
            """);
        Assert.Contains("void Configure", cs);
    }

    [Fact]
    public void Non_Void_Method_With_Params_Gets_Get_Prefix()
    {
        var cs = GenerateBinding("""
            @protocol MyProtocol
            -(NSString *)annotationForIndexPath:(NSString *)path;
            @end
            """);
        Assert.Contains("GetAnnotation", cs);
    }

    [Fact]
    public void Embedded_Sender_Prefix_Stripped_In_Protocol()
    {
        var cs = GenerateBinding("""
            @protocol MyDelegate
            -(void)myViewControllerDidCancel:(NSObject *)sender;
            @end
            """);
        Assert.Contains("void DidCancel", cs);
    }

    [Fact]
    public void Nonnull_Instancetype_Init_Becomes_Constructor()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(nonnull instancetype)initWithFrame:(CGRect)frame;
            @end
            """);
        Assert.Contains("NativeHandle Constructor", cs);
    }

    [Fact]
    public void Static_Factory_Instancetype_Resolves_To_Class_Name()
    {
        var cs = GenerateBinding("""
            @interface PSPDFStatusHUDItem
            +(instancetype)progressWithText:(NSString *)text;
            @end
            """);
        Assert.Contains("PSPDFStatusHUDItem GetProgress", cs);
        Assert.DoesNotContain("instancetype GetProgress", cs);
    }

    [Fact]
    public void IBAction_Maps_To_Void()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(IBAction)buttonPressed:(id)sender;
            @end
            """);
        Assert.Contains("void ButtonPressed", cs);
        Assert.DoesNotContain("IBAction", cs.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).Aggregate("", (a, b) => a + b));
    }

    [Fact]
    public void Block_Type_No_Unsafe_Keyword()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(void)doWork:(void (^)(BOOL done))completion;
            @end
            """);
        Assert.DoesNotContain("unsafe", cs);
        Assert.Contains("Action completion", cs);
    }

    [Fact]
    public void NSArray_Generic_Maps_To_Typed_Array()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(NSArray<NSString *> *)items;
            @end
            """);
        Assert.Contains("string []", cs);
    }

    [Fact]
    public void Id_Protocol_Maps_To_IProtocol()
    {
        var cs = GenerateBinding("""
            @interface Foo
            -(void)addClient:(id<MyClient>)client;
            @end
            """);
        Assert.Contains("IMyClient client", cs);
    }

    [Fact]
    public void Block_Typedef_Renamed_To_Handler()
    {
        var cs = GenerateBinding("""
            @interface Foo
            @property PSPDFAnnotationGroupItemConfigurationBlock configBlock;
            @end
            """);
        Assert.Contains("PSPDFAnnotationGroupItemConfigurationHandler", cs);
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

    [Fact]
    public void Generates_Protocol_Stub_Interface()
    {
        var cs = GenerateBinding("@protocol MyProto @end");
        Assert.Contains("interface IMyProto {}", cs);
    }

    [Fact]
    public void Generates_Model_For_Delegate_Protocol()
    {
        var cs = GenerateBinding("""
            @protocol MyViewDelegate
            @optional
            -(void)viewDidLoad;
            @end
            """);
        Assert.Contains("[Protocol, Model]", cs);
        Assert.Contains("[BaseType (typeof (NSObject))]", cs);
        Assert.Contains("interface MyViewDelegate", cs);
        Assert.Contains("interface IMyViewDelegate {}", cs);
    }

    [Fact]
    public void Generates_No_Model_For_NonDelegate_Protocol()
    {
        var cs = GenerateBinding("@protocol MyPresenting @end");
        Assert.Contains("[Protocol]", cs);
        Assert.DoesNotContain("[Model]", cs);
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
    public void Omits_DllImport_By_Default()
    {
        var cs = GenerateBinding("extern void DoSomething(int value);");
        Assert.DoesNotContain("[DllImport", cs);
        Assert.DoesNotContain("CFunctions", cs);
    }

    [Fact]
    public void Generates_DllImport_For_Extern_Function()
    {
        var cs = GenerateBinding("extern void DoSomething(int value);", emitCBindings: true);
        Assert.Contains("[DllImport (\"__Internal\")]", cs);
        Assert.Contains("static extern void DoSomething (int value)", cs);
    }

    [Fact]
    public void Generates_Variadic_As_IntPtr()
    {
        var cs = GenerateBinding("extern int myprintf(const char *fmt, ...);", emitCBindings: true);
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

    #region DesignatedInitializer and BlockProperty

    [Fact]
    public void Generates_DesignatedInitializer_Attribute()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
- (instancetype)initWithTitle:(NSString *)title NS_DESIGNATED_INITIALIZER;
@end");
        Assert.Contains("[DesignatedInitializer]", cs);
        Assert.Contains("[Export (\"initWithTitle:\")]", cs);
        Assert.Contains("NativeHandle Constructor", cs);
    }

    [Fact]
    public void Parses_Block_Type_Property()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
@property (nonatomic, copy, nullable) void (^completionHandler)(BOOL success);
@end");
        Assert.Contains("CompletionHandler", cs);
        Assert.Contains("completionHandler", cs);
        Assert.Contains("NullAllowed", cs);
    }

    [Fact]
    public void NS_REQUIRES_SUPER_Does_Not_Corrupt_Selector()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
- (void)viewDidLoad NS_REQUIRES_SUPER;
@end");
        Assert.Contains("[Export (\"viewDidLoad\")]", cs);
        Assert.DoesNotContain("NS_REQUIRES_SUPER", cs);
    }

    #endregion

    #region Notification attribute

    [Fact]
    public void Emits_Notification_For_NSNotificationName_Constant()
    {
        var cs = GenerateBinding(@"
PSPDF_EXPORT NSNotificationName const MyClassDidFinishNotification;
", emitCBindings: false);
        Assert.Contains("[Notification]", cs);
        Assert.Contains("[Field (\"MyClassDidFinishNotification\")]", cs);
    }

    [Fact]
    public void Emits_Notification_For_Name_Ending_With_Notification()
    {
        var cs = GenerateBinding(@"
extern NSString * const SomethingChangedNotification;
", emitCBindings: false);
        Assert.Contains("[Notification]", cs);
        Assert.Contains("[Field (\"SomethingChangedNotification\")]", cs);
    }

    #endregion

    #region Async attribute

    [Fact]
    public void Emits_Async_For_Completion_Handler_Method()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
- (void)fetchDataWithCompletion:(void (^)(BOOL success))completion;
@end");
        Assert.Contains("[Async]", cs);
        Assert.Contains("[Export (\"fetchDataWithCompletion:\")]", cs);
    }

    [Fact]
    public void Emits_Async_For_CompletionHandler_Method()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
- (void)saveToURL:(NSURL *)url completionHandler:(void (^)(NSError *error))completionHandler;
@end");
        Assert.Contains("[Async]", cs);
        Assert.Contains("[Export (\"saveToURL:completionHandler:\")]", cs);
    }

    [Fact]
    public void Does_Not_Emit_Async_For_Non_Completion_Method()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
- (void)doSomething:(NSString *)name;
@end");
        Assert.DoesNotContain("[Async]", cs);
    }

    #endregion

    #region Protocol property decomposition

    [Fact]
    public void Decomposes_Optional_Protocol_Property_Into_Getter_Setter()
    {
        var cs = GenerateBinding(@"
NS_ASSUME_NONNULL_BEGIN
@protocol MyProtocol
@optional
@property (nonatomic) NSInteger count;
@end
NS_ASSUME_NONNULL_END");
        Assert.Contains("[Export (\"count\")]", cs);
        Assert.Contains("GetCount ()", cs);
        Assert.Contains("[Export (\"setCount:\")]", cs);
        Assert.Contains("SetCount (", cs);
        Assert.DoesNotContain("[Abstract]", cs);
    }

    [Fact]
    public void Required_Protocol_Property_Stays_As_Property()
    {
        var cs = GenerateBinding(@"
NS_ASSUME_NONNULL_BEGIN
@protocol MyProtocol
@property (nonatomic) NSInteger count;
@end
NS_ASSUME_NONNULL_END");
        Assert.Contains("[Abstract]", cs);
        Assert.Contains("[Export (\"count\")]", cs);
        Assert.Contains("Count { get; set; }", cs);
    }

    [Fact]
    public void Decomposes_Optional_Protocol_Readonly_Property_Without_Setter()
    {
        var cs = GenerateBinding(@"
NS_ASSUME_NONNULL_BEGIN
@protocol MyProtocol
@optional
@property (nonatomic, readonly) NSString *title;
@end
NS_ASSUME_NONNULL_END");
        Assert.Contains("[Export (\"title\")]", cs);
        Assert.Contains("GetTitle ()", cs);
        Assert.DoesNotContain("SetTitle", cs);
    }

    [Fact]
    public void Required_Protocol_Property_With_Custom_Getter_Uses_Bind()
    {
        var cs = GenerateBinding(@"
NS_ASSUME_NONNULL_BEGIN
@protocol MyProtocol
@required
@property (nonatomic, getter=isEnabled, readonly) BOOL enabled;
@end
NS_ASSUME_NONNULL_END");
        Assert.Contains("[Abstract]", cs);
        Assert.Contains("[Export (\"enabled\")]", cs);
        Assert.Contains("[Bind (\"isEnabled\")]", cs);
        Assert.Contains("get;", cs);
        Assert.DoesNotContain("GetEnabled", cs);
    }

    [Fact]
    public void Optional_Protocol_Property_With_Custom_Getter_Decomposes()
    {
        var cs = GenerateBinding(@"
NS_ASSUME_NONNULL_BEGIN
@protocol MyProtocol
@optional
@property (nonatomic, getter=isSelected) BOOL selected;
@end
NS_ASSUME_NONNULL_END");
        Assert.Contains("[Export (\"isSelected\")]", cs);
        Assert.Contains("GetSelected ()", cs);
        Assert.Contains("[Export (\"setSelected:\")]", cs);
        Assert.Contains("SetSelected (", cs);
        Assert.DoesNotContain("[Abstract]", cs);
    }

    [Fact]
    public void Interface_Property_Not_Decomposed()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
@property (nonatomic) NSInteger count;
@end");
        Assert.Contains("Count { get; set; }", cs);
        Assert.DoesNotContain("GetCount", cs);
        Assert.DoesNotContain("SetCount", cs);
    }

    [Fact]
    public void Interface_Property_With_Custom_Getter_Uses_Bind()
    {
        var cs = GenerateBinding(@"
@interface Foo : NSObject
@property (nonatomic, getter=isEnabled) BOOL enabled;
@end");
        Assert.Contains("[Bind (\"isEnabled\")]", cs);
        Assert.Contains("get;", cs);
        Assert.DoesNotContain("GetEnabled", cs);
    }

    #endregion
}
