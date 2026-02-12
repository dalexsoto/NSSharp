namespace NSSharp.Binding;

/// <summary>
/// Maps Objective-C types to C# types for binding generation.
/// Based on type mapping rules from dotnet/macios Objective-Sharpie.
/// </summary>
public static class ObjCTypeMapper
{
    private static readonly Dictionary<string, string> s_primitiveMap = new(StringComparer.Ordinal)
    {
        ["void"] = "void",
        ["BOOL"] = "bool",
        ["bool"] = "bool",
        ["_Bool"] = "bool",
        ["GLboolean"] = "bool",
        ["char"] = "sbyte",
        ["signed char"] = "sbyte",
        ["unsigned char"] = "byte",
        ["uint8_t"] = "byte",
        ["int8_t"] = "sbyte",
        ["short"] = "short",
        ["unsigned short"] = "ushort",
        ["int16_t"] = "short",
        ["uint16_t"] = "ushort",
        ["int"] = "int",
        ["unsigned int"] = "uint",
        ["int32_t"] = "int",
        ["uint32_t"] = "uint",
        ["SInt8"] = "sbyte",
        ["SInt16"] = "short",
        ["SInt32"] = "int",
        ["UInt8"] = "byte",
        ["UInt16"] = "ushort",
        ["UInt32"] = "uint",
        ["long"] = "nint",
        ["unsigned long"] = "nuint",
        ["long long"] = "long",
        ["unsigned long long"] = "ulong",
        ["int64_t"] = "long",
        ["uint64_t"] = "ulong",
        ["UInt64"] = "ulong",
        ["float"] = "float",
        ["double"] = "double",
        ["long double"] = "decimal",
        ["size_t"] = "nuint",
        ["NSInteger"] = "nint",
        ["NSUInteger"] = "nuint",
        ["CGFloat"] = "nfloat",
        ["instancetype"] = "instancetype", // handled specially
        ["id"] = "NSObject",
        ["Class"] = "Class",
        ["SEL"] = "Selector",
        ["IMP"] = "IntPtr",
        ["intptr_t"] = "IntPtr",
        ["uintptr_t"] = "UIntPtr",
        ["NSTimeInterval"] = "double",
        ["dispatch_queue_t"] = "DispatchQueue",
        // Common CF/CG handle types
        ["CGColorRef"] = "CGColor",
        ["CGPathRef"] = "CGPath",
        ["CGImageRef"] = "CGImage",
        ["CGContextRef"] = "CGContext",
        ["CGColorSpaceRef"] = "CGColorSpace",
        ["CGGradientRef"] = "CGGradient",
        ["CGLayerRef"] = "CGLayer",
        ["CGPDFDocumentRef"] = "CGPDFDocument",
        ["CGPDFPageRef"] = "CGPDFPage",
        ["CGImageSourceRef"] = "CGImageSource",
        ["CFRunLoopRef"] = "CFRunLoop",
        ["SecIdentityRef"] = "SecIdentity",
        ["SecTrustRef"] = "SecTrust",
        ["SecAccessControlRef"] = "SecAccessControl",
        ["CMTimebaseRef"] = "CMTimebase",
        ["CMClockRef"] = "CMClock",
        ["CMSampleBufferRef"] = "CMSampleBuffer",
        ["CVImageBufferRef"] = "CVImageBuffer",
        ["CVPixelBufferRef"] = "CVPixelBuffer",
        ["CMFormatDescriptionRef"] = "CMFormatDescription",
        ["CMAudioFormatDescriptionRef"] = "CMAudioFormatDescription",
        ["CMVideoFormatDescriptionRef"] = "CMVideoFormatDescription",
        ["MIDIEndpointRef"] = "int",
        ["dispatch_data_t"] = "DispatchData",
        ["sec_identity_t"] = "SecIdentity2",
        ["sec_trust_t"] = "SecTrust2",
        ["sec_protocol_options_t"] = "SecProtocolOptions",
        ["sec_protocol_metadata_t"] = "SecProtocolMetadata",
    };

    private static readonly Dictionary<string, string> s_nsToCs = new(StringComparer.Ordinal)
    {
        ["NSString"] = "string",
        ["NSNumber"] = "NSNumber",
        ["NSArray"] = "NSObject[]",
        ["NSDictionary"] = "NSDictionary",
        ["NSData"] = "NSData",
        ["NSDate"] = "NSDate",
        ["NSURL"] = "NSUrl",
        ["NSError"] = "NSError",
        ["NSObject"] = "NSObject",
        ["NSSet"] = "NSSet",
        ["NSValue"] = "NSValue",
    };

