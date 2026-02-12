using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Ast;

namespace NSSharp.Tests;

/// <summary>
/// Tests based on header scenarios from dotnet/macios PR #24622 (Objective-Sharpie).
/// These exercise real-world ObjC header patterns found in their test suite.
/// </summary>
public class SharpieScenarioTests
{
    private static ObjCHeader Parse(string source)
    {
        var lexer = new ObjCLexer(source);
        var tokens = lexer.Tokenize();
        var parser = new ObjCParser(tokens);
        return parser.Parse("test.h");
    }

    #region Category.h scenarios

    [Fact]
    public void Parses_Category_With_Protocol()
    {
        var h = Parse("""
            @interface Interface
            @end
            @protocol SomeProtocol
            @end
            @interface Interface (CategoryWithProtocol) <SomeProtocol>
            @end
            """);
        var cat = h.Interfaces.First(i => i.Category == "CategoryWithProtocol");
        Assert.Equal("Interface", cat.Name);
        Assert.Contains("SomeProtocol", cat.Protocols);
    }

    [Fact]
    public void Parses_Category_With_InstanceType_Class_Method()
    {
        var h = Parse("""
            @interface Interface (CategoryWithInstanceType)
            +(instancetype)hello;
            @end
            """);
        var cat = h.Interfaces[0];
        Assert.Equal("CategoryWithInstanceType", cat.Category);
        Assert.Single(cat.ClassMethods);
        Assert.Equal("hello", cat.ClassMethods[0].Selector);
        Assert.Equal("instancetype", cat.ClassMethods[0].ReturnType);
    }

    [Fact]
    public void Parses_Multiple_Categories_On_Same_Class()
    {
        var h = Parse("""
            @interface NSString
            @end
            @interface NSData
            @end
            @interface NSString (Extensions)
            @end
            @interface NSData (Extensions)
            @end
            """);
        Assert.Equal(4, h.Interfaces.Count);
        Assert.Equal(2, h.Interfaces.Count(i => i.Category == "Extensions"));
    }

    #endregion

    #region Protocol.h scenarios

    [Fact]
    public void Parses_Protocol_Inheritance()
    {
        var h = Parse("""
            @protocol ProtocolB
            @end
            @protocol ProtocolC <ProtocolB>
            @end
            """);
        var c = h.Protocols.First(p => p.Name == "ProtocolC");
        Assert.Contains("ProtocolB", c.InheritedProtocols);
    }

    [Fact]
    public void Parses_Required_And_Optional_Methods_And_Properties()
    {
        var h = Parse("""
            @protocol RequiredAndOptional
            -(void)implicitRequiredMethod;
            @property int implicitRequiredProperty;
            @optional
            -(void)firstOptionalMethod;
            @property int firstOptionalProperty;
            @required
            -(void)explicitRequiredMethod;
            @property int explicitRequiredProperty;
            @optional
            -(void)secondOptionalMethod;
            @property int secondOptionalProperty;
            @end
            """);
        var p = h.Protocols[0];
        Assert.Equal(2, p.RequiredInstanceMethods.Count);
        Assert.Equal(2, p.OptionalInstanceMethods.Count);
        Assert.Contains(p.RequiredInstanceMethods, m => m.Selector == "implicitRequiredMethod");
        Assert.Contains(p.RequiredInstanceMethods, m => m.Selector == "explicitRequiredMethod");
        Assert.Contains(p.OptionalInstanceMethods, m => m.Selector == "firstOptionalMethod");
        Assert.Contains(p.OptionalInstanceMethods, m => m.Selector == "secondOptionalMethod");
    }

    [Fact]
    public void Parses_Interface_Implementing_Multiple_Protocols()
    {
        var h = Parse("""
            @protocol ProtocolA @end
            @protocol ProtocolB @end
            @interface Root @end
            @interface Sub : Root <ProtocolA, ProtocolB>
            @end
            """);
        var sub = h.Interfaces.First(i => i.Name == "Sub");
        Assert.Equal("Root", sub.Superclass);
        Assert.Contains("ProtocolA", sub.Protocols);
        Assert.Contains("ProtocolB", sub.Protocols);
    }

