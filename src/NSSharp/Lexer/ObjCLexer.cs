using System.Text;

namespace NSSharp.Lexer;

public sealed class ObjCLexer
{
    private readonly string _source;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    // Macros we skip entirely (they consume balanced parens if present)
    private static readonly HashSet<string> s_skipMacros =
    [
        "NS_ASSUME_NONNULL_BEGIN", "NS_ASSUME_NONNULL_END",
        "API_AVAILABLE", "API_UNAVAILABLE", "API_DEPRECATED",
        "API_DEPRECATED_WITH_REPLACEMENT",
        "NS_AVAILABLE", "NS_AVAILABLE_IOS", "NS_AVAILABLE_MAC",
        "NS_DEPRECATED", "NS_DEPRECATED_IOS",
        "NS_DESIGNATED_INITIALIZER", "NS_REQUIRES_SUPER",
        "NS_SWIFT_NAME", "NS_SWIFT_UNAVAILABLE", "NS_SWIFT_ASYNC",
        "NS_SWIFT_ASYNC_NAME", "NS_SWIFT_UI_ACTOR",
        "NS_REFINED_FOR_SWIFT", "NS_SWIFT_SENDABLE", "NS_SWIFT_NONSENDABLE",
        "NS_HEADER_AUDIT_BEGIN", "NS_HEADER_AUDIT_END",
        "NS_FORMAT_FUNCTION", "NS_FORMAT_ARGUMENT",
        "NS_RETURNS_RETAINED", "NS_RETURNS_NOT_RETAINED",
        "NS_RETURNS_INNER_POINTER",
        "NS_NOESCAPE", "NS_CLOSED_ENUM",
        "CF_ENUM_AVAILABLE", "CF_ENUM_DEPRECATED",
        "__attribute__", "__unused", "__kindof",
        "UIKIT_EXTERN", "FOUNDATION_EXTERN", "FOUNDATION_EXPORT",
        "OBJC_EXPORT", "OBJC_ROOT_CLASS",
        "NS_CLASS_AVAILABLE_IOS", "NS_CLASS_DEPRECATED_IOS",
        "NS_EXTENSION_UNAVAILABLE", "NS_EXTENSION_UNAVAILABLE_IOS",
        "CF_SWIFT_NAME",
    ];

    // Nullability qualifiers we keep as identifiers
    private static readonly HashSet<string> s_nullabilityKeywords =
    [
        "nullable", "nonnull", "_Nullable", "_Nonnull",
        "_Null_unspecified", "null_unspecified",
        "__nullable", "__nonnull",
    ];

