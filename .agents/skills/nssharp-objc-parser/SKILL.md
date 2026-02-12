---
name: nssharp-objc-parser
description: Parse Objective-C header files into structured AST using the NSSharp tool. Use when working with ObjC headers, generating JSON representations of ObjC APIs, inspecting xcframework contents, or analyzing ObjC type declarations (interfaces, protocols, enums, structs, functions, typedefs, categories, blocks, generics, nullability).
---

# NSSharp ObjC Parser

Parse Objective-C headers into structured JSON or AST using the NSSharp .NET 10 CLI tool. No libclang or native dependencies required.

## Quick Start

```bash
# Build (from repo root)
dotnet build NSSharp.slnx

# Or install as a dotnet tool
dotnet pack src/NSSharp/NSSharp.csproj -c Release
dotnet tool install -g --add-source src/NSSharp/bin/Release NSSharp

# Parse a header to C# bindings (default) — via dotnet run or installed tool
dotnet run --project src/NSSharp -- MyHeader.h
nssharp MyHeader.h

# Parse to JSON
nssharp MyHeader.h -f json

# Parse to file
nssharp MyHeader.h -f json -o output.json

# Parse xcframework
nssharp --xcframework MyLib.xcframework -f json

# List xcframework slices
nssharp --xcframework MyLib.xcframework --list-slices

# Parse specific slice
nssharp --xcframework MyLib.xcframework --slice ios-arm64 -f json
```

## CLI Options

| Option | Description |
|---|---|
| `<files>...` | One or more .h files |
| `--xcframework <path>` | Parse all headers in xcframework |
| `--slice <name>` | Select xcframework slice |
| `--list-slices` | List available slices and exit |
| `-f, --format` | `csharp` (default) or `json` |
| `-o, --output` | Write to file |
| `--compact` | Compact JSON |

## Supported ObjC Constructs

- `@interface` (classes, categories, extensions, generics)
- `@protocol` (`@required` / `@optional`)
- `@property` (attributes, nullability, custom getter/setter)
- Instance (`-`) and class (`+`) methods
- `NS_ENUM` / `NS_OPTIONS` / C enums with backing types
- Structs, typedefs, block types
- C function declarations (extern, static, bare)
- Forward declarations (`@class`, `@protocol`)
- Nullability annotations (`nullable`, `_Nullable`, `__nullable`, etc.)
- 30+ Apple/NS macros are auto-skipped

## JSON Schema

See [references/json-schema.md](references/json-schema.md) for the complete JSON output schema and AST node types.

## Programmatic Usage in C#

```csharp
using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Ast;
using NSSharp.Json;

var source = File.ReadAllText("MyHeader.h");
var lexer = new ObjCLexer(source);
var tokens = lexer.Tokenize();
var parser = new ObjCParser(tokens);
ObjCHeader header = parser.Parse("MyHeader.h");

// Access AST
foreach (var iface in header.Interfaces)
    Console.WriteLine($"{iface.Name} : {iface.Superclass}");

// Serialize to JSON
string json = ObjCJsonSerializer.Serialize(header, pretty: true);
```

## Project Layout

```
src/NSSharp/
├── Ast/ObjCNodes.cs              # AST model types
├── Lexer/Token.cs                # TokenKind enum
├── Lexer/ObjCLexer.cs            # Tokenizer
├── Parser/ObjCParser.cs          # Recursive-descent parser
├── Json/ObjCJsonSerializer.cs    # JSON serializer
├── Binding/                      # C# binding generator (see nssharp-binding-generator skill)
├── XCFrameworkResolver.cs        # XCFramework header discovery
└── Program.cs                    # CLI entry point
```

## Testing

```bash
dotnet test NSSharp.slnx
```

82 tests covering lexer, parser, JSON serializer, binding generator, and scenarios from dotnet/macios sharpie PR #24622.

## Known Limitations

- No full C preprocessor — common Apple macros recognized by name and skipped
- Enum values with complex expressions preserved as strings, not evaluated
- No C++ support (classes, templates, namespaces)
- No semantic analysis or cross-header type resolution
