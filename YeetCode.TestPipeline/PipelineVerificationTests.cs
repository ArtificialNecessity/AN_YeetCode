using Xunit;

namespace YeetCode.TestPipeline;

/// <summary>
/// Verifies that the MSBuild targets and CLI Exec targets produced output files
/// during the build. These tests run AFTER the build targets have executed.
/// </summary>
public class PipelineVerificationTests
{
    private static readonly string ProjectDirectory = FindProjectDirectory();
    private static readonly string GeneratedDirectory = Path.Combine(ProjectDirectory, "generated");

    [Fact]
    public void MsBuildHalfYeet_ProducesOutputFile()
    {
        string outputFilePath = Path.Combine(GeneratedDirectory, "msbuild-half", "greeting.out");
        Assert.True(File.Exists(outputFilePath),
            $"MSBuild half-yeet output not found at: {outputFilePath}. " +
            "Ensure the RunHalfYeetViaMSBuildTask target ran during build.");

        string outputContent = File.ReadAllText(outputFilePath);
        Assert.Contains("Hello", outputContent);
        Assert.Contains("World", outputContent);
        Assert.Contains("Alpha", outputContent);
    }

    [Fact]
    public void MsBuildFullYeet_ProducesOutputFile()
    {
        string outputFilePath = Path.Combine(GeneratedDirectory, "msbuild-full", "simple.out");
        Assert.True(File.Exists(outputFilePath),
            $"MSBuild full-yeet output not found at: {outputFilePath}. " +
            "Ensure the RunFullYeetViaMSBuildTask target ran during build.");

        string outputContent = File.ReadAllText(outputFilePath);
        Assert.Contains("Widget", outputContent);
        Assert.Contains("name", outputContent);
    }

    [Fact]
    public void ExecHalfYeet_ProducesOutputFile()
    {
        string outputFilePath = Path.Combine(GeneratedDirectory, "exec-half", "greeting.out");
        Assert.True(File.Exists(outputFilePath),
            $"Exec half-yeet output not found at: {outputFilePath}. " +
            "Ensure the RunHalfYeetViaExec target ran during build.");

        string outputContent = File.ReadAllText(outputFilePath);
        Assert.Contains("Hello", outputContent);
        Assert.Contains("World", outputContent);
    }

    [Fact]
    public void ExecFullYeet_ProducesOutputFile()
    {
        string outputFilePath = Path.Combine(GeneratedDirectory, "exec-full", "simple.out");
        Assert.True(File.Exists(outputFilePath),
            $"Exec full-yeet output not found at: {outputFilePath}. " +
            "Ensure the RunFullYeetViaExec target ran during build.");

        string outputContent = File.ReadAllText(outputFilePath);
        Assert.Contains("Widget", outputContent);
    }

    [Fact]
    public void MsBuildAndExecHalfYeet_ProduceIdenticalOutput()
    {
        string msbuildOutputPath = Path.Combine(GeneratedDirectory, "msbuild-half", "greeting.out");
        string execOutputPath = Path.Combine(GeneratedDirectory, "exec-half", "greeting.out");

        if (!File.Exists(msbuildOutputPath) || !File.Exists(execOutputPath)) {
            Assert.Fail("Both MSBuild and Exec half-yeet outputs must exist to compare them.");
        }

        string msbuildOutput = File.ReadAllText(msbuildOutputPath);
        string execOutput = File.ReadAllText(execOutputPath);

        Assert.Equal(msbuildOutput, execOutput);
    }

    [Fact]
    public void MsBuildAndExecFullYeet_ProduceIdenticalOutput()
    {
        string msbuildOutputPath = Path.Combine(GeneratedDirectory, "msbuild-full", "simple.out");
        string execOutputPath = Path.Combine(GeneratedDirectory, "exec-full", "simple.out");

        if (!File.Exists(msbuildOutputPath) || !File.Exists(execOutputPath)) {
            Assert.Fail("Both MSBuild and Exec full-yeet outputs must exist to compare them.");
        }

        string msbuildOutput = File.ReadAllText(msbuildOutputPath);
        string execOutput = File.ReadAllText(execOutputPath);

        Assert.Equal(msbuildOutput, execOutput);
    }

    /// <summary>
    /// Walk up from the test assembly location to find the project directory
    /// (the one containing YeetCode.TestPipeline.csproj).
    /// </summary>
    private static string FindProjectDirectory()
    {
        string? currentSearchDirectory = AppContext.BaseDirectory;
        while (currentSearchDirectory != null) {
            if (File.Exists(Path.Combine(currentSearchDirectory, "YeetCode.TestPipeline.csproj"))) {
                return currentSearchDirectory;
            }
            // Check for TestData directory as alternative marker
            if (Directory.Exists(Path.Combine(currentSearchDirectory, "TestData"))) {
                return currentSearchDirectory;
            }
            currentSearchDirectory = Path.GetDirectoryName(currentSearchDirectory);
        }
        // Fallback: assume we're in bin/Debug/net10.0 — go up 3 levels
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    }
}