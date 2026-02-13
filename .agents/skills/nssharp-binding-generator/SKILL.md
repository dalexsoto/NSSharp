---
name: nssharp-binding-generator
description: Generate Xamarin.iOS / .NET for iOS C# binding definitions from Objective-C headers using NSSharp. Use when creating API binding definitions, generating [Export], [BaseType], [Protocol] attributed interfaces, mapping ObjC types to C# types, converting ObjC selectors to C# method names, or producing interop code from .h files or xcframeworks.
---

# NSSharp C# Binding Generator

Generate Xamarin/MAUI-style C# binding API definitions from Objective-C headers. Supports category merging, NS_ASSUME_NONNULL scope-aware nullability, weak→NullAllowed inference, [Field] for extern constants, and configurable vendor macro handling.

## Quick Start

```bash
# Build and run during development
dotnet build NSSharp.slnx
dotnet run --project src/NSSharp -- MyHeader.h

# Or install as a dotnet tool
dotnet pack src/NSSharp/NSSharp.csproj -c Release
dotnet tool install -g --add-source src/NSSharp/bin/Release NSSharp

# C# bindings to stdout (default format)
nssharp MyHeader.h

# To file
nssharp MyHeader.h -o ApiDefinition.cs

# From xcframework
nssharp --xcframework MyLib.xcframework -o Bindings.cs

# Specific slice
nssharp --xcframework MyLib.xcframework --slice ios-arm64 -o Bindings.cs

# With vendor export macros
nssharp --xcframework MyLib.xcframework --extern-macros PSPDF_EXPORT -o Bindings.cs

# Include C functions and extern constants in output
nssharp MyHeader.h --emit-c-bindings -o Bindings.cs
```

## Binding Rules

| ObjC Construct | C# Output |
|---|---|
| `@interface Foo : Bar` | `[BaseType(typeof(Bar))] interface Foo` |
| `@interface Foo (Cat)` | Merged into parent `Foo` interface (or `[Category]` if parent not parsed) |
| `@protocol P` | `interface IP {}` stub + `[Protocol] interface P` |
| `@protocol PDelegate` | `interface IPDelegate {}` stub + `[Protocol, Model] [BaseType(typeof(NSObject))] interface PDelegate` |
| Protocol conformance `<P>` | `: IP` interface inheritance |
| `@required` method | `[Abstract] [Export("sel")]` |
| `@optional` method | `[Export("sel")]` (no `[Abstract]`) |
| `@property (copy)` | `ArgumentSemantic.Copy` |
| `@property (readonly)` | `{ get; }` only |
| `@property (nullable)` | `[NullAllowed]` |
| `@property (weak)` | `[NullAllowed]` (implicit) |
| `@property (class)` | `[Static]` |
| Object pointer property | `ArgumentSemantic.Strong` (readwrite default), `ArgumentSemantic.Retain` for explicit retain |
| Class method `+` | `[Static] [Export("sel")]` |
| `-(instancetype)init*` | `NativeHandle Constructor(...)` |
| `NS_DESIGNATED_INITIALIZER` | `[DesignatedInitializer]` on constructors |
| Init unavailable macro | `[DisableDefaultCtor]` (e.g., `PSPDF_EMPTY_INIT_UNAVAILABLE`) |
| Static factory `+classWithParam:` | `From<Param>` (class name match) or `Create<Name>` |
| Block-type property `void (^name)(...)` | Property with `Action` type |
| Method name with `Block` | Renamed to `Action` (e.g., `performBlock:` → `PerformAction`) |
| `isEqualTo<Class>:` | `IsEqualTo` (class name suffix stripped) |
| `NS_ENUM(NSInteger, X)` | `[Native] enum X : long` |
| `NS_OPTIONS(NSUInteger, X)` | `[Flags] enum X : ulong` |
| `NS_CLOSED_ENUM` / `NS_ERROR_ENUM` | Same as `NS_ENUM` |
| `struct` | `[StructLayout(LayoutKind.Sequential)] struct` |
| `extern` function | `[DllImport("__Internal")] static extern` in `CFunctions` class (requires `--emit-c-bindings`) |
| `extern` constant | `[Field("name", "__Internal")]` in `Constants` interface |
| `extern NSNotificationName` | `[Notification] [Field("name")]` |
| Completion handler method | `[Async]` attribute (class methods only, from `completion:` suffix) |
| Protocol `@required @property` | `[Abstract]` + C# property (with `[Bind("isX")]` for custom getters) |
| Protocol `@optional @property` | Decomposed into getter/setter method pairs |
| Protocol `@optional @property (getter=isX)` | Getter uses custom selector, setter uses `setX:` |
| Variadic `...` | `IntPtr varArgs` parameter |
| `NS_ASSUME_NONNULL_BEGIN/END` | Scope-aware `[NullAllowed]` inference |

