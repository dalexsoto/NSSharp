---
name: objc-to-csharp-binding
description: >
  Generate Xamarin.iOS / .NET for iOS C# binding API definitions from Objective-C headers by hand.
  Use when translating ObjC @interface, @protocol, @property, methods, enums, structs, typedefs,
  extern constants, blocks, and categories into C# [BaseType], [Export], [Protocol], [Model],
  [NullAllowed], [Field], [DesignatedInitializer], [Notification] attributed interfaces.
  Covers the complete dotnet/macios binding contract including type mapping, nullability,
  ArgumentSemantic, constructor patterns, delegate/event generation, and category handling.
---

# Objective-C → C# Binding Definition Skill

This skill encodes the complete knowledge needed to translate Objective-C header declarations into
Xamarin.iOS / .NET for iOS C# binding API definitions (the `ApiDefinition.cs` contract format).

The binding contract uses C# **interfaces** (not classes) decorated with attributes. The binding
generator (btouch / generator) turns these interfaces into actual classes at build time.

## File Structure

A binding project has two key files:

| File | Contents |
|---|---|
| `ApiDefinition.cs` | C# interfaces with `[BaseType]`, `[Export]`, `[Protocol]` attributes — the API contract |
| `StructsAndEnums.cs` | Enums, structs, delegates, and supporting type definitions |

**Rule**: `ApiDefinition.cs` contains ONLY interfaces and delegate declarations. All enums, structs, and
other value types go in `StructsAndEnums.cs`.

---

## Class Binding (`@interface`)

### Basic Class

```objc
@interface MyClass : NSObject
@end
```

```csharp
[BaseType (typeof (NSObject))]
interface MyClass
{
}
```

**Rules**:
- Every interface MUST have `[BaseType]` specifying the ObjC superclass
- The C# interface name matches the ObjC class name
- If the ObjC name doesn't match .NET conventions, use `[BaseType (typeof (NSObject), Name = "ObjCName")]`

### Class with Protocol Conformance

```objc
@interface MyClass : UIView <UITextInput, NSCoding>
@end
```

```csharp
[BaseType (typeof (UIView))]
interface MyClass : IUITextInput, INSCoding
{
}
```

**Rule**: Protocol conformance uses `I`-prefixed interface names in the inheritance list.

### Disabling Default Constructor

When a class marks `init` as `NS_UNAVAILABLE`:

```csharp
[DisableDefaultCtor]
[BaseType (typeof (NSObject))]
interface MyClass
{
    [DesignatedInitializer]
    [Export ("initWithName:")]
    NativeHandle Constructor (string name);
}
```

---

## Property Binding (`@property`)

### Basic Property

```objc
@property (nonatomic, copy) NSString *title;
```

```csharp
[Export ("title")]
string Title { get; set; }
```

**Rules**:
- Property name is PascalCase in C# (the `[Export]` carries the ObjC name)
- The `[Export]` selector is the ObjC property name (the getter selector)
- The runtime auto-derives the setter as `setTitle:` from the export name

### ArgumentSemantic

| ObjC Attribute | C# ArgumentSemantic | When to Use |
|---|---|---|
| `copy` | `ArgumentSemantic.Copy` | Always specify |
| `retain` / `strong` | `ArgumentSemantic.Strong` | When explicitly declared |
| `assign` | `ArgumentSemantic.Assign` | For value types or explicit |
| `weak` | `ArgumentSemantic.Weak` | Plus `[NullAllowed]` |
| *(none, object pointer)* | *(omit)* | Don't infer a default |
| *(none, primitive)* | *(omit)* | Don't specify ArgumentSemantic for primitives |
| *(none, enum/value type)* | `ArgumentSemantic.Assign` | Inferred for non-primitive value types |

```objc
@property (nonatomic, copy) NSString *name;
@property (nonatomic, strong) UIView *contentView;
@property (nonatomic, weak) id<MyDelegate> delegate;
@property (nonatomic, assign) NSInteger count;
@property (nonatomic) UIColor *tintColor;   // no explicit semantic
```

