# AGENTS.md

## Project Overview

NSSharp is a .NET 10 CLI tool (packaged as a `dotnet tool`) that parses Objective-C header files and produces C# binding definitions (Xamarin.iOS / .NET for iOS style) or structured JSON output. It uses a custom C# lexer and recursive-descent parser — no libclang or native dependencies. The lexer auto-detects vendor macros via an UPPER_SNAKE_CASE heuristic and tracks `NS_ASSUME_NONNULL` scopes for accurate nullability inference.

## Build, Test & Pack

```bash
# Build everything
dotnet build NSSharp.slnx

# Run all tests (189 tests)
dotnet test NSSharp.slnx

# Run the tool during development
dotnet run --project src/NSSharp -- <args>

# Pack as a dotnet tool (.nupkg)
dotnet pack src/NSSharp/NSSharp.csproj -c Release

# Install globally from local package
dotnet tool install -g --add-source src/NSSharp/bin/Release ASTools.NSSharp

# Run as installed tool
nssharp MyHeader.h

# Uninstall
dotnet tool uninstall -g ASTools.NSSharp
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
    Lexer/ObjCLexer.cs                 # Tokenizer (UPPER_SNAKE_CASE macro heuristic)
    Lexer/ObjCLexerOptions.cs          # Lexer config (heuristic, extern macros)
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
    VendorMacroScenarioTests.cs        # Macro heuristic & real-world scenario tests
```

## Architecture

The pipeline is: **Source → Lexer → Tokens → Parser → AST → Serializer/Generator → Output**

- **Lexer** (`ObjCLexer`): Tokenizes ObjC source, auto-detects vendor macros via UPPER_SNAKE_CASE heuristic (configurable via `ObjCLexerOptions`), tracks `NS_ASSUME_NONNULL_BEGIN/END` scopes, handles comments and preprocessor directives.
- **Parser** (`ObjCParser`): Recursive-descent parser producing `ObjCHeader` AST nodes. Handles interfaces, protocols, properties (including block-type properties `void (^name)(params)`), methods, enums (NS_ENUM/NS_OPTIONS/NS_CLOSED_ENUM/NS_ERROR_ENUM/C-style), structs, typedefs, functions, blocks, categories, lightweight generics (including generic superclasses), nullability, extern constants ([Field]). Recognizes `NS_DESIGNATED_INITIALIZER` and `NS_REQUIRES_SUPER` trailing macros. Skips preprocessor directives inside protocol/identifier lists. Handles `SWIFT_EXTENSION(Module)` category names.
- **JSON serializer**: Uses `System.Text.Json` with camelCase naming.
- **Binding generator**: Produces Xamarin-style `[BaseType]`, `[Export]`, `[Protocol]`, `[DesignatedInitializer]`, `[DisableDefaultCtor]`, `[Notification]`, `[Async]`, `[Abstract]` attributed C# interfaces. Emits `I`-prefixed protocol stub interfaces. Applies `[Protocol, Model]` to delegate/datasource protocols. `@required` protocol properties get `[Abstract]` with `[NullAllowed, Export]` combined on one line. `@optional` protocol properties are decomposed into getter/setter method pairs. Category properties decomposed into getter/setter methods (no `[Abstract]`). Smart method naming: strips trailing prepositions (With/At/For/Using/By etc.), strips sender prefix for delegate methods, finds verb parts (did/will/should/can) in multi-part delegate selectors, adds `Get` prefix for getter-style methods, `Create` prefix for static factory methods returning instancetype, and `Get` prefix for static zero-parameter handler methods. Renames `Block`→`Action` in multi-part method names while single-part `*Block` selectors normalize to `*Handler`. Handles `isEqualTo<Class>:` → `IsEqualTo`. Special-cases page-index context (`ForPageAtIndex`/`OnPageAtIndex`) and preserves semantic context for `InContext`/`WithTransform`/`ForEvent`. Trims leading underscores when converting selector parts to PascalCase. Merges ObjC categories into parent classes. Handles `NS_ASSUME_NONNULL` scope-aware nullability, weak→NullAllowed, `out NSError` always NullAllowed. Emits ArgumentSemantic for explicit copy/strong/weak/assign, infers Strong for readwrite object pointers and Assign for readwrite non-primitive value types. `[Field]` for extern constants, `[Notification]` for NSNotificationName. Detects completion handler patterns for `[Async]`. Detects init unavailable macros for `[DisableDefaultCtor]`. Maps ObjC types to C# via `ObjCTypeMapper`. Normalizes acronyms (URL→Url, JWT→Jwt, UID→Uid, XMP→Xmp, ID→Id) in method, property, parameter, and enum member names. Preserves Foundation types in generic params (NSSet<NSString>, NSDictionary<K,V>). Strips ObjC direction qualifiers (out/in/inout), IB annotations, ownership qualifiers (__strong, __weak). Enum prefix stripping with shortened prefix fallback (FooErrorCode values prefixed FooError → stripped). PSPDFKitUI comparison: 88.9% exact match on 550 common exports.

## Namespaces

All code is under the `NSSharp` root namespace:

- `NSSharp` — XCFrameworkResolver
- `NSSharp.Ast` — AST model types
- `NSSharp.Lexer` — Tokenizer, token types, and `ObjCLexerOptions`
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
- `PackageId=ASTools.NSSharp` — NuGet package ID
- `PackageReadmeFile=README.md` — bundled in the .nupkg
- NuGet package: https://www.nuget.org/packages/ASTools.NSSharp/

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
- **UPPER_SNAKE_CASE macro heuristic**: Unknown vendor macros (e.g. `PSPDF_CLASS_SWIFT`, `FB_EXTERN`) are auto-detected and skipped using an UPPER_SNAKE_CASE pattern. Structural macros (`NS_ENUM`, `NS_ASSUME_NONNULL_BEGIN`, etc.) and known types (`BOOL`, `SEL`, etc.) are allowlisted and never skipped. The heuristic is configurable via `ObjCLexerOptions` and `--extern-macros`/`--no-macro-heuristic` CLI flags.
- **NS_ASSUME_NONNULL scope tracking**: The lexer emits `NonnullBegin`/`NonnullEnd` tokens; the parser tracks a `_inNonnullScope` flag and stamps each property/method. Inside the scope, only explicitly nullable types get `[NullAllowed]`; outside, all object pointer types do.
- **Category merging**: ObjC categories (`@interface Foo (Bar)`) are merged into the parent class interface rather than emitting separate `[Category]` interfaces, matching the modern binding convention. `SWIFT_EXTENSION(Module)` categories are recognized and merged.
- **Protocol stub interfaces**: Each `@protocol Foo` emits `interface IFoo {}` before the protocol definition, enabling typed protocol references in bindings.
- **[Protocol, Model] for delegates**: Protocols ending in `Delegate` or `DataSource` automatically get `[Protocol, Model]` + `[BaseType(typeof(NSObject))]`, enabling the event pattern.
- **Default output is C#**: The `--format` option defaults to `csharp`, use `-f json` for JSON.
- **XCFramework slice selection**: `--slice` picks a specific platform slice, `--list-slices` enumerates them.
- **Binding output is a starting point**: Generated C# bindings may need manual adjustment (like Objective Sharpie).
- **Enum prefix stripping**: e.g., `MyStatusOK` → `OK` when the enum is named `MyStatus`. Also handles shortened prefixes: `FooErrorCode` values prefixed `FooError` → stripped.
- **Constructor detection**: Methods starting with `init` become `NativeHandle Constructor(...)`.
- **[DesignatedInitializer]**: `NS_DESIGNATED_INITIALIZER` trailing macros on init methods emit `[DesignatedInitializer]` in the binding.
- **[DisableDefaultCtor]**: Emitted only when init is explicitly marked unavailable via macros (e.g., `PSPDF_EMPTY_INIT_UNAVAILABLE`, `NS_INIT_UNAVAILABLE`). Not inferred from parameterized init presence.
- **Init unavailable macro detection**: The lexer whitelists macros containing `INIT_UNAVAILABLE` or `EMPTY_INIT`, preserving them as identifiers instead of skipping them via the macro heuristic, so the parser can detect them and set `IsInitUnavailable`.
- **Static factory method naming**: Static methods returning `instancetype` use `From<Param>` when the selector matches the class name pattern (e.g., `presetWithColor:` on `PSPDFColorPreset` → `FromColor`), or `Create<Name>` for other factory methods (e.g., `encryptedLibraryWithPath:` → `CreateEncryptedLibrary`).
- **Block→Action renaming**: Method names containing `Block` as a word boundary are renamed to `Action` (e.g., `performBlock:` → `PerformAction`, `performBlockForReading:` → `PerformActionForReading`).
- **isEqualTo pattern**: `isEqualTo<ClassName>:` selectors are simplified to `IsEqualTo` (class name suffix stripped).
- **Acronym normalization**: URL→Url, PDF→Pdf, JWT→Jwt, UID→Uid, XMP→Xmp, ID→Id in method names, property names, parameter names, and enum member names.
- **NS_REQUIRES_SUPER**: Consumed without corrupting selectors (not currently emitted as an attribute).
- **NS_ERROR_ENUM**: Parsed with implicit NSInteger backing type (first param is error domain, not backing type).
- **[Field] for extern constants**: Extern constants (no parameters) always emit `[Field]` attributes in a Constants interface. Functions with parameters emit `[DllImport]` in a CFunctions static class only when `--emit-c-bindings` is passed.
- **Weak → NullAllowed**: Properties declared `weak` are implicitly nullable and always get `[NullAllowed]`.
- **out NSError → NullAllowed**: All `out NSError` parameters always get `[NullAllowed]`, matching sharpie convention.
- **ArgumentSemantic**: Only emitted when explicitly declared in property attributes (copy, strong, retain, weak, assign) or inferred for readwrite object pointers (Strong) and non-primitive value types (Assign). Readonly properties without explicit semantic get no inference.
- **Acronym normalization**: URL→Url, PDF→Pdf, JWT→Jwt, UID→Uid, XMP→Xmp, ID→Id in method names, property names, parameter names, and enum member names.
- **Category property decomposition**: Category properties are decomposed into getter/setter methods (like protocol optional properties) but without `[Abstract]`.
- **Typedef inside protocols**: `typedef NS_OPTIONS(...)` declared inside `@protocol` bodies are parsed and added to the header's enum list.

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
