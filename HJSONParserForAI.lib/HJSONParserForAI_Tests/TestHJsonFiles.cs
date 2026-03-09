#!/usr/bin/env dotnet run
// HJSON Parser Gold File Test Runner
// Run from project root: dotnet run --file utils/HJSONParserForAI/Tests/TestHJsonFiles.cs
// Generate gold files: dotnet run --file utils/HJSONParserForAI/Tests/TestHJsonFiles.cs -- --gold

#:project ../HJSONParserForAI/HJSONParserForAI.csproj

using System;
using System.IO;
using System.Linq;
using HJSONParserForAI.Core;

bool generateGold = args.Contains("--gold");
// For file-based programs, use CallerFilePath to get the script's directory
string scriptDirectory = Path.GetDirectoryName(GetScriptPath())!;
string testDataDirectory = Path.Combine(scriptDirectory, "TestData");

static string GetScriptPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

if (generateGold)
{
    GenerateGoldFiles();
}
else
{
    Environment.Exit(RunTests());
}

void GenerateGoldFiles()
{
    Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                    Generating HJSON Parser Gold Files                       ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝\n");

    var testFiles = Directory.GetFiles(testDataDirectory, "*.hjson");
    if (testFiles.Length == 0)
    {
        Console.WriteLine($"ERROR: No test files found in {testDataDirectory}");
        Environment.Exit(1);
    }

    int generatedCount = 0;

    foreach (var testFilePath in testFiles)
    {
        string goldFilePath = testFilePath + ".gold";

        try
        {
            string sourceText = File.ReadAllText(testFilePath);
            string goldOutput = ParseAndFormatOutput(sourceText);
            File.WriteAllText(goldFilePath, goldOutput);

            generatedCount++;
            Console.WriteLine($"✅ Generated: {Path.GetFileName(goldFilePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERROR generating {Path.GetFileName(goldFilePath)}: {ex.Message}");
        }
    }

    Console.WriteLine($"\n{'═'.ToString().PadRight(80, '═')}");
    Console.WriteLine($"Generated {generatedCount} gold file(s)");
}

int RunTests()
{
    Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                    HJSON Parser Gold File Tests                             ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝\n");

    var testFiles = Directory.GetFiles(testDataDirectory, "*.hjson");
    if (testFiles.Length == 0)
    {
        Console.WriteLine($"ERROR: No test files found in {testDataDirectory}");
        return 1;
    }

    int totalTests = 0;
    int passedTests = 0;
    int failedTests = 0;

    foreach (var testFilePath in testFiles)
    {
        string goldFilePath = testFilePath + ".gold";

        if (!File.Exists(goldFilePath))
        {
            Console.WriteLine($"⚠️  SKIP: {Path.GetFileName(testFilePath)} (no gold file)");
            continue;
        }

        totalTests++;

        string testFileName = Path.GetFileName(testFilePath);
        string sourceText = File.ReadAllText(testFilePath);
        string expectedOutput = File.ReadAllText(goldFilePath);
        string actualOutput = ParseAndFormatOutput(sourceText);

        if (NormalizeOutput(actualOutput) == NormalizeOutput(expectedOutput))
        {
            Console.WriteLine($"✅ PASS: {testFileName}");
            passedTests++;
        }
        else
        {
            Console.WriteLine($"❌ FAIL: {testFileName}");
            Console.WriteLine($"\n  Expected output:");
            Console.WriteLine(IndentLines(expectedOutput, "    "));
            Console.WriteLine($"\n  Actual output:");
            Console.WriteLine(IndentLines(actualOutput, "    "));
            Console.WriteLine();
            failedTests++;
        }
    }

    Console.WriteLine($"\n{'═'.ToString().PadRight(80, '═')}");
    Console.WriteLine($"Test Results: {passedTests}/{totalTests} passed");

    if (failedTests > 0)
    {
        Console.WriteLine($"❌ {failedTests} test(s) FAILED");
        return 1;
    }
    else
    {
        Console.WriteLine($"✅ All tests PASSED");
        return 0;
    }
}

string ParseAndFormatOutput(string sourceText)
{
    var structuralAnalyzer = new StructuralAnalyzer();
    var structureResult = structuralAnalyzer.Analyze(sourceText);

    var hjsonParser = new HjsonParser();
    var parseResult = hjsonParser.Parse(sourceText, structureResult);

    var diagnosticFormatter = new DiagnosticFormatter();
    return diagnosticFormatter.FormatForAI(parseResult, sourceText, isTestMode: true);
}

string NormalizeOutput(string text)
{
    return text.Trim().Replace("\r\n", "\n");
}

string IndentLines(string text, string indent)
{
    var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    return string.Join("\n", lines.Select(line => indent + line));
}