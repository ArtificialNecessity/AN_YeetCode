using YeetCode.Grammar;
using Xunit;

namespace YeetCode_Tests;

/// <summary>
/// Tests for GrammarPreprocessor - %define, %if/%else/%endif resolution.
/// </summary>
public class TestGrammarPreprocessor
{
    [Fact]
    public void TestPassthroughWithNoDirectives()
    {
        string grammarText = """
        file ::= message*
        message ::= "message" name:IDENT "{" "}"
        IDENT ::= /[a-zA-Z_]+/
        %skip ::= /\s+/
        """;

        var preprocessor = new GrammarPreprocessor();
        string processedText = preprocessor.Preprocess(grammarText);

        Assert.Contains("file ::= message*", processedText);
        Assert.Contains("message ::= \"message\" name:IDENT \"{\" \"}\"", processedText);
        Assert.Contains("IDENT ::= /[a-zA-Z_]+/", processedText);
    }

    [Fact]
    public void TestDefineDirectiveStripped()
    {
        string grammarText = """
        %define syntax
        file ::= message*
        """;

        var preprocessor = new GrammarPreprocessor();
        string processedText = preprocessor.Preprocess(grammarText);

        Assert.DoesNotContain("%define", processedText);
        Assert.Contains("file ::= message*", processedText);
    }

    [Fact]
    public void TestIfTrueBranchIncluded()
    {
        string grammarText = """
        %define syntax
        %if syntax == "proto3"
          field ::= type:IDENT name:IDENT "=" tag:INT ";"
        %else
          field ::= label:LABEL type:IDENT name:IDENT "=" tag:INT ";"
        %endif
        """;

        var definedParams = new Dictionary<string, string> { { "syntax", "proto3" } };
        var preprocessor = new GrammarPreprocessor(definedParams);
        string processedText = preprocessor.Preprocess(grammarText);

        Assert.Contains("field ::= type:IDENT name:IDENT", processedText);
        Assert.DoesNotContain("label:LABEL", processedText);
        Assert.DoesNotContain("%if", processedText);
        Assert.DoesNotContain("%else", processedText);
        Assert.DoesNotContain("%endif", processedText);
    }

    [Fact]
    public void TestIfFalseBranchIncludesElse()
    {
        string grammarText = """
        %define syntax
        %if syntax == "proto3"
          field ::= type:IDENT name:IDENT "=" tag:INT ";"
        %else
          field ::= label:LABEL type:IDENT name:IDENT "=" tag:INT ";"
        %endif
        """;

        var definedParams = new Dictionary<string, string> { { "syntax", "proto2" } };
        var preprocessor = new GrammarPreprocessor(definedParams);
        string processedText = preprocessor.Preprocess(grammarText);

        Assert.Contains("label:LABEL", processedText);
        Assert.DoesNotContain("field ::= type:IDENT name:IDENT", processedText);
    }

    [Fact]
    public void TestIfWithoutElse()
    {
        string grammarText = """
        file ::= message*
        %if enable_enums == "true"
        enum_decl ::= "enum" name:IDENT "{" "}"
        %endif
        message ::= "message" name:IDENT "{" "}"
        """;

        // With enable_enums = true
        var enabledParams = new Dictionary<string, string> { { "enable_enums", "true" } };
        var enabledPreprocessor = new GrammarPreprocessor(enabledParams);
        string enabledText = enabledPreprocessor.Preprocess(grammarText);

        Assert.Contains("enum_decl", enabledText);
        Assert.Contains("message ::=", enabledText);

        // With enable_enums = false
        var disabledParams = new Dictionary<string, string> { { "enable_enums", "false" } };
        var disabledPreprocessor = new GrammarPreprocessor(disabledParams);
        string disabledText = disabledPreprocessor.Preprocess(grammarText);

        Assert.DoesNotContain("enum_decl", disabledText);
        Assert.Contains("message ::=", disabledText);
    }

    [Fact]
    public void TestNotEqualsCondition()
    {
        string grammarText = """
        %if mode != "strict"
          relaxed_rule ::= IDENT
        %endif
        """;

        var relaxedParams = new Dictionary<string, string> { { "mode", "relaxed" } };
        var relaxedPreprocessor = new GrammarPreprocessor(relaxedParams);
        string relaxedText = relaxedPreprocessor.Preprocess(grammarText);
        Assert.Contains("relaxed_rule", relaxedText);

        var strictParams = new Dictionary<string, string> { { "mode", "strict" } };
        var strictPreprocessor = new GrammarPreprocessor(strictParams);
        string strictText = strictPreprocessor.Preprocess(grammarText);
        Assert.DoesNotContain("relaxed_rule", strictText);
    }