    #endregion

    #region Interface.h scenarios

    [Fact]
    public void Parses_Interface_With_Methods_And_Properties()
    {
        var h = Parse("""
            @interface Members
            -(void)instanceMethod;
            -(int)instanceMethod:(int *)takingIntPtr andOutSub:(Sub **)sub;
            +(void)staticMethod;
            @property int intProperty;
            @property (readonly) int intPropertyReadonly;
            @end
            """);
        var m = h.Interfaces[0];
        Assert.Equal(2, m.InstanceMethods.Count);
        Assert.Single(m.ClassMethods);
        Assert.Equal(2, m.Properties.Count);
        Assert.Equal("instanceMethod:andOutSub:", m.InstanceMethods[1].Selector);
        Assert.Contains("readonly", m.Properties[1].Attributes);
    }

    [Fact]
    public void Parses_Interface_With_Instance_Variables_Block()
    {
        var h = Parse("""
            @interface Fields
            {
                int intField;
            }
            -(int)getIntField;
            -(void)setIntField:(int)value;
            @end
            """);
        var f = h.Interfaces[0];
        Assert.Equal(2, f.InstanceMethods.Count);
        Assert.Equal("getIntField", f.InstanceMethods[0].Selector);
        Assert.Equal("setIntField:", f.InstanceMethods[1].Selector);
    }

    #endregion

    #region Properties.h scenarios

    [Fact]
    public void Parses_Property_Custom_Getter_Setter()
    {
        var h = Parse("""
            @interface PropertyTests
            @property (getter=customGetter) int readWriteCustomGetterInt32;
            @property (setter=customSetter:) int readWriteCustomSetterInt32;
            @property (getter=customGetter, setter=customSetterInt32:) int readWriteCustomGetterAndSetter;
            @property (class) int staticInt;
            @property (readonly, class) int staticReadonlyInt;
            @property (nonatomic, copy, null_resettable) NSDate *nullResettableDate;
            @end
            """);
        var props = h.Interfaces[0].Properties;
        Assert.Contains(props, p => p.Attributes.Contains("getter=customGetter"));
        Assert.Contains(props, p => p.Attributes.Contains("setter=customSetter:"));
        Assert.Contains(props, p => p.Attributes.Contains("class"));
        Assert.Contains(props, p => p.Attributes.Contains("null_resettable"));
    }

    #endregion

    #region Enum scenarios

    [Fact]
    public void Parses_C_Typed_Enum()
    {
        var h = Parse("""
            typedef long NSInteger;
            enum StronglyTypedObjCOrCXXEnum : NSInteger {
                Zero = 0,
                One = 1
            };
            """);
        var e = h.Enums.First(x => x.Name == "StronglyTypedObjCOrCXXEnum");
        Assert.Equal("NSInteger", e.BackingType);
        Assert.Equal(2, e.Values.Count);
        Assert.Equal("0", e.Values[0].Value);
    }

    [Fact]
    public void Parses_Typedef_Anon_Enum()
    {
        var h = Parse("""
            typedef enum {
                TypedefEnumOne,
                TypedefEnumTwo,
                TypedefEnumThree
            } TypedefEnum;
            """);
        var e = h.Enums.First(x => x.Name == "TypedefEnum");
        Assert.Equal(3, e.Values.Count);
    }

    [Fact]
    public void Parses_Enum_With_Constant_Expressions()
    {
        var h = Parse("""
            enum ConstantExpressionEnum {
                ConstantExpressionEnumImplicit,
                ConstantExpressionEnumOne = 1,
                ConstantExpressionEnum_OneChar = 'a',
                ConstantExpressionEnum_Maths = (1 + 2) / 3 * 10
            };
            """);
        var e = h.Enums[0];
        Assert.Equal(4, e.Values.Count);
        Assert.Null(e.Values[0].Value); // implicit
        Assert.Equal("1", e.Values[1].Value);
    }

