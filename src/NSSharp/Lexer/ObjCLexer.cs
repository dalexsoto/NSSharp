using System.Text;

namespace NSSharp.Lexer;

public sealed class ObjCLexer
{
    private readonly string _source;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    // Structural macros that the parser needs — never auto-skip these
    private static readonly HashSet<string> s_structuralMacros =
    [
        "NS_ENUM", "NS_OPTIONS", "NS_CLOSED_ENUM", "NS_ERROR_ENUM",
        "NS_ASSUME_NONNULL_BEGIN", "NS_ASSUME_NONNULL_END",
        "NS_DESIGNATED_INITIALIZER", "NS_REQUIRES_SUPER",
    ];

    // Known ALL_CAPS identifiers that are types/values, not macros
    private static readonly HashSet<string> s_knownUppercaseTypes =
    [
        "BOOL", "SEL", "IMP", "NULL",
        // C max-value constants used in enum values
        "UINT8_MAX", "UINT16_MAX", "UINT32_MAX", "UINT64_MAX", "UINT_MAX",
        "INT8_MAX", "INT16_MAX", "INT32_MAX", "INT64_MAX", "INT_MAX",
        "CGFLOAT_MAX", "CGFLOAT_MIN",
    ];

    // User-supplied macros that map to extern (e.g. PSPDF_EXPORT, MY_EXPORT)
    private readonly HashSet<string> _externMacros;
    // User-supplied extra macros to always skip
    private readonly HashSet<string> _extraSkipMacros;
    // Whether to use the UPPER_SNAKE_CASE heuristic
    private readonly bool _macroHeuristic;

    // Nullability qualifiers we keep as identifiers
    private static readonly HashSet<string> s_nullabilityKeywords =
    [
        "nullable", "nonnull", "_Nullable", "_Nonnull",
        "_Null_unspecified", "null_unspecified",
        "__nullable", "__nonnull",
    ];

    public ObjCLexer(string source, ObjCLexerOptions? options = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        options ??= ObjCLexerOptions.Default;
        _externMacros = new HashSet<string>(options.ExternMacros);
        _extraSkipMacros = new HashSet<string>(options.ExtraSkipMacros);
        _macroHeuristic = options.MacroHeuristic;
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

            // Handle user-supplied skip macros
            if (_extraSkipMacros.Contains(ident))
            {
                SkipWhitespace();
                if (Peek() == '(')
                    SkipBalancedParens();
                goto restart;
            }

            // Compiler attributes with double-underscore prefix
            if (ident == "__attribute__")
            {
                SkipWhitespace();
                if (Peek() == '(')
                    SkipBalancedParens();
                goto restart;
            }
            if (ident is "__unused" or "__kindof")
                goto restart;

            // Extern-mapped macros (e.g. PSPDF_EXPORT → extern)
            if (ident == "PSPDF_EXPORT" || _externMacros.Contains(ident))
                return new Token(TokenKind.Extern, ident, startLine, startCol);

            // Init-unavailable macros: preserve as identifiers so parser can detect them
            if (ident.Contains("INIT_UNAVAILABLE") || ident.Contains("EMPTY_INIT"))
                return new Token(TokenKind.Identifier, ident, startLine, startCol);

            // UPPER_SNAKE_CASE heuristic: skip likely macros
            if (_macroHeuristic && IsLikelyMacro(ident))
            {
                SkipWhitespace();
                if (Peek() == '(')
                    SkipBalancedParens();
                goto restart;
            }

            // Map C/ObjC keywords
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
                "NS_ASSUME_NONNULL_BEGIN" => new Token(TokenKind.NonnullBegin, ident, startLine, startCol),
                "NS_ASSUME_NONNULL_END" => new Token(TokenKind.NonnullEnd, ident, startLine, startCol),
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

    /// <summary>
    /// Returns true if the identifier looks like a C preprocessor macro
    /// based on UPPER_SNAKE_CASE naming convention.
    /// Criteria: 3+ chars, all uppercase/digits/underscores, contains at least one underscore,
    /// and is not a known type or structural macro.
    /// </summary>
    internal static bool IsLikelyMacro(string ident)
    {
        if (ident.Length < 3)
            return false;

        // Must not be a structural macro the parser needs
        if (s_structuralMacros.Contains(ident))
            return false;

        // Must not be a known type
        if (s_knownUppercaseTypes.Contains(ident))
            return false;

        // Check: all chars are uppercase, digit, or underscore
        bool hasUnderscore = false;
        bool hasLetter = false;
        foreach (var c in ident)
        {
            if (c == '_')
                hasUnderscore = true;
            else if (c >= 'A' && c <= 'Z')
                hasLetter = true;
            else if (c >= '0' && c <= '9')
                { } // digits ok
            else
                return false; // lowercase or other char → not a macro
        }

        // Require at least one underscore (e.g. NS_ENUM pattern) and one letter
        // Single-word ALL_CAPS without underscore (e.g. "BOOL") are handled by s_knownUppercaseTypes
        return hasUnderscore && hasLetter;
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
