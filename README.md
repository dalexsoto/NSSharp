# NSSharp

A .NET 10 command-line tool that parses Objective-C header files and produces either a **JSON representation** of the API surface or **C# binding definitions** compatible with Xamarin.iOS / .NET for iOS.

Built as a self-contained C# parser — no libclang or native dependencies required.

## Features

- **Custom ObjC lexer & parser** — tokenizes and parses Objective-C headers directly in C#
- **JSON output** — structured JSON representation of all API constructs
- **C# binding generation** — produces Xamarin/MAUI-style `[Export]`/`[BaseType]` binding definitions
- **XCFramework support** — discovers and parses all headers inside `.xcframework` bundles
- **Comprehensive construct support**:
  - `@interface` (classes, categories, extensions)
  - `@protocol` (with `@required` / `@optional`)
  - `@property` (attributes, nullability, custom getter/setter)
  - Instance and class methods
  - `NS_ENUM` / `NS_OPTIONS` / plain C enums (with backing types)
  - Structs and typedefs
  - C function declarations
  - Block types
  - Forward declarations (`@class`, `@protocol`)
  - Lightweight generics (`NSArray<NSString *>`)
  - Nullability annotations (`nullable`, `_Nullable`, `__nullable`, etc.)
- **Macro handling** — recognizes and skips 30+ common Apple/NS macros (`NS_ASSUME_NONNULL_BEGIN`, `API_AVAILABLE`, `__attribute__`, etc.)
- **Packaged as a dotnet tool** — installable via `dotnet tool install`

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Building

```bash
dotnet build
```

## Running

### Generate C# bindings (default)

```bash
# Single header → C# bindings to stdout
dotnet run -- MyHeader.h

# From an xcframework
dotnet run -- --xcframework path/to/MyLib.xcframework

# Write to file
dotnet run -- MyHeader.h -o ApiDefinition.cs

# Multiple headers
dotnet run -- Header1.h Header2.h Header3.h -o Bindings.cs
```

### Parse headers to JSON

```bash
# JSON output
dotnet run -- MyHeader.h -f json

# Compact JSON to file
dotnet run -- MyHeader.h -f json --compact -o output.json

# xcframework to JSON
dotnet run -- --xcframework MyLib.xcframework -f json
```

## CLI Reference

```
nssharp [<files>...] [options]

Arguments:
  <files>          One or more Objective-C header files to parse.

Options:
  --xcframework    Path to an .xcframework bundle to discover and parse all headers.
  --slice          Select a specific xcframework slice (e.g. ios-arm64).
  --list-slices    List available slices in the xcframework and exit.
  -o, --output     Write output to a file instead of stdout.
  -f, --format     Output format: csharp (default) or json.
  --compact        Output compact JSON (only applies to json format).
  -h, --help       Show help and usage information.
  --version        Show version information.
```

## JSON Output Schema

Each header file produces a JSON object:

```json
{
  "file": "MyHeader.h",
  "interfaces": [
    {
      "name": "MyClass",
      "superclass": "NSObject",
      "protocols": ["NSCoding"],
      "category": null,
      "properties": [
        {
          "name": "title",
          "type": "NSString *",
          "attributes": ["nonatomic", "copy"],
          "isNullable": false
        }
      ],
      "instanceMethods": [
        {
          "selector": "initWithTitle:",
          "returnType": "instancetype",
          "parameters": [
            { "name": "title", "type": "NSString *", "isNullable": false }
          ],
          "isOptional": false
        }
      ],
      "classMethods": []
    }
  ],
  "protocols": [
    {
      "name": "MyDelegate",
      "inheritedProtocols": ["NSObject"],
      "properties": [],
      "requiredInstanceMethods": [],
      "requiredClassMethods": [],
      "optionalInstanceMethods": [],
      "optionalClassMethods": []
    }
  ],
  "enums": [
    {
      "name": "MyStatus",
      "backingType": "NSInteger",
      "isOptions": false,
      "values": [
        { "name": "MyStatusOK", "value": "0" },
        { "name": "MyStatusFail" }
      ]
    }
  ],
  "structs": [
    {
      "name": "MyPoint",
      "fields": [
        { "name": "x", "type": "CGFloat" },
        { "name": "y", "type": "CGFloat" }
      ]
    }
  ],
  "typedefs": [
    { "name": "CompletionHandler", "underlyingType": "void (^)(BOOL success)" }
  ],
  "functions": [
    {
      "name": "NSStringFromMyStatus",
      "returnType": "NSString *",
      "parameters": [
        { "name": "status", "type": "MyStatus", "isNullable": false }
      ]
    }
  ],
  "forwardDeclarations": {
    "classes": ["NSData", "NSError"],
    "protocols": []
  }
}
```

