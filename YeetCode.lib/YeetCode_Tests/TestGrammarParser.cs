using YeetCode.Grammar;
using Xunit;

namespace YeetCode_Tests;

/// <summary>
/// Tests for GrammarParser - parsing .yeet grammar files into AST.
/// </summary>
public class TestGrammarParser
{
    [Fact]
    public void TestSimpleRuleParsing()
    {
        string grammarText = """
        message: "message" name:IDENT "{" "}"
          -> @Message
        
        IDENT: /[a-zA-Z_][a-zA-Z0-9_]*/
        %skip: /\s+/
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();

        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        // Verify we got the rule
        Assert.True(grammar.ParserRules.ContainsKey("message"));
        var messageRule = grammar.ParserRules["message"];
        Assert.NotNull(messageRule.Expression);
        Assert.Single(messageRule.TypeMappings);
        Assert.Equal("Message", messageRule.TypeMappings[0].TargetTypeName);

        // Verify we got the token
        Assert.True(grammar.LexerTokens.ContainsKey("IDENT"));
        Assert.Equal("[a-zA-Z_][a-zA-Z0-9_]*", grammar.LexerTokens["IDENT"].RegexPattern);

        // Verify skip pattern (lexer strips the / delimiters)
        Assert.Equal("\\s+", grammar.SkipPattern);
    }

    [Fact]
    public void TestChoiceExpression()
    {
        string grammarText = """
        primitive: "int32" | "string" | "bool"
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var primitiveRule = grammar.ParserRules["primitive"];
        Assert.IsType<ChoiceExpression>(primitiveRule.Expression);

        var choiceExpr = (ChoiceExpression)primitiveRule.Expression;
        Assert.Equal(3, choiceExpr.ChoiceAlternatives.Count);
    }

    [Fact]
    public void TestSequenceExpression()
    {
        string grammarText = """
        field: label:LABEL type:IDENT name:IDENT "=" tag:INT ";"
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var fieldRule = grammar.ParserRules["field"];
        Assert.IsType<SequenceExpression>(fieldRule.Expression);

        var seqExpr = (SequenceExpression)fieldRule.Expression;
        Assert.True(seqExpr.SequenceElements.Count >= 6); // At least 6 elements
    }

    [Fact]
    public void TestRepeatExpressions()
    {
        string grammarText = """
        file: item* item+ item?
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var fileRule = grammar.ParserRules["file"];
        Assert.IsType<SequenceExpression>(fileRule.Expression);

        var seqExpr = (SequenceExpression)fileRule.Expression;
        Assert.Equal(3, seqExpr.SequenceElements.Count);

        Assert.IsType<RepeatExpression>(seqExpr.SequenceElements[0]);
        Assert.IsType<RepeatExpression>(seqExpr.SequenceElements[1]);
        Assert.IsType<RepeatExpression>(seqExpr.SequenceElements[2]);

        Assert.Equal(RepeatMode.ZeroOrMore, ((RepeatExpression)seqExpr.SequenceElements[0]).Mode);
        Assert.Equal(RepeatMode.OneOrMore, ((RepeatExpression)seqExpr.SequenceElements[1]).Mode);
        Assert.Equal(RepeatMode.Optional, ((RepeatExpression)seqExpr.SequenceElements[2]).Mode);
    }

    [Fact]
    public void TestCaptureExpression()
    {
        string grammarText = """
        rule: name:IDENT
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var rule = grammar.ParserRules["rule"];
        Assert.IsType<CaptureExpression>(rule.Expression);

        var captureExpr = (CaptureExpression)rule.Expression;
        Assert.Equal("name", captureExpr.CaptureName);
        Assert.IsType<TokenRefExpression>(captureExpr.CapturedExpression);
    }

    [Fact]
    public void TestGroupExpression()
    {
        string grammarText = """
        expr: ("+" | "-") term
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var exprRule = grammar.ParserRules["expr"];
        Assert.IsType<SequenceExpression>(exprRule.Expression);

        var seqExpr = (SequenceExpression)exprRule.Expression;
        Assert.IsType<GroupExpression>(seqExpr.SequenceElements[0]);
    }

    [Fact]
    public void TestTypeMappingWithDiscriminator()
    {
        string grammarText = """
        binary: left:expr op:OP right:expr
          -> @Expression { kind: @Binary }
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var binaryRule = grammar.ParserRules["binary"];
        Assert.Single(binaryRule.TypeMappings);

        var mapping = binaryRule.TypeMappings[0];
        Assert.Equal("Expression", mapping.TargetTypeName);
        Assert.Equal("Binary", mapping.KindVariantName);
    }

    [Fact]
    public void TestArrayAppendMapping()
    {
        string grammarText = """
        message: "message" name:IDENT "{" "}"
          -> messages[]
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var messageRule = grammar.ParserRules["message"];
        Assert.Single(messageRule.TypeMappings);

        var mapping = messageRule.TypeMappings[0];
        Assert.Equal("messages", mapping.SchemaPath);
        Assert.True(mapping.IsArrayAppend);
    }

    [Fact]
    public void TestMapInsertMapping()
    {
        string grammarText = """
        enum: "enum" name:IDENT "{" "}"
          -> enums[name]
        """;

        var lexer = new GrammarLexer(grammarText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        var enumRule = grammar.ParserRules["enum"];
        Assert.Single(enumRule.TypeMappings);

        var mapping = enumRule.TypeMappings[0];
        Assert.Equal("enums", mapping.SchemaPath);
        Assert.Equal("name", mapping.MapKeyCaptureName);
        Assert.False(mapping.IsArrayAppend);
    }
}