```csharp
[Export ("name", ArgumentSemantic.Copy)]
string Name { get; set; }

[Export ("contentView", ArgumentSemantic.Strong)]
UIView ContentView { get; set; }

[NullAllowed, Export ("delegate", ArgumentSemantic.Weak)]
IMyDelegate Delegate { get; set; }

[Export ("count")]
nint Count { get; set; }

[Export ("tintColor")]   // no default semantic for object pointers
UIColor TintColor { get; set; }
```

### Readonly Properties

```objc
@property (nonatomic, readonly) NSInteger count;
```

```csharp
[Export ("count")]
nint Count { get; }
```

**Rule**: Omit the setter for `readonly` properties.

### Class Properties (Static)

```objc
@property (class, nonatomic, readonly) MyClass *sharedInstance;
```

```csharp
[Static]
[Export ("sharedInstance")]
MyClass SharedInstance { get; }
```

### Custom Getter Name

```objc
@property (nonatomic, getter=isEnabled) BOOL enabled;
```

```csharp
[Export ("enabled")]
bool Enabled { [Bind ("isEnabled")] get; set; }
```

### Block-Type Properties

```objc
@property (nonatomic, copy) void (^completionHandler)(BOOL success);
```

```csharp
[Export ("completionHandler", ArgumentSemantic.Copy)]
Action<bool> CompletionHandler { get; set; }
```

**Rule**: Block properties use `Action<>` for void-returning blocks, `Func<>` for value-returning blocks.

---

## Method Binding

### Instance Methods

```objc
- (void)performAction:(NSString *)action withCompletion:(void (^)(BOOL))handler;
```

```csharp
[Export ("performAction:withCompletion:")]
void PerformAction (string action, Action<bool> handler);
```

**Rules**:
- The `[Export]` selector includes ALL colons: `"performAction:withCompletion:"`
- Method name uses only the **first selector part** (PascalCased), with trailing parameter context stripped
- `addAnnotation:options:animated:` → `AddAnnotation`
- `cancelSearchAnimated:` → `CancelSearch` (strips "Animated")
- For protocol/delegate methods, strip the sender class name prefix: `myController:didSelectItem:` → `DidSelectItem`
- For single-part protocol selectors with embedded sender prefix + verb: `myViewControllerDidCancel:` → `DidCancel`
- For non-void methods with parameters that don't start with a verb, add `Get` prefix: `annotationForIndexPath:` → `GetAnnotation`
- Normalize acronyms: URL→Url, PDF→Pdf, HUD→Hud, HTML→Html, JSON→Json
- `[Async]` is only added to class methods with completion handlers, never protocol methods
- Constructor detection handles `nonnull instancetype` and `nullable instancetype`, not just bare `instancetype`
- Parameter names come from the ObjC parameter names

### Class Methods (Static)

```objc
+ (instancetype)documentWithURL:(NSURL *)url;
```

```csharp
[Static]
[Export ("documentWithURL:")]
MyClass FromUrl (NSUrl url);
```

**Rule**: Add `[Static]` before `[Export]` for class methods (`+`).

### NullAllowed on Parameters

```objc
- (void)setText:(nullable NSString *)text;
```

```csharp
[Export ("setText:")]
void SetText ([NullAllowed] string text);
```

### NullAllowed on Return Values

```objc
- (nullable UIView *)viewForKey:(NSString *)key;
```

```csharp
[return: NullAllowed]
[Export ("viewForKey:")]
UIView ViewForKey (string key);
```

---

## Constructor Binding

### Basic Constructor

```objc
- (instancetype)initWithFrame:(CGRect)frame;
```

```csharp
[Export ("initWithFrame:")]
NativeHandle Constructor (CGRect frame);
```

**Rules**:
- Constructors return `NativeHandle` (was `IntPtr` in older Xamarin)
- Method name is always `Constructor`
- The `[Export]` carries the full ObjC selector

### Designated Initializer

```objc
- (instancetype)initWithTitle:(NSString *)title NS_DESIGNATED_INITIALIZER;
```

```csharp
[DesignatedInitializer]
[Export ("initWithTitle:")]
NativeHandle Constructor (string title);
```

### Auto-Generated Constructors