When multiple headers are parsed, the output is a JSON array of header objects.

## C# Binding Output

The `--format csharp` mode generates Xamarin.iOS / .NET for iOS binding-style API definitions. Example input:

```objc
@interface MyClass : NSObject <NSCoding>
@property (nonatomic, copy) NSString *title;
@property (nonatomic, readonly) NSInteger count;
+(instancetype)sharedInstance;
-(void)performAction:(NSString *)action withCompletion:(void (^)(BOOL))handler;
@end
```

Generated output:

```csharp
using Foundation;

// @interface MyClass : NSObject
[BaseType (typeof (NSObject))]
interface MyClass : INSCoding
{
    // @property (nonatomic, copy) NSString * title;
    [Export ("title", ArgumentSemantic.Copy)]
    string Title { get; set; }

    // @property (nonatomic, readonly) NSInteger count;
    [Export ("count")]
    nint Count { get; }

    // +(instancetype)sharedInstance;
    [Static]
    [Export ("sharedInstance")]
    instancetype SharedInstance ();

    // -(void)performAction:withCompletion:;
    [Export ("performAction:withCompletion:")]
    void PerformActionWithCompletion (string action, Action handler);
}
```

### Binding generation rules

| ObjC Construct | C# Output |
|---|---|
| `@interface Foo : Bar` | `[BaseType(typeof(Bar))] interface Foo` |
| `@interface Foo (Cat)` | `[Category] [BaseType(typeof(Foo))] interface Foo_Cat` |
| `@protocol P` | `[Protocol] interface P` |
| Protocol conformance `<P>` | `: IP` interface inheritance |
| `@required` methods | `[Abstract] [Export("sel")]` |
| `@optional` methods | `[Export("sel")]` (no `[Abstract]`) |
| `@property` | `[Export("name")] Type Name { get; set; }` |
| `@property (readonly)` | `{ get; }` only |
| `@property (copy)` | `ArgumentSemantic.Copy` |
| `@property (nullable)` | `[NullAllowed]` |
| `@property (class)` | `[Static]` |
| Instance method `-` | `[Export("selector:")]` |
| Class method `+` | `[Static] [Export("selector:")]` |
| `-(instancetype)init*` | `NativeHandle Constructor(...)` |
| `NS_ENUM(NSInteger, X)` | `[Native] enum X : long` |
| `NS_OPTIONS(NSUInteger, X)` | `[Flags] enum X : ulong` |
| C `enum Foo : type` | `enum Foo : mappedType` |
| `struct` | `[StructLayout(LayoutKind.Sequential)] struct` |
| `extern` function | `[DllImport("__Internal")] static extern` |
| Variadic `...` | `IntPtr varArgs` parameter |

### Type mapping

| Objective-C | C# |
|---|---|
| `void` | `void` |
| `BOOL` | `bool` |
| `char` / `signed char` | `sbyte` |
| `unsigned char` | `byte` |
| `short` | `short` |
| `unsigned short` | `ushort` |
| `int` | `int` |
| `unsigned int` | `uint` |
| `long` | `nint` |
| `unsigned long` | `nuint` |
| `long long` | `long` |
| `unsigned long long` | `ulong` |
| `float` | `float` |
| `double` | `double` |
| `NSInteger` | `nint` |
| `NSUInteger` | `nuint` |
| `CGFloat` | `nfloat` |
| `id` | `NSObject` |
| `SEL` | `Selector` |
| `Class` | `Class` |
| `instancetype` | `instancetype` |
| `NSString *` | `string` |
| `NSArray *` | `NSObject[]` |
| `CGColorRef` | `CGColor` |
| `dispatch_queue_t` | `DispatchQueue` |

See `Binding/ObjCTypeMapper.cs` for the full mapping table (70+ types).

## XCFramework Support

The `--xcframework` option discovers headers inside `.xcframework` bundles:

```bash
dotnet run -- --xcframework MyLib.xcframework

# List available slices
dotnet run -- --xcframework MyLib.xcframework --list-slices

# Use a specific slice
dotnet run -- --xcframework MyLib.xcframework --slice ios-arm64
```

