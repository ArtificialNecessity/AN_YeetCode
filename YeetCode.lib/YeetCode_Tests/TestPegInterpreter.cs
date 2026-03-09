using System.Text.Json;
using YeetCode.Grammar;
using Xunit;

namespace YeetCode_Tests;

/// <summary>
/// Tests for PegInterpreter - executing grammars against input text.
/// </summary>
public class TestPegInterpreter
{
    [Fact]
    public void TestSimpleLiteralMatching()
    {
        // Grammar: hello: "hello" "world" (with skip pattern for whitespace)
        string grammarText = """
        hello ::= "hello" "world"
        %skip ::= /\s+/
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var interpreter = new PegInterpreter(grammar);
        var result = interpreter.Parse("hello world");

        Assert.NotNull(result);
    }

    [Fact]
    public void TestTokenMatching()
    {
        // Grammar with token
        string grammarText = """
        greeting ::= "hello" name:IDENT
        IDENT ::= /[a-zA-Z]+/
        %skip ::= /\s+/
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var interpreter = new PegInterpreter(grammar);
        var result = interpreter.Parse("hello world");

        Assert.NotNull(result);

        // Should have captured "name"
        Assert.True(result.RootElement.TryGetProperty("name", out var nameElement));
    }

    [Fact]
    public void TestChoiceExpression()
    {
        string grammarText = """
        color ::= "red" | "green" | "blue"
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var interpreter = new PegInterpreter(grammar);

        // Should match "green"
        var result = interpreter.Parse("green");
        Assert.NotNull(result);
    }

    [Fact]
    public void TestRepeatZeroOrMore()
    {
        string grammarText = """
        items ::= item*
        item ::= IDENT
        IDENT ::= /[a-z]+/
        %skip ::= /\s+/
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var interpreter = new PegInterpreter(grammar);

        // Should match zero items (empty input)
        var emptyResult = interpreter.Parse("");
        Assert.NotNull(emptyResult);

        // Should match multiple items
        var multiResult = interpreter.Parse("foo bar baz");
        Assert.NotNull(multiResult);
    }

    [Fact]
    public void TestFailedParse()
    {
        string grammarText = """
        exact ::= "hello"
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var interpreter = new PegInterpreter(grammar);

        // Should throw on mismatch
        Assert.Throws<PegInterpreterException>(() => interpreter.Parse("goodbye"));
    }
}