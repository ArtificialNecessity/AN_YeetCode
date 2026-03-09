namespace YeetCode.Template;

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YeetCode.Core;

/// <summary>
/// Evaluates a parsed template AST against JsonDocument data to produce output.
/// Supports single-file and multi-file output modes.
/// </summary>
public class TemplateEvaluator
{
    private readonly JsonElement _rootDataElement;
    private readonly JsonElement? _functionsElement;
    private readonly Dictionary<string, DefineBlockNode> _macroDefinitions = new();
    private readonly Stack<Dictionary<string, JsonElement>> _variableScopeStack = new();
    private readonly Dictionary<string, StringBuilder> _outputFileBuilders = new();
    private StringBuilder _currentOutputBuilder;
    private bool _hasOutputDirectives;

    // Each-loop context
    private int _currentEachIndex;
    private int _currentEachCount;

    public TemplateEvaluator(JsonDocument dataDocument, JsonDocument? functionsDocument = null)
    {
        _rootDataElement = dataDocument.RootElement;
        _functionsElement = functionsDocument?.RootElement;
        _currentOutputBuilder = new StringBuilder();
        _variableScopeStack.Push(new Dictionary<string, JsonElement>());
    }

    /// <summary>
    /// Evaluate a template and return the single-file output string.
    /// </summary>
    public string Evaluate(List<TemplateNode> templateNodes)
    {
        _currentOutputBuilder = new StringBuilder();
        _hasOutputDirectives = false;
        _outputFileBuilders.Clear();

        EvaluateNodes(templateNodes);

        if (_hasOutputDirectives) {
            // Multi-file mode — the main output should be empty
            return "";
        }

        return _currentOutputBuilder.ToString();
    }

    /// <summary>
    /// Evaluate a template and return multi-file output.
    /// Key = filename, Value = file content.
    /// </summary>
    public Dictionary<string, string> EvaluateMultiFile(List<TemplateNode> templateNodes)
    {
        _currentOutputBuilder = new StringBuilder();
        _hasOutputDirectives = false;
        _outputFileBuilders.Clear();

        EvaluateNodes(templateNodes);

        var outputFiles = new Dictionary<string, string>();
        foreach (var (fileName, contentBuilder) in _outputFileBuilders) {
            outputFiles[fileName] = contentBuilder.ToString();
        }
        return outputFiles;
    }

    // ── Node evaluation ──────────────────────────────────────

    private void EvaluateNodes(List<TemplateNode> nodes)
    {
        foreach (var node in nodes) {
            EvaluateNode(node);
        }
    }

    private void EvaluateNode(TemplateNode node)
    {
        switch (node)
        {
            case LiteralTextNode literalNode:
                _currentOutputBuilder.Append(literalNode.Text);
                break;

            case ValueExpressionNode valueNode:
                string evaluatedValue = EvaluateExpressionToString(valueNode.Expression);
                _currentOutputBuilder.Append(evaluatedValue);
                break;

            case EachBlockNode eachNode:
                EvaluateEachBlock(eachNode);
                break;

            case IfBlockNode ifNode:
                EvaluateIfBlock(ifNode);
                break;

            case DefineBlockNode defineNode:
                _macroDefinitions[defineNode.MacroName] = defineNode;
                break;

            case CallNode callNode:
                EvaluateCallNode(callNode);
                break;

            case OutputBlockNode outputNode:
                EvaluateOutputBlock(outputNode);
                break;
        }
    }

    // ── Each block ───────────────────────────────────────────

    private void EvaluateEachBlock(EachBlockNode eachNode)
    {
        var collectionElement = EvaluateExpressionToElement(eachNode.CollectionExpression);
        if (collectionElement == null) return;

        var collectionValue = collectionElement.Value;

        if (collectionValue.ValueKind == JsonValueKind.Array) {
            EvaluateEachArray(eachNode, collectionValue);
        } else if (collectionValue.ValueKind == JsonValueKind.Object) {
            EvaluateEachObject(eachNode, collectionValue);
        }
    }