## Type Mapping

See [references/type-mapping.md](references/type-mapping.md) for the complete ObjC→C# type mapping table (70+ types).

### Key Mappings

| ObjC | C# |
|---|---|
| `NSString *` | `string` |
| `NSInteger` | `nint` |
| `NSUInteger` | `nuint` |
| `CGFloat` | `nfloat` |
| `BOOL` | `bool` |
| `id` | `NSObject` |
| `SEL` | `Selector` |
| `instancetype` | Class name (static methods) or `NativeHandle Constructor` (init) |
| `NSArray *` | `NSObject []` |
| `NSArray<Type *>` | `Type []` (typed arrays) |
| `NSDictionary<K, V>` | `NSDictionary<MappedK, MappedV>` (preserves Foundation types) |
| `NSSet<T>` | `NSSet<MappedT>` (preserves Foundation types) |
| `id<Protocol>` | `IProtocol` |
| `UIView<Protocol>` | `IProtocol` (protocol interface) |
| `IBAction` | `void` |
| `IBInspectable BOOL` | `bool` (IB annotations stripped) |
| `Type **` | `out Type` |
| `NSError **` | `out NSError` + always `[NullAllowed]` |
| Block `(^)(...)` | `Action` |
| `*Block` typedef | `*Handler` (.NET convention) |

## Programmatic Usage

```csharp
using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Binding;

var source = File.ReadAllText("MyHeader.h");
var options = new ObjCLexerOptions
{
    ExternMacros = ["PSPDF_EXPORT"],
};
var tokens = new ObjCLexer(source, options).Tokenize();
var header = new ObjCParser(tokens).Parse("MyHeader.h");

var generator = new CSharpBindingGenerator();
string csharpBindings = generator.Generate(header);

// Include C functions and extern constants:
string withCBindings = generator.Generate(header, emitCBindings: true);

// For xcframeworks, merge categories across headers before generating:
var headers = new List<ObjCHeader> { header1, header2 };
CSharpBindingGenerator.MergeCategories(headers);
```

## Source Files

| File | Purpose |
|---|---|
| `src/NSSharp/Binding/CSharpBindingGenerator.cs` | Main generator (~1078 lines) |
| `src/NSSharp/Binding/ObjCTypeMapper.cs` | Type mapping + selector→method name conversion (~630 lines) |
| `src/NSSharp/Lexer/ObjCLexerOptions.cs` | Lexer config (macro heuristic, extern macros) |

## Key Methods in ObjCTypeMapper

- `MapType(string objcType)` → C# type string
- `MapEnumBackingType(string? objcType)` → C# enum backing type
- `IsNativeEnum(string? objcType)` → whether `[Native]` attribute is needed
- `SelectorToMethodName(string selector, bool isProtocolMethod)` → Smart method name: first part only, strips trailing prepositions, strips sender prefix for protocols (multi-part and embedded single-part), renames Block→Action, handles isEqualTo pattern, normalizes acronyms (URL→Url, PDF→Pdf, UID→Uid, XMP→Xmp, etc.)
- `PascalCase(string name)` → PascalCase conversion with acronym normalization
- `SetTypedefMap(Dictionary<string, string>)` → Sets typedef resolution map
- `ResolveTypedef(string typeName)` → Resolves typedef aliases to base types

