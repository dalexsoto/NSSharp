---
name: nssharp-binding-generator
description: Generate Xamarin.iOS / .NET for iOS C# binding definitions from Objective-C headers using NSSharp. Use when creating API binding definitions, generating [Export], [BaseType], [Protocol] attributed interfaces, mapping ObjC types to C# types, converting ObjC selectors to C# method names, or producing interop code from .h files or xcframeworks.
---

# NSSharp C# Binding Generator

Generate Xamarin/MAUI-style C# binding API definitions from Objective-C headers.

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
```

## Binding Rules

| ObjC Construct | C# Output |
|---|---|
| `@interface Foo : Bar` | `[BaseType(typeof(Bar))] interface Foo` |
| `@interface Foo (Cat)` | `[Category] [BaseType(typeof(Foo))] interface Foo_Cat` |
| `@protocol P` | `[Protocol] interface P` |
| Protocol conformance `<P>` | `: IP` interface inheritance |
| `@required` method | `[Abstract] [Export("sel")]` |
| `@optional` method | `[Export("sel")]` (no `[Abstract]`) |
| `@property (copy)` | `ArgumentSemantic.Copy` |
| `@property (readonly)` | `{ get; }` only |
| `@property (nullable)` | `[NullAllowed]` |
| `@property (class)` | `[Static]` |
| Class method `+` | `[Static] [Export("sel")]` |
| `-(instancetype)init*` | `NativeHandle Constructor(...)` |
| `NS_ENUM(NSInteger, X)` | `[Native] enum X : long` |
| `NS_OPTIONS(NSUInteger, X)` | `[Flags] enum X : ulong` |
| `struct` | `[StructLayout(LayoutKind.Sequential)] struct` |
| `extern` function | `[DllImport("__Internal")] static extern` in `CFunctions` class |
| Variadic `...` | `IntPtr varArgs` parameter |

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
| `instancetype` | kept as-is (constructors become `NativeHandle Constructor`) |
| `NSArray *` | `NSObject[]` |
| `id<Protocol>` | `IProtocol` |
| `Type **` | `out Type` |
| Block `(^)(...)` | `Action` |

## Programmatic Usage

```csharp
using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Binding;

var source = File.ReadAllText("MyHeader.h");
var tokens = new ObjCLexer(source).Tokenize();
var header = new ObjCParser(tokens).Parse("MyHeader.h");

var generator = new CSharpBindingGenerator();
string csharpBindings = generator.Generate(header);
```

## Source Files

| File | Purpose |
|---|---|
| `src/NSSharp/Binding/CSharpBindingGenerator.cs` | Main generator (~400 lines) |
| `src/NSSharp/Binding/ObjCTypeMapper.cs` | Type mapping + selector→method name conversion |

## Key Methods in ObjCTypeMapper

- `MapType(string objcType)` → C# type string
- `MapEnumBackingType(string? objcType)` → C# enum backing type
- `IsNativeEnum(string? objcType)` → whether `[Native]` attribute is needed
- `SelectorToMethodName(string selector)` → PascalCase method name
- `PascalCase(string name)` → PascalCase conversion

## Notes

- Generated bindings are a starting point; manual review may be needed
- Enum prefix stripping: `MyStatusOK` → `OK` when enum is `MyStatus`
- Constructor detection: methods starting with `init` become `NativeHandle Constructor(...)`
- Properties with `copy`/`strong`/`retain`/`assign`/`weak` get `ArgumentSemantic` annotations
