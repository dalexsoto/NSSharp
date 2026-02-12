# AGENTS.md

## Project Overview

NSSharp is a .NET 10 CLI tool (packaged as a `dotnet tool`) that parses Objective-C header files and produces C# binding definitions (Xamarin.iOS / .NET for iOS style) or structured JSON output. It uses a custom C# lexer and recursive-descent parser — no libclang or native dependencies.

## Build, Test & Pack

```bash
# Build everything
dotnet build NSSharp.slnx

# Run all tests (82 tests)
dotnet test NSSharp.slnx

# Run the tool during development
dotnet run --project src/NSSharp -- <args>

# Pack as a dotnet tool (.nupkg)
dotnet pack src/NSSharp/NSSharp.csproj -c Release

# Install globally from local package
dotnet tool install -g --add-source src/NSSharp/bin/Release NSSharp

# Run as installed tool
nssharp MyHeader.h

# Uninstall
dotnet tool uninstall -g NSSharp
```

Always build from the repo root using `NSSharp.slnx`. Requires .NET 10 SDK (pinned via `global.json`).

## Project Structure

```
global.json                            # .NET 10 SDK version pin
version.json                           # Nerdbank.GitVersioning config (CalVer)
NSSharp.slnx                          # Solution file (XML format, not .sln)
.github/workflows/ci.yml              # CI: build, test, publish to NuGet
src/
  NSSharp/                             # Main CLI tool (dotnet tool)
    NSSharp.csproj                     # PackAsTool=true, ToolCommandName=nssharp
    Program.cs                         # Entry point (System.CommandLine)
    Ast/ObjCNodes.cs                   # AST model types
    Lexer/Token.cs                     # TokenKind enum
    Lexer/ObjCLexer.cs                 # Tokenizer
    Parser/ObjCParser.cs               # Recursive-descent parser
    Json/ObjCJsonSerializer.cs         # JSON serializer
    Binding/ObjCTypeMapper.cs          # ObjC→C# type mapping (70+ types)
    Binding/CSharpBindingGenerator.cs  # C# binding code generator
    XCFrameworkResolver.cs             # XCFramework header discovery
  NSSharp.Tests/                       # xUnit test project
    LexerTests.cs                      # Tokenizer tests
    ParserTests.cs                     # Parser tests
    JsonSerializerTests.cs             # JSON output tests
    SharpieScenarioTests.cs            # Tests from dotnet/macios sharpie PR #24622
    BindingGeneratorTests.cs           # C# binding generation tests
```

## Architecture

The pipeline is: **Source → Lexer → Tokens → Parser → AST → Serializer/Generator → Output**

- **Lexer** (`ObjCLexer`): Tokenizes ObjC source, skips 30+ Apple/NS macros, handles comments and preprocessor directives.
- **Parser** (`ObjCParser`): Recursive-descent parser producing `ObjCHeader` AST nodes. Handles interfaces, protocols, properties, methods, enums (NS_ENUM/NS_OPTIONS/C-style), structs, typedefs, functions, blocks, generics, nullability.
- **JSON serializer**: Uses `System.Text.Json` with camelCase naming.
- **Binding generator**: Produces Xamarin-style `[BaseType]`, `[Export]`, `[Protocol]` attributed C# interfaces. Maps ObjC types to C# via `ObjCTypeMapper`.

## Namespaces

All code is under the `NSSharp` root namespace:

- `NSSharp` — XCFrameworkResolver
- `NSSharp.Ast` — AST model types
- `NSSharp.Lexer` — Tokenizer and token types
- `NSSharp.Parser` — Recursive-descent parser
- `NSSharp.Json` — JSON serialization
- `NSSharp.Binding` — C# binding generator and type mapper

Test namespace: `NSSharp.Tests`

## Coding Conventions

