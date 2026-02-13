---
name: nssharp-objc-parser
description: Parse Objective-C header files into structured AST using the NSSharp tool. Use when working with ObjC headers, generating JSON representations of ObjC APIs, inspecting xcframework contents, or analyzing ObjC type declarations (interfaces, protocols, enums, structs, functions, typedefs, categories, blocks, generics, nullability).
---

# NSSharp ObjC Parser

Parse Objective-C headers into structured JSON or AST using the NSSharp .NET 10 CLI tool. No libclang or native dependencies required. Auto-detects vendor macros via UPPER_SNAKE_CASE heuristic and tracks `NS_ASSUME_NONNULL` scopes.

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

# Specify vendor export macros (treated as extern instead of skipped)
nssharp MyHeader.h --extern-macros PSPDF_EXPORT,FB_EXTERN

# Disable macro heuristic (all UPPER_SNAKE_CASE identifiers kept as-is)
nssharp MyHeader.h --no-macro-heuristic
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
| `--extern-macros` | Comma-separated macros to treat as extern |
| `--emit-c-bindings` | Include C function DllImport declarations in C# output |
| `--no-macro-heuristic` | Disable UPPER_SNAKE_CASE auto-detection |

## Supported ObjC Constructs

- `@interface` (classes, categories, extensions, generics, generic superclasses, SWIFT_EXTENSION)
- `@protocol` (`@required` / `@optional`, I-prefixed stubs, `[Model]` for delegates)
- `@property` (attributes, nullability, custom getter/setter, weak)
- Instance (`-`) and class (`+`) methods
- `NS_ENUM` / `NS_OPTIONS` / `NS_CLOSED_ENUM` / `NS_ERROR_ENUM` / C enums with backing types
- Structs, typedefs, block types
- C function declarations (extern, static, bare) and extern constants
- Forward declarations (`@class`, `@protocol`)
- Nullability annotations (`nullable`, `_Nullable`, `__nullable`, etc.)
- `NS_ASSUME_NONNULL_BEGIN/END` scope tracking
- Vendor macros auto-detected via UPPER_SNAKE_CASE heuristic

## JSON Schema

See [references/json-schema.md](references/json-schema.md) for the complete JSON output schema and AST node types.

## Programmatic Usage in C#

```csharp
using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Ast;
using NSSharp.Json;

var source = File.ReadAllText("MyHeader.h");
var options = new ObjCLexerOptions
{
    MacroHeuristic = true,
    ExternMacros = ["PSPDF_EXPORT"],
};
var lexer = new ObjCLexer(source, options);
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
├── Lexer/ObjCLexer.cs            # Tokenizer (UPPER_SNAKE_CASE macro heuristic)
├── Lexer/ObjCLexerOptions.cs     # Lexer config (heuristic, extern macros)
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

168 tests covering lexer, parser, JSON serializer, binding generator, macro heuristic scenarios, and tests from dotnet/macios sharpie PR #24622.

## Known Limitations

- No full C preprocessor — vendor macros auto-detected via UPPER_SNAKE_CASE heuristic; use `--extern-macros` for export macros
- Enum values with complex expressions preserved as strings, not evaluated
- No C++ support (classes, templates, namespaces)
- No semantic analysis or cross-header type resolution