The binding generator automatically creates these constructors for every bound class:
1. `Foo ()` — default, maps to ObjC `init`
2. `Foo (NSCoder)` — for NIB deserialization (`initWithCoder:`)
3. `Foo (NativeHandle)` — handle-based creation (runtime use)
4. `Foo (NSEmptyFlag)` — for derived classes (prevents double init)

Use `[DisableDefaultCtor]` to suppress the parameterless constructor.

---

## Protocol Binding (`@protocol`)

### Protocol with Required and Optional Methods

```objc
@protocol MyDelegate <NSObject>
@required
- (void)didFinish:(MyClass *)sender;
@optional
- (BOOL)shouldStart:(MyClass *)sender;
@end
```

```csharp
// 1. Empty stub interface (REQUIRED for typed protocol references)
interface IMyDelegate {}

// 2. Protocol definition
[Protocol, Model]
[BaseType (typeof (NSObject))]
interface MyDelegate
{
    // @required → [Abstract]
    [Abstract]
    [Export ("didFinish:")]
    void DidFinish (MyClass sender);

    // @optional → no [Abstract]
    [Export ("shouldStart:")]
    bool ShouldStart (MyClass sender);
}
```

**Critical Rules**:
- Every `@protocol` needs an empty `interface IProtocolName {}` stub BEFORE the definition
- The stub enables typed protocol references (`IMyDelegate` as parameter/property type)
- `@required` methods get `[Abstract]`
- `@optional` methods do NOT get `[Abstract]`
- Protocols ending in `Delegate` or `DataSource` get `[Protocol, Model]` + `[BaseType (typeof (NSObject))]`
- Other protocols get `[Protocol]` only (no `[Model]`, no `[BaseType]`)

### Protocol Properties — Required vs Optional

Protocol properties are handled differently based on `@required` vs `@optional`:

```objc
@protocol MyProtocol <NSObject>
@required
@property (nonatomic, readonly, getter=isActive) BOOL active;
@property (nonatomic) NSString *name;
@optional
@property (nonatomic, getter=isSelected) BOOL selected;
@property (nonatomic) NSObject *value;
@end
```

```csharp
[Protocol]
[BaseType (typeof (NSObject))]
interface MyProtocol
{
    // @required → [Abstract] + C# property (with [Bind] for custom getter)
    [Abstract]
    [Export ("active")]
    bool Active { [Bind ("isActive")] get; }

    [Abstract]
    [Export ("name")]
    string Name { get; set; }

    // @optional → decomposed into getter/setter method pairs
    [Export ("isSelected")]
    bool GetSelected ();

    [Export ("setSelected:")]
    void SetSelected (bool selected);

    [Export ("value")]
    NSObject GetValue ();

    [Export ("setValue:")]
    void SetValue ([NullAllowed] NSObject value);
}
```

**Critical Rules**:
- `@required` properties get `[Abstract]` and stay as C# properties
- `@required` properties with custom getters use `[Bind("isX")]` syntax
- `@optional` properties are decomposed into `GetXxx()`/`SetXxx()` method pairs
- `@optional` properties with custom getters use the custom selector as the getter export (e.g., `isSelected`)
- Regular class (`@interface`) properties are NEVER decomposed — they always use `{ get; set; }`

### Protocol Used as Parameter Type

```objc
- (void)setDelegate:(id<MyDelegate>)delegate;
```

```csharp
[Export ("setDelegate:")]
void SetDelegate ([NullAllowed] IMyDelegate delegate);
```

**Rule**: Use `I`-prefixed stub interface for protocol-typed parameters.

### Delegate/Event Pattern

To generate C# events from an ObjC delegate protocol:

```csharp
[BaseType (typeof (NSObject),
    Delegates = new string [] { "WeakDelegate" },
    Events = new Type [] { typeof (MyDelegate) })]
interface MyClass
{
    [Wrap ("WeakDelegate")]
    [NullAllowed]
    IMyDelegate Delegate { get; set; }

    [Export ("delegate", ArgumentSemantic.Assign)]
    [NullAllowed]
    NSObject WeakDelegate { get; set; }
}

[Protocol, Model]
[BaseType (typeof (NSObject))]
interface MyDelegate
{
    [Export ("myClass:didSelectItem:"), EventArgs ("ItemSelected")]
    void DidSelectItem (MyClass sender, NSObject item);

    [Export ("myClassShouldClose:"), DelegateName ("Predicate<MyClass>"), DefaultValue (true)]
    bool ShouldClose (MyClass sender);
}
```

