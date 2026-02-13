using System.Text;
using NSSharp.Ast;
using NSSharp.Lexer;

namespace NSSharp.Parser;

public sealed class ObjCParser
{
    private readonly List<Token> _tokens;
    private int _pos;
    private bool _inNonnullScope;

    public ObjCParser(List<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public ObjCHeader Parse(string fileName)
    {
        var header = new ObjCHeader { File = fileName };

        while (!IsAtEnd())
        {
            try
            {
                if (Match(TokenKind.NonnullBegin))
                    _inNonnullScope = true;
                else if (Match(TokenKind.NonnullEnd))
                    _inNonnullScope = false;
                else if (Match(TokenKind.AtInterface))
                    header.Interfaces.Add(ParseInterface());
                else if (Match(TokenKind.AtProtocol))
                    ParseProtocolOrForward(header);
                else if (Match(TokenKind.AtClass))
                    ParseForwardClassDeclaration(header);
                else if (Check(TokenKind.Typedef))
                    ParseTypedef(header);
                else if (Check(TokenKind.Enum))
                    header.Enums.Add(ParseEnum());
                else if (Check(TokenKind.Struct))
                    header.Structs.Add(ParseStruct());
                else if (Check(TokenKind.Extern) || Check(TokenKind.Static) || Check(TokenKind.Inline))
                    TryParseFunctionOrSkip(header);
                else if ((Check(TokenKind.Identifier) || Check(TokenKind.Void)) && LooksLikeFunctionDeclaration())
                    TryParseFunctionOrSkip(header);
                else
                    Advance(); // skip unknown tokens
            }
            catch (ParseException)
            {
                // Recovery: skip to next semicolon or @end
                SkipToRecoveryPoint();
            }
        }

        return header;
    }

    #region Interface

    private ObjCInterface ParseInterface()
    {
        var iface = new ObjCInterface();
        iface.Name = Expect(TokenKind.Identifier, "Expected interface name").Value;

        // Category: @interface Foo (Bar) or extension @interface Foo ()
        if (Match(TokenKind.OpenParen))
        {
            if (Check(TokenKind.Identifier))
                iface.Category = Advance().Value;
            else
                iface.Category = ""; // Extension or macro-swallowed category name
            Expect(TokenKind.CloseParen, "Expected ')' after category name");
        }

        // Superclass
        if (Match(TokenKind.Colon))
        {
            iface.Superclass = Expect(TokenKind.Identifier, "Expected superclass name").Value;

            // Skip generic type parameters on superclass (e.g. NSObject<GenericType *>)
            if (Check(TokenKind.OpenAngle) && LooksLikeGenericParams())
                SkipBalancedAngles();
        }

        // Adopted protocols
        if (Match(TokenKind.OpenAngle))
        {
            iface.Protocols = ParseIdentifierList(TokenKind.CloseAngle);
            Expect(TokenKind.CloseAngle, "Expected '>'");
        }

        // Body until @end
        ParseInterfaceBody(iface);
        Expect(TokenKind.AtEnd, "Expected '@end'");
        return iface;
    }

    private void ParseInterfaceBody(ObjCInterface iface)
    {
        // Skip instance variable blocks { ... }
        if (Match(TokenKind.OpenBrace))
            SkipBalancedBraces();

        while (!Check(TokenKind.AtEnd) && !IsAtEnd())
        {
            if (Match(TokenKind.AtProperty))
            {
                iface.Properties.Add(ParseProperty());
            }
            else if (Check(TokenKind.Minus))
            {
                Advance();
                iface.InstanceMethods.Add(ParseMethod());
            }
            else if (Check(TokenKind.Plus))
            {
                Advance();
                iface.ClassMethods.Add(ParseMethod());
            }
            else if (Match(TokenKind.OpenBrace))
            {
                SkipBalancedBraces();
            }
            else
            {
                // Detect init-unavailable macros (e.g., PSPDF_EMPTY_INIT_UNAVAILABLE)
                if (Check(TokenKind.Identifier))
                {
                    var val = Peek().Value;
                    if (val.Contains("INIT_UNAVAILABLE") || val.Contains("EMPTY_INIT"))
                        iface.IsInitUnavailable = true;
                }
                Advance();
            }
        }
    }

    #endregion

    #region Protocol

    private void ParseProtocolOrForward(ObjCHeader header)
    {
        var name = Expect(TokenKind.Identifier, "Expected protocol name").Value;

        // Forward declaration: @protocol Foo, Bar;
        if (Match(TokenKind.Comma) || Check(TokenKind.Semicolon))
        {
            header.ForwardDeclarations.Protocols.Add(name);
            if (Previous().Kind == TokenKind.Comma)
            {
                while (!Check(TokenKind.Semicolon) && !IsAtEnd())
                {
                    if (Check(TokenKind.Identifier))
                        header.ForwardDeclarations.Protocols.Add(Advance().Value);
                    else if (!Match(TokenKind.Comma))
                        break;
                }
            }
            Match(TokenKind.Semicolon);
            return;
        }

        var proto = new ObjCProtocol { Name = name };

        // Inherited protocols
        if (Match(TokenKind.OpenAngle))
        {
            proto.InheritedProtocols = ParseIdentifierList(TokenKind.CloseAngle);
            Expect(TokenKind.CloseAngle, "Expected '>'");
        }

        ParseProtocolBody(proto, header);
        Expect(TokenKind.AtEnd, "Expected '@end'");
        header.Protocols.Add(proto);
    }

    private void ParseProtocolBody(ObjCProtocol proto, ObjCHeader header)
    {
        bool isOptional = false;

        while (!Check(TokenKind.AtEnd) && !IsAtEnd())
        {
            if (Match(TokenKind.AtOptional)) { isOptional = true; continue; }
            if (Match(TokenKind.AtRequired)) { isOptional = false; continue; }

            if (Match(TokenKind.AtProperty))
            {
                var prop = ParseProperty();
                prop.IsOptional = isOptional;
                proto.Properties.Add(prop);
            }
            else if (Check(TokenKind.Minus))
            {
                Advance();
                var method = ParseMethod();
                method.IsOptional = isOptional;
                if (isOptional)
                    proto.OptionalInstanceMethods.Add(method);
                else
                    proto.RequiredInstanceMethods.Add(method);
            }
            else if (Check(TokenKind.Plus))
            {
                Advance();
                var method = ParseMethod();
                method.IsOptional = isOptional;
                if (isOptional)
                    proto.OptionalClassMethods.Add(method);
                else
                    proto.RequiredClassMethods.Add(method);
            }
            else if (Check(TokenKind.Typedef))
            {
                // typedef inside protocol body (e.g., NS_OPTIONS declared inline)
                ParseTypedef(header);
            }
            else
            {
                Advance();
            }
        }
    }

    #endregion

    #region Property

    private ObjCProperty ParseProperty()
    {
        var prop = new ObjCProperty { InNonnullScope = _inNonnullScope };

        // Parse attributes: (nonatomic, copy, nullable, ...)
        if (Match(TokenKind.OpenParen))
        {
            while (!Check(TokenKind.CloseParen) && !IsAtEnd())
            {
                if (Check(TokenKind.Identifier) || Check(TokenKind.Const))
                {
                    var attr = Advance().Value;
                    // getter=xxx or setter=xxx:
                    if (Match(TokenKind.Equals))
                    {
                        var val = Advance().Value;
                        if (Match(TokenKind.Colon)) val += ":";
                        attr += "=" + val;
                    }
                    prop.Attributes.Add(attr);

                    if (attr is "nullable" or "_Nullable" or "__nullable")
                        prop.IsNullable = true;
                }
                if (!Match(TokenKind.Comma))
                    break;
            }
            Expect(TokenKind.CloseParen, "Expected ')' after property attributes");
        }

        // Parse type and name - type goes until last identifier before semicolon
        // Block type property: void (^blockName)(params);
        var baseType = ParseType();
        if (Check(TokenKind.OpenParen) && PeekNext().Kind == TokenKind.Caret)
        {
            Advance(); // consume '('
            Advance(); // consume '^'

            // Optional nullability before block name
            if (Check(TokenKind.Identifier) && IsNullabilityAnnotation(Peek().Value))
            {
                var nullAnnot = Advance().Value;
                if (nullAnnot is "nullable" or "_Nullable" or "__nullable")
                    prop.IsNullable = true;
            }

            prop.Name = Expect(TokenKind.Identifier, "Expected block property name").Value;
            Expect(TokenKind.CloseParen, "Expected ')' after block name");

            // Parse block parameter list
            var paramStr = new StringBuilder();
            if (Match(TokenKind.OpenParen))
            {
                int depth = 1;
                while (!IsAtEnd() && depth > 0)
                {
                    if (Check(TokenKind.OpenParen)) depth++;
                    if (Check(TokenKind.CloseParen)) { depth--; if (depth == 0) { Advance(); break; } }
                    if (paramStr.Length > 0) paramStr.Append(' ');
                    paramStr.Append(Advance().Value);
                }
            }
            prop.Type = $"{baseType} (^)({paramStr})";
        }
        else
        {
            prop.Type = baseType;
            prop.Name = Expect(TokenKind.Identifier, "Expected property name").Value;
        }

        // Detect nullable from type string too (e.g. "nullable NSString *")
        if (!prop.IsNullable && (prop.Type.Contains("nullable") || prop.Type.Contains("_Nullable")))
            prop.IsNullable = true;

        Match(TokenKind.Semicolon);
        return prop;
    }

    #endregion

    #region Method

    private ObjCMethod ParseMethod()
    {
        var method = new ObjCMethod { InNonnullScope = _inNonnullScope };

        // Return type
        Expect(TokenKind.OpenParen, "Expected '(' before return type");
        method.ReturnType = ParseTypeInsideParens();
        Expect(TokenKind.CloseParen, "Expected ')' after return type");

        // Detect nullable return type
        if (method.ReturnType.Contains("nullable") || method.ReturnType.Contains("_Nullable"))
            method.IsReturnNullable = true;

        // Selector parts and parameters
        var selectorParts = new StringBuilder();

        // First selector part
        if ((Check(TokenKind.Identifier) && !IsTrailingMethodMacro(Peek().Value)) || CheckKeywordAsIdentifier())
        {
            selectorParts.Append(Advance().Value);
        }

        if (Match(TokenKind.Colon))
        {
            selectorParts.Append(':');
            method.Parameters.Add(ParseMethodParameter());

            // Subsequent selector:param pairs
            while ((Check(TokenKind.Identifier) && !IsTrailingMethodMacro(Peek().Value)) || CheckKeywordAsIdentifier())
            {
                selectorParts.Append(Advance().Value);
                if (Match(TokenKind.Colon))
                {
                    selectorParts.Append(':');
                    method.Parameters.Add(ParseMethodParameter());
                }
            }
        }

        method.Selector = selectorParts.ToString();

        // Detect trailing macros before semicolon
        while (Check(TokenKind.Identifier) && Peek().Value is "NS_DESIGNATED_INITIALIZER" or "NS_REQUIRES_SUPER")
        {
            var macro = Advance().Value;
            if (macro == "NS_DESIGNATED_INITIALIZER")
                method.IsDesignatedInitializer = true;
        }

        Match(TokenKind.Semicolon);
        return method;
    }

    private ObjCParameter ParseMethodParameter()
    {
        var param = new ObjCParameter();
        Expect(TokenKind.OpenParen, "Expected '(' before parameter type");
        param.Type = ParseTypeInsideParens();
        Expect(TokenKind.CloseParen, "Expected ')' after parameter type");

        if (param.Type.Contains("_Nullable") || param.Type.Contains("nullable"))
            param.IsNullable = true;

        if (Check(TokenKind.Identifier))
            param.Name = Advance().Value;

        return param;
    }

    #endregion

    #region Enum

    private ObjCEnum ParseEnum()
    {
        var enumNode = new ObjCEnum();
        Advance(); // consume 'enum'

        // C++ enum class/struct
        if (Check(TokenKind.Identifier) && Peek().Value is "class" or "struct")
            Advance(); // skip class/struct keyword

        // Named enum
        if (Check(TokenKind.Identifier) && Peek().Value is not ("NS_ENUM" or "NS_OPTIONS" or "NS_CLOSED_ENUM" or "NS_ERROR_ENUM"))
        {
            enumNode.Name = Advance().Value;
        }

        // C-style typed enum: enum Foo : NSInteger { ... }
        if (Match(TokenKind.Colon))
        {
            var backingParts = new System.Text.StringBuilder();
            while (!Check(TokenKind.OpenBrace) && !Check(TokenKind.Semicolon) && !IsAtEnd())
            {
                if (backingParts.Length > 0) backingParts.Append(' ');
                backingParts.Append(Advance().Value);
            }
            enumNode.BackingType = backingParts.ToString().Trim();
        }

        if (Match(TokenKind.OpenBrace))
        {
            enumNode.Values = ParseEnumValues();
            Expect(TokenKind.CloseBrace, "Expected '}'");
        }

        Match(TokenKind.Semicolon);
        return enumNode;
    }

    public ObjCEnum ParseNSEnum(bool isOptions, bool isErrorEnum = false)
    {
        var enumNode = new ObjCEnum { IsOptions = isOptions };

        Expect(TokenKind.OpenParen, "Expected '(' after NS_ENUM/NS_OPTIONS");

        if (isErrorEnum)
        {
            // NS_ERROR_ENUM(ErrorDomain, EnumName) — backing type is implicitly NSInteger
            Advance(); // skip error domain identifier
            enumNode.BackingType = "NSInteger";
        }
        else
        {
            enumNode.BackingType = Advance().Value; // e.g., NSInteger, NSUInteger
        }

        Expect(TokenKind.Comma, "Expected ','");
        enumNode.Name = Expect(TokenKind.Identifier, "Expected enum name").Value;
        Expect(TokenKind.CloseParen, "Expected ')'");

        if (Match(TokenKind.OpenBrace))
        {
            enumNode.Values = ParseEnumValues();
            Expect(TokenKind.CloseBrace, "Expected '}'");
        }

        return enumNode;
    }

    private List<ObjCEnumValue> ParseEnumValues()
    {
        var values = new List<ObjCEnumValue>();
        while (!Check(TokenKind.CloseBrace) && !IsAtEnd())
        {
            if (!Check(TokenKind.Identifier))
            {
                Advance();
                continue;
            }

            var ev = new ObjCEnumValue { Name = Advance().Value };
            if (Match(TokenKind.Equals))
            {
                ev.Value = ParseExpressionUntil(TokenKind.Comma, TokenKind.CloseBrace);
            }
            values.Add(ev);
            Match(TokenKind.Comma);
        }
        return values;
    }

    #endregion

    #region Struct

    private ObjCStruct ParseStruct()
    {
        Advance(); // consume 'struct'
        var s = new ObjCStruct();

        if (Check(TokenKind.Identifier))
            s.Name = Advance().Value;

        if (Match(TokenKind.OpenBrace))
        {
            while (!Check(TokenKind.CloseBrace) && !IsAtEnd())
            {
                var field = ParseStructField();
                if (field != null) s.Fields.Add(field);
            }
            Expect(TokenKind.CloseBrace, "Expected '}'");
        }

        Match(TokenKind.Semicolon);
        return s;
    }

    private ObjCStructField? ParseStructField()
    {
        var type = ParseType();
        if (string.IsNullOrEmpty(type))
        {
            Advance();
            return null;
        }
        var name = Check(TokenKind.Identifier) ? Advance().Value : "";
        Match(TokenKind.Semicolon);
        return new ObjCStructField { Name = name, Type = type };
    }

    #endregion

    #region Typedef

    private void ParseTypedef(ObjCHeader header)
    {
        Advance(); // consume 'typedef'

        // typedef NS_ENUM(...) { ... };
        if (Check(TokenKind.Identifier) && Peek().Value is "NS_ENUM" or "NS_OPTIONS" or "NS_CLOSED_ENUM" or "NS_ERROR_ENUM")
        {
            bool isOptions = Peek().Value == "NS_OPTIONS";
            bool isErrorEnum = Peek().Value == "NS_ERROR_ENUM";
            Advance(); // consume NS_ENUM/NS_OPTIONS/NS_CLOSED_ENUM/NS_ERROR_ENUM
            var enumNode = ParseNSEnum(isOptions, isErrorEnum);
            header.Enums.Add(enumNode);
            Match(TokenKind.Semicolon);
            return;
        }

        // typedef enum ... { ... } Name;
        if (Check(TokenKind.Enum))
        {
            var enumNode = ParseEnum();
            // The name after closing brace
            if (Check(TokenKind.Identifier))
            {
                enumNode.Name = Advance().Value;
                Match(TokenKind.Semicolon);
            }
            header.Enums.Add(enumNode);
            return;
        }

        // typedef struct ... { ... } Name;
        if (Check(TokenKind.Struct))
        {
            var structNode = ParseStruct();
            if (Check(TokenKind.Identifier))
            {
                structNode.Name = Advance().Value;
                Match(TokenKind.Semicolon);
            }
            header.Structs.Add(structNode);
            return;
        }

        // typedef returnType (^BlockName)(params);
        // Check for block typedefs
        if (LooksLikeBlockTypedef())
        {
            var td = ParseBlockTypedef();
            header.Typedefs.Add(td);
            return;
        }

        // Generic typedef: typedef ExistingType NewName;
        var underlying = ParseTypeForTypedef();
        if (Check(TokenKind.Identifier))
        {
            var name = Advance().Value;
            Match(TokenKind.Semicolon);
            header.Typedefs.Add(new ObjCTypedef { Name = name, UnderlyingType = underlying });
        }
        else
        {
            Match(TokenKind.Semicolon);
        }
    }

    private bool LooksLikeBlockTypedef()
    {
        // Look ahead for pattern: type (^Name)(...)
        int lookahead = _pos;
        int depth = 0;
        while (lookahead < _tokens.Count && lookahead < _pos + 20)
        {
            var t = _tokens[lookahead];
            if (t.Kind == TokenKind.Caret) return true;
            if (t.Kind == TokenKind.Semicolon) return false;
            if (t.Kind == TokenKind.OpenParen) depth++;
            if (t.Kind == TokenKind.CloseParen) depth--;
            lookahead++;
        }
        return false;
    }

    private ObjCTypedef ParseBlockTypedef()
    {
        // Parse return type
        var returnType = new StringBuilder();
        while (!Check(TokenKind.OpenParen) && !IsAtEnd())
        {
            returnType.Append(Advance().Value);
            if (Check(TokenKind.Asterisk) || Check(TokenKind.Identifier))
                returnType.Append(' ');
        }

        // (^Name)
        Expect(TokenKind.OpenParen, "Expected '('");
        Expect(TokenKind.Caret, "Expected '^'");
        var name = Check(TokenKind.Identifier) ? Advance().Value : "";
        Expect(TokenKind.CloseParen, "Expected ')'");

        // (params)
        var paramsStr = new StringBuilder();
        Expect(TokenKind.OpenParen, "Expected '('");
        int depth = 1;
        while (depth > 0 && !IsAtEnd())
        {
            var t = Advance();
            if (t.Kind == TokenKind.OpenParen) depth++;
            else if (t.Kind == TokenKind.CloseParen) { depth--; if (depth == 0) break; }
            if (paramsStr.Length > 0) paramsStr.Append(' ');
            paramsStr.Append(t.Value);
        }

        Match(TokenKind.Semicolon);
        var underlying = $"{returnType.ToString().Trim()} (^)({paramsStr})";
        return new ObjCTypedef { Name = name, UnderlyingType = underlying };
    }

    #endregion

    #region Functions

    private bool LooksLikeFunctionDeclaration()
    {
        // Heuristic: identifier followed eventually by '(' before ';' or '{' or '@'
        int lookahead = _pos;
        bool seenParen = false;
        while (lookahead < _tokens.Count && lookahead < _pos + 30)
        {
            var t = _tokens[lookahead];
            if (t.Kind == TokenKind.OpenParen) { seenParen = true; break; }
            if (t.Kind == TokenKind.Semicolon || t.Kind == TokenKind.OpenBrace ||
                t.Kind == TokenKind.AtInterface || t.Kind == TokenKind.AtProtocol ||
                t.Kind == TokenKind.AtEnd)
                return false;
            lookahead++;
        }
        return seenParen;
    }

    private void TryParseFunctionOrSkip(ObjCHeader header)
    {
        // Skip extern/static/inline
        while (Check(TokenKind.Extern) || Check(TokenKind.Static) || Check(TokenKind.Inline))
            Advance();

        // Try to parse as function declaration
        var returnType = new StringBuilder();
        while (!Check(TokenKind.OpenParen) && !Check(TokenKind.Semicolon) && !IsAtEnd())
        {
            var t = Advance();
            if (returnType.Length > 0) returnType.Append(' ');
            returnType.Append(t.Value);
        }

        // Split return type from function name: last word is the name
        var parts = returnType.ToString().Trim();
        var lastSpace = parts.LastIndexOf(' ');
        string funcName;
        string retType;
        if (lastSpace >= 0)
        {
            retType = parts[..lastSpace].Trim();
            funcName = parts[(lastSpace + 1)..].Trim();
        }
        else
        {
            // Can't determine — skip
            SkipToSemicolon();
            return;
        }

        // Remove pointer from name if attached
        if (funcName.StartsWith('*'))
        {
            retType += " *";
            funcName = funcName.TrimStart('*');
        }

        if (!Match(TokenKind.OpenParen))
        {
            // Not a function — likely an extern constant (e.g. extern NSString * const Key;)
            if (!string.IsNullOrEmpty(funcName))
            {
                header.Functions.Add(new ObjCFunction { Name = funcName, ReturnType = retType });
            }
            SkipToSemicolon();
            return;
        }

        var func = new ObjCFunction
        {
            Name = funcName,
            ReturnType = retType,
            Parameters = ParseFunctionParameters(),
        };

        Expect(TokenKind.CloseParen, "Expected ')'");

        // Skip function body if present
        if (Check(TokenKind.OpenBrace))
        {
            Advance();
            SkipBalancedBraces();
        }

        Match(TokenKind.Semicolon);
        header.Functions.Add(func);
    }

    private List<ObjCParameter> ParseFunctionParameters()
    {
        var parameters = new List<ObjCParameter>();
        if (Check(TokenKind.CloseParen)) return parameters;

        // void parameter
        if (Check(TokenKind.Void) && PeekNext().Kind == TokenKind.CloseParen)
        {
            Advance();
            return parameters;
        }

        while (!Check(TokenKind.CloseParen) && !IsAtEnd())
        {
            if (Check(TokenKind.Ellipsis))
            {
                Advance();
                parameters.Add(new ObjCParameter { Name = "...", Type = "..." });
                break;
            }

            var param = new ObjCParameter();
            var typeAndName = new StringBuilder();
            int depth = 0;
            while (!IsAtEnd())
            {
                if (depth == 0 && (Check(TokenKind.Comma) || Check(TokenKind.CloseParen)))
                    break;
                var t = Advance();
                if (t.Kind == TokenKind.OpenParen) depth++;
                if (t.Kind == TokenKind.CloseParen) depth--;
                if (typeAndName.Length > 0) typeAndName.Append(' ');
                typeAndName.Append(t.Value);
            }

            var raw = typeAndName.ToString().Trim();
            var li = raw.LastIndexOf(' ');
            if (li >= 0 && !raw.EndsWith('*'))
            {
                param.Type = raw[..li].Trim();
                param.Name = raw[(li + 1)..].Trim();
            }
            else
            {
                param.Type = raw;
            }
            parameters.Add(param);
            Match(TokenKind.Comma);
        }

        return parameters;
    }

    #endregion

    #region Forward declarations

    private void ParseForwardClassDeclaration(ObjCHeader header)
    {
        // @class Foo, Bar, Baz;
        while (!Check(TokenKind.Semicolon) && !IsAtEnd())
        {
            if (Check(TokenKind.Identifier))
                header.ForwardDeclarations.Classes.Add(Advance().Value);
            else if (!Match(TokenKind.Comma))
                break;
        }
        Match(TokenKind.Semicolon);
    }

    #endregion

    #region Type parsing helpers

    private string ParseType()
    {
        var sb = new StringBuilder();
        // Collect const, struct, enum prefixes
        while (Check(TokenKind.Const) || Check(TokenKind.Struct) || Check(TokenKind.Enum) || Check(TokenKind.Union))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Advance().Value);
        }

