namespace YeetJson;

using System.Text.RegularExpressions;

/// <summary>
/// Phase 1: Scans source text for structural validity
/// Tracks delimiters, finds mismatches, generates repair hypotheses
/// </summary>
public class StructuralAnalyzer
{
    public StructureResult Analyze(string sourceText)
    {
        var delimiterStack = new Stack<Delimiter>();
        var structuralErrors = new List<StructuralError>();
        var allDelimiters = new List<Delimiter>();

        int charIndex = 0;
        int lineNumber = 1;
        int columnNumber = 1;

        while (charIndex < sourceText.Length)
        {
            char currentChar = sourceText[charIndex];

            // Track line/column position
            if (currentChar == '\n')
            {
                lineNumber++;
                columnNumber = 1;
                charIndex++;
                continue;
            }
            else
            {
                columnNumber++;
            }

            // Inside string? Only look for closing quote
            if (delimiterStack.Count > 0 && IsStringDelimiter(delimiterStack.Peek().Type))
            {
                if (IsMatchingStringClose(delimiterStack.Peek(), sourceText, charIndex, out int skipCount))
                {
                    var stringOpener = delimiterStack.Pop();
                    var stringCloser = new Delimiter(
                        stringOpener.Type, lineNumber, columnNumber, charIndex, false,
                        GetContextSnippet(sourceText, charIndex, 20)
                    );
                    allDelimiters.Add(stringCloser);
                    charIndex += skipCount;
                    continue;
                }

                // Unclosed string at EOL (for non-multiline)
                if (currentChar == '\n' && delimiterStack.Peek().Type != DelimiterType.TripleDoubleQuote)
                {
                    var unclosedStringOpener = delimiterStack.Pop();
                    structuralErrors.Add(CreateUnclosedStringError(unclosedStringOpener, lineNumber, columnNumber));
                }

                // Handle escape sequences
                if (currentChar == '\\' && charIndex + 1 < sourceText.Length)
                {
                    charIndex += 2;
                    columnNumber++;
                    continue;
                }

                charIndex++;
                continue;
            }

            // Skip HJSON comments (//, /* */, and #)
            if (currentChar == '/' && charIndex + 1 < sourceText.Length)
            {
                if (sourceText[charIndex + 1] == '/')
                {
                    charIndex = SkipToEndOfLine(sourceText, charIndex);
                    continue;
                }
                if (sourceText[charIndex + 1] == '*')
                {
                    (charIndex, lineNumber, columnNumber) = SkipBlockComment(sourceText, charIndex, lineNumber, columnNumber);
                    continue;
                }
            }

            if (currentChar == '#')
            {
                charIndex = SkipToEndOfLine(sourceText, charIndex);
                continue;
            }

            // Check for opening delimiters
            if (TryGetOpenerDelimiter(sourceText, charIndex, out var openerType, out int openerLength))
            {
                var openingDelimiter = new Delimiter(
                    openerType, lineNumber, columnNumber, charIndex, true,
                    GetContextSnippet(sourceText, charIndex, 40)
                );
                delimiterStack.Push(openingDelimiter);
                allDelimiters.Add(openingDelimiter);
                charIndex += openerLength;
                columnNumber += openerLength - 1;
                continue;
            }

            // Check for closing delimiters
            if (TryGetCloserDelimiter(currentChar, out var closerType))
            {
                var closingDelimiter = new Delimiter(
                    closerType, lineNumber, columnNumber, charIndex, false,
                    GetContextSnippet(sourceText, charIndex, 40)
                );
                allDelimiters.Add(closingDelimiter);

                if (delimiterStack.Count == 0)
                {
                    // Unmatched close - no opener on stack
                    structuralErrors.Add(CreateUnmatchedCloseError(closingDelimiter, allDelimiters));
                }
                else if (delimiterStack.Peek().Type != closerType)
                {
                    // Mismatched pair - opener doesn't match closer
                    var mismatchedOpener = delimiterStack.Pop();
                    structuralErrors.Add(CreateMismatchError(mismatchedOpener, closingDelimiter));
                }
                else
                {
                    // Matched pair - all good
                    delimiterStack.Pop();
                }

                charIndex++;
                continue;
            }

            charIndex++;
        }

        // Anything left on stack is unclosed
        while (delimiterStack.Count > 0)
        {
            var unclosedDelimiter = delimiterStack.Pop();
            structuralErrors.Add(CreateUnclosedError(sourceText, allDelimiters, unclosedDelimiter));
        }

        // Phase 1.5: Identify regions
        var codeRegions = RegionIsolator.IdentifyRegions(sourceText, allDelimiters, structuralErrors);

        return new StructureResult(allDelimiters, structuralErrors, codeRegions);
    }