**Rules**:
- `Events` and `Delegates` on `[BaseType]` enable C# event generation
- `[EventArgs ("Name")]` generates a `NameEventArgs` class for multi-parameter events
- `[DelegateName]` + `[DefaultValue]` for methods that return values
- Provide both `Delegate` (strongly typed, `[Wrap]`) and `WeakDelegate` (weakly typed, `[Export]`)

---

## Nullability Rules

### NS_ASSUME_NONNULL Scope

Most modern ObjC headers use:
```objc
NS_ASSUME_NONNULL_BEGIN
// Everything here is nonnull by default
NS_ASSUME_NONNULL_END
```

**Inside NS_ASSUME_NONNULL scope**:
- All object pointers are assumed nonnull → do NOT add `[NullAllowed]`
- Only explicitly `nullable` types get `[NullAllowed]`

**Outside NS_ASSUME_NONNULL scope**:
- All object pointer types should get `[NullAllowed]`

### Nullability Annotation Mapping

| ObjC Annotation | C# Attribute |
|---|---|
| `nullable` / `_Nullable` / `__nullable` | `[NullAllowed]` |
| `nonnull` / `_Nonnull` / `__nonnull` | *(nothing — this is the default inside NONNULL scope)* |
| `null_unspecified` | `[NullAllowed]` (safer default) |
| `weak` property | Always `[NullAllowed]` (weak refs are inherently nullable) |

### Where NullAllowed Goes

| Context | Placement |
|---|---|
| Property | `[NullAllowed, Export ("name")]` (before Export) |
| Method parameter | `void Foo ([NullAllowed] string arg)` (inline on parameter) |
| Return value | `[return: NullAllowed]` (separate attribute line) |
| Setter only | `string Name { get; [NullAllowed] set; }` |

---

## Enum Binding

### NS_ENUM (Native Integer Enums)

```objc
typedef NS_ENUM(NSInteger, UITableViewCellStyle) {
    UITableViewCellStyleDefault,
    UITableViewCellStyleValue1,
    UITableViewCellStyleValue2,
    UITableViewCellStyleSubtitle
};
```

```csharp
// In StructsAndEnums.cs
[Native]
public enum UITableViewCellStyle : long
{
    Default,
    Value1,
    Value2,
    Subtitle,
}
```

**Rules**:
- `NS_ENUM(NSInteger, ...)` → `[Native]` + `: long`
- `NS_ENUM(NSUInteger, ...)` → `[Native]` + `: ulong`
- Strip the enum name prefix from member names: `UITableViewCellStyleDefault` → `Default`
- Goes in `StructsAndEnums.cs`, NOT in `ApiDefinition.cs`

### NS_OPTIONS (Flags Enums)

```objc
typedef NS_OPTIONS(NSUInteger, UIViewAutoresizing) {
    UIViewAutoresizingNone                 = 0,
    UIViewAutoresizingFlexibleLeftMargin   = 1 << 0,
    UIViewAutoresizingFlexibleWidth        = 1 << 1,
};
```

```csharp
[Native]
[Flags]
public enum UIViewAutoresizing : ulong
{
    None = 0,
    FlexibleLeftMargin = 1 << 0,
    FlexibleWidth = 1 << 1,
}
```

### Enum Backing Type Mapping

| ObjC | C# |
|---|---|
| `NSInteger` | `long` (with `[Native]`) |
| `NSUInteger` | `ulong` (with `[Native]`) |
| `int` / `int32_t` | `int` |
| `unsigned int` / `uint32_t` | `uint` |
| `uint8_t` | `byte` |

### String-Backed Enums

```csharp
enum NSRunLoopMode
{
    [DefaultEnumValue]
    [Field ("NSDefaultRunLoopMode")]
    Default,

    [Field ("NSRunLoopCommonModes")]
    Common,
}
```

---

## Field Binding (Extern Constants)