- .NET 10, C# latest, nullable enabled, implicit usings enabled
- File-scoped namespaces (`namespace X;`)
- Primary constructors and collection expressions (`[]`) where appropriate
- Top-level statements in `Program.cs`
- No comments unless clarifying non-obvious logic
- Tests use xUnit with global `using Xunit` (configured in csproj)

## System.CommandLine API

This project uses `System.CommandLine` v2.0.0-beta5.25306.1 which has a **new API** different from older betas:

- `Command.SetAction(Action<ParseResult, CancellationToken>)` — not the old `SetHandler`
- `ParseResult.GetValue(option)` — not `GetValueForOption`
- `new Option<T>(name, aliases...)` with params string[]
- `option.DefaultValueFactory = _ => value` for defaults
- `new CommandLineConfiguration(rootCommand).InvokeAsync(args)`
- Options/Arguments added via `.Options.Add()` / `.Arguments.Add()`

## Dotnet Tool Packaging

NSSharp is packaged as a [dotnet tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools). Key csproj properties:

- `PackAsTool=true` — enables tool packaging
- `ToolCommandName=nssharp` — the CLI command name
- `RollForward=Major` — runs on newer .NET runtimes
- `PackageId=NSSharp` — NuGet package ID
- `PackageReadmeFile=README.md` — bundled in the .nupkg

The `global.json` pins the SDK to `net10.0` with `latestMinor` rollforward.

## Versioning

Uses [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) (NBGV) with CalVer-style release tags:

- **Dev/PR builds**: `0.1.{git-height}-g{commit-hash}` (prerelease from `version.json` base)
- **Tagged releases**: The GitHub release tag becomes the NuGet version (e.g. tag `2026.02.12.1` → NuGet `2026.02.12.1`)
- `version.json` at repo root configures NBGV — `publicReleaseRefSpec: "^refs/tags/*"` makes any tag a public release
- Never set `<Version>` manually in csproj — NBGV handles it

**To release**: Create a GitHub release with a CalVer tag (e.g. `2026.02.12.1`). CI will build, pack with that version, and push to NuGet.

## CI (GitHub Actions)

`.github/workflows/ci.yml` runs on push, PR, and release:

- **Build & Pack** — builds and packs `.nupkg` + `.snupkg`, uploads as artifacts
- **Test** — runs on push/PR (skipped on release), publishes trx test results
- **Publish** — on GitHub release only, pushes to NuGet.org (requires `NUGET_ORG_API_KEY` secret)

Additional csproj CI features:
- `ContinuousIntegrationBuild=true` when `GITHUB_ACTIONS=true` (deterministic builds)
- `Microsoft.SourceLink.GitHub` for debugger source linking
- `.snupkg` symbol packages

## Key Design Decisions

- **No libclang dependency**: The parser is pure C# for portability and simplicity.
- **Default output is C#**: The `--format` option defaults to `csharp`, use `-f json` for JSON.
- **XCFramework slice selection**: `--slice` picks a specific platform slice, `--list-slices` enumerates them.
- **Binding output is a starting point**: Generated C# bindings may need manual adjustment (like Objective Sharpie).
- **Enum prefix stripping**: e.g., `MyStatusOK` → `OK` when the enum is named `MyStatus`.
- **Constructor detection**: Methods starting with `init` become `NativeHandle Constructor(...)`.

## Adding New ObjC Constructs

1. Add AST node to `Ast/ObjCNodes.cs`
2. Add parsing logic in `Parser/ObjCParser.cs`
3. Add JSON serialization support (automatic via `System.Text.Json` if property naming matches)
4. Add binding generation in `Binding/CSharpBindingGenerator.cs` if applicable
5. Add type mappings in `Binding/ObjCTypeMapper.cs` if applicable
6. Add tests in the appropriate test file
7. Run `dotnet test NSSharp.slnx` to verify

## Skills

Two agent skills are available in `.agents/skills/`:

- **nssharp-objc-parser** — Parsing ObjC headers, CLI usage, JSON schema
- **nssharp-binding-generator** — C# binding generation, type mapping, binding rules
