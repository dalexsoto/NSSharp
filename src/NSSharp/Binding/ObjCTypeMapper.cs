namespace NSSharp.Binding;

/// <summary>
/// Maps Objective-C types to C# types for binding generation.
/// Based on type mapping rules from dotnet/macios Objective-Sharpie.
/// </summary>
public static class ObjCTypeMapper
{
    private static Dictionary<string, string> s_typedefMap = new(StringComparer.Ordinal);

    /// <summary>Sets the typedef resolution map from parsed headers.</summary>
    public static void SetTypedefMap(Dictionary<string, string> typedefMap)
    {
        s_typedefMap = typedefMap;
    }

    /// <summary>Resolves a typedef name to its underlying base type.</summary>
    private static string ResolveTypedef(string typeName)
    {
        // Chase typedef chains (e.g., PSPDFPageIndex → NSUInteger → nuint)
        var seen = new HashSet<string>();
        var current = typeName;
        while (s_typedefMap.TryGetValue(current, out var underlying) && seen.Add(current))
        {
            // Strip pointer from underlying for resolution, re-add later
            current = underlying.TrimEnd(' ', '*');
        }
        return current;
    }

    private static readonly Dictionary<string, string> s_primitiveMap = new(StringComparer.Ordinal)
    {
        ["void"] = "void",
        ["IBAction"] = "void",
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

        // Strip ObjC parameter direction qualifiers (out, in, inout, bycopy, byref, oneway)
        foreach (var qual in new[] { "out ", "in ", "inout ", "bycopy ", "byref ", "oneway " })
        {
            if (type.StartsWith(qual) && type.Length > qual.Length)
                type = type[qual.Length..].Trim();
        }

        // Strip IB annotations and other type qualifiers
        foreach (var attr in new[] { "IBInspectable ", "IBOutlet ", "__block " })
        {
            if (type.StartsWith(attr))
                type = type[attr.Length..].Trim();
        }

        // Strip __kindof
        if (type.StartsWith("__kindof "))
            type = type["__kindof ".Length..].Trim();

        // Const qualifier (keep for pointer types, strip for value types)
        bool isConst = type.StartsWith("const ");
        if (isConst)
            type = type["const ".Length..].Trim();
        // Also strip trailing const (e.g., "PSPDFDocumentSharingDestination const")
        if (type.EndsWith(" const"))
            type = type[..^" const".Length].Trim();

        // Handle block types: returnType (^)(params) or spaced ( ^ )
        if (type.Contains("(^)") || type.Contains("( ^ )"))
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

        // Resolve typedefs to base types (e.g., PSPDFPageIndex → NSUInteger → nuint)
        var resolvedType = ResolveTypedef(type);
        if (resolvedType != type)
        {
            // Re-map with resolved type, preserving pointer depth
            var suffix = pointerDepth > 0 ? new string('*', pointerDepth) : "";
            return MapType(resolvedType + (suffix.Length > 0 ? " " + suffix : ""));
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

        // Handle id<Protocol> and id < Protocol > (with spaces)
        var idCheck = type.Replace(" ", "");
        if (idCheck.StartsWith("id<") && idCheck.EndsWith(">"))
        {
            var protoName = idCheck[3..^1].Trim();
            // For multiple protocols, use the first one
            if (protoName.Contains(','))
                protoName = protoName.Split(',')[0].Trim();
            return "I" + protoName;
        }

        // Handle generics: NSArray<PSPDFAnnotation *> → PSPDFAnnotation[]
        if (type.Contains('<'))
        {
            var ltIdx = type.IndexOf('<');
            var gtIdx = type.LastIndexOf('>');
            var baseType = type[..ltIdx].Trim();
            var genericParam = (gtIdx > ltIdx) ? type[(ltIdx + 1)..gtIdx].Trim() : "";

            // NSArray<Type *> → Type[]
            if (baseType is "NSArray" or "NSMutableArray" && !string.IsNullOrEmpty(genericParam))
            {
                var elementType = MapType(genericParam);
                return elementType + " []";
            }

            // NSDictionary<K, V> and NSSet<T> — preserve generics with mapped types
            if (baseType is "NSDictionary" or "NSMutableDictionary" && !string.IsNullOrEmpty(genericParam))
            {
                var mappedParams = MapGenericParams(genericParam);
                return MapType(baseType) + "<" + mappedParams + ">";
            }
            if (baseType is "NSSet" or "NSMutableSet" or "NSOrderedSet" or "NSMutableOrderedSet" && !string.IsNullOrEmpty(genericParam))
            {
                var mappedParams = MapGenericParams(genericParam);
                return MapType(baseType) + "<" + mappedParams + ">";
            }

            // ClassName<Protocol> (e.g., UIView<PSPDFAnnotationPresenting>) → IProtocol
            // When a non-collection class conforms to a protocol, use the protocol interface
            string[] collectionTypes = ["NSDictionary", "NSMutableDictionary", "NSSet", "NSMutableSet",
                "NSOrderedSet", "NSMutableOrderedSet", "NSMapTable", "NSHashTable"];
            if (!string.IsNullOrEmpty(genericParam) && !genericParam.Contains("*")
                && !Array.Exists(collectionTypes, ct => ct == baseType))
            {
                var cleanParam = genericParam.Trim();
                if (cleanParam.Contains(','))
                    cleanParam = cleanParam.Split(',')[0].Trim();
                return "I" + cleanParam;
            }

            // Other generics: return the base type
            return MapType(baseType + (pointerDepth > 0 ? " *" : ""));
        }

        // Handle char * (C strings)
        if (type == "char" && pointerDepth > 0)
        {
            if (isConst) return "string";
            return "unsafe sbyte*";
        }

        // Rename Block typedefs to Handler (.NET convention)
        if (type.EndsWith("Block", StringComparison.Ordinal) && type.Length > "Block".Length)
            type = type[..^"Block".Length] + "Handler";

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
            "uint8_t" or "UInt8" => "byte",
            "int8_t" or "SInt8" => "sbyte",
            "uint16_t" or "UInt16" => "ushort",
            "int16_t" or "SInt16" => "short",
            "uint32_t" or "UInt32" => "uint",
            "int32_t" or "SInt32" => "int",
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
        return "Action";
    }

    /// <summary>Splits and maps generic parameters, handling nested generics.</summary>
    private static string MapGenericParams(string genericParam)
    {
        var parts = SplitGenericParams(genericParam);
        return string.Join(", ", parts.Select(p => MapGenericParamType(p.Trim())));
    }

    /// <summary>Maps a type inside a generic parameter, preserving Foundation class names.</summary>
    private static string MapGenericParamType(string objcType)
    {
        var mapped = MapType(objcType);
        // In generic constraints, Foundation types must stay as their class names
        // (e.g., NSDictionary<NSString, V> requires NSObject subclasses)
        if (mapped == "string") return "NSString";
        return mapped;
    }

    /// <summary>Splits generic params at top-level commas, respecting nested angle brackets.</summary>
    private static List<string> SplitGenericParams(string param)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < param.Length; i++)
        {
            if (param[i] == '<') depth++;
            else if (param[i] == '>') depth--;
            else if (param[i] == ',' && depth == 0)
            {
                result.Add(param[start..i]);
                start = i + 1;
            }
        }
        result.Add(param[start..]);
        return result;
    }

    private static string StripNullability(string type)
    {
        string[] annotations = ["_Nullable", "_Nonnull", "_Null_unspecified",
            "__nullable", "__nonnull", "nullable", "nonnull", "null_unspecified",
            "__strong", "__weak", "__unsafe_unretained", "__autoreleasing"];
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
    public static string SelectorToMethodName(string selector, bool isProtocolMethod = false)
    {
        if (string.IsNullOrEmpty(selector)) return selector;

        // Single-part selector (no colons) → PascalCase
        if (!selector.Contains(':'))
            return PascalCase(RenameBlockToAction(selector));

        var parts = selector.TrimEnd(':').Split(':');
        parts[0] = RenameBlockToAction(parts[0]);

        // Handle "isEqualTo<ClassName>:" pattern → strip class name suffix, keep "isEqualTo"
        if (parts.Length == 1 && parts[0].StartsWith("isEqualTo", StringComparison.Ordinal)
            && parts[0].Length > "isEqualTo".Length && char.IsUpper(parts[0]["isEqualTo".Length]))
        {
            return PascalCase("isEqualTo");
        }

        // Protocol/delegate methods: strip the first part if it looks like a sender parameter
        // e.g., "annotationGridViewController:didSelectAnnotationSet:" → use second part onward
        if (isProtocolMethod && parts.Length >= 2)
        {
            var firstPart = parts[0];
            // Check if first part is a sender: either ends with a known suffix,
            // or any later part starts with a delegate verb (did/will/should/can/requested/failed)
            string[] delegateVerbs = ["did", "will", "should", "can", "requested", "failed"];
            bool laterPartHasVerb = false;
            for (int i = 1; i < parts.Length; i++)
            {
                foreach (var vp in delegateVerbs)
                {
                    if (parts[i].StartsWith(vp, StringComparison.Ordinal) &&
                        parts[i].Length > vp.Length && char.IsUpper(parts[i][vp.Length]))
                    {
                        laterPartHasVerb = true;
                        break;
                    }
                }
                if (laterPartHasVerb) break;
            }

            if (IsSenderParameterName(firstPart) || laterPartHasVerb)
            {
                parts = parts[1..];

                // Search all remaining parts (including single part) for delegate verb
                for (int i = 0; i < parts.Length; i++)
                {
                    foreach (var vp in delegateVerbs)
                    {
                        if (parts[i].StartsWith(vp, StringComparison.Ordinal) &&
                            parts[i].Length > vp.Length && char.IsUpper(parts[i][vp.Length]))
                        {
                            return PascalCase(StripTrailingParameterContext(parts[i]));
                        }
                    }
                }
            }
        }

        // Protocol single-part selectors: strip embedded sender prefix
        // e.g., "annotationGridViewControllerDidCancel:" → "DidCancel"
        if (isProtocolMethod && parts.Length == 1)
        {
            var stripped = StripEmbeddedSenderPrefix(parts[0]);
            if (stripped != null)
                return PascalCase(StripTrailingParameterContext(stripped));
        }

        // Use the first part (PascalCased), with trailing parameter context stripped
        return PascalCase(StripTrailingParameterContext(parts[0]));
    }

    /// <summary>
    /// Strips trailing parameter context from a selector part.
    /// e.g., "configureWithDocument" → "configure", "cancelSearchAnimated" → "cancelSearch"
    /// </summary>
    internal static string StripTrailingParameterContext(string part, bool isProtocolSecondPart = false)
    {
        // Generic preposition patterns: With*, At*, For*, From*, In*, On*, Of* + uppercase
        // e.g., "configureWithDocument" → "configure", "canActivateAtPoint" → "canActivate"
        string[] prepositions = ["With", "At", "For", "From", "In", "On", "Of", "Using", "By"];
        foreach (var prep in prepositions)
        {
            int idx = part.LastIndexOf(prep, StringComparison.Ordinal);
            if (idx > 0 && idx + prep.Length < part.Length && char.IsUpper(part[idx + prep.Length]))
            {
                return part[..idx];
            }
        }

        // Strip trailing "Animated" (common in UIKit-style APIs)
        if (part.EndsWith("Animated", StringComparison.Ordinal) && part.Length > "Animated".Length)
            return part[..^"Animated".Length];

        return part;
    }

    /// <summary>
    /// Renames "Block" to "Action" in method names (e.g., performBlock → performAction).
    /// Only applies when "Block" appears as a word boundary (preceded by lowercase letter or at start).
    /// </summary>
    private static string RenameBlockToAction(string name)
    {
        int idx = name.IndexOf("Block", StringComparison.Ordinal);
        if (idx < 0) return name;
        // Must be at a word boundary: preceded by lowercase or at start
        if (idx > 0 && !char.IsLower(name[idx - 1])) return name;
        // After "Block": must be end, uppercase, or specific continuations
        int after = idx + 5;
        if (after < name.Length && !char.IsUpper(name[after]) && name[after] != ':')
            return name;
        return name[..idx] + "Action" + name[after..];
    }

    /// <summary>Converts a name to PascalCase, normalizing common acronyms.</summary>
    public static string PascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var result = char.ToUpperInvariant(name[0]) + name[1..];
        return NormalizeAcronyms(result);
    }

    /// <summary>Normalizes a parameter name, lowering acronyms to match .NET conventions.</summary>
    public static string NormalizeParamName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var result = NormalizeAcronyms(name);
        // If the entire name is an acronym that got title-cased (e.g., JWT→Jwt), lowercase it entirely
        if (result.Length > 1 && char.IsUpper(result[0]) && result[1..].All(char.IsLower) &&
            name.All(char.IsUpper))
        {
            return result.ToLowerInvariant();
        }
        return result;
    }

    /// <summary>
    /// Normalizes common multi-letter acronyms to title case.
    /// e.g., "OpenURL" → "OpenUrl", "ExecutePDFAction" → "ExecutePdfAction"
    /// </summary>
    private static string NormalizeAcronyms(string name)
    {
        // Common acronyms that should be normalized in method names
        // Only normalize when they appear as part of a larger word, not standalone
        (string acronym, string normalized)[] acronyms = [
            ("UUID", "Uuid"),   // Must come before URL/URI/UID to avoid partial matches
            ("HTTPS", "Https"), // Must come before HTTP
            ("HTTP", "Http"),
            ("URL", "Url"),
            ("URI", "Uri"),
            ("UID", "Uid"),     // Must come after UUID
            ("PDF", "Pdf"),
            ("HUD", "Hud"),
            ("HTML", "Html"),
            ("JSON", "Json"),
            ("JWT", "Jwt"),
            ("XML", "Xml"),
            ("XMP", "Xmp"),
            ("API", "Api"),
            ("SDK", "Sdk"),
        ];

        foreach (var (acronym, normalized) in acronyms)
        {
            int idx = 0;
            while ((idx = name.IndexOf(acronym, idx, StringComparison.Ordinal)) >= 0)
            {
                int afterIdx = idx + acronym.Length;
                name = name[..idx] + normalized + name[afterIdx..];
                idx += normalized.Length;
            }
        }

        // Handle "ID" separately — only at end of name or followed by uppercase
        // to avoid matching inside words like "Identity", "Hidden"
        {
            int idx = 0;
            while ((idx = name.IndexOf("ID", idx, StringComparison.Ordinal)) >= 0)
            {
                int afterIdx = idx + 2;
                bool atEnd = afterIdx >= name.Length;
                bool followedByUpper = !atEnd && char.IsUpper(name[afterIdx]);
                bool precededByLower = idx > 0 && char.IsLower(name[idx - 1]);

                if (precededByLower && (atEnd || followedByUpper))
                {
                    name = name[..idx] + "Id" + name[afterIdx..];
                    idx += 2;
                }
                else
                {
                    idx += 2;
                }
            }
        }

        return name;
    }

    /// <summary>Checks if a selector part looks like a sender/source parameter name.</summary>
    private static bool IsSenderParameterName(string part)
    {
        // Common suffixes for sender parameters in delegate methods
        string[] suffixes = SenderSuffixes;
        foreach (var suffix in suffixes)
        {
            if (part.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static readonly string[] SenderSuffixes = [
        "Controller", "View", "Manager", "Delegate", "Bar", "Cell",
        "Picker", "Inspector", "Toolbar", "Button", "Item", "Store",
        "Search", "HUD", "Scrubber", "Presenter", "Container",
        "Coordinator", "Client", "Provider", "Service", "Handler",
        "Source", "Session", "Connection", "Cache",
        "List", "Document", "Parser", "Signer", "Editor",
        "Checkpointer", "Task", "Feature", "Object", "Library",
        "Processor", "Formatter", "Renderer",
    ];

    /// <summary>
    /// Strips an embedded sender prefix from a single-part protocol selector.
    /// e.g., "annotationGridViewControllerDidCancel" → "didCancel"
    /// Looks for a sender suffix (Controller, View, etc.) followed by a verb.
    /// </summary>
    internal static string? StripEmbeddedSenderPrefix(string part)
    {
        foreach (var suffix in SenderSuffixes)
        {
            int idx = part.IndexOf(suffix, StringComparison.Ordinal);
            if (idx <= 0) continue;

            int afterSuffix = idx + suffix.Length;
            if (afterSuffix >= part.Length) continue;

            // Must be followed by an uppercase letter (start of a verb like Did, Will, Should)
            if (!char.IsUpper(part[afterSuffix])) continue;

            string remainder = part[afterSuffix..];

            // Verify the remainder starts with a common verb/lifecycle word
            string[] verbs = [
                "Did", "Will", "Should", "Can", "Get", "Set",
            ];
            foreach (var verb in verbs)
            {
                if (remainder.StartsWith(verb, StringComparison.Ordinal))
                    return char.ToLowerInvariant(remainder[0]) + remainder[1..];
            }
        }
        return null;
    }
}