    private void EvaluateEachArray(EachBlockNode eachNode, JsonElement arrayElement)
    {
        int savedEachIndex = _currentEachIndex;
        int savedEachCount = _currentEachCount;

        int arrayLength = arrayElement.GetArrayLength();
        _currentEachCount = arrayLength;
        int itemIndex = 0;

        foreach (var arrayItem in arrayElement.EnumerateArray())
        {
            _currentEachIndex = itemIndex;

            // Emit separator between items (not before first)
            if (itemIndex > 0 && eachNode.SeparatorText != null) {
                _currentOutputBuilder.Append(eachNode.SeparatorText);
            }

            PushScope();
            SetVariable(eachNode.ItemVariableName, arrayItem);
            EvaluateNodes(eachNode.BodyNodes);
            PopScope();

            itemIndex++;
        }

        _currentEachIndex = savedEachIndex;
        _currentEachCount = savedEachCount;
    }

    private void EvaluateEachObject(EachBlockNode eachNode, JsonElement objectElement)
    {
        int savedEachIndex = _currentEachIndex;
        int savedEachCount = _currentEachCount;

        // Count properties (skip __keyAttributes)
        int propertyCount = 0;
        foreach (var prop in objectElement.EnumerateObject()) {
            if (prop.Name != "__keyAttributes") propertyCount++;
        }
        _currentEachCount = propertyCount;
        int itemIndex = 0;

        foreach (var objectProperty in objectElement.EnumerateObject())
        {
            if (objectProperty.Name == "__keyAttributes") continue;

            _currentEachIndex = itemIndex;

            if (itemIndex > 0 && eachNode.SeparatorText != null) {
                _currentOutputBuilder.Append(eachNode.SeparatorText);
            }

            PushScope();

            if (eachNode.ValueVariableName != null) {
                // Map iteration: each map as key, value
                SetVariable(eachNode.ItemVariableName, CreateStringElement(objectProperty.Name));
                SetVariable(eachNode.ValueVariableName, objectProperty.Value);
            } else {
                // Object iteration with single variable — bind to value
                SetVariable(eachNode.ItemVariableName, objectProperty.Value);
            }

            EvaluateNodes(eachNode.BodyNodes);
            PopScope();

            itemIndex++;
        }

        _currentEachIndex = savedEachIndex;
        _currentEachCount = savedEachCount;
    }

    // ── If block ─────────────────────────────────────────────

    private void EvaluateIfBlock(IfBlockNode ifNode)
    {
        foreach (var branch in ifNode.Branches) {
            if (EvaluateCondition(branch.Condition)) {
                EvaluateNodes(branch.BodyNodes);
                return;
            }
        }

        if (ifNode.ElseBodyNodes != null) {
            EvaluateNodes(ifNode.ElseBodyNodes);
        }
    }

    private bool EvaluateCondition(TemplateExpression condition)
    {
        if (condition is ComparisonExpression comparisonExpr) {
            string leftValue = EvaluateExpressionToString(comparisonExpr.LeftExpression);
            string rightValue = EvaluateExpressionToString(comparisonExpr.RightExpression);

            return comparisonExpr.Operator switch
            {
                "==" => leftValue == rightValue,
                "!=" => leftValue != rightValue,
                _ => false
            };
        }

        // Truthiness check — non-null, non-empty, non-false
        var element = EvaluateExpressionToElement(condition);
        return IsTruthy(element);
    }

    // ── Call node ────────────────────────────────────────────

    private void EvaluateCallNode(CallNode callNode)
    {
        if (!_macroDefinitions.TryGetValue(callNode.MacroName, out var macroDefinition)) {
            throw new InvalidOperationException(
                $"Template macro '{callNode.MacroName}' not defined at line {callNode.SourceLine}. " +
                "Define it with: define name(args) ... /define"
            );
        }

        if (callNode.ArgumentExpressions.Count != macroDefinition.ParameterNames.Count) {
            throw new InvalidOperationException(
                $"Macro '{callNode.MacroName}' expects {macroDefinition.ParameterNames.Count} arguments, " +
                $"got {callNode.ArgumentExpressions.Count} at line {callNode.SourceLine}"
            );
        }

        PushScope();
        for (int argIndex = 0; argIndex < callNode.ArgumentExpressions.Count; argIndex++) {
            var argElement = EvaluateExpressionToElement(callNode.ArgumentExpressions[argIndex]);
            if (argElement != null) {
                SetVariable(macroDefinition.ParameterNames[argIndex], argElement.Value);
            }
        }
        EvaluateNodes(macroDefinition.BodyNodes);
        PopScope();
    }

    // ── Output block ─────────────────────────────────────────