        // Main type identifier(s)
        while (Check(TokenKind.Identifier) || Check(TokenKind.Void))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Advance().Value);

            // Generics: NSArray<NSString *>
            if (Check(TokenKind.OpenAngle))
            {
                sb.Append(ParseGenericType());
            }

            // Check for next token — if it's an identifier followed by ';', stop (next is property name)
            if (Check(TokenKind.Identifier))
            {
                // Peek ahead: if next-next is ';' or just a name, the current is still part of the type
                if (PeekNext().Kind == TokenKind.Semicolon ||
                    PeekNext().Kind == TokenKind.CloseParen)
                    break;
            }
        }

        // Pointers
        while (Check(TokenKind.Asterisk))
        {
            sb.Append(' ');
            sb.Append(Advance().Value);
        }

        // Nullability
        if (Check(TokenKind.Identifier) && IsNullabilityAnnotation(Peek().Value))
        {
            sb.Append(' ');
            sb.Append(Advance().Value);
        }

        return sb.ToString().Trim();
    }

    private string ParseTypeInsideParens()
    {
        var sb = new StringBuilder();
        int depth = 0;
        while (!IsAtEnd())
        {
            if (depth == 0 && Check(TokenKind.CloseParen))
                break;
            var t = Advance();
            if (t.Kind == TokenKind.OpenParen) depth++;
            if (t.Kind == TokenKind.CloseParen) depth--;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(t.Value);
        }
        return sb.ToString().Trim();
    }

    private string ParseTypeForTypedef()
    {
        var sb = new StringBuilder();
        // Collect tokens until the last identifier before ';'
        var collected = new List<Token>();
        while (!Check(TokenKind.Semicolon) && !IsAtEnd())
        {
            // Handle generics
            if (Check(TokenKind.OpenAngle))
            {
                collected.Add(new Token(TokenKind.Identifier, ParseGenericType(), Peek().Line, Peek().Column));
                continue;
            }
            collected.Add(Advance());
        }

        // Last token is the typedef name — backtrack
        if (collected.Count > 0)
        {
            _pos--; // we haven't consumed ';' so we push back to let caller get the name
            // Reconstruct: everything except last token is the underlying type
            for (int i = 0; i < collected.Count - 1; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(collected[i].Value);
            }
            // Put the last token back as the "name" for the caller
            _pos = _pos - (collected.Count > 0 ? 0 : 0);
            // Actually, we consumed everything. Let's re-push the last token.
            _pos = _pos + 1; // undo the _pos-- above
            // Re-insert by just adjusting position
            _pos -= 1; // position at last collected token so caller sees it as Identifier
        }

        return sb.ToString().Trim();
    }

    private string ParseGenericType()
    {
        var sb = new StringBuilder();
        sb.Append(Advance().Value); // '<'
        int depth = 1;
        while (depth > 0 && !IsAtEnd())
        {
            var t = Advance();
            if (t.Kind == TokenKind.OpenAngle) depth++;
            else if (t.Kind == TokenKind.CloseAngle) depth--;
            sb.Append(t.Value);
            if (depth > 0 && t.Kind != TokenKind.Asterisk)
                sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static bool IsNullabilityAnnotation(string value) =>
        value is "nullable" or "nonnull" or "_Nullable" or "_Nonnull"
            or "_Null_unspecified" or "null_unspecified"
            or "__nullable" or "__nonnull";

    private static bool IsTrailingMethodMacro(string value) =>
        value is "NS_DESIGNATED_INITIALIZER" or "NS_REQUIRES_SUPER";

    #endregion

    #region Expression parsing (for enum values)

    private string ParseExpressionUntil(params TokenKind[] terminators)
    {
        var sb = new StringBuilder();
        int depth = 0;
        while (!IsAtEnd())
        {
            if (depth == 0 && terminators.Contains(Peek().Kind))
                break;
            var t = Advance();
            if (t.Kind == TokenKind.OpenParen) depth++;
            if (t.Kind == TokenKind.CloseParen) depth--;
            sb.Append(t.Value);
            if (t.Kind != TokenKind.OpenParen && !Check(TokenKind.CloseParen))
                sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    #endregion

    #region Token helpers

    private List<string> ParseIdentifierList(TokenKind terminator)
    {
        var list = new List<string>();
        while (!Check(terminator) && !IsAtEnd())
        {
            // Skip preprocessor directives inside lists (e.g. #if TARGET_OS_...)
            if (Match(TokenKind.PreprocessorDirective))
                continue;
            if (Check(TokenKind.Identifier))
                list.Add(Advance().Value);
            else if (!Match(TokenKind.Comma))
                break;
        }
        return list;
    }

    private Token Peek() => _tokens[_pos];
    private Token PeekNext() => (_pos + 1 < _tokens.Count) ? _tokens[_pos + 1] : new Token(TokenKind.Eof, "", 0, 0);
    private Token Previous() => _tokens[_pos - 1];
    private bool IsAtEnd() => _pos >= _tokens.Count || _tokens[_pos].Kind == TokenKind.Eof;

    private bool Check(TokenKind kind) => !IsAtEnd() && _tokens[_pos].Kind == kind;

    private bool CheckKeywordAsIdentifier() =>
        !IsAtEnd() && _tokens[_pos].Kind is TokenKind.Void or TokenKind.Const
            or TokenKind.Struct or TokenKind.Enum or TokenKind.Union;

    private bool Match(TokenKind kind)
    {
        if (!Check(kind)) return false;
        _pos++;
        return true;
    }

    private Token Advance()
    {
        var t = _tokens[_pos];
        _pos++;
        return t;
    }

    private Token Expect(TokenKind kind, string message)
    {
        if (Check(kind)) return Advance();
        var current = IsAtEnd() ? "EOF" : $"'{_tokens[_pos].Value}' ({_tokens[_pos].Kind})";
        throw new ParseException($"{message} at line {(IsAtEnd() ? -1 : _tokens[_pos].Line)}, got {current}");
    }

    private void SkipBalancedBraces()
    {
        int depth = 1;
        while (depth > 0 && !IsAtEnd())
        {
            if (Check(TokenKind.OpenBrace)) depth++;
            else if (Check(TokenKind.CloseBrace)) depth--;
            if (depth > 0) Advance();
            else Advance(); // consume final '}'
        }
    }

    /// <summary>
    /// Consumes balanced angle brackets including the opening &lt;.
    /// Used to skip generic type parameters like &lt;NSString *&gt;.
    /// </summary>
    private void SkipBalancedAngles()
    {
        if (!Match(TokenKind.OpenAngle)) return;
        int depth = 1;
        while (depth > 0 && !IsAtEnd())
        {
            if (Check(TokenKind.OpenAngle)) depth++;
            else if (Check(TokenKind.CloseAngle)) depth--;
            if (depth > 0) Advance();
            else Advance(); // consume final '>'
        }
    }

    /// <summary>
    /// Looks ahead to determine if the next &lt;...&gt; is a generic type parameter
    /// (contains *, commas with types) rather than a protocol adoption list.
    /// </summary>
    private bool LooksLikeGenericParams()
    {
        int lookahead = _pos + 1; // skip the '<'
        int depth = 1;
        while (lookahead < _tokens.Count && depth > 0)
        {
            var t = _tokens[lookahead];
            if (t.Kind == TokenKind.OpenAngle) depth++;
            else if (t.Kind == TokenKind.CloseAngle) depth--;
            // Asterisk inside angle brackets → generic type param (e.g. NSString *)
            if (t.Kind == TokenKind.Asterisk && depth > 0) return true;
            lookahead++;
        }
        return false;
    }

    private void SkipToSemicolon()
    {
        while (!Check(TokenKind.Semicolon) && !IsAtEnd())
            Advance();
        Match(TokenKind.Semicolon);
    }

    private void SkipToRecoveryPoint()
    {
        while (!IsAtEnd())
        {
            if (Check(TokenKind.Semicolon) || Check(TokenKind.AtEnd) ||
                Check(TokenKind.AtInterface) || Check(TokenKind.AtProtocol))
            {
                if (Check(TokenKind.Semicolon)) Advance();
                return;
            }
            Advance();
        }
    }

    #endregion
}

public class ParseException : Exception
{
    public ParseException(string message) : base(message) { }
}
