namespace YeetCode.Core;

/// <summary>
/// String case conversion functions used by templates and throughout YeetCode.
/// Converts between naming conventions: snake_case, PascalCase, camelCase, etc.
/// </summary>
public static class StringCaseConverter
{
    /// <summary>Convert to PascalCase: "hello_world" → "HelloWorld"</summary>
    public static string ToPascalCase(string inputText)
    {
        if (string.IsNullOrEmpty(inputText)) return inputText;

        var wordSegments = SplitIntoWords(inputText);
        return string.Concat(wordSegments.Select(CapitalizeFirstLetter));
    }

    /// <summary>Convert to camelCase: "hello_world" → "helloWorld"</summary>
    public static string ToCamelCase(string inputText)
    {
        if (string.IsNullOrEmpty(inputText)) return inputText;

        var wordSegments = SplitIntoWords(inputText);
        var firstWord = wordSegments.First().ToLowerInvariant();
        var remainingWords = wordSegments.Skip(1).Select(CapitalizeFirstLetter);
        return firstWord + string.Concat(remainingWords);
    }

    /// <summary>Convert to snake_case: "HelloWorld" → "hello_world"</summary>
    public static string ToSnakeCase(string inputText)
    {
        if (string.IsNullOrEmpty(inputText)) return inputText;

        var wordSegments = SplitIntoWords(inputText);
        return string.Join("_", wordSegments.Select(w => w.ToLowerInvariant()));
    }

    /// <summary>Convert to UPPER_CASE: "HelloWorld" → "HELLO_WORLD"</summary>
    public static string ToUpperCase(string inputText)
    {
        if (string.IsNullOrEmpty(inputText)) return inputText;

        var wordSegments = SplitIntoWords(inputText);
        return string.Join("_", wordSegments.Select(w => w.ToUpperInvariant()));
    }

    /// <summary>Convert to lower_case: "HelloWorld" → "hello_world"</summary>
    public static string ToLowerCase(string inputText)
    {
        return ToSnakeCase(inputText);
    }

    /// <summary>
    /// Convert dotted package name to PascalCase dotted: "acme.widgets" → "Acme.Widgets"
    /// </summary>
    public static string ToPascalDotted(string dottedPackageName)
    {
        if (string.IsNullOrEmpty(dottedPackageName)) return dottedPackageName;

        var dottedSegments = dottedPackageName.Split('.');
        return string.Join(".", dottedSegments.Select(ToPascalCase));
    }

    /// <summary>
    /// Split a string into word segments, handling:
    /// - snake_case (split on _)
    /// - camelCase/PascalCase (split on uppercase boundaries)
    /// - kebab-case (split on -)
    /// - dot.separated (split on .)
    /// </summary>
    private static List<string> SplitIntoWords(string inputText)
    {
        var detectedWords = new List<string>();
        int wordStartIndex = 0;

        for (int charIndex = 0; charIndex < inputText.Length; charIndex++)
        {
            char currentChar = inputText[charIndex];

            // Split on separators
            if (currentChar == '_' || currentChar == '-' || currentChar == '.')
            {
                if (charIndex > wordStartIndex)
                {
                    detectedWords.Add(inputText[wordStartIndex..charIndex]);
                }
                wordStartIndex = charIndex + 1;
                continue;
            }

            // Split on uppercase boundary (camelCase/PascalCase)
            if (char.IsUpper(currentChar) && charIndex > wordStartIndex)
            {
                // Check if this is a transition from lowercase to uppercase
                if (char.IsLower(inputText[charIndex - 1]))
                {
                    detectedWords.Add(inputText[wordStartIndex..charIndex]);
                    wordStartIndex = charIndex;
                }
                // Check if this is the start of a new word after an acronym (e.g., "XMLParser" → "XML", "Parser")
                else if (charIndex + 1 < inputText.Length && char.IsLower(inputText[charIndex + 1]) &&
                         charIndex > wordStartIndex + 1)
                {
                    detectedWords.Add(inputText[wordStartIndex..charIndex]);
                    wordStartIndex = charIndex;
                }
            }
        }

        // Add the last word
        if (wordStartIndex < inputText.Length)
        {
            detectedWords.Add(inputText[wordStartIndex..]);
        }

        return detectedWords;
    }

    private static string CapitalizeFirstLetter(string wordText)
    {
        if (string.IsNullOrEmpty(wordText)) return wordText;
        return char.ToUpperInvariant(wordText[0]) + wordText[1..].ToLowerInvariant();
    }
}