    private void EvaluateOutputBlock(OutputBlockNode outputNode)
    {
        _hasOutputDirectives = true;
        string fileName = EvaluateExpressionToString(outputNode.FileNameExpression);

        var savedOutputBuilder = _currentOutputBuilder;
        var fileOutputBuilder = new StringBuilder();
        _currentOutputBuilder = fileOutputBuilder;

        EvaluateNodes(outputNode.BodyNodes);

        _currentOutputBuilder = savedOutputBuilder;
        _outputFileBuilders[fileName] = fileOutputBuilder;
    }

    // ── Expression evaluation ────────────────────────────────

    private string EvaluateExpressionToString(TemplateExpression expression)
    {
        switch (expression)
        {
            case StringLiteralExpression stringLiteral:
                return stringLiteral.Value;

            case TypeRefExpression typeRef:
                return "@" + typeRef.TypeName;

            case ConcatExpression concatExpr:
                return EvaluateExpressionToString(concatExpr.LeftExpression) +
                       EvaluateExpressionToString(concatExpr.RightExpression);

            case FunctionCallExpression funcExpr:
                return EvaluateFunction(funcExpr);

            default:
                var element = EvaluateExpressionToElement(expression);
                if (element == null) return "";
                return ElementToString(element.Value);
        }
    }

    private JsonElement? EvaluateExpressionToElement(TemplateExpression expression)
    {
        switch (expression)
        {
            case PathExpression pathExpr:
                return ResolvePath(pathExpr.PathSegments);

            case BracketAccessExpression bracketExpr:
                return EvaluateBracketAccess(bracketExpr);

            case FunctionCallExpression funcExpr:
                string funcResult = EvaluateFunction(funcExpr);
                return CreateStringElement(funcResult);

            case StringLiteralExpression stringLiteral:
                return CreateStringElement(stringLiteral.Value);

            case TypeRefExpression typeRef:
                return CreateStringElement("@" + typeRef.TypeName);

            case ConcatExpression concatExpr:
                string concatResult = EvaluateExpressionToString(concatExpr.LeftExpression) +
                                      EvaluateExpressionToString(concatExpr.RightExpression);
                return CreateStringElement(concatResult);

            default:
                return null;
        }
    }

    // ── Path resolution ──────────────────────────────────────

    private JsonElement? ResolvePath(List<string> pathSegments)
    {
        if (pathSegments.Count == 0) return null;

        string firstSegment = pathSegments[0];

        // Check special variables
        if (firstSegment == "index" && pathSegments.Count == 1) {
            return CreateNumberElement(_currentEachIndex);
        }
        if (firstSegment == "first" && pathSegments.Count == 1) {
            return CreateBoolElement(_currentEachIndex == 0);
        }
        if (firstSegment == "last" && pathSegments.Count == 1) {
            return CreateBoolElement(_currentEachIndex == _currentEachCount - 1);
        }

        // Check variable scopes (innermost first)
        JsonElement? currentElement = LookupVariable(firstSegment);

        // Check functions document
        if (currentElement == null && _functionsElement != null) {
            if (_functionsElement.Value.ValueKind == JsonValueKind.Object &&
                _functionsElement.Value.TryGetProperty(firstSegment, out var funcElement)) {
                currentElement = funcElement;
            }
        }

        // Check root data
        if (currentElement == null) {
            if (_rootDataElement.ValueKind == JsonValueKind.Object &&
                _rootDataElement.TryGetProperty(firstSegment, out var rootProperty)) {
                currentElement = rootProperty;
            }
        }

        if (currentElement == null) return null;

        // Navigate remaining path segments
        for (int segmentIndex = 1; segmentIndex < pathSegments.Count; segmentIndex++) {
            string segment = pathSegments[segmentIndex];

            if (segment == "length") {
                if (currentElement.Value.ValueKind == JsonValueKind.Array) {
                    return CreateNumberElement(currentElement.Value.GetArrayLength());
                }
                if (currentElement.Value.ValueKind == JsonValueKind.Object) {
                    int propertyCount = 0;
                    foreach (var _ in currentElement.Value.EnumerateObject()) propertyCount++;
                    return CreateNumberElement(propertyCount);
                }
                return CreateNumberElement(0);
            }

            if (currentElement.Value.ValueKind == JsonValueKind.Object &&
                currentElement.Value.TryGetProperty(segment, out var nextElement)) {
                currentElement = nextElement;
            } else {
                return null; // Path doesn't exist
            }
        }

        return currentElement;
    }

