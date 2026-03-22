using System.Text.Json;
using YeetCode.Grammar;
using Xunit;

namespace YeetCode_Tests;

/// <summary>
/// Integration tests for the full grammar engine pipeline:
/// grammar text → lexer → parser → PEG interpreter → JsonDocument
/// </summary>
public class TestGrammarIntegration
{
    [Fact]
    public void TestSimpleProtobufParseSingleMessage()
    {
        string grammarText = """
        file ::= message*
        message ::= "message" name:IDENT "{" field* "}"
        field ::= type:IDENT name:IDENT "=" tag:INT ";"
        IDENT ::= /[a-zA-Z_][a-zA-Z0-9_]*/
        INT ::= /[0-9]+/
        %skip ::= /(?:\s|\/\/[^\n]*)*/
        """;

        string protoInput = """
        message Widget {
            string name = 1;
            int32 quantity = 2;
        }
        """;

        var parsedGrammar = ParseGrammar(grammarText);
        var interpreter = new PegInterpreter(parsedGrammar);
        var resultDocument = interpreter.Parse(protoInput);

        Assert.NotNull(resultDocument);
        // The parse should succeed without throwing
    }

    [Fact]
    public void TestCapturedFieldValues()
    {
        // Grammar that captures a single greeting with a name
        string grammarText = """
        greeting ::= "hello" name:IDENT
        IDENT ::= /[a-zA-Z_][a-zA-Z0-9_]*/
        %skip ::= /\s+/
        """;

        var parsedGrammar = ParseGrammar(grammarText);
        var interpreter = new PegInterpreter(parsedGrammar);
        var resultDocument = interpreter.Parse("hello world");

        Assert.NotNull(resultDocument);
        var rootElement = resultDocument.RootElement;
        Assert.True(rootElement.TryGetProperty("name", out var nameElement));
        Assert.Equal("world", nameElement.GetString());
    }

    [Fact]
    public void TestMultipleCaptures()
    {
        string grammarText = """
        assignment ::= target:IDENT "=" value:INT
        IDENT ::= /[a-zA-Z_][a-zA-Z0-9_]*/
        INT ::= /[0-9]+/
        %skip ::= /\s+/
        """;

        var parsedGrammar = ParseGrammar(grammarText);
        var interpreter = new PegInterpreter(parsedGrammar);
        var resultDocument = interpreter.Parse("x = 42");

        var rootElement = resultDocument.RootElement;
        Assert.True(rootElement.TryGetProperty("target", out var targetElement));
        Assert.Equal("x", targetElement.GetString());
        Assert.True(rootElement.TryGetProperty("value", out var valueElement));
        Assert.Equal("42", valueElement.GetString());
    }

    [Fact]
    public void TestChoiceWithCapture()
    {
        string grammarText = """
        value ::= name:IDENT | number:INT
        IDENT ::= /[a-zA-Z_][a-zA-Z0-9_]*/
        INT ::= /[0-9]+/
        %skip ::= /\s+/
        """;

        var parsedGrammar = ParseGrammar(grammarText);
        var interpreter = new PegInterpreter(parsedGrammar);

        // Should match IDENT branch
        var identResult = interpreter.Parse("foo");
        Assert.True(identResult.RootElement.TryGetProperty("name", out var nameElement));
        Assert.Equal("foo", nameElement.GetString());

        // Should match INT branch
        var intResult = interpreter.Parse("123");
        Assert.True(intResult.RootElement.TryGetProperty("number", out var numberElement));
        Assert.Equal("123", numberElement.GetString());
    }

    [Fact]
    public void TestOptionalCapture()
    {
        string grammarText = """
        decl ::= modifier:MODIFIER? name:IDENT
        MODIFIER ::= /public|private/
        IDENT ::= /[a-zA-Z_][a-zA-Z0-9_]*/
        %skip ::= /\s+/
        """;

        var parsedGrammar = ParseGrammar(grammarText);
        var interpreter = new PegInterpreter(parsedGrammar);

        // With modifier
        var withModifier = interpreter.Parse("public Widget");
        Assert.True(withModifier.RootElement.TryGetProperty("name", out var nameWithMod));
        Assert.Equal("Widget", nameWithMod.GetString());

        // Without modifier
        var withoutModifier = interpreter.Parse("Widget");
        Assert.True(withoutModifier.RootElement.TryGetProperty("name", out var nameWithoutMod));
        Assert.Equal("Widget", nameWithoutMod.GetString());
    }

