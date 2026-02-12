using NSSharp.Lexer;

namespace NSSharp.Tests;

public class LexerTests
{
    [Fact]
    public void Tokenizes_AtInterface()
    {
        var lexer = new ObjCLexer("@interface Foo");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.AtInterface, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("Foo", tokens[1].Value);
    }

    [Fact]
    public void Tokenizes_Property()
    {
        var lexer = new ObjCLexer("@property (nonatomic, copy) NSString *title;");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.AtProperty, tokens[0].Kind);
        Assert.Equal(TokenKind.OpenParen, tokens[1].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
        Assert.Equal("nonatomic", tokens[2].Value);
    }

    [Fact]
    public void Skips_Comments()
    {
        var lexer = new ObjCLexer("// comment\n@end /* block */ @interface");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.AtEnd, tokens[0].Kind);
        Assert.Equal(TokenKind.AtInterface, tokens[1].Kind);
    }

    [Fact]
    public void Skips_NS_ASSUME_NONNULL()
    {
        var lexer = new ObjCLexer("NS_ASSUME_NONNULL_BEGIN @interface Foo NS_ASSUME_NONNULL_END");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.AtInterface, tokens[0].Kind);
        Assert.Equal("Foo", tokens[1].Value);
    }

    [Fact]
    public void Skips_Attribute_With_Parens()
    {
        var lexer = new ObjCLexer("__attribute__((visibility(\"default\"))) @interface Foo");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.AtInterface, tokens[0].Kind);
    }

    [Fact]
    public void Tokenizes_Preprocessor_Directive()
    {
        var lexer = new ObjCLexer("#import <Foundation/Foundation.h>\n@end");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.PreprocessorDirective, tokens[0].Kind);
        Assert.Equal(TokenKind.AtEnd, tokens[1].Kind);
    }

    [Fact]
    public void Tokenizes_Method_Signature()
    {
        var lexer = new ObjCLexer("- (void)doSomething:(int)x;");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.Minus, tokens[0].Kind);
        Assert.Equal(TokenKind.OpenParen, tokens[1].Kind);
        Assert.Equal(TokenKind.Void, tokens[2].Kind);
        Assert.Equal(TokenKind.CloseParen, tokens[3].Kind);
    }

    [Fact]
    public void Tokenizes_NumberLiteral()
    {
        var lexer = new ObjCLexer("0xFF 42 3.14");
        var tokens = lexer.Tokenize();
        Assert.Equal(TokenKind.NumberLiteral, tokens[0].Kind);
        Assert.Equal("0xFF", tokens[0].Value);
        Assert.Equal("42", tokens[1].Value);
        Assert.Equal("3.14", tokens[2].Value);
    }
}
