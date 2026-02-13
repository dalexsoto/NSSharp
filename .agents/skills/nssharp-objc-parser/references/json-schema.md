# NSSharp JSON Output Schema

## AST Node Types

All types are in `NSSharp.Ast` namespace, defined in `src/NSSharp/Ast/ObjCNodes.cs`.

### ObjCHeader (root)

```json
{
  "file": "MyHeader.h",
  "interfaces": [],
  "protocols": [],
  "enums": [],
  "structs": [],
  "typedefs": [],
  "functions": [],
  "forwardDeclarations": { "classes": [], "protocols": [] }
}
```

### ObjCInterface

```json
{
  "name": "MyClass",
  "superclass": "NSObject",
  "protocols": ["NSCoding"],
  "category": null,
  "properties": [],
  "instanceMethods": [],
  "classMethods": []
}
```

- `category` is non-null for ObjC categories: `@interface Foo (Bar)` → `"category": "Bar"`
- Extensions have `category: ""` (empty string)

### ObjCProtocol

```json
{
  "name": "MyDelegate",
  "inheritedProtocols": ["NSObject"],
  "properties": [],
  "requiredInstanceMethods": [],
  "requiredClassMethods": [],
  "optionalInstanceMethods": [],
  "optionalClassMethods": []
}
```

### ObjCProperty

```json
{
  "name": "title",
  "type": "NSString *",
  "attributes": ["nonatomic", "copy", "nullable"],
  "isNullable": true,
  "inNonnullScope": true
}
```

- `inNonnullScope`: true if declared between `NS_ASSUME_NONNULL_BEGIN` and `NS_ASSUME_NONNULL_END`

### ObjCMethod

```json
{
  "selector": "initWithTitle:count:",
  "returnType": "instancetype",
  "parameters": [
    { "name": "title", "type": "NSString *", "isNullable": false },
    { "name": "count", "type": "NSInteger", "isNullable": false }
  ],
  "isOptional": false,
  "inNonnullScope": true,
  "isReturnNullable": false
}
```

- `inNonnullScope`: true if declared between `NS_ASSUME_NONNULL_BEGIN` and `NS_ASSUME_NONNULL_END`
- `isReturnNullable`: true if return type has nullable annotation

### ObjCEnum

```json
{
  "name": "MyStatus",
  "backingType": "NSInteger",
  "isOptions": true,
  "values": [
    { "name": "MyStatusNone", "value": "0" },
    { "name": "MyStatusActive", "value": "1 << 0" },
    { "name": "MyStatusPaused", "value": "1 << 1" }
  ]
}
```

- `isOptions: true` for `NS_OPTIONS`, `false` for `NS_ENUM` and plain enums
- `backingType` is null for plain C enums without explicit type
- `value` may be null when no explicit value is assigned

### ObjCStruct

```json
{
  "name": "MyPoint",
  "fields": [
    { "name": "x", "type": "CGFloat" },
    { "name": "y", "type": "CGFloat" }
  ]
}
```

### ObjCTypedef

```json
{
  "name": "CompletionHandler",
  "underlyingType": "void (^)(BOOL success)"
}
```

### ObjCFunction

```json
{
  "name": "NSStringFromMyStatus",
  "returnType": "NSString *",
  "parameters": [
    { "name": "status", "type": "MyStatus", "isNullable": false }
  ]
}
```

### ObjCForwardDeclarations

```json
{
  "classes": ["NSData", "NSError"],
  "protocols": ["NSCoding"]
}
```

## Multiple Headers

When parsing multiple files, the output is a JSON array of `ObjCHeader` objects.

## JSON Serialization

- Property names are camelCase
- Pretty-printed by default, use `--compact` for minified
- Uses `System.Text.Json` with `JsonNamingPolicy.CamelCase`
