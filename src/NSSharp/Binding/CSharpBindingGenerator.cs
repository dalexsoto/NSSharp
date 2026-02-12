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

    public string Generate(ObjCHeader header)
    {
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
        return value.Replace("< <", "<<").Replace("> >", ">>");
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

        AppendLine("static class CFunctions");
        AppendLine("{");
        _indent++;

        foreach (var f in header.Functions)
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
            AppendLine($"// @protocol {proto.Name}");

            AppendLine("[Protocol]");

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

        EmitMethodBody(method, isStatic);
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
        if (!isStatic && method.Selector.StartsWith("init") && method.ReturnType == "instancetype")
        {
            EmitConstructor(method);
            return;
        }

        if (isStatic)
            AppendLine("[Static]");

        EmitMethodBody(method, isStatic);
    }

    private void EmitConstructor(ObjCMethod method)
    {
        AppendLine($"[Export (\"{method.Selector}\")]");

        var csParams = MapMethodParameters(method.Parameters);
        var paramList = string.Join(", ", csParams.Select(p => $"{p.type} {p.name}"));

        AppendLine($"NativeHandle Constructor ({paramList});");
        AppendLine();
    }

    #endregion

    #region Properties

    private void EmitProperty(ObjCProperty prop, bool isProtocol)
    {
        var csType = ObjCTypeMapper.MapType(prop.Type);
        var csName = ObjCTypeMapper.PascalCase(prop.Name);
        var isReadonly = prop.Attributes.Contains("readonly");
        var isClassProp = prop.Attributes.Contains("class");

        // Build attribute parts
        var attrParts = new List<string>();

        if (prop.IsNullable)
            attrParts.Add("NullAllowed");

        // Determine argument semantic
        var semantic = GetArgumentSemantic(prop.Attributes);
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

        var accessors = isReadonly ? "{ get; }" : "{ get; set; }";
        AppendLine($"{csType} {csName} {accessors}");
        AppendLine();
    }

    private static string? GetArgumentSemantic(List<string> attributes)
    {
        if (attributes.Contains("copy")) return "Copy";
        if (attributes.Contains("retain") || attributes.Contains("strong")) return "Retain";
        if (attributes.Contains("assign") || attributes.Contains("weak")) return "Assign";
        return null;
    }

    #endregion

    #region Methods

    private void EmitMethodComment(ObjCMethod method, bool isStatic, string? prefix)
    {
        var sign = isStatic ? "+" : "-";
        var paramParts = method.Parameters.Select(p => $"({p.Type}){p.Name}");
        var prefixStr = prefix != null ? $"{prefix} " : "";
        AppendLine($"// {prefixStr}{sign}({method.ReturnType}){method.Selector};");
    }

    private void EmitMethodBody(ObjCMethod method, bool isStatic)
    {
        var csReturnType = ObjCTypeMapper.MapType(method.ReturnType);
        var csMethodName = ObjCTypeMapper.SelectorToMethodName(method.Selector);
        var csParams = MapMethodParameters(method.Parameters);

        // NullAllowed on return
        bool nullableReturn = method.ReturnType.Contains("Nullable") ||
                              method.ReturnType.Contains("nullable");
        if (nullableReturn)
            AppendLine("[return: NullAllowed]");

        AppendLine($"[Export (\"{method.Selector}\")]");

        var needsUnsafe = csReturnType.Contains("*") ||
                          csParams.Any(p => p.type.Contains("*"));
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

    private List<(string type, string name, bool nullable)> MapMethodParameters(List<ObjCParameter> parameters)
    {
        var result = new List<(string type, string name, bool nullable)>();
        foreach (var p in parameters)
        {
            var csType = ObjCTypeMapper.MapType(p.Type);
            result.Add((csType, p.Name, p.IsNullable));
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

    #endregion
}