### String Constants / Notification Names

```objc
extern NSString * const MyClassDidFinishNotification;
```

```csharp
[Field ("MyClassDidFinishNotification")]
NSString DidFinishNotification { get; }
```

**Rules**:
- Fields go inside an interface decorated with `[Static]`
- For statically linked libraries, use `[Field ("name", "__Internal")]`
- For framework constants, use `[Field ("name", "FrameworkName")]`

### Notification Binding

```objc
extern NSNotificationName const MyClassDidChangeNotification;
```

```csharp
[Notification]
[Field ("MyClassDidChangeNotification")]
NSString DidChangeNotification { get; }
```

This generates a `Notifications` nested class with `ObserveDidChange()` methods.

For notifications with payload:

```csharp
[Notification (typeof (MyChangedEventArgs))]
[Field ("MyClassDidChangeNotification")]
NSString DidChangeNotification { get; }

// Separate interface for EventArgs
interface MyChangedEventArgs
{
    [Export ("ChangedItemKey")]
    NSObject ChangedItem { get; set; }
}
```

---

## Category Binding

### Categories That Extend Known Types (Merge)

When the parent class is in your binding, merge the category members into the parent:

```objc
// PSPDFPageView+AnnotationMenu.h
@interface PSPDFPageView (AnnotationMenu)
- (void)showMenu;
@end
```

```csharp
// Add directly into the PSPDFPageView interface definition:
[BaseType (typeof (UIView))]
interface PSPDFPageView
{
    // ... existing members ...

    // From AnnotationMenu category
    [Export ("showMenu")]
    void ShowMenu ();
}
```

**Rule**: Modern binding convention is to merge categories into their parent class.

### Categories That Extend System Types (Extension Methods)

When extending a system type like `UIView` or `NSString`:

```objc
@interface UIView (MyExtensions)
- (void)makeBackgroundRed;
@end
```

```csharp
[Category]
[BaseType (typeof (UIView))]
interface UIView_MyExtensions
{
    [Export ("makeBackgroundRed")]
    void MakeBackgroundRed ();
}
```

**Rules**:
- Use `[Category]` + `[BaseType (typeof (ExtendedType))]`
- Interface name convention: `ExtendedType_CategoryName`
- These become C# extension methods
- Avoid `[Static]` members in categories (generates broken code — use `[Category (allowStaticMembers: true)]` if needed)

---

## Type Mapping Reference

### Primitive Types

| Objective-C | C# |
|---|---|
| `void` | `void` |
| `BOOL` / `bool` | `bool` |
| `char` / `signed char` | `sbyte` |
| `unsigned char` / `uint8_t` | `byte` |
| `short` / `int16_t` | `short` |
| `unsigned short` / `uint16_t` | `ushort` |
| `int` / `int32_t` | `int` |
| `unsigned int` / `uint32_t` | `uint` |
| `long` | `nint` |
| `unsigned long` | `nuint` |
| `long long` / `int64_t` | `long` |
| `unsigned long long` / `uint64_t` | `ulong` |
| `float` | `float` |
| `double` | `double` |
| `NSInteger` | `nint` |
| `NSUInteger` | `nuint` |
| `CGFloat` | `nfloat` |
| `NSTimeInterval` | `double` |
| `size_t` | `nuint` |

### Object Types

| Objective-C | C# |
|---|---|
| `NSString *` | `string` |
| `NSArray *` | `NSObject []` |
| `NSArray<Type *>` | `Type []` (typed arrays) |
| `NSDictionary *` | `NSDictionary` |
| `NSData *` | `NSData` |
| `NSDate *` | `NSDate` |
| `NSURL *` | `NSUrl` |
| `NSError *` | `NSError` |
| `NSSet *` | `NSSet` |
| `NSNumber *` | `NSNumber` |
| `NSObject *` | `NSObject` |
| `id` | `NSObject` |
| `id<Protocol>` | `IProtocol` |
| `UIView<Protocol>` | `IProtocol` (protocol interface) |
| `instancetype` | The class type itself (for factory methods) or `NativeHandle` (for constructors) |
| `IBAction` | `void` |
| `Class` | `Class` |
| `SEL` | `Selector` |
| `*Block` typedef | `*Handler` (.NET convention) |

