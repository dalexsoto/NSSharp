# Generating C# Bindings from Objective-C Headers — Learnings & Design Decisions

This document captures the accumulated knowledge from building NSSharp's ObjC→C# binding generator, including design decisions, hard-won lessons, iteration history, and the nuances of mapping Objective-C API surfaces to Xamarin.iOS / .NET for iOS binding definitions.

## Table of Contents

- [The Binding Problem](#the-binding-problem)
- [Architecture](#architecture)
- [Macro Handling — The Biggest Challenge](#macro-handling--the-biggest-challenge)
- [Nullability Inference](#nullability-inference)
- [Property Mapping](#property-mapping)
- [Method Mapping](#method-mapping)
- [Constructor Detection](#constructor-detection)
- [Protocol Binding](#protocol-binding)
- [Category Merging](#category-merging)
- [Enum Mapping](#enum-mapping)
- [Type Mapping](#type-mapping)
- [Block Types](#block-types)
- [Extern Constants vs Functions](#extern-constants-vs-functions)
- [XCFramework Multi-Header Challenges](#xcframework-multi-header-challenges)
- [Comparison with Objective Sharpie](#comparison-with-objective-sharpie)
- [Known Gaps and Future Work](#known-gaps-and-future-work)

---

## The Binding Problem

Xamarin.iOS / .NET for iOS uses a C# API definition file to describe how Objective-C APIs should be projected into C#. This file contains C# interfaces decorated with attributes like `[BaseType]`, `[Export]`, `[Protocol]`, and `[NullAllowed]` that map 1:1 to ObjC constructs. Historically, Apple's Objective Sharpie (using libclang/LLVM) generated these definitions. NSSharp replaces that with a pure C# parser — no native dependencies, no LLVM, no Xcode required.

The core challenge: ObjC headers are designed for the C preprocessor and clang. Parsing them without running a preprocessor means we must handle macros, conditional compilation, and vendor-specific annotations purely through heuristics and pattern matching.

---

## Architecture

The pipeline is strictly linear and each stage is independently testable:

```
Source text → Lexer → Token stream → Parser → AST → Generator → C# binding code
```

### Why a Custom Parser Instead of libclang

1. **No native dependencies** — runs anywhere .NET 10 runs, no Xcode needed
2. **Inspectable and debuggable** — the entire pipeline is C# code
3. **Fast iteration** — adding support for a new ObjC pattern is a parser change, not an LLVM binding update
4. **Portable** — works on Linux, Windows, macOS without platform-specific toolchain

### Trade-offs Accepted

- No semantic analysis (we don't resolve types across headers)
- No preprocessor evaluation (can't evaluate `#if` conditions)
- No expression evaluation in enum values (kept as strings)
- Must handle macros heuristically rather than expanding them

---

## Macro Handling — The Biggest Challenge

This is where we iterated the most. ObjC headers are packed with vendor macros that have no standard meaning:

```objc
PSPDF_EXPORT @interface PSPDFDocument : NSObject
PSPDF_CLASS_SWIFT(PDFDocument)
@property (nonatomic, copy) NSString * PSPDF_DEPRECATED(12.4, "Use other") title;
FB_INIT_AND_NEW_UNAVAILABLE_MACRO
```

### Iteration 1: Hardcoded Skip List

We started with a `HashSet<string>` of ~50 known macros to skip:

```csharp
private static readonly HashSet<string> s_skipMacros = [
    "PSPDF_EXPORT", "PSPDF_CLASS_SWIFT", "PSPDF_DEPRECATED",
    "FB_INIT_AND_NEW_UNAVAILABLE_MACRO", ...
];
```

**Problem**: Every new framework brought new macros. PSPDFKit alone had 30+ vendor macros. This didn't scale.

### Iteration 2: UPPER_SNAKE_CASE Heuristic

The key insight: virtually all vendor macros follow the `UPPER_SNAKE_CASE` naming convention (e.g., `PSPDF_EXPORT`, `NS_SWIFT_NAME`, `CF_RETURNS_RETAINED`). Actual ObjC type names are PascalCase or camelCase. So we check:

```csharp
internal static bool IsLikelyMacro(string ident)
{
    if (ident.Length < 2) return false;
    // Must have at least one underscore
    if (!ident.Contains('_')) return false;
    // All letters must be uppercase
    return ident.All(c => char.IsUpper(c) || c == '_' || char.IsDigit(c));
}
```

When a likely macro is detected, the lexer:
1. Skips the identifier
2. If followed by `(`, skips balanced parentheses (macro arguments)
3. Restarts tokenization from the next meaningful token

### Structural Macros — The Exception List

Some UPPER_SNAKE_CASE identifiers are NOT skip-worthy — they carry structural meaning:

```csharp
private static readonly HashSet<string> s_structuralMacros = [
    "NS_ENUM", "NS_OPTIONS", "NS_CLOSED_ENUM", "NS_ERROR_ENUM",
    "NS_ASSUME_NONNULL_BEGIN", "NS_ASSUME_NONNULL_END",
    "NS_DESIGNATED_INITIALIZER", "NS_REQUIRES_SUPER",
];
```

These are never auto-skipped. `NS_ENUM`/`NS_OPTIONS` are parsed for enum definitions. `NS_ASSUME_NONNULL_BEGIN/END` are converted to special tokens for nullability scope tracking. `NS_DESIGNATED_INITIALIZER` and `NS_REQUIRES_SUPER` are recognized as trailing method macros.

### Known Uppercase Types

Some ALL_CAPS identifiers are types, not macros:

```csharp
private static readonly HashSet<string> s_knownUppercaseTypes = [
    "BOOL", "SEL", "IMP", "NULL",
    "UINT8_MAX", "UINT16_MAX", "UINT32_MAX", "UINT64_MAX", "UINT_MAX",
    "INT8_MAX", "INT16_MAX", "INT32_MAX", "INT64_MAX", "INT_MAX",
    "CGFLOAT_MAX", "CGFLOAT_MIN",
];
```

### Extern-Mapped Macros

Some macros mean "extern" in disguise (e.g., `PSPDF_EXPORT`, `FB_EXTERN`). These are mapped to the `extern` keyword token:

```csharp
if (ident == "PSPDF_EXPORT" || _externMacros.Contains(ident))
    return new Token(TokenKind.Extern, ident, startLine, startCol);
```

Users specify these via `--extern-macros PSPDF_EXPORT,FB_EXTERN`.

### Lesson Learned

The heuristic approach works remarkably well in practice. Across 200+ PSPDFKitUI headers, it correctly handles all vendor macros without a single false positive on type names. The key was making the heuristic configurable (`--no-macro-heuristic`, `--extern-macros`) so edge cases can be addressed without code changes.

---

## Nullability Inference

Nullability in ObjC bindings is critical — it determines whether `[NullAllowed]` is emitted, which affects the generated C# API surface significantly.

### NS_ASSUME_NONNULL Scope Tracking

Most modern ObjC headers wrap their declarations in:

```objc
NS_ASSUME_NONNULL_BEGIN
// everything here is nonnull by default
NS_ASSUME_NONNULL_END
```

The lexer converts these to `NonnullBegin`/`NonnullEnd` tokens. The parser tracks a `_inNonnullScope` flag and stamps every property and method with `InNonnullScope = true/false`.

### The Rules

The binding generator applies `[NullAllowed]` based on these rules (in priority order):

1. **Explicit nullable annotation** (`nullable`, `_Nullable`, `__nullable`) → always `[NullAllowed]`
2. **Weak property** → always `[NullAllowed]` (weak references are inherently nullable)
3. **Inside NS_ASSUME_NONNULL scope** → only explicitly nullable types get `[NullAllowed]`
4. **Outside NS_ASSUME_NONNULL scope** → all object pointer types get `[NullAllowed]`
5. **Primitive types** (int, float, BOOL, etc.) → never `[NullAllowed]`

### Object Pointer Detection

To determine if a type is an "object pointer type" (and thus needs nullability handling), we check:

```csharp
private static bool IsObjectPointerType(string objcType)
{
    // Contains * → pointer type
    if (t.Contains('*') || t == "id" || t.StartsWith("id<"))
        return true;
    // Common ObjC class prefixes without explicit *
    if (t.StartsWith("NS") || t.StartsWith("UI") || ...)
        return !IsPrimitiveNSType(t); // exclude NSInteger, NSTimeInterval etc.
    return false;
}
```

### Lesson Learned

The `NS_ASSUME_NONNULL` scope tracking was a late addition but had a massive impact. Without it, we were either over-generating `[NullAllowed]` (every object pointer got it) or under-generating it (none did). Testing against real PSPDFKitUI headers showed our NullAllowed accuracy jumped from ~70% to 97% once scope tracking was implemented.

The remaining ~3% gap comes from protocol properties where sharpie decomposes them into separate getter/setter methods with individual NullAllowed annotations — a design difference, not a bug.

---

## Property Mapping

ObjC properties map to C# properties with `[Export]` and `ArgumentSemantic`:

```objc
@property (nonatomic, copy, nullable) NSString *title;
```
→
```csharp
[NullAllowed, Export ("title", ArgumentSemantic.Copy)]
string Title { get; set; }
```

### ArgumentSemantic Rules

| ObjC Attribute | C# Semantic |
|---|---|
| `copy` | `ArgumentSemantic.Copy` |
| `retain` / `strong` | `ArgumentSemantic.Retain` |
| `assign` / `weak` | `ArgumentSemantic.Assign` |
| *(none, object pointer type)* | `ArgumentSemantic.Retain` (default) |
| *(none, primitive type)* | *(omitted)* |

The default `Retain` for object pointers without an explicit semantic was discovered through comparison with sharpie output. Without it, generated bindings would crash at runtime because the managed GC wouldn't maintain the correct retain count.

### readonly Properties

`readonly` properties emit `{ get; }` only (no setter). This is straightforward but important — incorrectly emitting a setter for a readonly property causes a compile-time error in the binding.

### class Properties

`@property (class)` emits `[Static]` before the `[Export]`:

```objc
@property (class, readonly) MyClass *sharedInstance;
```
→
```csharp
[Static]
[Export ("sharedInstance")]
MyClass SharedInstance { get; }
```

### Custom getter/setter

Property attributes like `getter=isEnabled` are captured but not currently translated into the `[Export]` selector. The property name is used as-is. This is a known simplification.

### Block-Type Properties — A Hard-Won Fix

Block-type properties have a unique syntax where the property name is *embedded inside the type declaration*:

```objc
@property (nonatomic, copy, nullable) void (^completionHandler)(BOOL success);
```

The name `completionHandler` sits between `^` and `)`, not after the type. Our initial parser treated the type and name as sequential tokens, which caused a `ParseException` when it encountered `(` instead of an identifier for the property name. This exception was caught by the error-recovery mechanism, which skipped to `@end` — silently losing the *entire class* that contained the block property.

**The fix**: After `ParseType()` consumes the return type (e.g., `void`), check if the next tokens are `(^`:

```csharp
var baseType = ParseType();
if (Check(TokenKind.OpenParen) && PeekNext().Kind == TokenKind.Caret)
{
    Advance(); // '('
    Advance(); // '^'
    prop.Name = Expect(TokenKind.Identifier, "Expected block property name").Value;
    Expect(TokenKind.CloseParen, "Expected ')'");
    // Parse block parameter list into (^)(params)
    prop.Type = $"{baseType} (^)({paramStr})";
}
```

This fix alone recovered 43 exports in PSPDFKitUI (including the entire `PSPDFToolbarButton` class that was silently lost). **Lesson**: Silent parse failures are the worst kind of bug. Any interface containing a single unparseable member was entirely lost due to error recovery skipping to `@end`.

---

## Method Mapping

### Selector to Method Name

ObjC selectors are multi-part: `doThing:withValue:`. The C# method name uses only the **first selector part** (PascalCased), with trailing parameter context stripped:

```
doThing:withValue: → DoThing (not DoThingWithValue)
cancelSearchAnimated: → CancelSearch (strips "Animated")
initWithTitle: → InitWithTitle (but see Constructor Detection below)
```

For **protocol/delegate methods**, if the first selector part matches a sender class name pattern (ends in Controller/View/Manager/Bar/Cell etc.), it is stripped:

```
annotationGridViewController:didSelectAnnotationSet: → DidSelectAnnotationSet
annotationStyleController:didChangeProperties: → DidChangeProperties
```

For **single-part protocol selectors** with an embedded sender prefix followed by a verb (Did/Will/Should/Can/Get/Set), the sender prefix is stripped:

```
annotationGridViewControllerDidCancel: → DidCancel
flexibleToolbarContainerDidShow: → DidShow
documentPickerControllerWillBeginSearch: → WillBeginSearch
```

For **non-void methods with parameters** that don't start with a verb, a `Get` prefix is added:

```
annotationForIndexPath:inTableView: → GetAnnotation
numberOfAnnotationsInSection: → GetNumberOfAnnotations
```

**Acronyms** in method names are normalized to title case:

```
openURL: → OpenUrl
executePDFAction: → ExecutePdfAction
dismissStatusHUD: → DismissStatusHud
```

These rules achieve **89% name match** with Objective Sharpie.

### Parameter Mapping

Each selector part after `:` has a typed parameter:

```objc
- (void)setAnnotation:(PSPDFAnnotation *)annotation forView:(UIView *)view;
```
→
```csharp
[Export ("setAnnotation:forView:")]
void SetAnnotation (PSPDFAnnotation annotation, UIView view);
```

### Return Type Nullability

If a return type contains `nullable`, `_Nullable`, or is an object pointer outside `NS_ASSUME_NONNULL` scope:

```csharp
[return: NullAllowed]
[Export ("annotationForView:")]
PSPDFAnnotation GetAnnotation (UIView view);
```

### Static Methods

Class methods (`+`) get `[Static]`:

```objc
+ (instancetype)sharedInstance;
```
→
```csharp
[Static]
[Export ("sharedInstance")]
instancetype SharedInstance ();
```

---

## Constructor Detection

Methods starting with `init` that return `instancetype` are constructors:

```objc
- (instancetype)initWithTitle:(NSString *)title;
```
→
```csharp
[Export ("initWithTitle:")]
NativeHandle Constructor (string title);
```

The return type becomes `NativeHandle` (Xamarin convention) and the method name becomes `Constructor`.

### NS_DESIGNATED_INITIALIZER

`NS_DESIGNATED_INITIALIZER` appears as a trailing macro after the method signature:

```objc
- (instancetype)initWithTitle:(NSString *)title NS_DESIGNATED_INITIALIZER;
```

This posed two problems:

1. **Lexer level**: The UPPER_SNAKE_CASE heuristic would skip it entirely. Fix: add it to `s_structuralMacros` so the lexer emits it as a regular `Identifier` token.

2. **Parser level**: The selector-collecting loop would consume `NS_DESIGNATED_INITIALIZER` as part of the selector (producing `initWithTitle:NS_DESIGNATED_INITIALIZER`). Fix: add an `IsTrailingMethodMacro()` guard that stops selector collection when these macros are encountered:

```csharp
while ((Check(TokenKind.Identifier) && !IsTrailingMethodMacro(Peek().Value)) || CheckKeywordAsIdentifier())
{
    selectorParts.Append(Advance().Value);
    // ...
}
// After selector collection, consume trailing macros
while (Check(TokenKind.Identifier) && Peek().Value is "NS_DESIGNATED_INITIALIZER" or "NS_REQUIRES_SUPER")
{
    if (Advance().Value == "NS_DESIGNATED_INITIALIZER")
        method.IsDesignatedInitializer = true;
}
```

The generator then emits:

```csharp
[DesignatedInitializer]
[Export ("initWithTitle:")]
NativeHandle Constructor (string title);
```

### NS_REQUIRES_SUPER

`NS_REQUIRES_SUPER` is handled the same way at the parser level — consumed without corrupting the selector. We don't currently emit an attribute for it (there's no direct Xamarin equivalent), but correctly consuming it prevents selector corruption.

---

## Protocol Binding

ObjC protocols map to multiple C# constructs:

### 1. I-Prefixed Stub Interface

Every `@protocol Foo` first emits an empty marker interface:

```csharp
interface IFoo {}
```

This enables typed protocol references in bindings. Without it, you can't write `IFoo` as a parameter type.

### 2. Protocol Definition

```csharp
[Protocol]
interface Foo : IBar, IBaz
{
    // @required methods get [Abstract]
    [Abstract]
    [Export ("requiredMethod")]
    void RequiredMethod ();

    // @optional methods don't
    [Export ("optionalMethod")]
    void OptionalMethod ();
}
```

### 3. Delegate/DataSource Convention

Protocols whose names end with `Delegate` or `DataSource` get special treatment:

```csharp
[Protocol, Model]
[BaseType (typeof (NSObject))]
interface FooDelegate
{
    // ...
}
```

The `[Model]` attribute enables the C# event pattern (allowing `foo.Delegate = new FooDelegate { ... }` without explicit protocol adoption). The `[BaseType(typeof(NSObject))]` is required by the binding generator when `[Model]` is present.

**Design decision**: We use name-suffix heuristic (`Delegate`/`DataSource`) rather than semantic analysis. This matches sharpie's behavior. In PSPDFKitUI, our 64 `[Model]` protocols match sharpie's 63 exactly.

### Protocol Property Decomposition

Protocol properties are handled differently based on `@required` vs `@optional`:

### `@required` Protocol Properties

Required protocol properties stay as C# properties with `[Abstract]` — they MUST be implemented by conforming classes. Custom getters use `[Bind]`:

```csharp
// @required @property (nonatomic, readonly, getter=isRotationActive) BOOL rotationActive;
[Abstract]
[Export ("rotationActive")]
bool RotationActive { [Bind ("isRotationActive")] get; }

// @required @property (nonatomic) PSPDFAnnotation *annotation;
[Abstract]
[Export ("annotation", ArgumentSemantic.Retain)]
PSPDFAnnotation Annotation { get; set; }
```

### `@optional` Protocol Properties

Optional protocol properties are decomposed into separate getter/setter method pairs (matching sharpie's convention). This gives finer control since each accessor can be independently overridden:

```csharp
// @optional @property (nonatomic, getter=isSelected) BOOL selected;
[Export ("isSelected")]
bool GetSelected ();

[Export ("setSelected:")]
void SetSelected (bool selected);

// @optional @property (nonatomic) PSPDFAnnotation *annotation;
[Export ("annotation")]
PSPDFAnnotation GetAnnotation ();

[Export ("setAnnotation:")]
void SetAnnotation ([NullAllowed] PSPDFAnnotation annotation);
```

Note: for `@optional` properties with custom getters (`getter=isX`), the getter export uses the custom selector (`isX`), not the property name.

Regular `@interface` (class) properties are NOT decomposed — they use standard C# property syntax with `{ get; set; }`.

---

## Category Merging

ObjC categories (`@interface Foo (BarAdditions)`) add methods to existing classes. In modern Xamarin binding style, these are merged into the parent interface rather than emitted as separate `[Category]` interfaces.

### Single-Header Merging

When a category and its parent class are in the same header, `MergeCategoriesInPlace()` merges properties, methods, and protocol adoptions from the category into the main class:

```csharp
// Before merging: two interfaces
@interface PSPDFPageView : UIView  // main class
@interface PSPDFPageView (SubclassingHooks)  // category

// After merging: one interface with all members
interface PSPDFPageView : UIView { /* all members combined */ }
```

### Cross-Header Merging (XCFramework)

When parsing an xcframework with hundreds of headers, categories are often in separate files (e.g., `PSPDFPageView+AnnotationMenu.h`). `MergeCategories()` does a cross-header merge:

1. Build an index of all main class interfaces by name
2. For each category, find its parent in the index
3. Merge members into the parent, remove the category interface

### SWIFT_EXTENSION Categories

Swift extensions bridged to ObjC appear as categories with a special name:

```objc
@interface PSPDFDocument (SWIFT_EXTENSION(PSPDFKit))
```

The macro heuristic swallows `SWIFT_EXTENSION(PSPDFKit)`, leaving the parser seeing `()` — an empty category name. We handle this by setting `Category = ""` (empty string, not null), so the merge logic recognizes it as a category and merges it correctly.

### Unresolved Categories

If the parent class isn't found (e.g., it's in another framework), the category is emitted as a standalone `[Category]` interface:

```csharp
[Category]
[BaseType (typeof (ParentClass))]
interface ParentClass_CategoryName
{
    // category members
}
```

---

## Enum Mapping

### NS_ENUM / NS_OPTIONS

```objc
typedef NS_ENUM(NSInteger, PSPDFAnnotationType) {
    PSPDFAnnotationTypeNone = 0,
    PSPDFAnnotationTypeText,
};
```
→
```csharp
[Native]
public enum PSPDFAnnotationType : long
{
    None = 0,
    Text,
}
```

Key behaviors:
- `NS_ENUM` → `[Native]` attribute (because `NSInteger`/`NSUInteger` are pointer-sized)
- `NS_OPTIONS` → `[Flags]` attribute
- `NS_CLOSED_ENUM`, `NS_ERROR_ENUM` → treated same as `NS_ENUM`
- Enum member prefix stripping: `PSPDFAnnotationTypeNone` → `None` (strip the enum name prefix)

### Backing Type Mapping

| ObjC | C# |
|---|---|
| `NSInteger` | `long` (with `[Native]`) |
| `NSUInteger` | `ulong` (with `[Native]`) |
| `int` / `int32_t` | `int` |
| `unsigned int` / `uint32_t` | `uint` |
| *(default)* | `uint` |

### Enum Value Cleaning

Enum values with C-style expressions need cleaning:

```
1 < < 2  →  1 << 2   (token spacing artifact)
UINT32_MAX  →  uint.MaxValue
NSIntegerMax  →  long.MaxValue
```

---

## Type Mapping

The type mapper handles 70+ Objective-C → C# type conversions. Key design decisions:

### Pointer Stripping

Object pointers lose their `*`:

```
NSString *  →  string
NSArray *   →  NSObject[]
UIView *    →  UIView
```

### Double Pointers → out Parameters

```
NSError **  →  out NSError
```

### Primitive Value Pointer → unsafe

```
int *  →  unsafe int*
```

### id and id<Protocol>

```
id                →  NSObject
id<MyProtocol>    →  IMyProtocol
```

### Generics Are Erased

ObjC lightweight generics on collections are handled:

```
NSArray<PSPDFAnnotation *>  →  PSPDFAnnotation []   (typed array)
NSArray<NSString *>         →  string []
NSDictionary<NSString *, id>  →  NSDictionary
```

For non-collection generic types (e.g., `UIView<Protocol>`), the protocol interface is used:

```
UIView<PSPDFAnnotationPresenting>  →  IPSPDFAnnotationPresenting
```

### const char * → string

```
const char *  →  string   (C string to managed string)
char *        →  unsafe sbyte*   (mutable C string, kept as pointer)
```

### Block Types

Block types are simplified to `Action`:

```
void (^)(BOOL success)  →  Action /* block type */
```

A full implementation would parse block signatures and map to `Action<bool>`, `Func<string, bool>`, etc. This is a known simplification.

---

## Block Types

Block types appear in two contexts:

### 1. Block Properties

```objc
@property (nonatomic, copy) void (^completionHandler)(BOOL success);
```

These have a unique syntax where the name is inside the type declaration. See [Block-Type Properties](#block-type-properties--a-hard-won-fix) above.

### 2. Block Method Parameters

```objc
- (void)fetchData:(void (^)(NSData *data, NSError *error))completion;
```

These are handled by `ParseTypeInsideParens()` which captures the entire block type as a string. The type mapper then converts it to `Action`.

### Block Types with Non-Void Return

```objc
@property (nonatomic, copy) NSArray<UIColor *> * (^colorChoices)(PSPDFAnnotation *annotation);
```

The parser handles this correctly — `ParseType()` consumes `NSArray<UIColor *> *`, then sees `(^` and switches to block property parsing. The resulting type is `NSArray<UIColor *> * (^)(PSPDFAnnotation * annotation)`.

---

## Extern Constants vs Functions

Extern declarations in ObjC headers can be constants or functions:

```objc
// Constant (no parameters)
extern NSString * const PSPDFDocumentDidSaveNotification;

// Function (has parameters)
extern NSString * NSStringFromAnnotationType(PSPDFAnnotationType type);
```

### Design Decision: Split Emission

- **Constants** → `[Field]` attributes in a `Constants` interface. **Always emitted.**
- **Functions** → `[DllImport]` in a `CFunctions` static class. **Only with `--emit-c-bindings` flag.**

The rationale: constants are essential for bindings (notifications, keys, etc.) and are safe to reference. Functions require more careful handling (calling conventions, marshaling) and are typically only needed for advanced use cases.

The detection heuristic is simple: if the function has zero parameters AND its return type contains `const`, it's a constant. Everything else is a function.

```csharp
[Static]
interface Constants
{
    [Notification]
    [Field ("PSPDFDocumentDidSaveNotification")]
    NSString DocumentDidSaveNotification { get; }
}
```

Constants with `NSNotificationName` type or names ending in `Notification` automatically get the `[Notification]` attribute. This generates convenience `ObserveXxx()` methods in the binding output.

### Completion Handler Detection ([Async])

Methods whose last selector part ends with `completion:`, `completionHandler:`, or `completionBlock:` automatically get the `[Async]` attribute, which generates a `Task`-returning async wrapper:

```csharp
[Async]
[Export ("fetchDataWithCompletion:")]
void FetchData (Action<NSData, NSError> completion);
// Generated: Task<NSData> FetchDataAsync()
```

---

## XCFramework Multi-Header Challenges

Parsing a single header is relatively straightforward. Parsing 200+ headers from an xcframework introduces several additional challenges:

### Header Discovery

The `XCFrameworkResolver` navigates the xcframework bundle structure:

```
MyLib.xcframework/
  ios-arm64/
    MyLib.framework/
      Headers/
        MyLib.h          ← umbrella header
        Class1.h
        Class2.h
        ...
```

We parse ALL `.h` files in the `Headers/` directory, not just the umbrella header. The umbrella header typically just `#import`s everything else.

### Slice Selection

XCFrameworks contain multiple platform slices (`ios-arm64`, `ios-arm64_x86_64-simulator`, `ios-arm64_x86_64-maccatalyst`). By default we prefer `ios-arm64`; the `--slice` option allows explicit selection.

### Cross-Header Category Merging

See [Category Merging](#category-merging) above. This is the most complex part of multi-header processing.

### Preprocessor Directives in Protocol Lists

Some headers have conditional compilation inside protocol conformance lists:

```objc
@interface PSPDFAnnotationStateManager : NSObject <
    PSPDFOverridable
#if !TARGET_OS_VISION
    , PSPDFAnnotationStateManagerDelegate
#endif
>
```

The lexer emits `#if`/`#endif` as `PreprocessorDirective` tokens. The parser's `ParseIdentifierList` was updated to skip these:

```csharp
private List<string> ParseIdentifierList(TokenKind terminator)
{
    while (!Check(terminator) && !IsAtEnd())
    {
        // Skip preprocessor directives inside identifier lists
        if (Match(TokenKind.PreprocessorDirective))
            continue;
        // ... collect identifiers
    }
}
```

Without this fix, the entire `PSPDFAnnotationStateManager` class was lost due to a parse failure.

---

## Comparison with Objective Sharpie

### PSPDFKit.xcframework (current test target)

Tested against sharpie's output for PSPDFKit.xcframework (214 headers, 1583 common exports across 218 common interfaces). After 5 iterations of comparison and improvement:

| Metric | Count | Accuracy |
|---|---|---|
| Common exports | 1583 | — |
| Exact match | 1278 | **80.7%** |
| With diffs | 305 | 19.3% |

Diff categories (remaining):

| Category | Count | Notes |
|---|---|---|
| NAME | 120 | Method/property naming differences |
| SEMANTIC | 94 | ArgumentSemantic inconsistencies (sharpie may be manually edited) |
| PARAMS | 220 | Block types (Action vs typed delegates), param naming |
| RETURN_TYPE | 41 | `new` keyword, generic collection types |
| PROP_TYPE | 37 | Block handler types, NSString vs string |
| MISSING_EXPORT | 30 | Configuration builder patterns, cross-framework |
| EXTRA_EXPORT | 50 | NSSharp emits methods sharpie doesn't |
| NULLALLOWED | 24 | Scope inconsistencies |
| ASYNC | 17 | Detection differences |
| ACCESSORS | 15 | Getter/setter differences |

**Progress through iterations:**
- Baseline: 76.4% (1209/1583)
- After iteration 1 (semantic, verb prefixes, protocol naming): ~78%
- After iteration 2 (factory From naming, bool return): 79.5%
- After iteration 3 (verb word boundary, protocol property verb): 79.7%
- After iteration 4 (DisableDefaultCtor, init unavailable macros): 79.7%
- After iteration 5 (UID/XMP acronyms, Block→Action, isEqualTo, Create prefix): **80.7%**

### Instant.xcframework (historical test target)

Tested against sharpie's output for Instant.xcframework (9 headers, 60 exports). On the 55 common exports:

| Metric | Result |
|---|---|
| Method naming | **100%** (55/55) |
| Return types | **0 diffs** |
| Parameter types | **0 diffs** |
| Parameter names | **0 diffs** |

Attribute comparison:

| Metric | Sharpie | NSSharp | Accuracy |
|---|---|---|---|
| `[Export]` | 60 | 57 | 95% (4 missing: 2 cross-framework constructors, 2 EventArgs keys) |
| `[Abstract]` | 32 | 32 | 100% ✓ |
| `[BaseType]` | 5 | 5 | 100% ✓ |
| `[Notification]` | 9 | 9 | 100% ✓ |
| `[NullAllowed]` | 34 | 32 | 94% (2 from cross-framework constructors) |
| enums | 4 | 4 | 100% ✓ |
| interfaces | 13 | 13 | 100% ✓ |

Remaining gaps are structural (cross-framework inheritance, Notification EventArgs).

### PSPDFKitUI (historical test target, ~200 headers)

| Metric | Sharpie | NSSharp | Accuracy |
|---|---|---|---|
| `[Export]` | 2013 | 2009 | 100% |
| `[Abstract]` | 151 | 152 | 101% |
| `[Constructor]` | 75 | 72 | 96% |
| `[DesignatedInitializer]` | 34 | 37 | 109% |
| `[NullAllowed]` | 587 | 591 | 101% |
| `[BaseType]` | 268 | 272 | 101% |
| `[Field]` | 117 | 136 | 116% |
| `[Notification]` | 20 | 28 | 140% |
| `[Async]` | 19 | 29 | 153% |
| interfaces | 410 | 429 | 105% |
| Method naming | — | — | 89% |
| Return type match | — | — | 98% (26 diffs) |
| Param type match | — | — | 90% (146 diffs) |

### Why We Have *More* of Some Things

- **interfaces**: We emit `I`-prefixed protocol stubs that sharpie doesn't count separately
- **BaseType**: We emit BaseType for every interface; sharpie omits it on some protocol stubs
- **DesignatedInitializer**: We detect it in headers that sharpie doesn't bind (cross-framework classes)
- **NullAllowed**: Slightly more than sharpie outside `NS_ASSUME_NONNULL` scope
- **Field**: We detect all extern constants; sharpie skips some
- **Notification**: We detect all `NSNotificationName` typed constants; sharpie is more selective
- **Async**: We detect all completion handler patterns in class methods; sharpie is more conservative
- **Abstract**: Required protocol properties now correctly get `[Abstract]`

### Where Sharpie Wins

| Feature | Sharpie | NSSharp | Gap Reason |
|---|---|---|---|
| `[Wrap]` | 29 | 0 | Convenience wrapper generation for strongly-typed alternatives |
| `Constructor` | 75 | 56 | Sharpie adds parameterless `init` constructors explicitly; we don't synthesize inherited constructors |
| Builder pattern | ~35 exports | 0 | Inherited from `PSPDFBaseConfiguration` in another framework |

### Exports Missing Because of Design Differences

- **Protocol property decomposition**: Now implemented with required/optional distinction — `@required` properties get `[Abstract]` and stay as C# properties, `@optional` properties are decomposed into getter/setter method pairs.
- **Inherited constructors**: Sharpie explicitly adds `init` constructors inherited from superclasses. We only emit constructors declared in the current header.
- **Cross-framework inheritance**: Methods from parent classes in other frameworks (e.g., PSPDFKit vs PSPDFKitUI) are not duplicated in our output.

---

## Known Gaps and Future Work

### Not Yet Implemented

| Feature | Description | Difficulty |
|---|---|---|
| `[Wrap]` | Generate convenience methods for strongly-typed dictionary alternatives | Hard (needs semantic knowledge) |
| Typed `[Notification(typeof(EventArgs))]` | Generate EventArgs interfaces from associated `*Key` constants | Medium |
| Parameterless constructors | Synthesize `init` constructors for classes that don't explicitly declare one | Easy but risky (some classes have `NS_UNAVAILABLE` on init) |
| Full block type mapping | Map `void (^)(BOOL)` → `Action<bool>`, `NSString * (^)(NSError *)` → `Func<NSError, string>` | Medium |
| Keys/Options typed dictionaries | Generate `[Static] interface XxxKeys` wrappers | Hard (sharpie-specific pattern) |

### Parser Robustness

- **Silent parse failures**: When a `ParseException` is thrown inside `ParseInterface`, error recovery skips to `@end`, losing the entire class. Any unparseable member kills the whole interface. Consider more granular recovery (skip to next `;` within the interface body).
- **Duplicate member detection**: When categories are merged, duplicate selectors can appear. Currently no deduplication.
- **Expression evaluation**: Enum values like `1 << 3 | 1 << 4` are kept as strings. A simple expression evaluator could compute numeric values.

### Type Mapping Improvements

- Block types should be fully decomposed into `Action<T1, T2>` / `Func<T1, T2, TResult>` (currently all map to `Action`)
- Cross-framework typedef resolution (PSPDFPageIndex → nuint) requires multi-framework header parsing
- `new` modifier for methods that shadow base class members requires class hierarchy analysis
- `[BindAs]` attribute for enum-backed NSString parameters (PSPDFAnnotationString → AnnotationType)

---

## Summary of Key Lessons

1. **Macro heuristics beat macro lists.** The UPPER_SNAKE_CASE heuristic handles thousands of vendor macros without maintenance. Start with heuristics, add escape hatches for edge cases.

2. **Silent failures are the worst bugs.** Block-type properties caused silent loss of entire classes. Always log or surface parse failures, even in error-recovery paths.

3. **Nullability scope tracking is essential.** Without `NS_ASSUME_NONNULL` scope awareness, nullability accuracy drops dramatically. This single feature improved NullAllowed accuracy from ~70% to 97%.

4. **Category merging is non-trivial.** Cross-header merging, SWIFT_EXTENSION categories, and unresolvable parent classes all require special handling.

5. **Test against real frameworks.** Unit tests catch syntax issues; real-world frameworks catch design issues. The PSPDFKitUI comparison revealed dozens of gaps that synthetic tests never would have.

6. **Trailing macros corrupt selectors.** `NS_DESIGNATED_INITIALIZER`, `NS_REQUIRES_SUPER`, and similar trailing macros must be explicitly guarded against in the selector-collecting loop, or they become part of the selector string.

7. **Properties are not just "type + name".** Block-type properties embed the name inside the type syntax. Property getter/setter attributes change the export selector. `class` properties need `[Static]`. Every property attribute has binding implications.

8. **Protocol properties differ from class properties.** `@required` protocol properties get `[Abstract]` and stay as C# properties (with `[Bind("isX")]` for custom getters). `@optional` protocol properties are decomposed into getter/setter method pairs because each accessor can be independently overridden. Class properties use standard C# `{ get; set; }`. This distinction was discovered by comparing against sharpie's output for PSPDFPresentationContext (required) vs PSPDFAnnotationPresenting (optional).

9. **The binding is a starting point.** Even sharpie's output requires manual review. Our output is no different. The goal is to get 95%+ correct and let the developer fix the rest.

10. **Method naming is the biggest quality factor.** Simply concatenating all selector parts (the naive approach) produces 512 naming differences vs sharpie. Smart naming — using first part only, stripping trailing prepositions (With/At/For...), stripping delegate sender prefixes (both multi-part and embedded single-part), adding Get for getter-like parameterized methods, normalizing acronyms (URL→Url, PDF→Pdf, HUD→Hud, HTML→Html, JSON→Json) — achieves 89% match rate. The selector-to-name conversion needs context (is this a protocol method? what's the return type?) to produce good names.

11. **Use `Strong` not `Retain`, `Weak` not `Assign`.** Modern ObjC uses `strong`/`weak`; the binding should use `ArgumentSemantic.Strong`/`Weak` accordingly. Default to `Strong` for object pointer properties without explicit semantic.

12. **Normalize acronyms in method names.** Multi-letter acronyms like URL, PDF, HUD, HTML, JSON should be title-cased (Url, Pdf, Hud, Html, Json) in PascalCase method names. This matches sharpie's convention and standard .NET naming guidelines.

13. **Strip embedded sender prefix in single-part protocol selectors.** Selectors like `annotationGridViewControllerDidCancel:` contain the sender type embedded in the name. Look for known suffixes (Controller, View, Manager, etc.) followed by a verb (Did, Will, Should, Can, Get, Set) and strip everything up to the verb. Result: `DidCancel`.

14. **Handle `nonnull instancetype` in constructor detection.** ObjC headers often use `-(nonnull instancetype)init*`. The `nonnull` qualifier is preserved in the AST's return type string. Constructor detection must use `Contains("instancetype")` instead of exact match to catch these variants.

15. **Never emit `[Async]` on protocol methods.** Sharpie only adds `[Async]` to class/interface methods with completion handlers, never to protocol method declarations. Protocol implementations should manually add `[Async]` if desired.

16. **Handle trailing `const` in type mapping.** ObjC extern constants often have types like `PSPDFDocumentSharingDestination const` with `const` at the end. Strip both leading and trailing `const` qualifiers before type resolution.

17. **`IBAction` is `void`.** ObjC's `IBAction` return type is a macro for `void`. Map it accordingly.

18. **Block types should not trigger `unsafe`.** When block parameters produce `Action` (or block type comments), don't let the `*` inside comments like `/* block type */` falsely trigger `unsafe` keyword. Strip comments before checking for pointer types.

19. **`instancetype` on static factory methods resolves to the enclosing class.** A `+(instancetype)` method returns the class type itself. For non-init static methods, resolve `instancetype` to the enclosing class name (e.g., `PSPDFStatusHUDItem` instead of `instancetype`).

20. **`NSArray<Type *>` maps to `Type []` (typed arrays).** When ObjC uses lightweight generics like `NSArray<PSPDFAnnotation *>`, extract the element type and emit `PSPDFAnnotation []` instead of `NSObject []`. Note the space before `[]` matching sharpie convention.

21. **`id<Protocol>` and `Class<Protocol>` map to `IProtocol`.** Handle spaces in `id < Protocol >` (common in parsed AST). For `UIView<PSPDFAnnotationPresenting>`, map to `IPSPDFAnnotationPresenting` (the protocol interface), not `UIView`. Don't apply this to collection types (NSDictionary, NSSet).

22. **Rename `*Block` typedefs to `*Handler`.** .NET convention prefers `Handler` suffix for callback types. Transform `PSPDFAnnotationGroupItemConfigurationBlock` → `PSPDFAnnotationGroupItemConfigurationHandler`.

23. **`out NSError` parameters always get `[NullAllowed]`.** In Xamarin bindings, ALL `NSError **` (out) parameters should get `[NullAllowed]`, even inside `NS_ASSUME_NONNULL` scope. This applies to both regular methods AND constructors.

24. **ObjC direction qualifiers must be stripped from types.** `out`, `in`, `inout`, `bycopy`, `byref`, `oneway` are ObjC parameter direction qualifiers that appear at the start of type strings. Strip them before type mapping to avoid double `out` (ObjC `out` + C# `out` for double pointers).

25. **Delegate method naming: find the verb part.** For selectors like `instantClient:documentDescriptor:didFailDownloadWithError:`, after stripping the sender (`instantClient`), search ALL remaining parts for a verb prefix (`did`, `will`, `should`, `can`). Don't just use the first remaining part (`documentDescriptor`). Result: `DidFailDownload`.

26. **Sender suffix list must be comprehensive.** Beyond Controller/View/Manager, include: Client, Provider, Service, Handler, Source, Session, Connection, Cache. Missing a sender suffix means the first part isn't stripped in delegate method naming.

27. **Preposition stripping list must be comprehensive.** Beyond With/At/For/From/In/On/Of, include: Using, By, To. Missing `Using` causes `downloadUsingJWT` to not strip to `Download`.

28. **`NS_ERROR_ENUM` has different parameter order.** `NS_ERROR_ENUM(ErrorDomain, EnumName)` — first param is the error domain, NOT the backing type. The backing type is implicitly `NSInteger`. Parse accordingly.

29. **Typedef inside `@protocol` bodies.** `typedef NS_OPTIONS(...)` can be declared inline inside protocol bodies. Parse them and add to the header's enum list, not the protocol.

30. **Preserve Foundation types in generic parameters.** `NSDictionary<NSString *, NSSet<id<Protocol>> *>` → `NSDictionary<NSString, NSSet<IProtocol>>`. Inside generic params, use `NSString` not `string`, because generic constraints require `NSObject` subclasses.

31. **ArgumentSemantic: only emit when explicitly declared.** Don't infer `Strong` for object pointers by default. Only emit when the property has explicit `copy`/`strong`/`retain`/`weak`/`assign`. For non-primitive value types (enums), infer `Assign`.

32. **Category properties should be decomposed into methods.** Unlike regular class properties, category properties should be emitted as getter/setter methods (like optional protocol properties) without `[Abstract]`. ObjC categories can't add stored properties.

33. **`IBInspectable` and `IBOutlet` are type qualifiers to strip.** These Interface Builder annotations appear in type strings and should be removed before type mapping.

34. **Enum prefix stripping with shortened prefixes.** When enum values don't match the full enum name as prefix, try shortened forms: strip common suffixes (Code, Type, Kind, Status, Style, Mode, State, Options, Flag) from the enum name. `PSPDFInstantErrorCode` values prefixed `PSPDFInstantError` → strip `PSPDFInstantError`.

35. **Normalize acronyms in enum member names.** After prefix stripping, apply the same acronym normalization (URL→Url, etc.) to enum member names. `InvalidURL` → `InvalidUrl`.

36. **`[DisableDefaultCtor]`** should only be emitted when init is EXPLICITLY marked unavailable via macros (e.g., `PSPDF_EMPTY_INIT_UNAVAILABLE`, `NS_INIT_UNAVAILABLE`). Don't infer from parameterized init presence — many classes with parameterized inits still support `init`.

37. **Init unavailable macro detection requires lexer cooperation.** The lexer's UPPER_SNAKE_CASE macro heuristic skips macros like `PSPDF_EMPTY_INIT_UNAVAILABLE` before the parser sees them. Fix: whitelist macros containing `INIT_UNAVAILABLE` or `EMPTY_INIT` in the lexer, preserving them as identifiers.

38. **Static factory methods get `Create` prefix, not `Get`.** Static methods returning `instancetype` that aren't covered by the `From<Param>` pattern should use `CreateXxx` naming (e.g., `encryptedLibraryWithPath:` → `CreateEncryptedLibrary`), not `GetEncryptedLibrary`.

39. **`performBlock:` → `PerformAction`.** The word `Block` in method names should be renamed to `Action` at word boundaries (e.g., `performBlockForReading:` → `PerformActionForReading`). This matches .NET naming conventions.

40. **`isEqualTo<ClassName>:` → `IsEqualTo`.** ObjC's `isEqualTo<Type>:` equality methods strip the class name suffix, keeping just `IsEqualTo`. This is a well-known NSObject pattern.

41. **`UID` → `Uid`, `XMP` → `Xmp`.** Add to the acronym normalization table alongside URL/PDF/JSON/etc. These appear frequently in PSPDFKit APIs.

42. **Preposition stripping: `To` is part of the method name.** Unlike `With`/`At`/`For` which indicate parameter context, `To` is usually semantically meaningful (e.g., `BindToObjectLifetime`, `ConvertIntentTypeTo`). Don't strip it.

43. **Verb search after sender stripping must check single remaining parts.** After stripping the delegate sender, if only one part remains, it may still contain a verb prefix (e.g., `didBeginSyncForDocumentDescriptor`). Search for verb prefixes in ALL remaining parts, not just when ≥2 parts remain.

44. **ArgumentSemantic for readwrite object pointers: infer `Strong`.** Readwrite properties with object pointer types but no explicit semantic should get `ArgumentSemantic.Strong` (sharpie convention for PSPDFKit). Readonly properties get no inference. Explicit `copy`/`strong`/`weak`/`assign` always emitted regardless of readonly status.

45. **Sharpie's output may be manually edited.** The reference ApiDefinition.cs for PSPDFKit shows inconsistencies in ArgumentSemantic, naming, and attribute application. Some differences may not be sharpie's raw output but post-generation manual edits. Target 80%+ match, not 100%.