    /// <summary>Maps an ObjC type string to a C# type string.</summary>
    public static string MapType(string objcType)
    {
        if (string.IsNullOrWhiteSpace(objcType))
            return "void";

        var type = objcType.Trim();

        // Strip nullability annotations
        type = StripNullability(type);

        // Strip __kindof
        if (type.StartsWith("__kindof "))
            type = type["__kindof ".Length..].Trim();

        // Const qualifier (keep for pointer types, strip for value types)
        bool isConst = type.StartsWith("const ");
        if (isConst)
            type = type["const ".Length..].Trim();

        // Handle block types: returnType (^)(params)
        if (type.Contains("(^)"))
            return MapBlockType(type);

        // Count and strip pointer stars
        int pointerDepth = 0;
        while (type.EndsWith(" *") || type.EndsWith("*"))
        {
            pointerDepth++;
            type = type.TrimEnd('*').TrimEnd();
        }

        // Handle double pointers (out params)
        if (pointerDepth >= 2)
        {
            var innerType = MapType(type + " *");
            return "out " + innerType;
        }

        // Check primitive map
        if (s_primitiveMap.TryGetValue(type, out var mapped))
        {
            if (pointerDepth > 0 && IsValueType(mapped))
                return "unsafe " + mapped + "*";
            return mapped;
        }

        // Check NS type map
        if (s_nsToCs.TryGetValue(type, out var nsType))
            return nsType;

        // Handle id<Protocol>
        if (type.StartsWith("id<") && type.EndsWith(">"))
        {
            var protoName = type[3..^1].Trim();
            return "I" + protoName;
        }

        // Handle generics: NSArray<NSString *>
        if (type.Contains('<'))
        {
            var baseType = type[..type.IndexOf('<')].Trim();
            // For binding purposes, return the base type
            return MapType(baseType + (pointerDepth > 0 ? " *" : ""));
        }

        // Handle char * (C strings)
        if (type == "char" && pointerDepth > 0)
        {
            if (isConst) return "string";
            return "unsafe sbyte*";
        }

        // Default: use the ObjC name as-is (likely a custom type)
        return type;
    }

    /// <summary>Map an ObjC type to a C# enum backing type.</summary>
    public static string MapEnumBackingType(string? objcType)
    {
        if (string.IsNullOrWhiteSpace(objcType)) return "uint";
        return objcType!.Trim() switch
        {
            "NSInteger" => "long",
            "NSUInteger" => "ulong",
            "int" => "int",
            "unsigned int" or "unsigned" => "uint",
            "short" => "short",
            "unsigned short" => "ushort",
            "long" => "nint",
            "unsigned long" => "nuint",
            "long long" => "long",
            "unsigned long long" => "ulong",
            "int64_t" => "long",
            "uint64_t" => "ulong",
            "uint8_t" => "byte",
            "int8_t" => "sbyte",
            _ => "uint",
        };
    }

    /// <summary>Whether the backing type is NSInteger/NSUInteger (needs [Native]).</summary>
    public static bool IsNativeEnum(string? objcType)
    {
        if (string.IsNullOrWhiteSpace(objcType)) return false;
        return objcType!.Trim() is "NSInteger" or "NSUInteger";
    }

    private static string MapBlockType(string blockType)
    {
        // Very simplified: just return Action/Func for common patterns
        // Full implementation would parse the block signature
        return "Action /* block type */";
    }

    private static string StripNullability(string type)
    {
        string[] annotations = ["_Nullable", "_Nonnull", "_Null_unspecified",
            "__nullable", "__nonnull", "nullable", "nonnull", "null_unspecified"];
        foreach (var ann in annotations)
        {
            type = type.Replace(ann, "").Trim();
        }
        // Clean up double spaces
        while (type.Contains("  "))
            type = type.Replace("  ", " ");
        return type.Trim();
    }

    private static bool IsValueType(string csType) =>
        csType is "int" or "uint" or "short" or "ushort" or "long" or "ulong"
            or "byte" or "sbyte" or "float" or "double" or "decimal"
            or "nint" or "nuint" or "nfloat" or "bool" or "char";

    /// <summary>Converts an ObjC selector to a PascalCase C# method name.</summary>
    public static string SelectorToMethodName(string selector)
    {
        if (string.IsNullOrEmpty(selector)) return selector;

        // Remove trailing colons and split on ':'
        var parts = selector.TrimEnd(':').Split(':');
        return string.Join("", parts.Select(PascalCase));
    }

    /// <summary>Converts a name to PascalCase.</summary>
    public static string PascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