### Pointer / Handle Types

| Objective-C | C# |
|---|---|
| `NSError **` | `out NSError` |
| `Type **` | `out Type` |
| `void *` | `IntPtr` |
| `CGColorRef` | `CGColor` |
| `CGPathRef` | `CGPath` |
| `CGImageRef` | `CGImage` |
| `CGContextRef` | `CGContext` |
| `SecIdentityRef` | `SecIdentity` |
| `SecTrustRef` | `SecTrust` |
| `dispatch_queue_t` | `DispatchQueue` |

### Block Types

| Objective-C Block | C# |
|---|---|
| `void (^)(void)` | `Action` |
| `void (^)(BOOL)` | `Action<bool>` |
| `void (^)(NSString *, NSError *)` | `Action<string, NSError>` |
| `BOOL (^)(NSString *)` | `Func<string, bool>` |
| `id (^)(void)` | `Func<NSObject>` |

### const char * (C Strings)

| Objective-C | C# |
|---|---|
| `const char *` | `string` (marshaled automatically) |
| `char *` (mutable) | `IntPtr` (manual marshaling) |

### Variadic Parameters

```objc
- (void)appendItems:(NSObject *)firstItem, ... NS_REQUIRES_NIL_TERMINATION;
```

```csharp
[Export ("appendItems:"), Internal]
void AppendItems (NSObject firstItem, IntPtr varArgs);

// Then provide a public wrapper using params:
// public void AppendItems (params NSObject[] items) { ... }
```

---

## Struct Binding

```objc
typedef struct {
    CGFloat x;
    CGFloat y;
    CGFloat width;
} MyRect;
```

```csharp
// In StructsAndEnums.cs
[StructLayout (LayoutKind.Sequential)]
public struct MyRect
{
    public nfloat X;
    public nfloat Y;
    public nfloat Width;
}
```

---

## Advanced Attributes

### [Wrap] — Strongly Typed Convenience

```csharp
[Export ("delegate", ArgumentSemantic.Assign)]
[NullAllowed]
NSObject WeakDelegate { get; set; }

[Wrap ("WeakDelegate")]
[NullAllowed]
IMyDelegate Delegate { get; set; }
```

### [Async] — Async Wrapper Generation

```csharp
[Export ("fetchDataWithCompletion:")]
[Async]
void FetchData (Action<NSData, NSError> completion);
```

Generates: `Task<NSData> FetchDataAsync ()`

### [Sealed] — Prevent Overriding

```csharp
[Sealed]
[Export ("uniqueIdentifier")]
string UniqueIdentifier { get; }
```

### [New] — Hide Inherited Member

```csharp
[New]
[Export ("init")]
NativeHandle Constructor ();
```

### [Internal] — Hide from Public API

```csharp
[Internal]
[Export ("_privateMethod")]
void PrivateMethod ();
```

### [Verify] — Mark for Manual Review

```csharp
[Verify (MethodToProperty)]
[Export ("count")]
nint Count { get; }
```

Used by Objective Sharpie to flag bindings that need human review.

### [BindAs] — Better Type Conversion

```csharp
[return: BindAs (typeof (bool?))]
[Export ("shouldDraw:")]
NSNumber ShouldDraw ([BindAs (typeof (CGRect))] NSValue rect);
// Generates: bool? ShouldDraw (CGRect rect)
```

---

## Common Patterns and Pitfalls

### 1. Missing I-Prefix Protocol Stub

**Wrong**: Using protocol directly without stub
```csharp
[Protocol, Model]
[BaseType (typeof (NSObject))]
interface MyDelegate { ... }
```

**Right**: Always add the empty stub interface
```csharp
interface IMyDelegate {}

[Protocol, Model]
[BaseType (typeof (NSObject))]
interface MyDelegate { ... }
```

### 2. Forgetting ArgumentSemantic for Object Properties

**Wrong**: Missing semantic for object pointer property
```csharp
[Export ("view")]
UIView View { get; set; }
```

**Right**: Only include ArgumentSemantic when explicitly declared in ObjC property attributes
```csharp
[Export ("view")]
UIView View { get; set; }  // no default semantic needed
```