    [Fact]
    public void TestPreprocessorThenParse()
    {
        // Full pipeline: preprocess → lex → parse → interpret
        string grammarText = """
        %define version
        %if version == "v2"
        decl ::= label:LABEL name:IDENT
        %else
        decl ::= name:IDENT
        %endif
        LABEL ::= /required|optional/
        IDENT ::= /[a-zA-Z_][a-zA-Z0-9_]*/
        %skip ::= /\s+/
        """;

        // v1 mode: no label
        var v1Params = new Dictionary<string, string> { { "version", "v1" } };
        var v1Preprocessor = new GrammarPreprocessor(v1Params);
        string v1ProcessedText = v1Preprocessor.Preprocess(grammarText);
        var v1Grammar = ParseGrammar(v1ProcessedText);
        var v1Interpreter = new PegInterpreter(v1Grammar);
        var v1Result = v1Interpreter.Parse("Widget");
        Assert.True(v1Result.RootElement.TryGetProperty("name", out var v1Name));
        Assert.Equal("Widget", v1Name.GetString());

        // v2 mode: with label
        var v2Params = new Dictionary<string, string> { { "version", "v2" } };
        var v2Preprocessor = new GrammarPreprocessor(v2Params);
        string v2ProcessedText = v2Preprocessor.Preprocess(grammarText);
        var v2Grammar = ParseGrammar(v2ProcessedText);
        var v2Interpreter = new PegInterpreter(v2Grammar);
        var v2Result = v2Interpreter.Parse("required Widget");
        Assert.True(v2Result.RootElement.TryGetProperty("label", out var v2Label));
        Assert.Equal("required", v2Label.GetString());
        Assert.True(v2Result.RootElement.TryGetProperty("name", out var v2Name));
        Assert.Equal("Widget", v2Name.GetString());
    }

    [Fact]
    public void TestGrammarFromFile()
    {
        // Load grammar from test data file
        string grammarFilePath = Path.Combine("TestData", "simple_proto.grammar.yeet");
        string grammarText = File.ReadAllText(grammarFilePath);

        var parsedGrammar = ParseGrammar(grammarText);

        // Verify grammar structure
        Assert.True(parsedGrammar.ParserRules.ContainsKey("file"));
        Assert.True(parsedGrammar.ParserRules.ContainsKey("message"));
        Assert.True(parsedGrammar.ParserRules.ContainsKey("field"));
        Assert.True(parsedGrammar.LexerTokens.ContainsKey("IDENT"));
        Assert.True(parsedGrammar.LexerTokens.ContainsKey("INT"));
        Assert.NotNull(parsedGrammar.SkipPattern);

        // Parse a simple proto input
        var interpreter = new PegInterpreter(parsedGrammar);
        string protoInput = """
        message Widget {
            string name = 1;
            int32 quantity = 2;
        }
        """;

        var resultDocument = interpreter.Parse(protoInput);
        Assert.NotNull(resultDocument);
    }

    [Fact]
    public void TestParseFailureGivesGoodError()
    {
        string grammarText = """
        decl ::= "let" name:IDENT "=" value:INT ";"
        IDENT ::= /[a-zA-Z_][a-zA-Z0-9_]*/
        INT ::= /[0-9]+/
        %skip ::= /\s+/
        """;

        var parsedGrammar = ParseGrammar(grammarText);
        var interpreter = new PegInterpreter(parsedGrammar);

        // Missing semicolon should fail
        var parseException = Assert.Throws<PegInterpreterException>(
            () => interpreter.Parse("let x = 42")
        );
        Assert.Contains("Failed to match", parseException.Message);
    }

    // ── Helper ──────────────────────────────────────

    private static ParsedGrammar ParseGrammar(string grammarText)
    {
        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        return parser.Parse(tokens);
    }
}