    private bool IsStringDelimiter(DelimiterType delimiterType)
    {
        return delimiterType == DelimiterType.DoubleQuote ||
               delimiterType == DelimiterType.SingleQuote ||
               delimiterType == DelimiterType.TripleDoubleQuote;
    }

    private bool TryGetOpenerDelimiter(string sourceText, int position, out DelimiterType delimiterType, out int length)
    {
        delimiterType = default;
        length = 1;

        // Check for triple-double-quote first (multiline string — standard HJSON uses """)
        if (position + 2 < sourceText.Length && sourceText.Substring(position, 3) == "\"\"\"")
        {
            delimiterType = DelimiterType.TripleDoubleQuote;
            length = 3;
            return true;
        }

        switch (sourceText[position])
        {
            case '{': delimiterType = DelimiterType.Brace; return true;
            case '[': delimiterType = DelimiterType.Bracket; return true;
            case '(': delimiterType = DelimiterType.Paren; return true;
            case '"': delimiterType = DelimiterType.DoubleQuote; return true;
            case '\'': delimiterType = DelimiterType.SingleQuote; return true;
            default: return false;
        }
    }

    private bool TryGetCloserDelimiter(char character, out DelimiterType delimiterType)
    {
        delimiterType = character switch
        {
            '}' => DelimiterType.Brace,
            ']' => DelimiterType.Bracket,
            ')' => DelimiterType.Paren,
            _ => default
        };
        return delimiterType != default || character == '}' || character == ']' || character == ')';
    }

    private bool IsMatchingStringClose(Delimiter stringOpener, string sourceText, int position, out int skipCount)
    {
        skipCount = 1;

        if (stringOpener.Type == DelimiterType.TripleDoubleQuote)
        {
            if (position + 2 < sourceText.Length && sourceText.Substring(position, 3) == "\"\"\"")
            {
                skipCount = 3;
                return true;
            }
            return false;
        }

        char expectedCloseChar = stringOpener.Type switch
        {
            DelimiterType.DoubleQuote => '"',
            DelimiterType.SingleQuote => '\'',
            _ => '\0'
        };

        return sourceText[position] == expectedCloseChar;
    }

    private string GetContextSnippet(string sourceText, int centerOffset, int maxLength)
    {
        int startOffset = Math.Max(0, centerOffset - maxLength / 2);
        int endOffset = Math.Min(sourceText.Length, centerOffset + maxLength / 2);
        return sourceText.Substring(startOffset, endOffset - startOffset).Replace("\n", "\\n");
    }

    private StructuralError CreateUnclosedError(string sourceText, List<Delimiter> allDelimiters, Delimiter unclosedOpener)
    {
        var repairHypotheses = HypothesisGenerators.UnclosedHypothesisGenerator.Generate(
            sourceText, allDelimiters, unclosedOpener
        );

        return new StructuralError(
            StructuralErrorKind.UnclosedDelimiter,
            unclosedOpener, null,
            unclosedOpener.Line, unclosedOpener.Column,
            $"Opening '{GetDelimiterDisplayChar(unclosedOpener.Type)}' at line {unclosedOpener.Line} col {unclosedOpener.Column} is never closed",
            repairHypotheses
        );
    }

