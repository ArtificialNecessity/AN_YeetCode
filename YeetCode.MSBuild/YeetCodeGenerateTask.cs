using Microsoft.Build.Framework;
using YeetCode.Pipeline;

namespace YeetCode.MSBuild;

using Task = Microsoft.Build.Utilities.Task;

/// <summary>
/// MSBuild task for the full YeetCode pipeline:
/// grammar + input → parse → validate → template → output
///
/// Usage in .targets:
///   <YeetCodeGenerate
///     SchemaFile="proto.schema.ytson"
///     GrammarFile="proto.grammar.yeet"
///     InputFile="widgets.proto"
///     TemplateFile="proto-csharp.yt"
///     OutputDirectory="generated/" />
/// </summary>
public class YeetCodeGenerateTask : Task
{
    [Required]
    public string SchemaFile { get; set; } = "";

    [Required]
    public string GrammarFile { get; set; } = "";

    [Required]
    public string InputFile { get; set; } = "";

    [Required]
    public string TemplateFile { get; set; } = "";

    public string? FunctionsFile { get; set; }

    public string? OutputFile { get; set; }

    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Semicolon-separated list of name=value grammar defines.
    /// Example: "syntax=proto3;mode=strict"
    /// </summary>
    public string? Defines { get; set; }

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.Normal,
            "YeetCode Generate: {0} + {1} → {2}", InputFile, GrammarFile, TemplateFile);

        var grammarDefines = ParseDefines(Defines);

        var generateOptions = new GenerateOptions
        {
            SchemaFilePath = SchemaFile,
            GrammarFilePath = GrammarFile,
            InputFilePath = InputFile,
            TemplateFilePath = TemplateFile,
            FunctionsFilePath = string.IsNullOrEmpty(FunctionsFile) ? null : FunctionsFile,
            SingleOutputFilePath = string.IsNullOrEmpty(OutputFile) ? null : OutputFile,
            OutputDirectoryPath = string.IsNullOrEmpty(OutputDirectory) ? null : OutputDirectory,
            GrammarDefines = grammarDefines.Count > 0 ? grammarDefines : null
        };

        var pipelineResult = YeetCodePipeline.Generate(generateOptions);

        if (!pipelineResult.Succeeded) {
            Log.LogError("YeetCode Generate failed: {0}", pipelineResult.ErrorMessage ?? "unknown error");
            return false;
        }

        if (pipelineResult.GeneratedFiles != null) {
            foreach (var (fileName, _) in pipelineResult.GeneratedFiles) {
                Log.LogMessage(MessageImportance.Normal, "  → {0}", fileName);
            }
        }

        return true;
    }

    private static Dictionary<string, string> ParseDefines(string? definesText)
    {
        var grammarDefines = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(definesText)) return grammarDefines;

        foreach (string defineEntry in definesText.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            int equalsIndex = defineEntry.IndexOf('=');
            if (equalsIndex > 0) {
                string defineName = defineEntry[..equalsIndex].Trim();
                string defineValue = defineEntry[(equalsIndex + 1)..].Trim();
                grammarDefines[defineName] = defineValue;
            } else {
                grammarDefines[defineEntry.Trim()] = "true";
            }
        }

        return grammarDefines;
    }
}