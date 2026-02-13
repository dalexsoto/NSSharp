# NSSharp Project Memories

Accumulated learnings and conventions discovered while building NSSharp — an ObjC header parser and C# binding generator.

---

## Build & Test

- **Build**: `dotnet build NSSharp.slnx`
- **Test**: `dotnet test NSSharp.slnx` (168 tests)
- **Run from source**: `dotnet run --project src/NSSharp -- [args]`
- **Install as tool**: `dotnet pack src/NSSharp/NSSharp.csproj -c Release && dotnet tool install -g --add-source src/NSSharp/bin/Release NSSharp`
- The installed tool may be stale (git version hash unchanged); prefer `dotnet run --project src/NSSharp` during development.

---

## Type Mapping

- **`NSArray<Type *>`** maps to typed arrays (`Type []`). For non-collection generics like `UIView<Protocol>`, map to `IProtocol`. Collection types (NSDictionary, NSSet, etc.) are excluded from protocol inference.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:197-230`

- **Block types** map to `Action` (no comment annotation). The `unsafe` keyword check must strip C-style comments before checking for pointer `*` to avoid false positives from block type annotations like `/* block type */`.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:255`, `CSharpBindingGenerator.cs:795-798`

- **`IBAction`** is a macro for `void` — mapped in the primitive type dictionary.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:34`

- **`*Block` typedefs** are renamed to `*Handler` (.NET convention). E.g., `PSPDFAnnotationGroupItemConfigurationBlock` → `PSPDFAnnotationGroupItemConfigurationHandler`.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:215-216`

- **`id < Protocol >`** (with spaces) must be handled — the parsed AST often inserts spaces around `<` and `>`. Strip spaces before matching.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:190-195`

- **Trailing `const`** must be stripped (e.g., `PSPDFDocumentSharingDestination const`). Both leading and trailing `const` qualifiers need handling.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:142-147`

- **Typedef resolution** chains through aliases with cycle detection. `SetTypedefMap()` / `ResolveTypedef()` in ObjCTypeMapper, `BuildTypedefMap()` in CSharpBindingGenerator.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:8-29`

---

## Method Naming

- **Sharpie normalizes acronyms** in method names: `URL` → `Url`, `PDF` → `Pdf`, `HUD` → `Hud`, `HTML` → `Html`, `JSON` → `Json`. Our `PascalCase()` includes `NormalizeAcronyms()` to match.
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:349-383`

- **Sharpie never emits `[Async]` on protocol methods** — only class/interface methods get `[Async]`.
  - *Source*: `src/NSSharp/Binding/CSharpBindingGenerator.cs:789-791`

- **Embedded sender prefix stripping** for single-part protocol selectors: `annotationGridViewControllerDidCancel:` → `DidCancel`. Look for sender suffixes (Controller, View, Manager, etc.) followed by verbs (Did, Will, Should, Can, Get, Set).
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:417-450`

- **Multi-part protocol selectors**: strip the first part if it looks like a sender parameter (ends in Controller/View/Manager/Bar/Cell/Picker/Inspector/Toolbar/Button/Item/Store/Search/HUD/Scrubber/Presenter/Container/Coordinator).
  - *Source*: `src/NSSharp/Binding/ObjCTypeMapper.cs:370-395`

- **Preposition stripping** only strips trailing `Animated` suffix from the first selector part. Generic preposition stripping (`With`/`At`/`For`/`From`/`In`/`On`/`Of`) was removed — it hurt more cases than it helped (15 hurt vs 2 helped in PSPDFKitUI comparison).

- **Get prefix** is added for non-void methods with parameters that don't start with a verb. The verb list includes ~80 verbs (Add, Remove, Get, Set, Did, Will, Should, Can, Is, Has, Show, Hide, Present, Dismiss, etc.).
  - *Source*: `src/NSSharp/Binding/CSharpBindingGenerator.cs:708-745`

---

## Constructor Detection

- **Constructor detection** uses `method.ReturnType.Contains("instancetype")` (not exact match) to handle `nonnull instancetype` and `nullable instancetype` variants.
  - *Source*: `src/NSSharp/Binding/CSharpBindingGenerator.cs:552-558`

- **`instancetype` on static factory methods** resolves to the enclosing class name (e.g., `PSPDFStatusHUDItem` instead of `instancetype`).
  - *Source*: `src/NSSharp/Binding/CSharpBindingGenerator.cs:763-770`

- **Protocol init methods** stay as `instancetype Init(...)` (not constructors) — sharpie comments these out with "must be manually bound".

---

## ArgumentSemantic

- Use **`Strong`** not `Retain`, **`Weak`** not `Assign` — modern ObjC conventions.
- Default to `Strong` for object pointer properties without explicit semantic.
- Value types (enums, structs) don't get a default semantic — sharpie infers `Assign` via clang, which we can't replicate.
  - *Source*: `src/NSSharp/Binding/CSharpBindingGenerator.cs:694-704`

---

## Parser

- **Macro heuristic**: UPPER_SNAKE_CASE tokens are treated as macros and skipped. This handles vendor macros (PSPDF_EXPORT, NS_SWIFT_NAME, etc.) without needing to list them explicitly.
  - *Source*: `src/NSSharp/Lexer/ObjCLexerOptions.cs`

- **`--extern-macros`** flag: for macros like `PSPDF_EXPORT` that appear before `extern` declarations, tell the parser they're export macros so it can parse the extern function/constant correctly.

- **Preprocessor directives** (`#if`, `#endif`) inside protocol conformance lists are skipped to avoid parse failures.
  - *Source*: `src/NSSharp/Parser/ObjCParser.cs`