    private StructuralError CreateMismatchError(Delimiter opener, Delimiter closer)
    {
        var repairHypotheses = HypothesisGenerators.MismatchHypothesisGenerator.Generate(opener, closer);

        return new StructuralError(
            StructuralErrorKind.MismatchedPair,
            opener, closer,
            closer.Line, closer.Column,
            $"Closing '{GetDelimiterDisplayChar(closer.Type)}' at line {closer.Line} doesn't match opening '{GetDelimiterDisplayChar(opener.Type)}' at line {opener.Line}",
            repairHypotheses
        );
    }

    private StructuralError CreateUnmatchedCloseError(Delimiter unmatchedCloser, List<Delimiter> allDelimiters)
    {
        var repairHypotheses = HypothesisGenerators.UnmatchedCloseHypothesisGenerator.Generate(allDelimiters, unmatchedCloser);

        return new StructuralError(
            StructuralErrorKind.UnmatchedClose,
            null, unmatchedCloser,
            unmatchedCloser.Line, unmatchedCloser.Column,
            $"Unexpected closing '{GetDelimiterDisplayChar(unmatchedCloser.Type)}' with no matching opener",
            repairHypotheses
        );
    }

    private StructuralError CreateUnclosedStringError(Delimiter stringOpener, int currentLine, int currentColumn)
    {
        var repairHypotheses = new List<RepairHypothesis>
        {
            new(RepairAction.Insert, currentLine - 1, currentColumn,
                $"Insert closing quote before end of line {currentLine - 1}",
                0.8f, GetCloserDisplayChar(stringOpener.Type)),
            new(RepairAction.Delete, stringOpener.Line, stringOpener.Column,
                $"Delete the opening quote at line {stringOpener.Line}",
                0.3f)
        };

        return new StructuralError(
            StructuralErrorKind.UnclosedString,
            stringOpener, null,
            stringOpener.Line, stringOpener.Column,
            $"String opened at line {stringOpener.Line} not closed before end of line",
            repairHypotheses
        );
    }

    private string GetDelimiterDisplayChar(DelimiterType delimiterType)
    {
        return delimiterType switch
        {
            DelimiterType.Brace => "{",
            DelimiterType.Bracket => "[",
            DelimiterType.Paren => "(",
            DelimiterType.DoubleQuote => "\"",
            DelimiterType.SingleQuote => "'",
            DelimiterType.TripleDoubleQuote => "\"\"\"",
            _ => "?"
        };
    }

    private string GetCloserDisplayChar(DelimiterType delimiterType)
    {
        return delimiterType switch
        {
            DelimiterType.Brace => "}",
            DelimiterType.Bracket => "]",
            DelimiterType.Paren => ")",
            DelimiterType.DoubleQuote => "\"",
            DelimiterType.SingleQuote => "'",
            DelimiterType.TripleDoubleQuote => "\"\"\"",
            _ => "?"
        };
    }

    private int SkipToEndOfLine(string sourceText, int startPosition)
    {
        while (startPosition < sourceText.Length && sourceText[startPosition] != '\n')
        {
            startPosition++;
        }
        return startPosition;
    }

    private (int offset, int line, int col) SkipBlockComment(string sourceText, int startPosition, int currentLine, int currentColumn)
    {
        startPosition += 2; // Skip /*
        currentColumn += 2;

        while (startPosition + 1 < sourceText.Length)
        {
            if (sourceText[startPosition] == '\n')
            {
                currentLine++;
                currentColumn = 1;
            }
            else
            {
                currentColumn++;
            }

            if (sourceText[startPosition] == '*' && sourceText[startPosition + 1] == '/')
            {
                return (startPosition + 2, currentLine, currentColumn + 2);
            }
            startPosition++;
        }

        return (startPosition, currentLine, currentColumn);
    }
}