    [Fact]
    public void TestNestedConditionals()
    {
        string grammarText = """
        %if outer == "yes"
          outer_rule ::= IDENT
          %if inner == "yes"
            inner_rule ::= INT
          %endif
        %endif
        """;

        // Both true
        var bothTrueParams = new Dictionary<string, string> { { "outer", "yes" }, { "inner", "yes" } };
        var bothTruePreprocessor = new GrammarPreprocessor(bothTrueParams);
        string bothTrueText = bothTruePreprocessor.Preprocess(grammarText);
        Assert.Contains("outer_rule", bothTrueText);
        Assert.Contains("inner_rule", bothTrueText);

        // Outer true, inner false
        var outerOnlyParams = new Dictionary<string, string> { { "outer", "yes" }, { "inner", "no" } };
        var outerOnlyPreprocessor = new GrammarPreprocessor(outerOnlyParams);
        string outerOnlyText = outerOnlyPreprocessor.Preprocess(grammarText);
        Assert.Contains("outer_rule", outerOnlyText);
        Assert.DoesNotContain("inner_rule", outerOnlyText);

        // Outer false — nothing included
        var outerFalseParams = new Dictionary<string, string> { { "outer", "no" } };
        var outerFalsePreprocessor = new GrammarPreprocessor(outerFalseParams);
        string outerFalseText = outerFalsePreprocessor.Preprocess(grammarText);
        Assert.DoesNotContain("outer_rule", outerFalseText);
        Assert.DoesNotContain("inner_rule", outerFalseText);
    }

    [Fact]
    public void TestUnclosedIfThrows()
    {
        string grammarText = """
        %if mode == "test"
          rule ::= IDENT
        """;

        var preprocessor = new GrammarPreprocessor(new Dictionary<string, string> { { "mode", "test" } });
        Assert.Throws<GrammarPreprocessorException>(() => preprocessor.Preprocess(grammarText));
    }

    [Fact]
    public void TestOrphanElseThrows()
    {
        string grammarText = """
        %else
          rule ::= IDENT
        %endif
        """;

        var preprocessor = new GrammarPreprocessor();
        Assert.Throws<GrammarPreprocessorException>(() => preprocessor.Preprocess(grammarText));
    }

    [Fact]
    public void TestBooleanParameterExistence()
    {
        string grammarText = """
        %if debug
          debug_rule ::= IDENT
        %endif
        """;

        // Parameter exists and is truthy
        var debugParams = new Dictionary<string, string> { { "debug", "true" } };
        var debugPreprocessor = new GrammarPreprocessor(debugParams);
        string debugText = debugPreprocessor.Preprocess(grammarText);
        Assert.Contains("debug_rule", debugText);

        // Parameter doesn't exist
        var noDebugPreprocessor = new GrammarPreprocessor();
        string noDebugText = noDebugPreprocessor.Preprocess(grammarText);
        Assert.DoesNotContain("debug_rule", noDebugText);
    }

    [Fact]
    public void TestPreprocessThenParse()
    {
        // End-to-end: preprocess then lex+parse a grammar
        string grammarText = """
        %define syntax
        %if syntax == "proto3"
        field ::= type:IDENT name:IDENT "=" tag:INT ";"
        %else
        field ::= label:LABEL type:IDENT name:IDENT "=" tag:INT ";"
        %endif
        IDENT ::= /[a-zA-Z_]+/
        INT ::= /[0-9]+/
        LABEL ::= /required|optional|repeated/
        """;

        var definedParams = new Dictionary<string, string> { { "syntax", "proto3" } };
        var preprocessor = new GrammarPreprocessor(definedParams);
        string processedText = preprocessor.Preprocess(grammarText);

        // Now lex and parse the processed grammar
        var lexer = new GrammarLexer(processedText);
        var tokens = lexer.Tokenize();
        var parser = new GrammarParser();
        var grammar = parser.Parse(tokens);

        // Should have the proto3 version of field (no label capture)
        Assert.True(grammar.ParserRules.ContainsKey("field"));
        var fieldRule = grammar.ParserRules["field"];
        Assert.NotNull(fieldRule.Expression);
    }
}