- **`NS_REQUIRES_SUPER`** is consumed after method declarations without corrupting selectors.

- **`NS_ASSUME_NONNULL_BEGIN/END`** creates a scope — inside, only explicitly nullable types get `[NullAllowed]`; outside, all object pointers get `[NullAllowed]`.

---

## Category Merging

- **Cross-header category merging**: `CSharpBindingGenerator.MergeCategories(headers)` merges ObjC categories into their parent class interfaces across all parsed headers. This includes `SWIFT_EXTENSION` categories.
- When the parent class is not found in parsed headers, the category is emitted with `[Category]` attribute.

---

## Protocol Handling

- Each `@protocol` emits an **`interface IProtocolName {}` stub** before the protocol definition.
- Protocols ending in `Delegate` or `DataSource` get **`[Protocol, Model]`** + `[BaseType(typeof(NSObject))]`.
- **Protocol property decomposition**: `@required` properties stay as C# properties with `[Abstract]`; `@optional` properties are decomposed into getter/setter method pairs.
- Protocol properties with **custom getters** (e.g., `getter=isEnabled`) emit `[Bind("isEnabled")] get;` syntax.
- **Protocol count gap** (96 vs 157 in sharpie): NOT a real gap — sharpie emits `#if NET` / `[Protocol]` / `#else` / `[Protocol, Model]` / `#endif`, which double-counts each protocol. Our 96 is correct.

---

## Comparison with Sharpie (PSPDFKitUI, ~200 headers)

| Metric | Sharpie | NSSharp | Accuracy |
|---|---|---|---|
| `[Export]` | 2013 | 2009 | 100% |
| `[Abstract]` | 151 | 152 | 101% |
| `[Constructor]` | 75 | 72 | 96% |
| `[NullAllowed]` | 587 | 591 | 101% |
| `[Async]` | 19 | 29 | 153% |
| Method naming | — | — | 89% |
| Return type match | — | — | 98% (26 diffs) |
| Param type match | — | — | 90% (146 diffs) |

### Known unfixable gaps

- **35 missing exports**: PSPDFPageView API version diffs (17), builder pattern inheritance (4), EventArgs manual curation (3), UINavigationItem overrides with `[New]` (2), cross-framework (9)
- **`new` modifier** (12 return type diffs): Requires class hierarchy analysis we don't have
- **Cross-framework typedefs** (PSPDFPageIndex → nuint, 22 param diffs): Would need PSPDFKit framework headers (not just PSPDFKitUI)
- **`[Wrap]`** (29 in sharpie, 0 in ours): Requires understanding enum constant mapping
- **`[BindAs]`** attributes: Advanced sharpie feature for enum-backed NSString parameters
- **Named block types** (Action → Action<bool>, 8 diffs): Needs full block signature parsing
- **Forward-declared types** (PSPDFFormSubmissionController → NSObject, 5 diffs): Types from other frameworks

---

## Project Structure

| Path | Purpose |
|---|---|
| `src/NSSharp/Lexer/` | ObjC tokenizer with macro heuristic |
| `src/NSSharp/Parser/` | Recursive descent ObjC parser |
| `src/NSSharp/Ast/` | AST node definitions |
| `src/NSSharp/Binding/` | C# binding generator + type mapper |
| `src/NSSharp/Program.cs` | CLI entry point |
| `src/NSSharp.Tests/` | xUnit tests (168) |
| `DemoFramework/` | PSPDFKitUI.xcframework + sharpie ApiDefinition.cs for comparison |
| `.agents/skills/` | 3 Copilot skills for binding generation |

---

## Tips

- Use `dotnet run --project src/NSSharp -- -f json` for JSON AST output (useful for debugging type issues).
- The comparison script at `/tmp/compare_bindings.py` analyzes attribute counts, missing exports, and naming differences between two C# binding files.
- When debugging type mapping, check the raw type in the JSON AST — nullability qualifiers, spaces, and pointer stars are preserved as-is from the header.
- The `version.json` uses Nerdbank.GitVersioning — the tool version is tied to git history.