### 3. Wrong Selector for Multi-Part Methods

**Wrong**: Missing colons in selector
```csharp
[Export ("setValueForKey")]
void SetValue (NSObject value, string key);
```

**Right**: Include ALL colons
```csharp
[Export ("setValue:forKey:")]
void SetValue (NSObject value, string key);
```

### 4. Using `int` Instead of `nint` for NSInteger

**Wrong**:
```csharp
[Export ("count")]
int Count { get; }
```

**Right**: NSInteger maps to nint (pointer-sized)
```csharp
[Export ("count")]
nint Count { get; }
```

### 5. Enums in Wrong File

**Wrong**: Putting enums in `ApiDefinition.cs`

**Right**: All enums, structs, and delegates go in `StructsAndEnums.cs`

### 6. Constructor Returning Wrong Type

**Wrong**:
```csharp
[Export ("initWithName:")]
IntPtr Constructor (string name);  // Old Xamarin style
```

**Right** (modern .NET for iOS):
```csharp
[Export ("initWithName:")]
NativeHandle Constructor (string name);
```

---

## Vendor Macro Handling

When reading ObjC headers from third-party frameworks, ignore these common macros:

| Macro Pattern | Meaning | Action |
|---|---|---|
| `NS_ASSUME_NONNULL_BEGIN/END` | Nullability scope | Affects `[NullAllowed]` placement |
| `NS_DESIGNATED_INITIALIZER` | Designated init | Add `[DesignatedInitializer]` |
| `NS_REQUIRES_SUPER` | Must call super | Ignore (no C# equivalent) |
| `NS_SWIFT_NAME(...)` | Swift name override | Ignore |
| `API_AVAILABLE(...)` | Availability | Map to `[Introduced]` if needed |
| `API_DEPRECATED(...)` | Deprecation | Map to `[Deprecated]` if needed |
| `NS_UNAVAILABLE` | Unavailable init | Use `[DisableDefaultCtor]` |
| `NS_REFINED_FOR_SWIFT` | Swift refinement | Ignore |
| `OBJC_EXPORT` / vendor export macros | Visibility | Treat as `extern` |
| Other UPPER_SNAKE_CASE macros | Vendor annotations | Skip entirely |
| `__attribute__((...))` | Compiler attributes | Skip entirely |

---

## Checklist for Binding an ObjC Header

1. **Identify the scope**: Is the header inside `NS_ASSUME_NONNULL_BEGIN/END`?
2. **Map the class**: `@interface` → `[BaseType] interface`
3. **Map the superclass**: `: NSObject` → `typeof (NSObject)`
4. **Map protocol conformance**: `<P1, P2>` → `: IP1, IP2`
5. **Map each property**: type mapping, ArgumentSemantic, readonly, nullable
6. **Map each method**: selector with colons, parameter types, return type, nullable
7. **Map constructors**: `init*` methods → `NativeHandle Constructor`, add `[DesignatedInitializer]` if marked
8. **Map protocols**: `@protocol` → `interface IFoo {}` stub + `[Protocol]` definition, `[Abstract]` for `@required`
9. **Map enums**: `NS_ENUM` → `[Native] enum`, `NS_OPTIONS` → `[Native] [Flags] enum`
10. **Map extern constants**: `extern NSString *` → `[Field]` in `[Static] interface Constants`
11. **Map categories**: Merge into parent class or use `[Category]` for system types
12. **Verify nullability**: Check every object pointer parameter, property, and return value
13. **Strip enum prefixes**: `UITableViewCellStyleDefault` → `Default`
14. **Put types in correct files**: Interfaces in ApiDefinition.cs, enums/structs in StructsAndEnums.cs

---

## References

- [Binding Types Reference Guide](https://github.com/dotnet/macios/blob/main/docs/website/binding_types_reference_guide.md)
- [Binding Objective-C Libraries](https://github.com/dotnet/macios/blob/main/docs/website/binding_objc_libs.md)
- [type-mapping.md](references/type-mapping.md) — Complete ObjC→C# type mapping table
- [attributes.md](references/attributes.md) — Complete attribute reference
