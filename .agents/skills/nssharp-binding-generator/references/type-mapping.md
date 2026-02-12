# ObjC → C# Type Mapping Reference

Complete mapping from `ObjCTypeMapper.cs` in `src/NSSharp/Binding/ObjCTypeMapper.cs`.

## Primitive Types

| ObjC | C# |
|---|---|
| `void` | `void` |
| `BOOL` / `bool` / `_Bool` / `GLboolean` | `bool` |
| `char` / `signed char` | `sbyte` |
| `unsigned char` / `uint8_t` / `UInt8` | `byte` |
| `int8_t` / `SInt8` | `sbyte` |
| `short` / `int16_t` / `SInt16` | `short` |
| `unsigned short` / `uint16_t` / `UInt16` | `ushort` |
| `int` / `int32_t` / `SInt32` | `int` |
| `unsigned int` / `uint32_t` / `UInt32` | `uint` |
| `long` | `nint` |
| `unsigned long` | `nuint` |
| `long long` / `int64_t` | `long` |
| `unsigned long long` / `uint64_t` / `UInt64` | `ulong` |
| `float` | `float` |
| `double` | `double` |
| `long double` | `decimal` |
| `size_t` | `nuint` |

## Apple Framework Types

| ObjC | C# |
|---|---|
| `NSInteger` | `nint` |
| `NSUInteger` | `nuint` |
| `CGFloat` | `nfloat` |
| `NSTimeInterval` | `double` |
| `id` | `NSObject` |
| `Class` | `Class` |
| `SEL` | `Selector` |
| `IMP` | `IntPtr` |
| `instancetype` | `instancetype` |
| `intptr_t` | `IntPtr` |
| `uintptr_t` | `UIntPtr` |

## Foundation Types

| ObjC | C# |
|---|---|
| `NSString *` | `string` |
| `NSNumber *` | `NSNumber` |
| `NSArray *` | `NSObject[]` |
| `NSDictionary *` | `NSDictionary` |
| `NSData *` | `NSData` |
| `NSDate *` | `NSDate` |
| `NSURL *` | `NSUrl` |
| `NSError *` | `NSError` |
| `NSObject *` | `NSObject` |
| `NSSet *` | `NSSet` |
| `NSValue *` | `NSValue` |

## CoreFoundation / CoreGraphics Ref Types

| ObjC | C# |
|---|---|
| `CGColorRef` | `CGColor` |
| `CGPathRef` | `CGPath` |
| `CGImageRef` | `CGImage` |
| `CGContextRef` | `CGContext` |
| `CGColorSpaceRef` | `CGColorSpace` |
| `CGGradientRef` | `CGGradient` |
| `CGLayerRef` | `CGLayer` |
| `CGPDFDocumentRef` | `CGPDFDocument` |
| `CGPDFPageRef` | `CGPDFPage` |
| `CGImageSourceRef` | `CGImageSource` |
| `CFRunLoopRef` | `CFRunLoop` |

## Security / Media Types

| ObjC | C# |
|---|---|
| `SecIdentityRef` | `SecIdentity` |
| `SecTrustRef` | `SecTrust` |
| `SecAccessControlRef` | `SecAccessControl` |
| `CMTimebaseRef` | `CMTimebase` |
| `CMClockRef` | `CMClock` |
| `CMSampleBufferRef` | `CMSampleBuffer` |
| `CVImageBufferRef` | `CVImageBuffer` |
| `CVPixelBufferRef` | `CVPixelBuffer` |
| `CMFormatDescriptionRef` | `CMFormatDescription` |
| `CMAudioFormatDescriptionRef` | `CMAudioFormatDescription` |
| `CMVideoFormatDescriptionRef` | `CMVideoFormatDescription` |
| `MIDIEndpointRef` | `int` |

## Dispatch / Network Types

| ObjC | C# |
|---|---|
| `dispatch_queue_t` | `DispatchQueue` |
| `dispatch_data_t` | `DispatchData` |
| `sec_identity_t` | `SecIdentity2` |
| `sec_trust_t` | `SecTrust2` |
| `sec_protocol_options_t` | `SecProtocolOptions` |
| `sec_protocol_metadata_t` | `SecProtocolMetadata` |

## Special Mapping Rules

- **Pointers to value types**: `int *` → `unsafe int*`
- **Double pointers**: `NSError **` → `out NSError` (out parameter)
- **`id<Protocol>`**: `id<MyProtocol>` → `IMyProtocol`
- **Generics**: `NSArray<NSString *>` → maps base type (`NSObject[]`)
- **Block types**: `void (^)(BOOL)` → `Action`
- **`const char *`**: → `string`
- **`char *`** (non-const): → `unsafe sbyte*`
- **`__kindof`**: stripped before mapping
- **Nullability annotations**: stripped (`_Nullable`, `_Nonnull`, `__nullable`, `__nonnull`, etc.)

## Enum Backing Type Mapping

Used for `NS_ENUM` / `NS_OPTIONS` / typed C enums:

| ObjC | C# |
|---|---|
| `NSInteger` | `long` (gets `[Native]` attribute) |
| `NSUInteger` | `ulong` (gets `[Native]` attribute) |
| `int` | `int` |
| `unsigned int` | `uint` |
| `long long` | `long` |
| `unsigned long long` | `ulong` |
| (default/unknown) | `uint` |
