namespace NSSharp.Ast;

/// <summary>Represents the parsed contents of a single Objective-C header file.</summary>
public sealed class ObjCHeader
{
    public string File { get; set; } = string.Empty;
    public List<ObjCInterface> Interfaces { get; set; } = [];
    public List<ObjCProtocol> Protocols { get; set; } = [];
    public List<ObjCEnum> Enums { get; set; } = [];
    public List<ObjCStruct> Structs { get; set; } = [];
    public List<ObjCTypedef> Typedefs { get; set; } = [];
    public List<ObjCFunction> Functions { get; set; } = [];
    public ObjCForwardDeclarations ForwardDeclarations { get; set; } = new();
}

public sealed class ObjCInterface
{
    public string Name { get; set; } = string.Empty;
    public string? Superclass { get; set; }
    public List<string> Protocols { get; set; } = [];
    public string? Category { get; set; }
    public List<ObjCProperty> Properties { get; set; } = [];
    public List<ObjCMethod> InstanceMethods { get; set; } = [];
    public List<ObjCMethod> ClassMethods { get; set; } = [];
}

public sealed class ObjCProtocol
{
    public string Name { get; set; } = string.Empty;
    public List<string> InheritedProtocols { get; set; } = [];
    public List<ObjCProperty> Properties { get; set; } = [];
    public List<ObjCMethod> RequiredInstanceMethods { get; set; } = [];
    public List<ObjCMethod> RequiredClassMethods { get; set; } = [];
    public List<ObjCMethod> OptionalInstanceMethods { get; set; } = [];
    public List<ObjCMethod> OptionalClassMethods { get; set; } = [];
}

public sealed class ObjCProperty
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Attributes { get; set; } = [];
    public bool IsNullable { get; set; }
}

public sealed class ObjCMethod
{
    public string Selector { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public List<ObjCParameter> Parameters { get; set; } = [];
    public bool IsOptional { get; set; }
}

public sealed class ObjCParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
}

public sealed class ObjCEnum
{
    public string? Name { get; set; }
    public string? BackingType { get; set; }
    public bool IsOptions { get; set; }
    public List<ObjCEnumValue> Values { get; set; } = [];
}

public sealed class ObjCEnumValue
{
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class ObjCStruct
{
    public string Name { get; set; } = string.Empty;
    public List<ObjCStructField> Fields { get; set; } = [];
}

public sealed class ObjCStructField
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public sealed class ObjCTypedef
{
    public string Name { get; set; } = string.Empty;
    public string UnderlyingType { get; set; } = string.Empty;
}

public sealed class ObjCFunction
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public List<ObjCParameter> Parameters { get; set; } = [];
}

public sealed class ObjCForwardDeclarations
{
    public List<string> Classes { get; set; } = [];
    public List<string> Protocols { get; set; } = [];
}
