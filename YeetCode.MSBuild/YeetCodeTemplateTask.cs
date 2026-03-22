using Microsoft.Build.Framework;
using YeetCode.Pipeline;

namespace YeetCode.MSBuild;

using Task = Microsoft.Build.Utilities.Task;

/// <summary>
/// MSBuild task for the "half yeet" pipeline:
/// HJSON data → template → output (no grammar parsing)
///
/// Usage in .targets:
///   <YeetCodeTemplate
///     DataFile="data.hjson"
///     TemplateFile="template.yt"
///     OutputDirectory="generated/" />
/// </summary>
public class YeetCodeTemplateTask : Task
{
    [Required]
    public string DataFile { get; set; } = "";

    [Required]
    public string TemplateFile { get; set; } = "";

    public string? SchemaFile { get; set; }

    public string? FunctionsFile { get; set; }

    public string? OutputFile { get; set; }

    public string? OutputDirectory { get; set; }

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.Normal,
            "YeetCode Template: {0} → {1}", DataFile, TemplateFile);

        var templateOptions = new TemplateOptions
        {
            SchemaFilePath = string.IsNullOrEmpty(SchemaFile) ? null : SchemaFile,
            DataFilePath = DataFile,
            TemplateFilePath = TemplateFile,
            FunctionsFilePath = string.IsNullOrEmpty(FunctionsFile) ? null : FunctionsFile,
            SingleOutputFilePath = string.IsNullOrEmpty(OutputFile) ? null : OutputFile,
            OutputDirectoryPath = string.IsNullOrEmpty(OutputDirectory) ? null : OutputDirectory
        };

        var pipelineResult = YeetCodePipeline.RunTemplateCommand(templateOptions);

        if (!pipelineResult.Succeeded) {
            Log.LogError("YeetCode Template failed: {0}", pipelineResult.ErrorMessage ?? "unknown error");
            return false;
        }

        if (pipelineResult.GeneratedFiles != null) {
            foreach (var (fileName, _) in pipelineResult.GeneratedFiles) {
                Log.LogMessage(MessageImportance.Normal, "  → {0}", fileName);
            }
        }

        return true;
    }
}