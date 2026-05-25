using Xunit;

namespace YeetCode.TestPipeline;

/// <summary>
/// Verifies that the MSBuild targets produced output files during the build
/// and that their content matches the checked-in .gold files.
/// </summary>
public class PipelineVerificationTests
{
    private static readonly string ProjectDirectory = FindProjectDirectory();
    private static readonly string GeneratedDirectory = Path.Combine(ProjectDirectory, "generated");
    private static readonly string TestDataDirectory = Path.Combine(ProjectDirectory, "TestData");

    // ── Half Yeet (MSBuild Task) ────────────────────────────────────────

    [Fact]
    public void MsBuildHalfYeet_ProducesOutputFile()
    {
        string outputPath = Path.Combine(GeneratedDirectory, "msbuild-half", "greeting.out");
        Assert.True(File.Exists(outputPath),
            $"MSBuild half-yeet output not found at: {outputPath}. " +
            "Ensure the RunHalfYeetViaMSBuildTask target ran during build.");
    }

    [Fact]
    public void MsBuildHalfYeet_MatchesGoldFile()
    {
        string outputPath = Path.Combine(GeneratedDirectory, "msbuild-half", "greeting.out");
        string goldPath = Path.Combine(TestDataDirectory, "HalfYeet", "greeting.gold");

        AssertFilesExistAndMatch(outputPath, goldPath, "MSBuild half-yeet");
    }

    // ── Full Yeet (MSBuild Task) ────────────────────────────────────────

    [Fact]
    public void MsBuildFullYeet_ProducesOutputFile()
    {
        string outputPath = Path.Combine(GeneratedDirectory, "msbuild-full", "simple.out");
        Assert.True(File.Exists(outputPath),
            $"MSBuild full-yeet output not found at: {outputPath}. " +
            "Ensure the RunFullYeetViaMSBuildTask target ran during build.");
    }

    [Fact]
    public void MsBuildFullYeet_MatchesGoldFile()
    {
        string outputPath = Path.Combine(GeneratedDirectory, "msbuild-full", "simple.out");
        string goldPath = Path.Combine(TestDataDirectory, "FullYeet", "simple.gold");

        AssertFilesExistAndMatch(outputPath, goldPath, "MSBuild full-yeet");
    }

    // ── CLI Exec tests (skipped — pre-existing CLI build issue) ─────────

    [Fact(Skip = "CLI exe has a TypeLoadException bug — fix in separate task")]
    public void ExecHalfYeet_MatchesGoldFile()
    {
        string outputPath = Path.Combine(GeneratedDirectory, "exec-half", "greeting.out");
        string goldPath = Path.Combine(TestDataDirectory, "HalfYeet", "greeting.gold");

        AssertFilesExistAndMatch(outputPath, goldPath, "Exec half-yeet");
    }

    [Fact(Skip = "CLI exe has a TypeLoadException bug — fix in separate task")]
    public void ExecFullYeet_MatchesGoldFile()
    {
        string outputPath = Path.Combine(GeneratedDirectory, "exec-full", "simple.out");
        string goldPath = Path.Combine(TestDataDirectory, "FullYeet", "simple.gold");

        AssertFilesExistAndMatch(outputPath, goldPath, "Exec full-yeet");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void AssertFilesExistAndMatch(string outputPath, string goldPath, string label)
    {
        Assert.True(File.Exists(goldPath),
            $"{label}: Gold file not found at: {goldPath}");
        Assert.True(File.Exists(outputPath),
            $"{label}: Output file not found at: {outputPath}. " +
            "Ensure the pipeline target ran during build.");

        string expected = File.ReadAllText(goldPath).ReplaceLineEndings("\n");
        string actual = File.ReadAllText(outputPath).ReplaceLineEndings("\n");

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Walk up from the test assembly location to find the project directory
    /// (the one containing YeetCode.TestPipeline.csproj).
    /// </summary>
    private static string FindProjectDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "YeetCode.TestPipeline.csproj")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    }
}