using System.Text;
using NSSharp.Ast;

namespace NSSharp.Binding;

/// <summary>
/// Generates C# binding code from parsed ObjC header AST.
/// Output format follows the Xamarin.iOS/MAUI binding API definition style
/// (matching dotnet/macios Objective-Sharpie output).
/// </summary>
public sealed class CSharpBindingGenerator
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _emitCBindings;

    public string Generate(ObjCHeader header, bool emitCBindings = false)
    {
        _emitCBindings = emitCBindings;

        // Merge categories into their parent class within this header
        MergeCategoriesInPlace(header);

        _sb.Clear();
        _indent = 0;

        EmitUsings(header);
        EmitForwardDeclarations(header);
        EmitEnums(header);
        EmitStructs(header);
        EmitFunctions(header);
        EmitProtocols(header);
        EmitInterfaces(header);
        EmitTypedefs(header);

        return _sb.ToString().TrimEnd();
    }

    /// <summary>Builds a typedef→base type resolution map from parsed headers.</summary>
    public static void BuildTypedefMap(List<ObjCHeader> headers)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            foreach (var td in header.Typedefs)
            {
                if (!string.IsNullOrEmpty(td.Name) && !string.IsNullOrEmpty(td.UnderlyingType))
                    map.TryAdd(td.Name, td.UnderlyingType);
            }
        }
        ObjCTypeMapper.SetTypedefMap(map);
    }

    /// <summary>
    /// Merges ObjC category members into their parent class across multiple headers.
    /// Call this before generating bindings when processing an xcframework.
    /// </summary>
    public static void MergeCategories(List<ObjCHeader> headers)
    {
        // Build index of all main class interfaces (no category) by name
        var mainClasses = new Dictionary<string, ObjCInterface>();
        foreach (var header in headers)
        {
            foreach (var iface in header.Interfaces)
            {
                if (iface.Category == null && !mainClasses.ContainsKey(iface.Name))
                    mainClasses[iface.Name] = iface;
            }
        }

        // Merge category members into main class
        foreach (var header in headers)
        {
            var merged = new List<ObjCInterface>();
            foreach (var iface in header.Interfaces)
            {
                if (iface.Category != null && mainClasses.TryGetValue(iface.Name, out var main))
                {
                    main.Properties.AddRange(iface.Properties);
                    main.InstanceMethods.AddRange(iface.InstanceMethods);
                    main.ClassMethods.AddRange(iface.ClassMethods);
                    foreach (var p in iface.Protocols)
                    {
                        if (!main.Protocols.Contains(p))
                            main.Protocols.Add(p);
                    }
                    // Don't add this category interface to output
                }
                else
                {
                    merged.Add(iface);
                }
            }
            header.Interfaces = merged;
        }
    }

    /// <summary>
    /// Merges categories within a single header.
    /// </summary>
    private static void MergeCategoriesInPlace(ObjCHeader header)
    {
        var mainClasses = new Dictionary<string, ObjCInterface>();
        foreach (var iface in header.Interfaces)
        {
            if (iface.Category == null && !mainClasses.ContainsKey(iface.Name))
                mainClasses[iface.Name] = iface;
        }

        var merged = new List<ObjCInterface>();
        foreach (var iface in header.Interfaces)
        {
            if (iface.Category != null && mainClasses.TryGetValue(iface.Name, out var main))
            {
                main.Properties.AddRange(iface.Properties);
                main.InstanceMethods.AddRange(iface.InstanceMethods);
                main.ClassMethods.AddRange(iface.ClassMethods);
                foreach (var p in iface.Protocols)
                {
                    if (!main.Protocols.Contains(p))
                        main.Protocols.Add(p);
                }
            }
            else
            {
                merged.Add(iface);
            }
        }
        header.Interfaces = merged;
    }

    #region Usings

    private void EmitUsings(ObjCHeader header)
    {
        var usings = new HashSet<string> { "Foundation" };

        // Detect if we need ObjCRuntime, System, etc.
        if (header.Enums.Any(e => ObjCTypeMapper.IsNativeEnum(e.BackingType)))
            usings.Add("ObjCRuntime");

        if (header.Structs.Count > 0)
            usings.Add("System.Runtime.InteropServices");

        if (header.Functions.Count > 0)
        {
            usings.Add("System.Runtime.InteropServices");
            usings.Add("ObjCRuntime");
        }

        if (header.Enums.Any(e => e.IsOptions))
            usings.Add("System");

        foreach (var u in usings.Order())
        {
            AppendLine($"using {u};");
        }
        AppendLine();
    }

    #endregion

    #region Enums

    private void EmitEnums(ObjCHeader header)
    {
        foreach (var e in header.Enums)
        {
            if (e.Values.Count == 0 && string.IsNullOrEmpty(e.Name))
                continue;

            var name = e.Name ?? "AnonymousEnum";
            var backingType = ObjCTypeMapper.MapEnumBackingType(e.BackingType);

            if (ObjCTypeMapper.IsNativeEnum(e.BackingType))
                AppendLine("[Native]");

            if (e.IsOptions)
                AppendLine("[Flags]");

            AppendLine($"public enum {name} : {backingType}");
            AppendLine("{");
            _indent++;

            for (int i = 0; i < e.Values.Count; i++)
            {
                var v = e.Values[i];
                var enumMemberName = StripEnumPrefix(v.Name, name);
                var comma = i < e.Values.Count - 1 ? "," : "";

                if (v.Value != null)
                    AppendLine($"{enumMemberName} = {CleanEnumValue(v.Value)}{comma}");
                else
                    AppendLine($"{enumMemberName}{comma}");
            }

            _indent--;
            AppendLine("}");
            AppendLine();
        }
    }

    private static string StripEnumPrefix(string memberName, string enumName)
    {
        // Try to strip the enum name prefix from member names
        if (memberName.StartsWith(enumName, StringComparison.Ordinal) &&
            memberName.Length > enumName.Length)
        {
            var rest = memberName[enumName.Length..];
            // Handle underscore separator
            if (rest.StartsWith('_'))
                rest = rest[1..];
            if (rest.Length > 0 && char.IsUpper(rest[0]))
                return rest;
        }
        return memberName;
    }

    private static string CleanEnumValue(string value)
    {
        // Clean up spacing in expressions like "1 < < 2" -> "1 << 2"
        var cleaned = value.Replace("< <", "<<").Replace("> >", ">>");
        // Replace common C max value constants
        cleaned = cleaned switch
        {
            "UINT8_MAX" => "byte.MaxValue",
            "UINT16_MAX" => "ushort.MaxValue",
            "UINT32_MAX" => "uint.MaxValue",
            "UINT_MAX" => "uint.MaxValue",
            "UINT64_MAX" => "ulong.MaxValue",
            "INT8_MAX" => "sbyte.MaxValue",
            "INT16_MAX" => "short.MaxValue",
            "INT32_MAX" => "int.MaxValue",
            "INT_MAX" => "int.MaxValue",
            "INT64_MAX" => "long.MaxValue",
            "NSIntegerMax" => "long.MaxValue",
            "NSUIntegerMax" => "ulong.MaxValue",
            _ => cleaned,
        };
        return cleaned;
    }

    #endregion

    #region Structs

    private void EmitStructs(ObjCHeader header)
    {
        foreach (var s in header.Structs)
        {
            if (string.IsNullOrEmpty(s.Name))
                continue;

            AppendLine("[StructLayout (LayoutKind.Sequential)]");
            AppendLine($"public struct {s.Name}");
            AppendLine("{");
            _indent++;

            foreach (var f in s.Fields)
            {
                if (string.IsNullOrEmpty(f.Name))
                    continue;
                var csType = ObjCTypeMapper.MapType(f.Type);
                AppendLine($"public {csType} {f.Name};");
                AppendLine();
            }

            _indent--;
            AppendLine("}");
            AppendLine();
        }
    }

    #endregion

    #region Functions (P/Invoke)

    private void EmitFunctions(ObjCHeader header)
    {
        if (header.Functions.Count == 0) return;

        // Separate extern constants (no params, no parens) from real functions
        var constants = header.Functions.Where(f => f.Parameters.Count == 0 && IsExternConstantType(f.ReturnType)).ToList();
        var functions = header.Functions.Except(constants).ToList();

        // Emit [Field] entries for extern constants (always included)
        if (constants.Count > 0)
        {
            AppendLine("[Static]");
            AppendLine("interface Constants");
            AppendLine("{");
            _indent++;

            foreach (var c in constants)
            {
                var csType = ObjCTypeMapper.MapType(c.ReturnType);
                AppendLine($"// extern {c.ReturnType} {c.Name};");

                // Add [Notification] for NSNotificationName constants
                if (IsNotificationConstant(c.ReturnType, c.Name))
                    AppendLine("[Notification]");

                AppendLine($"[Field (\"{c.Name}\")]");
                AppendLine($"{csType} {c.Name} {{ get; }}");
                AppendLine();
            }

            _indent--;
            AppendLine("}");
            AppendLine();
        }

        // Emit DllImport for real functions (only when opt-in)
        if (functions.Count > 0 && _emitCBindings)
        {
            AppendLine("static class CFunctions");
            AppendLine("{");
            _indent++;

            foreach (var f in functions)
            {
                // Comment with original declaration
                var paramStr = string.Join(", ", f.Parameters.Select(p =>
                    p.Name == "..." ? "..." : $"{p.Type} {p.Name}"));
                AppendLine($"// extern {f.ReturnType} {f.Name} ({paramStr});");

                AppendLine("[DllImport (\"__Internal\")]");

                var csReturnType = ObjCTypeMapper.MapType(f.ReturnType);
                var csParams = MapFunctionParameters(f.Parameters);

                var needsUnsafe = csReturnType.StartsWith("unsafe ") ||
                                  csParams.Any(p => p.type.StartsWith("unsafe ") || p.type.Contains("*"));

                var unsafeKw = needsUnsafe ? "unsafe " : "";
                var cleanReturn = csReturnType.Replace("unsafe ", "");

                var paramList = string.Join(", ", csParams.Select(p =>
                {
                    var cleanType = p.type.Replace("unsafe ", "");
                    return $"{cleanType} {p.name}";
                }));

                AppendLine($"static extern {unsafeKw}{cleanReturn} {f.Name} ({paramList});");
                AppendLine();
            }

            _indent--;
            AppendLine("}");
            AppendLine();
        }
    }

    private static bool IsExternConstantType(string type)
    {
        // Extern constants have `const` in their type and no parameters
        return type.Contains("const");
    }

    private static bool IsNotificationConstant(string type, string name)
    {
        // NSNotificationName typed constants are notifications
        return type.Contains("NSNotificationName") || name.EndsWith("Notification");
    }

    /// <summary>
    /// Returns true if the method's last selector part suggests a completion handler
    /// (i.e. it should get [Async]).
    /// </summary>
    private static bool HasCompletionHandler(ObjCMethod method)
    {
        if (method.Parameters.Count == 0) return false;

        var selector = method.Selector;
        // Check if the last selector part ends with completion:/completionHandler:/completionBlock:
        if (selector.EndsWith("completion:") ||
            selector.EndsWith("completionHandler:") ||
            selector.EndsWith("completionBlock:"))
            return true;

        // Also check the last parameter's type — if it's a block type
        var lastParam = method.Parameters[^1];
        if (lastParam.Type.Contains("(^)") || lastParam.Type.Contains("( ^ )"))
        {
            // Only if the selector part also looks like a completion handler name
            var parts = selector.TrimEnd(':').Split(':');
            if (parts.Length > 0)
            {
                var lastPart = parts[^1].ToLowerInvariant();
                if (lastPart.Contains("completion") || lastPart.Contains("handler"))
                    return true;
            }
        }

        return false;
    }

    private List<(string type, string name)> MapFunctionParameters(List<ObjCParameter> parameters)
    {
        var result = new List<(string type, string name)>();
        foreach (var p in parameters)
        {
            if (p.Name == "...")
            {
                result.Add(("IntPtr", "varArgs"));
                continue;
            }
            var csType = ObjCTypeMapper.MapType(p.Type);
            result.Add((csType, p.Name));
        }
        return result;
    }

    #endregion

    #region Protocols

    private void EmitProtocols(ObjCHeader header)
    {
        foreach (var proto in header.Protocols)
        {
            // Emit the I-prefixed protocol stub interface (for type safety in bindings)
            AppendLine($"interface I{proto.Name} {{}}");
            AppendLine();

            AppendLine($"// @protocol {proto.Name}");

            var isDelegate = proto.Name.EndsWith("Delegate") || proto.Name.EndsWith("DataSource");

            if (isDelegate)
            {
                AppendLine("[Protocol, Model]");
                AppendLine("[BaseType (typeof (NSObject))]");
            }
            else
            {
                AppendLine("[Protocol]");
            }

            // Build inheritance list
            var inherits = proto.InheritedProtocols
                .Where(p => p != "NSObject")
                .Select(p => $"I{p}")
                .ToList();
            var inheritStr = inherits.Count > 0 ? $" : {string.Join(", ", inherits)}" : "";

            AppendLine($"interface {proto.Name}{inheritStr}");
            AppendLine("{");
            _indent++;

            // Required methods
            foreach (var m in proto.RequiredInstanceMethods)
                EmitProtocolMethod(m, isStatic: false, isRequired: true);

            foreach (var m in proto.RequiredClassMethods)
                EmitProtocolMethod(m, isStatic: true, isRequired: true);

            // Properties (from protocol)
            foreach (var p in proto.Properties)
                EmitProperty(p, isProtocol: true);

            // Optional methods
            foreach (var m in proto.OptionalInstanceMethods)
                EmitProtocolMethod(m, isStatic: false, isRequired: false);

            foreach (var m in proto.OptionalClassMethods)
                EmitProtocolMethod(m, isStatic: true, isRequired: false);

            _indent--;
            AppendLine("}");
            AppendLine();
        }
    }

    private void EmitProtocolMethod(ObjCMethod method, bool isStatic, bool isRequired)
    {
        var prefix = isRequired ? "@required" : "@optional";
        EmitMethodComment(method, isStatic, prefix);

        if (isRequired)
            AppendLine("[Abstract]");

        EmitMethodBody(method, isStatic, isProtocolMethod: true);
    }

    #endregion

    #region Interfaces

    private void EmitInterfaces(ObjCHeader header)
    {
        foreach (var iface in header.Interfaces)
        {
            // Comment
            if (iface.Category != null)
                AppendLine($"// @interface {iface.Category} ({iface.Name})");
            else
                AppendLine($"// @interface {iface.Name}{(iface.Superclass != null ? $" : {iface.Superclass}" : "")}");

            // Attributes
            if (iface.Category != null)
            {
                AppendLine("[Category]");
                AppendLine($"[BaseType (typeof ({iface.Name}))]");
            }
            else if (iface.Superclass != null)
            {
                AppendLine($"[BaseType (typeof ({iface.Superclass}))]");
            }

            // Check for disabled default constructor
            bool hasInit = iface.InstanceMethods.Any(m =>
                m.Selector.StartsWith("init") && m.ReturnType == "instancetype");
            // Note: NS_UNAVAILABLE on init would need more parsing; skip for now

            // Build type name and inheritance
            var typeName = iface.Category != null
                ? $"{iface.Name}_{iface.Category}"
                : iface.Name;

            var protocols = iface.Protocols
                .Select(p => $"I{p}")
                .ToList();
            var inheritStr = protocols.Count > 0 ? $" : {string.Join(", ", protocols)}" : "";

            AppendLine($"interface {typeName}{inheritStr}");
            AppendLine("{");
            _indent++;

            // Properties
            foreach (var prop in iface.Properties)
                EmitProperty(prop, isProtocol: false);

            // Instance methods
            foreach (var m in iface.InstanceMethods)
                EmitInterfaceMethod(m, iface, isStatic: false);

            // Class methods
            foreach (var m in iface.ClassMethods)
                EmitInterfaceMethod(m, iface, isStatic: true);

            _indent--;
            AppendLine("}");
            AppendLine();
        }
    }

    private void EmitInterfaceMethod(ObjCMethod method, ObjCInterface iface, bool isStatic)
    {
        EmitMethodComment(method, isStatic, null);

        // Check if this is a constructor (init method returning instancetype)
        bool isInstancetype = method.ReturnType == "instancetype" ||
                              method.ReturnType.Contains("instancetype");
        if (!isStatic && method.Selector.StartsWith("init") && isInstancetype)
        {
            EmitConstructor(method);
            return;
        }

        if (isStatic)
            AppendLine("[Static]");

        EmitMethodBody(method, isStatic, enclosingTypeName: iface.Name);
    }

    private void EmitConstructor(ObjCMethod method)
    {
        if (method.IsDesignatedInitializer)
            AppendLine("[DesignatedInitializer]");

        AppendLine($"[Export (\"{method.Selector}\")]");

        var csParams = MapMethodParameters(method.Parameters, method.InNonnullScope);
        var paramList = string.Join(", ", csParams.Select(p => $"{p.type} {p.name}"));

        AppendLine($"NativeHandle Constructor ({paramList});");
        AppendLine();
    }

    #endregion

    #region Properties

    private void EmitProperty(ObjCProperty prop, bool isProtocol)
    {
        // Protocol properties are decomposed into getter/setter method pairs
        // (Xamarin convention: each accessor can be independently @optional/@required)
        if (isProtocol)
        {
            EmitProtocolPropertyAccessors(prop);
            return;
        }

        var csType = ObjCTypeMapper.MapType(prop.Type);
        var csName = ObjCTypeMapper.PascalCase(prop.Name);
        var isReadonly = prop.Attributes.Contains("readonly");
        var isClassProp = prop.Attributes.Contains("class");

        // Build attribute parts
        var attrParts = new List<string>();

        bool isWeak = prop.Attributes.Contains("weak");
        bool isNullable = prop.IsNullable || isWeak ||
                          (!prop.InNonnullScope && IsObjectPointerType(prop.Type));
        if (isNullable)
            attrParts.Add("NullAllowed");

        // Determine argument semantic
        var semantic = GetArgumentSemantic(prop.Attributes, prop.Type);
        var exportParts = $"\"{prop.Name}\"";
        if (semantic != null)
            exportParts += $", ArgumentSemantic.{semantic}";

        attrParts.Add($"Export ({exportParts})");

        // Comment
        var attrStr = string.Join(", ", prop.Attributes);
        if (attrStr.Length > 0) attrStr = $" ({attrStr})";
        AppendLine($"// @property{attrStr} {prop.Type} {prop.Name};");

        // Emit attributes
        if (isClassProp)
            AppendLine("[Static]");

        AppendLine($"[{string.Join(", ", attrParts)}]");

        // Handle custom getter name (e.g., getter=isEnabled) with [Bind] syntax
        var getterAttr = prop.Attributes.FirstOrDefault(a => a.StartsWith("getter="));
        string? customGetter = getterAttr?.Substring("getter=".Length);

        var accessors = isReadonly ? "{ get; }" : "{ get; set; }";
        if (customGetter != null)
            accessors = isReadonly
                ? $"{{ [Bind (\"{customGetter}\")] get; }}"
                : $"{{ [Bind (\"{customGetter}\")] get; set; }}";

        AppendLine($"{csType} {csName} {accessors}");
        AppendLine();
    }

    private void EmitProtocolPropertyAccessors(ObjCProperty prop)
    {
        var csType = ObjCTypeMapper.MapType(prop.Type);
        var isReadonly = prop.Attributes.Contains("readonly");
        var isClassProp = prop.Attributes.Contains("class");

        bool isWeak = prop.Attributes.Contains("weak");
        bool isNullable = prop.IsNullable || isWeak ||
                          (!prop.InNonnullScope && IsObjectPointerType(prop.Type));

        // Check for custom getter name (e.g., getter=isEnabled)
        var getterAttr = prop.Attributes.FirstOrDefault(a => a.StartsWith("getter="));
        string? customGetter = getterAttr?.Substring("getter=".Length);
        string getterSelector = customGetter ?? prop.Name;

        // Comment
        var attrStr = string.Join(", ", prop.Attributes);
        if (attrStr.Length > 0) attrStr = $" ({attrStr})";
        AppendLine($"// @property{attrStr} {prop.Type} {prop.Name};");

        // @required properties stay as C# properties with [Abstract] and optional [Bind]
        if (!prop.IsOptional)
        {
            AppendLine("[Abstract]");
            if (isNullable)
                AppendLine("[NullAllowed]");
            if (isClassProp)
                AppendLine("[Static]");
            AppendLine($"[Export (\"{prop.Name}\")]");

            var csName = ObjCTypeMapper.PascalCase(prop.Name);
            string accessors;
            if (customGetter != null)
                accessors = isReadonly
                    ? $"{{ [Bind (\"{customGetter}\")] get; }}"
                    : $"{{ [Bind (\"{customGetter}\")] get; set; }}";
            else
                accessors = isReadonly ? "{ get; }" : "{ get; set; }";

            AppendLine($"{csType} {csName} {accessors}");
            AppendLine();
            return;
        }

        // @optional properties are decomposed into getter/setter methods
        // Getter
        if (isNullable)
            AppendLine("[return: NullAllowed]");
        if (isClassProp)
            AppendLine("[Static]");
        AppendLine($"[Export (\"{getterSelector}\")]");

        AppendLine($"{csType} Get{ObjCTypeMapper.PascalCase(prop.Name)} ();");
        AppendLine();

        // Setter (if not readonly)
        if (!isReadonly)
        {
            if (isClassProp)
                AppendLine("[Static]");
            AppendLine($"[Export (\"set{char.ToUpper(prop.Name[0])}{prop.Name.Substring(1)}:\")]");

            var nullAttr = isNullable ? "[NullAllowed] " : "";
            var paramName = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);
            AppendLine($"void Set{ObjCTypeMapper.PascalCase(prop.Name)} ({nullAttr}{csType} {paramName});");
            AppendLine();
        }
    }

    private static string? GetArgumentSemantic(List<string> attributes, string type = "")
    {
        if (attributes.Contains("copy")) return "Copy";
        if (attributes.Contains("retain") || attributes.Contains("strong")) return "Strong";
        if (attributes.Contains("weak")) return "Weak";
        if (attributes.Contains("assign")) return "Assign";
        // Default: Strong for object pointer types without explicit semantic (matches sharpie)
        if (type.Contains('*') || type == "id" || type.StartsWith("id<"))
            return "Strong";
        return null;
    }

    #endregion

    #region Methods

    /// <summary>Checks if a method name starts with a verb pattern (action, not getter).</summary>
    private static bool IsVerbPrefixedName(string name)
    {
        string[] verbPrefixes = [
            "Add", "Remove", "Get", "Set", "Did", "Will", "Should", "Can", "Is", "Has",
            "Show", "Hide", "Present", "Dismiss", "Perform", "Execute", "Handle",
            "Process", "Create", "Delete", "Update", "Insert", "Apply", "Cancel",
            "Start", "Stop", "Begin", "End", "Continue", "Resume", "Pause",
            "Toggle", "Enable", "Disable", "Select", "Deselect", "Clear", "Reset",
            "Load", "Save", "Open", "Close", "Send", "Receive", "Prepare",
            "Configure", "Register", "Unregister", "Notify", "Observe",
            "Validate", "Verify", "Check", "Test", "Compare", "Sort", "Filter",
            "Draw", "Render", "Layout", "Animate", "Scroll", "Zoom", "Navigate",
            "Reload", "Refresh", "Discard", "Revert", "Undo", "Redo",
            "Log", "Print", "Export", "Import", "Encode", "Decode",
            "Allow", "Deny", "Accept", "Reject", "Submit", "Forward",
            "Match", "Common", "Seek", "Override", "Invalidate",
            "Try", "Request", "Restore", "Fetch", "Contains", "Dequeue",
        ];

        foreach (var prefix in verbPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private void EmitMethodComment(ObjCMethod method, bool isStatic, string? prefix)
    {
        var sign = isStatic ? "+" : "-";
        var paramParts = method.Parameters.Select(p => $"({p.Type}){p.Name}");
        var prefixStr = prefix != null ? $"{prefix} " : "";
        AppendLine($"// {prefixStr}{sign}({method.ReturnType}){method.Selector};");
    }

    private void EmitMethodBody(ObjCMethod method, bool isStatic, bool isProtocolMethod = false, string? enclosingTypeName = null)
    {
        var csReturnType = ObjCTypeMapper.MapType(method.ReturnType);

        // Resolve instancetype to the enclosing class name for non-constructor methods
        if (csReturnType == "instancetype" && enclosingTypeName != null)
            csReturnType = enclosingTypeName;

        var csMethodName = ObjCTypeMapper.SelectorToMethodName(method.Selector, isProtocolMethod);
        var csParams = MapMethodParameters(method.Parameters, method.InNonnullScope);

        // Add "Get" prefix for non-void getter-like methods with parameters
        // Methods with parameters that return non-void and don't start with a verb get "Get" prefix
        if (csReturnType != "void" && !IsVerbPrefixedName(csMethodName)
            && !method.Selector.StartsWith("init", StringComparison.Ordinal)
            && method.Parameters.Count > 0)
        {
            csMethodName = "Get" + csMethodName;
        }

        // NullAllowed on return
        bool nullableReturn = method.IsReturnNullable ||
                              method.ReturnType.Contains("Nullable") ||
                              method.ReturnType.Contains("nullable") ||
                              (!method.InNonnullScope && IsObjectPointerType(method.ReturnType));
        if (nullableReturn)
            AppendLine("[return: NullAllowed]");

        if (method.IsDesignatedInitializer)
            AppendLine("[DesignatedInitializer]");

        // Emit [Async] for methods with completion handler parameters (skip for protocol methods)
        if (!isProtocolMethod && HasCompletionHandler(method))
            AppendLine("[Async]");

        AppendLine($"[Export (\"{method.Selector}\")]");

        // Check if unsafe is needed (actual pointer types, not comments like /* block type */)
        static string stripComments(string t) => System.Text.RegularExpressions.Regex.Replace(t, @"/\*.*?\*/", "");
        var needsUnsafe = stripComments(csReturnType).Contains('*') ||
                          csParams.Any(p => stripComments(p.type).Contains('*'));
        var unsafeKw = needsUnsafe ? "unsafe " : "";
        var cleanReturn = csReturnType.Replace("unsafe ", "");

        var paramList = string.Join(", ", csParams.Select(p =>
        {
            var attrs = new List<string>();
            if (p.nullable) attrs.Add("[NullAllowed]");
            var cleanType = p.type.Replace("unsafe ", "");
            var prefix = attrs.Count > 0 ? string.Join(" ", attrs) + " " : "";
            return $"{prefix}{cleanType} {p.name}";
        }));

        AppendLine($"{unsafeKw}{cleanReturn} {csMethodName} ({paramList});");
        AppendLine();
    }

    private List<(string type, string name, bool nullable)> MapMethodParameters(List<ObjCParameter> parameters, bool inNonnullScope = true)
    {
        var result = new List<(string type, string name, bool nullable)>();
        foreach (var p in parameters)
        {
            var csType = ObjCTypeMapper.MapType(p.Type);
            bool isNullable = p.IsNullable || (!inNonnullScope && IsObjectPointerType(p.Type));

            // out NSError parameters are always nullable in Xamarin bindings
            if (csType == "out NSError")
                isNullable = true;

            result.Add((csType, p.Name, isNullable));
        }
        return result;
    }

    #endregion

    #region Typedefs

    private void EmitTypedefs(ObjCHeader header)
    {
        foreach (var td in header.Typedefs)
        {
            if (td.UnderlyingType.Contains("(^)"))
            {
                // Block typedef → delegate
                AppendLine($"// typedef {td.UnderlyingType}");
                AppendLine($"delegate void {td.Name} (/* block params */);");
                AppendLine();
            }
        }
    }

    #endregion

    #region Forward declarations

    private void EmitForwardDeclarations(ObjCHeader header)
    {
        // Forward declarations are typically not emitted in bindings,
        // but we note them as comments
        foreach (var cls in header.ForwardDeclarations.Classes)
            AppendLine($"// @class {cls};");
        foreach (var proto in header.ForwardDeclarations.Protocols)
            AppendLine($"// @protocol {proto};");
        if (header.ForwardDeclarations.Classes.Count > 0 || header.ForwardDeclarations.Protocols.Count > 0)
            AppendLine();
    }

    #endregion

    #region Output helpers

    private void AppendLine(string line = "")
    {
        if (string.IsNullOrEmpty(line))
        {
            _sb.AppendLine();
        }
        else
        {
            _sb.Append('\t', _indent);
            _sb.AppendLine(line);
        }
    }

    /// <summary>
    /// Returns true if the ObjC type is an object pointer type (i.e. not a primitive).
    /// Used to determine if NullAllowed should be applied when outside NS_ASSUME_NONNULL scope.
    /// </summary>
    private static bool IsObjectPointerType(string objcType)
    {
        var t = objcType.Replace("const", "").Replace("nullable", "").Replace("_Nullable", "")
                        .Replace("__nullable", "").Replace("nonnull", "").Replace("_Nonnull", "")
                        .Replace("__nonnull", "").Replace("__kindof", "").Trim();

        // Pointer types (NSString *, id, etc.)
        if (t.Contains('*') || t == "id" || t.StartsWith("id<"))
            return true;

        // Common ObjC class types that may appear without *
        if (t.StartsWith("NS") || t.StartsWith("UI") || t.StartsWith("CL") ||
            t.StartsWith("MK") || t.StartsWith("AV") || t.StartsWith("SK") ||
            t.StartsWith("WK") || t.StartsWith("PSPDF") || t.StartsWith("PSC"))
            return !IsPrimitiveNSType(t);

        return false;
    }

    private static bool IsPrimitiveNSType(string type)
    {
        return type is "NSInteger" or "NSUInteger" or "NSTimeInterval"
            or "NSComparisonResult" or "NSRange" or "NSZone";
    }

    #endregion
}
