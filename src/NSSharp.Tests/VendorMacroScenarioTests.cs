using NSSharp.Ast;
using NSSharp.Binding;
using NSSharp.Lexer;
using NSSharp.Parser;

namespace NSSharp.Tests;

/// <summary>
/// Tests for vendor macro handling, NS_CLOSED_ENUM, nullable types,
/// extern constants, and other real-world header patterns.
/// </summary>
public class VendorMacroScenarioTests
{
    private static ObjCHeader Parse(string source, ObjCLexerOptions? options = null)
    {
        var lexer = new ObjCLexer(source, options);
        var tokens = lexer.Tokenize();
        var parser = new ObjCParser(tokens);
        return parser.Parse("test.h");
    }

    private static string GenerateBindings(string source, ObjCLexerOptions? options = null, bool emitCBindings = false)
    {
        var header = Parse(source, options);
        return new CSharpBindingGenerator().Generate(header, emitCBindings);
    }

    #region NS_CLOSED_ENUM

    [Fact]
    public void NS_CLOSED_ENUM_ParsedLikeNS_ENUM()
    {
        var header = Parse(@"
            typedef NS_CLOSED_ENUM(NSInteger, MYLineEndType) {
                MYLineEndTypeNone,
                MYLineEndTypeSquare,
            };
        ");

        Assert.Single(header.Enums);
        var e = header.Enums[0];
        Assert.Equal("MYLineEndType", e.Name);
        Assert.Equal("NSInteger", e.BackingType);
        Assert.False(e.IsOptions);
        Assert.Equal(2, e.Values.Count);
    }

    [Fact]
    public void NS_CLOSED_ENUM_UInt8_BackingType()
    {
        var header = Parse(@"
            typedef NS_CLOSED_ENUM(UInt8, MYActionType) {
                MYActionTypeURL,
                MYActionTypeGoTo,
                MYActionTypeUnknown = UINT8_MAX
            };
        ");

        Assert.Single(header.Enums);
        var e = header.Enums[0];
        Assert.Equal("UInt8", e.BackingType);
        Assert.Equal("UINT8_MAX", e.Values[2].Value);
    }

    [Fact]
    public void NS_CLOSED_ENUM_BindingOutput_ByteBackingType()
    {
        var cs = GenerateBindings(@"
            typedef NS_CLOSED_ENUM(UInt8, MYActionType) {
                MYActionTypeURL,
                MYActionTypeUnknown = UINT8_MAX
            };
        ");

        Assert.Contains("enum MYActionType : byte", cs);
        Assert.Contains("Unknown = byte.MaxValue", cs);
    }

    #endregion

    #region NS_ERROR_ENUM

    [Fact]
    public void NS_ERROR_ENUM_ParsedLikeNS_ENUM()
    {
        var header = Parse(@"
            typedef NS_ERROR_ENUM(NSInteger, MYErrorCode) {
                MYErrorCodeOutOfMemory = 10,
                MYErrorCodeFileNotFound = 11,
            };
        ");

        Assert.Single(header.Enums);
        var e = header.Enums[0];
        Assert.Equal("MYErrorCode", e.Name);
        Assert.Equal("NSInteger", e.BackingType);
        Assert.Equal(2, e.Values.Count);
        Assert.Equal("10", e.Values[0].Value);
    }

    #endregion

    #region Vendor Export Macros

    [Fact]
    public void PSPDF_EXPORT_TreatedAsExtern()
    {
        var header = Parse(@"
            PSPDF_EXPORT NSString * const MYOptionButtonKey;
        ");

        Assert.Single(header.Functions);
        Assert.Equal("MYOptionButtonKey", header.Functions[0].Name);
    }

    [Fact]
    public void ExternConstant_Generates_FieldAttribute()
    {
        var cs = GenerateBindings(@"
            PSPDF_EXPORT NSString * const MYOptionButtonKey;
            PSPDF_EXPORT NSNotificationName const MYDocumentDidChangeNotification;
        ");

        Assert.Contains("[Field (\"MYOptionButtonKey\")]", cs);
        Assert.Contains("[Field (\"MYDocumentDidChangeNotification\")]", cs);
        Assert.Contains("interface Constants", cs);
    }

    #endregion

    #region Vendor Macros Skipped

    [Fact]
    public void PSPDF_CLASS_SWIFT_Skipped()
    {
        var header = Parse(@"
            PSPDF_CLASS_SWIFT(Action)
            @interface MYAction : MYModel
            @property (nonatomic, readonly) NSInteger type;
            @end
        ");

        Assert.Single(header.Interfaces);
        Assert.Equal("MYAction", header.Interfaces[0].Name);
        Assert.Single(header.Interfaces[0].Properties);
    }

    [Fact]
    public void PSPDF_ENUM_SWIFT_Skipped()
    {
        var header = Parse(@"
            typedef NS_CLOSED_ENUM(NSInteger, MYLineEndType) {
                MYLineEndTypeNone,
            } PSPDF_ENUM_SWIFT(LineAnnotation.EndType);
        ");

        Assert.Single(header.Enums);
        Assert.Equal("MYLineEndType", header.Enums[0].Name);
    }

    [Fact]
    public void PSPDF_DEPRECATED_Skipped()
    {
        var header = Parse(@"
            @interface MYWidget : NSObject
            @property (nonatomic) NSString *name PSPDF_DEPRECATED(14.2, ""Use fullName instead."");
            @end
        ");

        Assert.Single(header.Interfaces);
        Assert.Single(header.Interfaces[0].Properties);
    }

    [Fact]
    public void PSPDF_EMPTY_INIT_UNAVAILABLE_Skipped()
    {
        var header = Parse(@"
            PSPDF_CLASS_SWIFT(Processor)
            @interface MYProcessor : NSObject
            PSPDF_EMPTY_INIT_UNAVAILABLE
            -(instancetype)initWithDocument:(MYDocument *)document;
            @end
        ");

        Assert.Single(header.Interfaces);
        Assert.Single(header.Interfaces[0].InstanceMethods);
    }

    #endregion

    #region Property Nullable Detection

    [Fact]
    public void Property_NullableAttribute_DetectedAsNullable()
    {
        var header = Parse(@"
            @interface Foo : NSObject
            @property (nonatomic, readonly, nullable) NSString *title;
            @end
        ");

        Assert.True(header.Interfaces[0].Properties[0].IsNullable);
    }

    [Fact]
    public void Property_NullableInType_DetectedAsNullable()
    {
        var header = Parse(@"
            @interface Foo : NSObject
            @property (nonatomic) nullable NSString *title;
            @end
        ");

        Assert.True(header.Interfaces[0].Properties[0].IsNullable);
    }

    [Fact]
    public void Property_NullableGenerates_NullAllowed()
    {
        var cs = GenerateBindings(@"
            @interface Foo : NSObject
            @property (nonatomic, copy, nullable) NSString *title;
            @end
        ");

        Assert.Contains("[NullAllowed, Export", cs);
    }

    [Fact]
    public void Property_WeakGenerates_ArgumentSemanticWeak()
    {
        var cs = GenerateBindings(@"
            @interface Foo : NSObject
            @property (atomic, weak, readonly) Foo *parent;
            @end
        ");

        Assert.Contains("ArgumentSemantic.Weak", cs);
    }

    [Fact]
    public void Property_StrongGenerates_ArgumentSemanticStrong()
    {
        var cs = GenerateBindings(@"
            @interface Foo : NSObject
            @property (nonatomic, strong) NSArray *items;
            @end
        ");

        Assert.Contains("ArgumentSemantic.Strong", cs);
    }

    [Fact]
    public void Property_ObjectPointerWithoutExplicitSemantic_ReadwriteInfersStrong()
    {
        var cs = GenerateBindings(@"
            @interface Foo : NSObject
            @property (nonatomic) NSArray *items;
            @property (nonatomic, readonly) NSArray *readonlyItems;
            @end
        ");

        // Readwrite object pointer → Strong
        Assert.Contains("items\", ArgumentSemantic.Strong", cs);
        // Readonly object pointer → no semantic
        Assert.DoesNotContain("readonlyItems\", ArgumentSemantic", cs);
    }

    #endregion

    #region Protocol Required Methods

    [Fact]
    public void Protocol_RequiredMethods_GetAbstractAttribute()
    {
        var cs = GenerateBindings(@"
            @protocol MYOverridable <NSObject>
            @required
            -(void)doWork;
            @optional
            -(void)maybeWork;
            @end
        ");

        // Required → [Abstract]
        Assert.Contains("[Abstract]", cs);
        // Count: only 1 [Abstract] for the required method
        var abstractCount = cs.Split("[Abstract]").Length - 1;
        Assert.Equal(1, abstractCount);
    }

    #endregion

    #region Constructor Detection

    [Fact]
    public void InitMethod_BecomesConstructor()
    {
        var cs = GenerateBindings(@"
            @interface MYProcessor : NSObject
            -(instancetype)initWithDocument:(MYDocument *)document;
            @end
        ");

        Assert.Contains("NativeHandle Constructor", cs);
        Assert.Contains("MYDocument document", cs);
    }

    [Fact]
    public void InitMethodWithMultipleParams_BecomesConstructor()
    {
        var cs = GenerateBindings(@"
            @interface MYCryptoProvider : NSObject
            -(instancetype)initWithURL:(NSURL *)url passphraseProvider:(NSString *)provider salt:(NSString *)salt rounds:(NSUInteger)rounds;
            @end
        ");

        Assert.Contains("NativeHandle Constructor", cs);
        Assert.Contains("NSUrl url", cs);
        Assert.Contains("nuint rounds", cs);
    }

    #endregion

    #region Enum Value Constants

    [Fact]
    public void EnumValue_UINT8_MAX_BecomesByteMaxValue()
    {
        var cs = GenerateBindings(@"
            typedef NS_CLOSED_ENUM(UInt8, TestEnum) {
                TestEnumA,
                TestEnumMax = UINT8_MAX
            };
        ");

        Assert.Contains("byte.MaxValue", cs);
    }

    [Fact]
    public void EnumValue_NSIntegerMax_BecomesLongMaxValue()
    {
        var cs = GenerateBindings(@"
            typedef NS_ENUM(NSInteger, TestEnum) {
                TestEnumUnknown = NSIntegerMax
            };
        ");

        Assert.Contains("long.MaxValue", cs);
    }

    #endregion

    #region Generic Superclass

    [Fact]
    public void Interface_WithGenericSuperclass_ParsedCorrectly()
    {
        var header = Parse(@"
            @interface MYConfig : MYBaseConfig<MYConfigBuilder *> <MYOverridable>
            @property (nonatomic, readonly) NSInteger pageCount;
            @end
        ");

        Assert.Single(header.Interfaces);
        var iface = header.Interfaces[0];
        Assert.Equal("MYConfig", iface.Name);
        Assert.Equal("MYBaseConfig", iface.Superclass);
        Assert.Contains("MYOverridable", iface.Protocols);
        Assert.Single(iface.Properties);
    }

    #endregion

    #region Category Merging

    [Fact]
    public void Category_MergedIntoParentClass()
    {
        var cs = GenerateBindings(@"
            @interface MYView : NSObject
            @property (nonatomic) NSInteger tag;
            @end
            @interface MYView (SubclassingHooks)
            -(void)updateLayout;
            @end
        ");

        // Category should be merged — no separate [Category] interface
        Assert.DoesNotContain("[Category]", cs);
        Assert.DoesNotContain("MYView_SubclassingHooks", cs);
        // Both the property and the category method appear in one interface
        Assert.Contains("[Export (\"tag\")]", cs);
        Assert.Contains("[Export (\"updateLayout\")]", cs);
    }

    #endregion

    #region Full Header Integration

    [Fact]
    public void ComplexHeader_WithVendorMacros_ParsesCompletely()
    {
        var header = Parse(@"
            typedef NS_CLOSED_ENUM(UInt8, MYActionType) {
                MYActionTypeURL,
                MYActionTypeGoTo,
                MYActionTypeUnknown = UINT8_MAX
            } PSPDF_ENUM_SWIFT(Action.Kind);

            PSPDF_CLASS_SWIFT(Action)
            @interface MYAction : MYModel
            +(nullable Class)actionClassForType:(MYActionType)actionType;
            @property (nonatomic, readonly) MYActionType type;
            @property (atomic, weak, readonly) MYAction *parentAction;
            @property (nonatomic) NSArray<MYAction *> *subActions;
            @property (nonatomic, readonly, nullable) NSDictionary *options;
            -(NSString *)localizedDescriptionWithDocumentProvider:(nullable MYDocumentProvider *)documentProvider;
            @property (nonatomic, readonly) NSString *localizedActionType;
            @end
        ");

        // Enum
        Assert.Single(header.Enums);
        Assert.Equal("MYActionType", header.Enums[0].Name);
        Assert.Equal("UInt8", header.Enums[0].BackingType);
        Assert.Equal(3, header.Enums[0].Values.Count);

        // Interface
        Assert.Single(header.Interfaces);
        var iface = header.Interfaces[0];
        Assert.Equal("MYAction", iface.Name);
        Assert.Equal("MYModel", iface.Superclass);
        Assert.Equal(5, iface.Properties.Count);
        Assert.Single(iface.InstanceMethods);
        Assert.Single(iface.ClassMethods);

        // Nullable properties
        var nullableProps = iface.Properties.Where(p => p.IsNullable).ToList();
        Assert.Single(nullableProps);
        Assert.Equal("options", nullableProps[0].Name);
    }

    #endregion

    #region Macro Heuristic

    [Theory]
    [InlineData("NS_SWIFT_NAME")]
    [InlineData("API_AVAILABLE")]
    [InlineData("CF_ENUM_DEPRECATED")]
    [InlineData("PSPDF_CLASS_SWIFT")]
    [InlineData("OBJC_ROOT_CLASS")]
    [InlineData("SOME_VENDOR_MACRO")]
    public void Heuristic_DetectsUpperSnakeCaseAsMacro(string ident)
    {
        Assert.True(ObjCLexer.IsLikelyMacro(ident));
    }

    [Theory]
    [InlineData("BOOL")]         // known type, no underscore
    [InlineData("SEL")]          // known type
    [InlineData("NULL")]         // known type
    [InlineData("UINT8_MAX")]    // known constant
    [InlineData("INT_MAX")]      // known constant
    [InlineData("NS_ENUM")]      // structural macro
    [InlineData("NS_OPTIONS")]   // structural macro
    [InlineData("NS_CLOSED_ENUM")] // structural macro
    [InlineData("NS_ERROR_ENUM")]  // structural macro
    [InlineData("NS_ASSUME_NONNULL_BEGIN")] // structural macro
    [InlineData("myVariable")]   // lowercase
    [InlineData("NSString")]     // PascalCase
    [InlineData("AB")]           // too short
    public void Heuristic_DoesNotDetectAsMacro(string ident)
    {
        Assert.False(ObjCLexer.IsLikelyMacro(ident));
    }

    [Fact]
    public void Heuristic_SkipsUnknownVendorMacro_WithParens()
    {
        var header = Parse(@"
            SOME_VENDOR_MACRO(SomeSwiftName)
            @interface Foo : NSObject
            @property (nonatomic) int value;
            @end
        ");

        Assert.Single(header.Interfaces);
        Assert.Equal("Foo", header.Interfaces[0].Name);
    }

    [Fact]
    public void Heuristic_SkipsUnknownVendorMacro_WithoutParens()
    {
        var header = Parse(@"
            @interface Foo : NSObject
            MY_REQUIRES_SUPER
            -(void)doWork;
            @end
        ");

        Assert.Single(header.Interfaces);
        Assert.Single(header.Interfaces[0].InstanceMethods);
    }

    [Fact]
    public void Heuristic_PreservesEnumConstants()
    {
        var header = Parse(@"
            typedef NS_ENUM(NSUInteger, TestEnum) {
                TestEnumMax = UINT8_MAX
            };
        ");

        Assert.Single(header.Enums);
        Assert.Equal("UINT8_MAX", header.Enums[0].Values[0].Value);
    }

    [Fact]
    public void Heuristic_Disabled_KeepsAllMacros()
    {
        var opts = new ObjCLexerOptions { MacroHeuristic = false };
        var lexer = new ObjCLexer("NS_SWIFT_NAME(Foo) @interface Bar", opts);
        var tokens = lexer.Tokenize();

        // NS_SWIFT_NAME should be kept as identifier when heuristic is off
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("NS_SWIFT_NAME", tokens[0].Value);
    }

    [Fact]
    public void ExternMacros_CanBeConfigured()
    {
        var opts = new ObjCLexerOptions { ExternMacros = ["MY_EXPORT"] };
        var header = Parse(@"
            MY_EXPORT NSString * const MyKey;
        ", opts);

        Assert.Single(header.Functions);
        Assert.Equal("MyKey", header.Functions[0].Name);
    }

    [Fact]
    public void ExtraSkipMacros_CanBeConfigured()
    {
        var opts = new ObjCLexerOptions { ExtraSkipMacros = ["myWeirdMacro"] };
        var header = Parse(@"
            @interface Foo : NSObject
            myWeirdMacro
            @property (nonatomic) int value;
            @end
        ", opts);

        Assert.Single(header.Interfaces);
        Assert.Single(header.Interfaces[0].Properties);
    }

    #endregion

    #region Preprocessor Directives

    [Fact]
    public void PreprocessorDirective_InProtocolList_IsSkipped()
    {
        var header = Parse(@"
            @interface Foo : NSObject
            <ProtocolA
            #if !TARGET_OS_VISION
            ,ProtocolB
            #endif
            >
            @property (nonatomic) int value;
            @end
        ");

        Assert.Single(header.Interfaces);
        var iface = header.Interfaces[0];
        Assert.Equal("Foo", iface.Name);
        Assert.Contains("ProtocolA", iface.Protocols);
        Assert.Contains("ProtocolB", iface.Protocols);
        Assert.Single(iface.Properties);
    }

    [Fact]
    public void SwiftExtension_CategoryParsedAsCategory()
    {
        var header = Parse(@"
            @interface Foo (SWIFT_EXTENSION(MyModule))
            -(void)doWork;
            @end
        ");

        Assert.Single(header.Interfaces);
        Assert.NotNull(header.Interfaces[0].Category);
        Assert.Single(header.Interfaces[0].InstanceMethods);
    }

    #endregion
}