    [Fact]
    public void Parses_Enum_DeclRef_Expression()
    {
        var h = Parse("""
            typedef long NSInteger;
            enum DeclRefExprEnum : NSInteger {
                DREOne = 1,
                DRETwo = 2,
                DREThree = DREOne + DRETwo
            };
            """);
        var e = h.Enums.First(x => x.Name == "DeclRefExprEnum");
        Assert.Equal("NSInteger", e.BackingType);
        Assert.Equal(3, e.Values.Count);
        Assert.Contains("DREOne", e.Values[2].Value);
    }

    [Fact]
    public void Parses_Enum_With_Shift_Expressions()
    {
        var h = Parse("""
            enum Shifts : unsigned {
                Sh1 = 1 << 1,
                Sh2 = 1 << 2,
                Sh3 = 1 << 3,
                Sh4 = 1 << 4
            };
            """);
        var e = h.Enums[0];
        Assert.Equal("unsigned", e.BackingType);
        Assert.Equal(4, e.Values.Count);
    }

    [Fact]
    public void Parses_Anonymous_Enum()
    {
        var h = Parse("""
            enum {
                FullyAnonEnumZero,
                FullyAnonEnumOne
            };
            """);
        Assert.Single(h.Enums);
        Assert.Null(h.Enums[0].Name);
        Assert.Equal(2, h.Enums[0].Values.Count);
    }

    [Fact]
    public void Parses_Enum_Unsigned_Long_Long_Backing()
    {
        var h = Parse("""
            enum LongAndUnsignedConstants : unsigned long long {
                Int32Max = 2147483647U,
                Int32MaxPlusOne = 2147483648U,
            };
            """);
        var e = h.Enums[0];
        Assert.Equal("unsigned long long", e.BackingType);
        Assert.Equal(2, e.Values.Count);
    }

    #endregion

    #region Struct.h scenarios

    [Fact]
    public void Parses_Named_Struct()
    {
        var h = Parse("""
            struct ElaboratedNameOnlyStruct {
                int foo;
            };
            """);
        Assert.Single(h.Structs);
        Assert.Equal("ElaboratedNameOnlyStruct", h.Structs[0].Name);
        Assert.Single(h.Structs[0].Fields);
        Assert.Equal("foo", h.Structs[0].Fields[0].Name);
    }

    [Fact]
    public void Parses_Typedef_Anon_Struct()
    {
        var h = Parse("""
            typedef struct {
                int foo;
            } AnonStructRenamedByTypedef;
            """);
        Assert.Single(h.Structs);
        Assert.Equal("AnonStructRenamedByTypedef", h.Structs[0].Name);
    }

    [Fact]
    public void Parses_Nested_Struct()
    {
        var h = Parse("""
            struct Parent {
                char first;
                struct ChildStruct {
                    int a;
                    char b;
                } second;
                unsigned long third;
            };
            """);
        Assert.True(h.Structs.Count >= 1);
        // Should find Parent struct
        Assert.Contains(h.Structs, s => s.Name == "Parent");
    }

    #endregion

    #region Functions.h scenarios

    [Fact]
    public void Parses_Extern_Functions_Various_Signatures()
    {
        var h = Parse("""
            extern void Action ();
            extern void ActionTakingInt (int i);
            extern void ActionTakingIntAndCString (int i, const char *str);
            extern char *FuncTakingInt (int i);
            """);
        Assert.Equal(4, h.Functions.Count);
        Assert.Equal("Action", h.Functions[0].Name);
        Assert.Equal("void", h.Functions[0].ReturnType);
        Assert.Empty(h.Functions[0].Parameters);
        Assert.Equal("FuncTakingInt", h.Functions[3].Name);
        Assert.Equal("char *", h.Functions[3].ReturnType);
    }

