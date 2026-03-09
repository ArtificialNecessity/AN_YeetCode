using YeetCode.Grammar;
using Xunit;

namespace YeetCode_Tests;

public class TestGrammarLexer
{
    [Fact]
    public void TestSimpleRuleTokenization()
    {
        string grammarText = @"
# Simple grammar
message: ""message"" name:IDENT ""{""  ""}""
  -> @Message

IDENT: /[a-zA-Z_][a-zA-Z0-9_]*/
%skip: /\s+/
";

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();

        // Verify we got tokens
        Assert.NotEmpty(tokens);

        // Should have: message, :, "message", name, :, IDENT, "{", "}", ->, @Message, IDENT, :, regex, %skip, :, regex, EOF
        Assert.Contains(tokens, t => t.Type == TokenType.RuleName && t.Text == "message");
        Assert.Contains(tokens, t => t.Type == TokenType.StringLiteral && t.Text == "message");
        Assert.Contains(tokens, t => t.Type == TokenType.TokenName && t.Text == "IDENT");
        Assert.Contains(tokens, t => t.Type == TokenType.Arrow && t.Text == "->");
        Assert.Contains(tokens, t => t.Type == TokenType.TypeReference && t.Text == "@Message");
        Assert.Contains(tokens, t => t.Type == TokenType.DirectiveSkip && t.Text == "%skip");
        Assert.Contains(tokens, t => t.Type == TokenType.RegexPattern);

        // Last token should be EOF
        Assert.Equal(TokenType.EndOfFile, tokens[^1].Type);
    }

    [Fact]
    public void TestOperatorsAndDelimiters()
    {
        string grammarText = "expr: a | b* c+ d? (e)";

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();

        // Verify operators
        Assert.Contains(tokens, t => t.Type == TokenType.Pipe);
        Assert.Contains(tokens, t => t.Type == TokenType.Star);
        Assert.Contains(tokens, t => t.Type == TokenType.Plus);
        Assert.Contains(tokens, t => t.Type == TokenType.Question);
        Assert.Contains(tokens, t => t.Type == TokenType.LeftParen);
        Assert.Contains(tokens, t => t.Type == TokenType.RightParen);
    }

    [Fact]
    public void TestDirectives()
    {
        string grammarText = @"
%define syntax
%if syntax == proto3
%else
%endif
%skip: /\s+/
";

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();

        Assert.Contains(tokens, t => t.Type == TokenType.DirectiveDefine);
        Assert.Contains(tokens, t => t.Type == TokenType.DirectiveIf);
        Assert.Contains(tokens, t => t.Type == TokenType.DirectiveElse);
        Assert.Contains(tokens, t => t.Type == TokenType.DirectiveEndif);
        Assert.Contains(tokens, t => t.Type == TokenType.DirectiveSkip);
    }

    [Fact]
    public void TestRegexWithFlags()
    {
        string grammarText = @"MULTILINE: /(?:(?!''').)*'''/s";

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();

        var regexToken = tokens.FirstOrDefault(t => t.Type == TokenType.RegexPattern);
        Assert.NotNull(regexToken);
        Assert.Equal("(?:(?!''').)*'''", regexToken.Text);
        Assert.Equal("s", regexToken.RegexFlags);
    }

    [Fact]
    public void TestComments()
    {
        string grammarText = @"
# Line comment
rule: expr  // another comment
/* block
   comment */
token: /pattern/
";

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();

        // Comments should be skipped, not tokenized
        Assert.DoesNotContain(tokens, t => t.Text.Contains("comment"));

        // But actual content should be there
        Assert.Contains(tokens, t => t.Type == TokenType.RuleName && t.Text == "rule");
        Assert.Contains(tokens, t => t.Type == TokenType.RuleName && t.Text == "token");
    }

    [Fact]
    public void TestUnterminatedStringThrows()
    {
        string grammarText = @"rule: ""unterminated";

        var lexer = new GrammarLexer(grammarText);

        var exception = Assert.Throws<GrammarLexerException>(() => lexer.Tokenize());
        Assert.Contains("Unterminated string literal", exception.Message);
    }

    [Fact]
    public void TestUnterminatedRegexThrows()
    {
        string grammarText = @"TOKEN: /unterminated";

        var lexer = new GrammarLexer(grammarText);

        var exception = Assert.Throws<GrammarLexerException>(() => lexer.Tokenize());
        Assert.Contains("Unterminated regex pattern", exception.Message);
    }
}