    private JsonElement? EvaluateBracketAccess(BracketAccessExpression bracketExpr)
    {
        var baseElement = EvaluateExpressionToElement(bracketExpr.BaseExpression);
        if (baseElement == null) return null;

        string indexKey = EvaluateExpressionToString(bracketExpr.IndexExpression);

        if (baseElement.Value.ValueKind == JsonValueKind.Object &&
            baseElement.Value.TryGetProperty(indexKey, out var resultElement)) {
            return resultElement;
        }

        return null; // Key not found
    }

    // ── Built-in functions ───────────────────────────────────

    private string EvaluateFunction(FunctionCallExpression funcExpr)
    {
        if (funcExpr.ArgumentExpressions.Count == 0) {
            throw new InvalidOperationException(
                $"Function '{funcExpr.FunctionName}' requires at least one argument"
            );
        }

        string argumentValue = EvaluateExpressionToString(funcExpr.ArgumentExpressions[0]);

        return funcExpr.FunctionName switch
        {
            "pascal" => StringCaseConverter.ToPascalCase(argumentValue),
            "camel" => StringCaseConverter.ToCamelCase(argumentValue),
            "snake" => StringCaseConverter.ToSnakeCase(argumentValue),
            "upper" => StringCaseConverter.ToUpperCase(argumentValue),
            "lower" => StringCaseConverter.ToLowerCase(argumentValue),
            "pascal_dotted" => StringCaseConverter.ToPascalDotted(argumentValue),
            "length" => EvaluateLengthFunction(funcExpr.ArgumentExpressions[0]),
            _ => throw new InvalidOperationException(
                $"Unknown built-in function '{funcExpr.FunctionName}'. " +
                "Available: pascal, camel, snake, upper, lower, pascal_dotted, length"
            )
        };
    }

    private string EvaluateLengthFunction(TemplateExpression argumentExpression)
    {
        var element = EvaluateExpressionToElement(argumentExpression);
        if (element == null) return "0";

        return element.Value.ValueKind switch
        {
            JsonValueKind.Array => element.Value.GetArrayLength().ToString(),
            JsonValueKind.Object => CountObjectProperties(element.Value).ToString(),
            JsonValueKind.String => element.Value.GetString()!.Length.ToString(),
            _ => "0"
        };
    }

    // ── Variable scope management ────────────────────────────

    private void PushScope()
    {
        _variableScopeStack.Push(new Dictionary<string, JsonElement>());
    }

    private void PopScope()
    {
        _variableScopeStack.Pop();
    }

    private void SetVariable(string variableName, JsonElement variableValue)
    {
        _variableScopeStack.Peek()[variableName] = variableValue;
    }

    private JsonElement? LookupVariable(string variableName)
    {
        foreach (var scope in _variableScopeStack) {
            if (scope.TryGetValue(variableName, out var variableValue)) {
                return variableValue;
            }
        }
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────

    private static bool IsTruthy(JsonElement? element)
    {
        if (element == null) return false;
        return element.Value.ValueKind switch
        {
            JsonValueKind.Null => false,
            JsonValueKind.Undefined => false,
            JsonValueKind.False => false,
            JsonValueKind.String => element.Value.GetString()!.Length > 0,
            JsonValueKind.Number => true,
            JsonValueKind.True => true,
            JsonValueKind.Object => true,
            JsonValueKind.Array => element.Value.GetArrayLength() > 0,
            _ => false
        };
    }

    private static string ElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => element.GetRawText()
        };
    }

    private static int CountObjectProperties(JsonElement objectElement)
    {
        int propertyCount = 0;
        foreach (var _ in objectElement.EnumerateObject()) propertyCount++;
        return propertyCount;
    }

    private static JsonElement CreateStringElement(string value)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, YeetCodeJsonContext.Default.String);
        return JsonDocument.Parse(jsonBytes).RootElement;
    }

    private static JsonElement CreateNumberElement(int value)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, YeetCodeJsonContext.Default.Int32);
        return JsonDocument.Parse(jsonBytes).RootElement;
    }

    private static JsonElement CreateBoolElement(bool value)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, YeetCodeJsonContext.Default.Boolean);
        return JsonDocument.Parse(jsonBytes).RootElement;
    }
}