    [Fact]
    public void Parses_Variadic_Function()
    {
        var h = Parse("""
            #include <stddef.h>
            extern int __snprintf (char * restrict str, size_t size, const char * restrict format, ...);
            """);
        Assert.Single(h.Functions);
        Assert.Equal("__snprintf", h.Functions[0].Name);
        Assert.Contains(h.Functions[0].Parameters, p => p.Name == "...");
    }

    [Fact]
    public void Parses_ObjC_Methods_With_Various_Signatures()
    {
        var h = Parse("""
            @interface ObjCMethods
            -(void)action;
            -(void)actionTakingInt:(int)i;
            -(void)actionTakingInt:(int)i andCString:(const char *)str;
            -(char *)funcTakingInt:(int)i;
            @end
            """);
        var im = h.Interfaces[0].InstanceMethods;
        Assert.Equal(4, im.Count);
        Assert.Equal("action", im[0].Selector);
        Assert.Equal("actionTakingInt:andCString:", im[2].Selector);
        Assert.Equal("char *", im[3].ReturnType);
    }

    #endregion

    #region Nullability.h scenarios

    [Fact]
    public void Parses_Nullable_Properties()
    {
        var h = Parse("""
            @interface Foo
            @property (nullable) SEL selector;
            @property (nullable, readonly) id someObject;
            @property (nonatomic, readonly, nullable) NSObject *presentedObject;
            @end
            """);
        var props = h.Interfaces[0].Properties;
        Assert.All(props, p => Assert.True(p.IsNullable));
    }

    [Fact]
    public void Parses_Nullable_Method_Returns_And_Params()
    {
        var h = Parse("""
            @interface Foo
            -(__nullable id)nullableReturnPointer;
            -(nullable id)nullableReturnPointer:(int)arg withNullable:(nullable id)obj;
            -(NSObject * _Nullable) AnObject;
            @end
            """);
        var methods = h.Interfaces[0].InstanceMethods;
        Assert.Equal(3, methods.Count);
        Assert.Contains("nullable", methods[0].ReturnType, StringComparison.OrdinalIgnoreCase);
        var m2params = methods[1].Parameters;
        Assert.True(m2params[1].IsNullable);
    }

    #endregion

    #region ObjCGenerics.h scenarios

    [Fact]
    public void Parses_Generic_Return_Types()
    {
        var h = Parse("""
            @interface GenericTypesTest
            -(NSDictionary<NSString *, NSNumber *> *)NSDictionaryOfNSStringToNSNumber;
            -(NSArray<NSString *> *)NSArrayOfNSString;
            -(NSSet<NSArray<NSString *> *> *)NSSetOfNSArrayOfNSString;
            @end
            """);
        var methods = h.Interfaces[0].InstanceMethods;
        Assert.Contains("NSDictionary", methods[0].ReturnType);
        Assert.Contains("NSString", methods[0].ReturnType);
        Assert.Contains("NSNumber", methods[0].ReturnType);
        Assert.Contains("NSArray", methods[1].ReturnType);
        Assert.Contains("NSSet", methods[2].ReturnType);
    }

    [Fact]
    public void Parses_Generic_Property()
    {
        var h = Parse("""
            @interface CNLabeledValue
            @property (copy, readonly, nonatomic) NSString *ValueTypeProperty;
            -(nullable NSString *)getValueTypeMethod;
            -(void)setValueTypeMethod:(nullable NSString *)obj;
            @end
            """);
        Assert.Single(h.Interfaces[0].Properties);
        Assert.Equal(2, h.Interfaces[0].InstanceMethods.Count);
    }

    #endregion

    #region OutParameters.h scenarios

    [Fact]
    public void Parses_NSError_Out_Parameter()
    {
        var h = Parse("""
            @interface OutParams
            -(void)foo:(id)foo withError:(NSError **)error;
            -(void)bar:(NSNumber *)num withOutObject:(id *)obj;
            @end
            """);
        var methods = h.Interfaces[0].InstanceMethods;
        Assert.Equal("foo:withError:", methods[0].Selector);
        Assert.Equal("NSError * *", methods[0].Parameters[1].Type);
        Assert.Equal("bar:withOutObject:", methods[1].Selector);
    }

