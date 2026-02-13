# Binding Attributes Reference

Complete reference for Xamarin.iOS / .NET for iOS binding attributes used in `ApiDefinition.cs`.

## Type-Level Attributes

### [BaseType]

Required on every interface. Specifies the ObjC superclass.

```csharp
// Basic
[BaseType (typeof (NSObject))]
interface MyClass { }

// With delegate wiring (generates C# events)
[BaseType (typeof (NSObject),
    Delegates = new string [] { "WeakDelegate" },
    Events = new Type [] { typeof (MyDelegate) })]
interface MyClass { }

// With ObjC name override
[BaseType (typeof (NSObject), Name = "ABPerson")]
interface AddressBookPerson { }

// With keep-ref semantics (prevents GC of managed wrapper while native side holds it)
[BaseType (typeof (NSObject), KeepRefUntil = "Dismiss")]
interface AlertView { }
```

**Properties**:
- `typeof(SuperClass)` — required, maps to ObjC superclass
- `Name` — override ObjC class name (rare)
- `Delegates` — array of weak delegate property names for event generation
- `Events` — array of delegate protocol types for event generation
- `KeepRefUntil` — prevent GC until named selector is called

### [Protocol]

Marks an interface as an ObjC protocol binding.

```csharp
[Protocol]
interface IMyProtocol { }
```

### [Model]

Used with `[Protocol]` for delegate/datasource protocols. Generates a concrete class
that can be subclassed (not just an interface).

```csharp
[Protocol, Model]
[BaseType (typeof (NSObject))]
interface MyDelegate { }
```

**Rule**: Use `[Protocol, Model]` for protocols ending in `Delegate` or `DataSource`.
Use `[Protocol]` alone for other protocols.

### [Category]

Marks an interface as an ObjC category extension.

```csharp
[Category]
[BaseType (typeof (UIView))]
interface UIView_MyAdditions
{
    [Export ("shake")]
    void Shake ();
}
```

**Rule**: Generates C# extension methods. Interface name = `BaseType_CategoryName`.

### [Static]

At type level: all members are class-level. Used for utility classes.

```csharp
[Static]
interface Constants
{
    [Field ("kMyConstant")]
    NSString MyConstant { get; }
}
```

### [DisableDefaultCtor]

Prevents generation of parameterless constructor.

```csharp
[DisableDefaultCtor]
[BaseType (typeof (NSObject))]
interface MyClass { }
```

---

## Member-Level Attributes

### [Export]

Maps a C# member to an ObjC selector or property name.

```csharp
// Property
[Export ("title")]
string Title { get; set; }

// Property with ArgumentSemantic
[Export ("name", ArgumentSemantic.Copy)]
string Name { get; set; }

// Method
[Export ("doSomething:with:")]
void DoSomething (NSObject thing, string context);

// Constructor
[Export ("initWithName:")]
NativeHandle Constructor (string name);
```

### [Bind]

Overrides getter selector for a property.

```csharp
[Export ("enabled")]
bool Enabled { [Bind ("isEnabled")] get; set; }
```

### [Static]

At member level: marks as a class method (`+`).

```csharp
[Static]
[Export ("sharedInstance")]
MyClass SharedInstance { get; }
```

### [Abstract]

Marks a protocol method as `@required`. The generated class throws if not overridden.

```csharp
[Abstract]
[Export ("didFinish")]
void DidFinish ();
```

### [NullAllowed]

Indicates the value can be `null`.

```csharp
// On property
[NullAllowed, Export ("title")]
string Title { get; set; }

// On parameter
void SetText ([NullAllowed] string text);

// On return value
[return: NullAllowed]
[Export ("viewForKey:")]
UIView ViewForKey (string key);

// On setter only
string Name { get; [NullAllowed] set; }
```

### [DesignatedInitializer]

Marks as the designated initializer.

```csharp
[DesignatedInitializer]
[Export ("initWithFrame:")]
NativeHandle Constructor (CGRect frame);
```

### [Field]

Binds an extern constant (string, numeric, etc.).

```csharp
[Field ("UIKeyboardDidShowNotification")]
NSString KeyboardDidShowNotification { get; }

// With library name
[Field ("kCFBooleanTrue", "CoreFoundation")]
NSNumber CFBooleanTrue { get; }

// With __Internal for statically linked
[Field ("MyConstant", "__Internal")]
NSString MyConstant { get; }
```

### [Notification]

Generates convenience methods for NSNotification observation.

