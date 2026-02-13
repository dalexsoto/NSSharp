namespace NSSharp.Lexer;

public enum TokenKind
{
    // Literals / identifiers
    Identifier,
    StringLiteral,
    NumberLiteral,
    CharLiteral,

    // ObjC keywords
    AtInterface,
    AtEnd,
    AtProtocol,
    AtProperty,
    AtOptional,
    AtRequired,
    AtClass,

    // C keywords relevant to headers
    Typedef,
    Enum,
    Struct,
    Union,
    Const,
    Void,
    Extern,
    Static,
    Inline,

    // Punctuation
    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,
    OpenBracket,
    CloseBracket,
    OpenAngle,
    CloseAngle,
    Semicolon,
    Colon,
    Comma,
    Asterisk,
    Minus,
    Plus,
    Equals,
    Dot,
    Ellipsis,
    Caret,
    Ampersand,
    Pipe,
    Tilde,
    Exclamation,
    Slash,
    Percent,
    Hat,

    // Preprocessor (kept as single token for skipping)
    PreprocessorDirective,

    // Nullability scope
    NonnullBegin,
    NonnullEnd,

    // Special
    Eof,
    Unknown,
}

public readonly record struct Token(TokenKind Kind, string Value, int Line, int Column);