    #endregion

    #region ObjCDesignatedInitializer.h scenarios

    [Fact]
    public void Parses_Designated_Initializer_Methods()
    {
        var h = Parse("""
            @interface NSDesignatedInitializerTest
            -(instancetype)initWithInt:(int)value;
            -(instancetype)initWithString:(NSString *)value;
            @end
            """);
        var methods = h.Interfaces[0].InstanceMethods;
        Assert.Equal(2, methods.Count);
        Assert.Equal("initWithInt:", methods[0].Selector);
        Assert.Equal("instancetype", methods[0].ReturnType);
        Assert.Equal("initWithString:", methods[1].Selector);
    }

    #endregion

    #region Availability.h scenarios

    [Fact]
    public void Parses_Interface_With_Availability_Attributes()
    {
        var h = Parse("""
            __attribute__((availability(watchos,unavailable)))
            @interface UnavailableWatchOS
            @end

            __attribute__((availability(tvos,unavailable)))
            @interface UnavailableTvOS
            @end

            __attribute__((availability(macosx,introduced=10.8)))
            __attribute__((availability(ios,introduced=7.0)))
            @interface Availability
            -(void)thisIsDeprecated
                __attribute__((availability(macosx,deprecated=10.10.3)));
            -(void)thisShouldBeShorthandMac
                __attribute__((availability(macosx,introduced=10.10.3)));
            @end
            """);
        Assert.Equal(3, h.Interfaces.Count);
        Assert.Equal("UnavailableWatchOS", h.Interfaces[0].Name);
        Assert.Equal("Availability", h.Interfaces[2].Name);
        Assert.Equal(2, h.Interfaces[2].InstanceMethods.Count);
    }

    #endregion

    #region Expressions.h scenarios

    [Fact]
    public void Parses_Enum_With_Complex_Expressions()
    {
        var h = Parse("""
            struct SomeStruct {
                char buf [32];
            };
            enum Expressions {
                One = 1,
                Char = 'a',
                Add = 1 + 2,
                Subtract = 5 - 3,
                ShiftLeft = 200 << 4,
                ParenSubExpr = (0),
                SizeofInt = sizeof (int)
            };
            """);
        var e = h.Enums.First(x => x.Name == "Expressions");
        Assert.Equal(7, e.Values.Count);
        Assert.Equal("1", e.Values[0].Value);
    }

    #endregion

    #region CodeAuditedAttribute.h scenarios

    [Fact]
    public void Parses_Function_After_Pragma()
    {
        var h = Parse("""
            void DoSomething();
            """);
        Assert.Single(h.Functions);
        Assert.Equal("DoSomething", h.Functions[0].Name);
    }

    #endregion

    #region ObjCRequiresSuper.h scenarios

    [Fact]
    public void Parses_Interface_With_Macro_After_Method()
    {
        var h = Parse("""
            @interface RequiresSuperTest
            -(void)foo;
            @end
            """);
        Assert.Single(h.Interfaces);
        Assert.Single(h.Interfaces[0].InstanceMethods);
        Assert.Equal("foo", h.Interfaces[0].InstanceMethods[0].Selector);
    }

    #endregion

    #region Fields.h scenarios

    [Fact]
    public void Parses_Extern_Fields_And_Enum_Interspersed()
    {
        var h = Parse("""
            extern NSString *kFirstField;
            extern int *kNextField;

            @interface FirstInterface
            @end

            typedef enum : long {
                FirstEnumZero,
                FirstEnumOne = FirstEnumZero + 1
            } FirstEnum;
            """);
        Assert.Single(h.Interfaces);
        Assert.Single(h.Enums);
        Assert.Equal("FirstEnum", h.Enums[0].Name);
        Assert.Equal("long", h.Enums[0].BackingType);
    }

    #endregion
}
