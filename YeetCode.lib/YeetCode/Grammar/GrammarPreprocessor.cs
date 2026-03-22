using System.Text;

namespace YeetCode.Grammar;

/// <summary>
/// Preprocesses .yeet grammar files by resolving %define parameters and
/// %if/%else/%endif conditional blocks.
/// 
/// Preprocessing happens BEFORE lexing/parsing - the output is a modified
/// grammar text with dead branches removed and parameters substituted.
/// </summary>
public class GrammarPreprocessor
{
    private readonly Dictionary<string, string> _definedParameters;

    public GrammarPreprocessor(Dictionary<string, string>? definedParameters = null)
    {
        _definedParameters = definedParameters ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Preprocess grammar text by resolving conditionals and parameter substitutions.
    /// Returns the processed grammar text ready for lexing.
    /// </summary>
    public string Preprocess(string grammarSourceText)
    {
        var processedLines = new List<string>();
        var grammarLines = grammarSourceText.Split('\n');
        
        int lineIndex = 0;
        while (lineIndex < grammarLines.Length)
        {
            string currentLine = grammarLines[lineIndex];
            string trimmedLine = currentLine.Trim();

            // Handle %define directive
            if (trimmedLine.StartsWith("%define "))
            {
                // %define declares a parameter name - skip this line in output
                // (parameters are supplied via constructor, not declared in output)
                lineIndex++;
                continue;
            }

            // Handle %if directive
            if (trimmedLine.StartsWith("%if "))
            {
                string conditionExpression = trimmedLine.Substring(4).Trim();
                bool conditionResult = EvaluateCondition(conditionExpression);
                
                // Process the conditional block
                var (processedBlock, nextLineIndex) = ProcessConditionalBlock(
                    grammarLines, lineIndex + 1, conditionResult);
                
                processedLines.AddRange(processedBlock);
                lineIndex = nextLineIndex;
                continue;
            }

            // Handle %else and %endif outside of %if context (error)
            if (trimmedLine.StartsWith("%else") || trimmedLine.StartsWith("%endif"))
            {
                throw new GrammarPreprocessorException(
                    $"Unexpected {trimmedLine.Split(' ')[0]} directive at line {lineIndex + 1} without matching %if"
                );
            }

            // Regular line - keep it
            processedLines.Add(currentLine);
            lineIndex++;
        }

        return string.Join("\n", processedLines);
    }

    /// <summary>
    /// Process a conditional block starting after %if.
    /// Returns the lines to include and the line index after %endif.
    /// </summary>
    private (List<string> includedLines, int nextLineIndex) ProcessConditionalBlock(
        string[] grammarLines, int startLineIndex, bool includeIfBranch)
    {
        var includedLines = new List<string>();
        int lineIndex = startLineIndex;
        bool inElseBranch = false;
        bool foundEndif = false;

        while (lineIndex < grammarLines.Length)
        {
            string currentLine = grammarLines[lineIndex];
            string trimmedLine = currentLine.Trim();

            // Handle nested %if
            if (trimmedLine.StartsWith("%if "))
            {
                string nestedCondition = trimmedLine.Substring(4).Trim();
                bool nestedConditionResult = EvaluateCondition(nestedCondition);
                
                // Recursively process nested conditional
                var (nestedLines, nextIndex) = ProcessConditionalBlock(
                    grammarLines, lineIndex + 1, nestedConditionResult);
                
                // Only include nested block if we're in the active branch
                if ((includeIfBranch && !inElseBranch) || (!includeIfBranch && inElseBranch))
                {
                    includedLines.AddRange(nestedLines);
                }
                
                lineIndex = nextIndex;
                continue;
            }

            // Handle %else
            if (trimmedLine.StartsWith("%else"))
            {
                inElseBranch = true;
                lineIndex++;
                continue;
            }

            // Handle %endif
            if (trimmedLine.StartsWith("%endif"))
            {
                foundEndif = true;
                lineIndex++;
                break;
            }

            // Regular line - include if we're in the active branch
            if ((includeIfBranch && !inElseBranch) || (!includeIfBranch && inElseBranch))
            {
                includedLines.Add(currentLine);
            }

            lineIndex++;
        }

        if (!foundEndif)
        {
            throw new GrammarPreprocessorException(
                $"Unclosed %if directive starting at line {startLineIndex} - missing %endif"
            );
        }

        return (includedLines, lineIndex);
    }

    /// <summary>
    /// Evaluate a condition expression like "syntax == proto3" or "mode != strict".
    /// Supports: ==, !=
    /// </summary>
    private bool EvaluateCondition(string conditionExpression)
    {
        // Parse: param_name == "value" or param_name != "value"
        string[] comparisonOperators = { "==", "!=" };
        
        foreach (var comparisonOp in comparisonOperators)
        {
            int operatorIndex = conditionExpression.IndexOf(comparisonOp);
            if (operatorIndex > 0)
            {
                string parameterName = conditionExpression.Substring(0, operatorIndex).Trim();
                string expectedValue = conditionExpression.Substring(operatorIndex + comparisonOp.Length).Trim();
                
                // Remove quotes from expected value if present
                if (expectedValue.StartsWith('"') && expectedValue.EndsWith('"'))
                {
                    expectedValue = expectedValue[1..^1];
                }

                // Get actual parameter value
                string actualValue = _definedParameters.GetValueOrDefault(parameterName, "");

                // Evaluate comparison
                bool comparisonResult = comparisonOp switch
                {
                    "==" => actualValue == expectedValue,
                    "!=" => actualValue != expectedValue,
                    _ => throw new GrammarPreprocessorException($"Unknown comparison operator: {comparisonOp}")
                };

                return comparisonResult;
            }
        }

        // No comparison operator - treat as boolean parameter existence check
        string boolParamName = conditionExpression.Trim();
        return _definedParameters.ContainsKey(boolParamName) && 
               _definedParameters[boolParamName] != "false" &&
               _definedParameters[boolParamName] != "0";
    }
}

/// <summary>
/// Exception thrown when the grammar preprocessor encounters an error.
/// </summary>
public class GrammarPreprocessorException : Exception
{
    public GrammarPreprocessorException(string message) : base(message) { }
}