```csharp
[Notification]
[Field ("UIKeyboardDidShowNotification")]
NSString KeyboardDidShowNotification { get; }

// With EventArgs
[Notification (typeof (KeyboardEventArgs))]
[Field ("UIKeyboardDidShowNotification")]
NSString KeyboardDidShowNotification { get; }
```

### [Async]

Generates a Task-returning async wrapper for completion handler methods.

```csharp
[Async]
[Export ("fetchData:")]
void FetchData (Action<NSData, NSError> completion);
// Generates: Task<NSData> FetchDataAsync ()
```

### [Wrap]

Creates a strongly-typed convenience property/method backed by another member.

```csharp
[Export ("delegate", ArgumentSemantic.Assign)]
[NullAllowed]
NSObject WeakDelegate { get; set; }

[Wrap ("WeakDelegate")]
[NullAllowed]
IMyDelegate Delegate { get; set; }
```

### [Sealed]

Prevents the method from being `virtual` in the generated class.

```csharp
[Sealed]
[Export ("uniqueId")]
string UniqueId { get; }
```

### [New]

Adds the C# `new` modifier to hide an inherited member.

```csharp
[New]
[Export ("init")]
NativeHandle Constructor ();
```

### [Internal]

Makes the generated member `internal` instead of `public`.

```csharp
[Internal]
[Export ("_rawValue")]
IntPtr RawValue { get; }
```

### [Appearance]

Registers the property with UIAppearance proxy system.

```csharp
[Appearance]
[Export ("tintColor")]
UIColor TintColor { get; set; }
```

### [ForcedType]

Forces a cast at runtime (for loosely-typed ObjC APIs).

```csharp
[return: ForcedType]
[Export ("objectForKey:")]
NSObject ObjectForKey (NSObject key);
```

### [BindAs]

Converts between managed and native types automatically.

```csharp
[return: BindAs (typeof (bool?))]
[Export ("isAvailable")]
NSNumber IsAvailable { get; }
```

---

## Event-Related Attributes

### [EventArgs]

Names the EventArgs class for delegate methods.

```csharp
[Export ("tableView:didSelectRowAtIndexPath:"), EventArgs ("RowSelected")]
void RowSelected (UITableView tableView, NSIndexPath indexPath);
```

### [EventName]

Overrides the C# event name (default is method name).

```csharp
[Export ("textFieldShouldReturn:"), EventName ("ShouldReturn"), DelegateName ("UITextFieldCondition"), DefaultValue (true)]
bool ShouldReturn (UITextField textField);
```

### [DelegateName]

Specifies the delegate type name for value-returning delegate methods.

```csharp
[Export ("textFieldShouldClear:"), DelegateName ("UITextFieldCondition"), DefaultValue (true)]
bool ShouldClear (UITextField textField);
```

### [DefaultValue]

Specifies the default return value when delegate method is not overridden.

```csharp
[DefaultValue (true)]
[Export ("shouldBegin")]
bool ShouldBegin ();
```

### [DefaultValueFromArgument]

Uses a parameter value as default return.

```csharp
[DefaultValueFromArgument ("point")]
[Export ("adjstedPoint:")]
CGPoint AdjustedPoint (CGPoint point);
```

### [NoDefaultValue]

Forces the delegate method to throw if not overridden (instead of returning a default).

```csharp
[NoDefaultValue]
[Export ("requiredAction")]
NSObject RequiredAction ();
```

---

## Availability Attributes

### [Introduced]

```csharp
[Introduced (PlatformName.iOS, 15, 0)]
[Export ("newFeature")]
void NewFeature ();
```

### [Deprecated]

```csharp
[Deprecated (PlatformName.iOS, 16, 0, message: "Use NewMethod instead")]
[Export ("oldMethod")]
void OldMethod ();
```

### [Unavailable]

```csharp
[Unavailable (PlatformName.MacCatalyst)]
[Export ("iOSOnly")]
void IOSOnly ();
```

---

## Verify Attributes (Objective Sharpie)

These are added by Objective Sharpie to flag bindings needing human review:

| Verify Type | Meaning |
|---|---|
| `InferredFromMemberPrefix` | Method name was inferred from common prefix |
| `MethodToProperty` | Method was converted to property |
| `StronglyTypedNSArray` | Generic array type was inferred |
| `PlatformInvoke` | DllImport signature needs manual review |

```csharp
[Verify (MethodToProperty)]
[Export ("count")]
nint Count { get; }
```