## Key Methods in CSharpBindingGenerator

- `Generate(ObjCHeader header)` → full C# binding output (merges categories in-place)
- `MergeCategories(List<ObjCHeader> headers)` → static cross-header category merging
- `IsObjectPointerType(string type)` → checks if type is an object pointer
- `BuildTypedefMap(List<ObjCHeader>)` → builds typedef resolution map from parsed headers

## Notes

- Generated bindings are a starting point; manual review may be needed
- Enum prefix stripping: `MyStatusOK` → `OK` when enum is `MyStatus`
- Constructor detection: methods with `init` prefix returning `instancetype` (including `nonnull instancetype`) become `NativeHandle Constructor(...)`
- `NS_DESIGNATED_INITIALIZER` emits `[DesignatedInitializer]` attribute
- `NS_REQUIRES_SUPER` is consumed without corrupting selectors
- Block-type properties (`void (^name)(params)`) are correctly parsed with the block name extracted
- Properties with `copy`/`strong`/`retain`/`assign`/`weak` get `ArgumentSemantic` annotations
- Object pointer properties without explicit semantic: readwrite infer `ArgumentSemantic.Strong`; readonly get no inference
- Non-primitive value type properties (enums) without explicit semantic: readwrite infer `ArgumentSemantic.Assign`
- `out NSError` parameters always get `[NullAllowed]`
- Category properties are decomposed into getter/setter methods (not C# properties)
- `[DisableDefaultCtor]` only emitted when init is explicitly unavailable via macros (e.g., `PSPDF_EMPTY_INIT_UNAVAILABLE`), not inferred
- The lexer whitelists macros containing `INIT_UNAVAILABLE` or `EMPTY_INIT`, preserving them for the parser
- Static factory methods returning `instancetype` get `Create<Name>` prefix instead of `Get<Name>`
- `Block` in method names is renamed to `Action` at word boundaries (e.g., `performBlock:` → `PerformAction`)
- `isEqualTo<ClassName>:` selectors are simplified to `IsEqualTo` (class name suffix stripped)
- Selector naming special-cases: collapse trailing `ForPageAtIndex` / `OnPageAtIndex`; preserve `InContext`, `WithTransform`, and `ForEvent`
- Weak properties always get `[NullAllowed]`
- Inside `NS_ASSUME_NONNULL` scope: only explicitly nullable types get `[NullAllowed]`; outside scope: all object pointers get `[NullAllowed]`
- ObjC categories are merged into parent class interfaces when the parent is available (including `SWIFT_EXTENSION` categories)
- Each `@protocol` emits an `interface IProtocolName {}` stub before the protocol definition
- Protocols ending in `Delegate` or `DataSource` get `[Protocol, Model]` + `[BaseType(typeof(NSObject))]`
- Protocol properties are decomposed differently based on `@required`/`@optional`:
  - `@required` properties get `[Abstract]` and stay as C# properties with `[Bind("isX")]` for custom getters
  - `@optional` properties are decomposed into getter/setter method pairs (custom getter selectors used when available)
- `NSNotificationName` typed extern constants get `[Notification]` attribute
- Methods with completion handler parameters (ending in `completion:`, `completionHandler:`, `completionBlock:`) get `[Async]` — only on class methods, not protocol methods
- Acronyms in method names are normalized: URL→Url, PDF→Pdf, HUD→Hud, HTML→Html, JSON→Json, UID→Uid, XMP→Xmp
- Typedef aliases are resolved to their base types for correct C# type mapping
- Both leading and trailing `const` qualifiers are stripped during type mapping
- Preprocessor directives (`#if`, `#endif`) inside protocol conformance lists are skipped
- PSPDFKitUI benchmark (current DemoFramework): 88.9% exact export parity vs sharpie (489/550 common exports)