The tool:
1. Scans the xcframework for platform slices
2. Prefers the current platform (macOS on macOS, iOS on iOS)
3. Locates the `Headers/` directory inside framework bundles
4. Parses all `.h` files found

## Installing as a dotnet tool

```bash
# Pack
dotnet pack -c Release

# Install globally
dotnet tool install --global --add-source ./bin/Release NSSharp

# Use
nssharp MyHeader.h
nssharp MyHeader.h -o ApiDefinition.cs
nssharp --xcframework MyLib.xcframework -o Bindings.cs
nssharp MyHeader.h -f json -o output.json
```

## AI Agent Skills

NSSharp ships with two [agent skills](https://github.com/anthropics/skills) in `.agents/skills/` that give AI coding assistants (e.g. Claude) specialized knowledge of this tool:

| Skill | Triggers on | What it provides |
|---|---|---|
| **nssharp-objc-parser** | Parsing ObjC headers, JSON output, xcframework analysis | CLI reference, supported constructs, programmatic API, JSON schema |
| **nssharp-binding-generator** | Generating C# bindings, type mapping, interop code | Binding rules, type mapping table (70+ types), selector conversion |

Each skill follows the progressive disclosure pattern — the `SKILL.md` body loads only when triggered, and detailed reference docs (JSON schema, full type mapping) load only when needed.

## Project Structure

```
NSSharp/
├── NSSharp.slnx
├── README.md
├── .agents/skills/
│   ├── nssharp-objc-parser/        # Skill for parsing ObjC headers
│   │   ├── SKILL.md
│   │   └── references/json-schema.md
│   └── nssharp-binding-generator/  # Skill for generating C# bindings
│       ├── SKILL.md
│       └── references/type-mapping.md
└── src/
    ├── NSSharp/
    │   ├── Ast/
    │   │   └── ObjCNodes.cs           # AST model types
    │   ├── Lexer/
    │   │   ├── Token.cs                # Token types and TokenKind enum
    │   │   └── ObjCLexer.cs            # Tokenizer with macro skipping
    │   ├── Parser/
    │   │   └── ObjCParser.cs           # Recursive-descent parser
    │   ├── Json/
    │   │   └── ObjCJsonSerializer.cs   # System.Text.Json serialization
    │   ├── Binding/
    │   │   ├── ObjCTypeMapper.cs       # ObjC→C# type mapping
    │   │   └── CSharpBindingGenerator.cs # C# binding code generation
    │   ├── XCFrameworkResolver.cs      # XCFramework header discovery
    │   ├── Program.cs                  # CLI entry point (System.CommandLine)
    │   └── NSSharp.csproj
    └── NSSharp.Tests/
        ├── LexerTests.cs
        ├── ParserTests.cs
        ├── JsonSerializerTests.cs
        ├── SharpieScenarioTests.cs     # Tests from dotnet/macios sharpie PR
        ├── BindingGeneratorTests.cs
        └── NSSharp.Tests.csproj
```

## Testing

```bash
dotnet test
```

82 tests covering:
- **Lexer**: tokenization, comment skipping, macro handling, number literals
- **Parser**: all ObjC constructs (interfaces, protocols, properties, methods, enums, structs, typedefs, functions, blocks, categories, generics, forward declarations)
- **JSON serializer**: schema correctness, camelCase, compact mode
- **Binding generator**: type mapping, `[Export]`, `[BaseType]`, `[Protocol]`, `[Category]`, constructors, properties, enums, structs, P/Invoke
- **Sharpie scenarios**: 33 tests ported from [dotnet/macios PR #24622](https://github.com/dotnet/macios/pull/24622) test headers

## Limitations

- **No preprocessor**: does not run a full C preprocessor. Common Apple/NS macros are recognized by name and skipped. Unknown macros may cause parse issues.
- **No expression evaluation**: enum values with complex expressions are preserved as strings, not evaluated.
- **No C++ support**: `__cplusplus` guarded code, C++ classes, templates, and namespaces are out of scope.
- **No semantic analysis**: the parser works syntactically. It does not resolve types across headers or validate type correctness.
- **Binding output is a starting point**: generated C# bindings may need manual review and adjustment (similar to Objective Sharpie's `[Verify]` hints).

## License

See repository root for license information.