    public ObjCLexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var tok = NextToken();
            tokens.Add(tok);
            if (tok.Kind == TokenKind.Eof)
                break;
        }
        return tokens;
    }

    private char Peek() => _pos < _source.Length ? _source[_pos] : '\0';
    private char PeekAt(int offset) => (_pos + offset) < _source.Length ? _source[_pos + offset] : '\0';

    private char Advance()
    {
        var ch = _source[_pos++];
        if (ch == '\n') { _line++; _col = 1; }
        else { _col++; }
        return ch;
    }

    private void SkipWhitespace()
    {
        while (_pos < _source.Length && char.IsWhiteSpace(_source[_pos]))
            Advance();
    }

    private void SkipLineComment()
    {
        while (_pos < _source.Length && _source[_pos] != '\n')
            Advance();
    }

    private void SkipBlockComment()
    {
        // Already consumed /*
        while (_pos < _source.Length)
        {
            if (_source[_pos] == '*' && PeekAt(1) == '/')
            {
                Advance(); Advance();
                return;
            }
            Advance();
        }
    }

    private void SkipBalancedParens()
    {
        if (Peek() != '(') return;
        Advance(); // consume '('
        int depth = 1;
        while (_pos < _source.Length && depth > 0)
        {
            var ch = Advance();
            if (ch == '(') depth++;
            else if (ch == ')') depth--;
        }
    }

    private Token NextToken()
    {
    restart:
        SkipWhitespace();

        if (_pos >= _source.Length)
            return new Token(TokenKind.Eof, "", _line, _col);

        int startLine = _line, startCol = _col;
        char ch = Peek();

        // Comments
        if (ch == '/' && PeekAt(1) == '/')
        {
            SkipLineComment();
            goto restart;
        }
        if (ch == '/' && PeekAt(1) == '*')
        {
            Advance(); Advance();
            SkipBlockComment();
            goto restart;
        }

        // Preprocessor directives: skip entire line
        if (ch == '#')
        {
            var sb = new StringBuilder();
            while (_pos < _source.Length && _source[_pos] != '\n')
            {
                // Handle line continuation
                if (_source[_pos] == '\\' && PeekAt(1) == '\n')
                {
                    Advance(); Advance();
                    continue;
                }
                sb.Append(Advance());
            }
            return new Token(TokenKind.PreprocessorDirective, sb.ToString(), startLine, startCol);
        }

        // @ keywords
        if (ch == '@')
        {
            Advance();
            if (_pos < _source.Length && (char.IsLetter(Peek()) || Peek() == '"'))
            {
                if (Peek() == '"')
                {
                    // @"string literal"
                    return ReadStringLiteral(startLine, startCol);
                }
                var ident = ReadIdentifierRaw();
                return ident switch
                {
                    "interface" => new Token(TokenKind.AtInterface, "@interface", startLine, startCol),
                    "end" => new Token(TokenKind.AtEnd, "@end", startLine, startCol),
                    "protocol" => new Token(TokenKind.AtProtocol, "@protocol", startLine, startCol),
                    "property" => new Token(TokenKind.AtProperty, "@property", startLine, startCol),
                    "optional" => new Token(TokenKind.AtOptional, "@optional", startLine, startCol),
                    "required" => new Token(TokenKind.AtRequired, "@required", startLine, startCol),
                    "class" => new Token(TokenKind.AtClass, "@class", startLine, startCol),
                    _ => new Token(TokenKind.Identifier, "@" + ident, startLine, startCol),
                };
            }
            return new Token(TokenKind.Unknown, "@", startLine, startCol);
        }

        // String literals
        if (ch == '"')
            return ReadStringLiteral(startLine, startCol);

        // Char literals
        if (ch == '\'' && _pos + 2 < _source.Length)
        {
            Advance(); // opening '
            var sb = new StringBuilder();
            while (_pos < _source.Length && Peek() != '\'')
            {
                if (Peek() == '\\') { sb.Append(Advance()); }
                sb.Append(Advance());
            }
            if (_pos < _source.Length) Advance(); // closing '
            return new Token(TokenKind.CharLiteral, sb.ToString(), startLine, startCol);
        }

        // Numbers
        if (char.IsDigit(ch) || (ch == '.' && _pos + 1 < _source.Length && char.IsDigit(PeekAt(1))))
        {
            return ReadNumber(startLine, startCol);
        }

        // Identifiers and keywords
        if (char.IsLetter(ch) || ch == '_')
        {
            var ident = ReadIdentifierRaw();

            // Handle skip macros
            if (s_skipMacros.Contains(ident))
            {
                SkipWhitespace();
                if (Peek() == '(')
                    SkipBalancedParens();
                goto restart;
            }

            // Map C keywords
            return ident switch
            {
                "typedef" => new Token(TokenKind.Typedef, ident, startLine, startCol),
                "enum" => new Token(TokenKind.Enum, ident, startLine, startCol),
                "struct" => new Token(TokenKind.Struct, ident, startLine, startCol),
                "union" => new Token(TokenKind.Union, ident, startLine, startCol),
                "const" => new Token(TokenKind.Const, ident, startLine, startCol),
                "void" => new Token(TokenKind.Void, ident, startLine, startCol),
                "extern" => new Token(TokenKind.Extern, ident, startLine, startCol),
                "static" => new Token(TokenKind.Static, ident, startLine, startCol),
                "inline" => new Token(TokenKind.Inline, ident, startLine, startCol),
                _ => new Token(TokenKind.Identifier, ident, startLine, startCol),
            };
        }

        // Punctuation
        Advance();
        return ch switch
        {
            '(' => new Token(TokenKind.OpenParen, "(", startLine, startCol),
            ')' => new Token(TokenKind.CloseParen, ")", startLine, startCol),
            '{' => new Token(TokenKind.OpenBrace, "{", startLine, startCol),
            '}' => new Token(TokenKind.CloseBrace, "}", startLine, startCol),
            '[' => new Token(TokenKind.OpenBracket, "[", startLine, startCol),
            ']' => new Token(TokenKind.CloseBracket, "]", startLine, startCol),
            '<' => new Token(TokenKind.OpenAngle, "<", startLine, startCol),
            '>' => new Token(TokenKind.CloseAngle, ">", startLine, startCol),
            ';' => new Token(TokenKind.Semicolon, ";", startLine, startCol),
            ':' => new Token(TokenKind.Colon, ":", startLine, startCol),
            ',' => new Token(TokenKind.Comma, ",", startLine, startCol),
            '*' => new Token(TokenKind.Asterisk, "*", startLine, startCol),
            '-' => new Token(TokenKind.Minus, "-", startLine, startCol),
            '+' => new Token(TokenKind.Plus, "+", startLine, startCol),
            '=' => new Token(TokenKind.Equals, "=", startLine, startCol),
            '.' when PeekAt(0) == '.' && PeekAt(1) == '.' => DotDotDot(startLine, startCol),
            '.' => new Token(TokenKind.Dot, ".", startLine, startCol),
            '^' => new Token(TokenKind.Caret, "^", startLine, startCol),
            '&' => new Token(TokenKind.Ampersand, "&", startLine, startCol),
            '|' => new Token(TokenKind.Pipe, "|", startLine, startCol),
            '~' => new Token(TokenKind.Tilde, "~", startLine, startCol),
            '!' => new Token(TokenKind.Exclamation, "!", startLine, startCol),
            '/' => new Token(TokenKind.Slash, "/", startLine, startCol),
            '%' => new Token(TokenKind.Percent, "%", startLine, startCol),
            _ => new Token(TokenKind.Unknown, ch.ToString(), startLine, startCol),
        };
    }

    private Token DotDotDot(int line, int col)
    {
        Advance(); Advance(); // consume the other two dots
        return new Token(TokenKind.Ellipsis, "...", line, col);
    }

    private string ReadIdentifierRaw()
    {
        var start = _pos;
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_'))
            Advance();
        return _source[start.._pos];
    }

    private Token ReadStringLiteral(int line, int col)
    {
        Advance(); // opening "
        var sb = new StringBuilder();
        while (_pos < _source.Length && Peek() != '"')
        {
            if (Peek() == '\\')
            {
                sb.Append(Advance()); // backslash
            }
            sb.Append(Advance());
        }
        if (_pos < _source.Length) Advance(); // closing "
        return new Token(TokenKind.StringLiteral, sb.ToString(), line, col);
    }

    private Token ReadNumber(int line, int col)
    {
        var start = _pos;
        // Handle hex
        if (Peek() == '0' && (_pos + 1 < _source.Length) && (PeekAt(1) == 'x' || PeekAt(1) == 'X'))
        {
            Advance(); Advance();
            while (_pos < _source.Length && IsHexDigit(Peek())) Advance();
        }
        else
        {
            while (_pos < _source.Length && (char.IsDigit(Peek()) || Peek() == '.'))
                Advance();
        }
        // Suffixes: u, U, l, L, f, F, LL, UL, etc.
        while (_pos < _source.Length && "uUlLfF".Contains(Peek()))
            Advance();
        return new Token(TokenKind.NumberLiteral, _source[start.._pos], line, col);
    }

    private static bool IsHexDigit(char c) =>
        char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
