using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public sealed class McpClientCapabilityState
{
    public static McpClientCapabilityState Empty { get; } = new McpClientCapabilityState();

    public bool SupportsRoots { get; init; }

    public bool RootsListChanged { get; init; }

    public bool SupportsSampling { get; init; }

    public bool SamplingSupportsTools { get; init; }

    public bool SamplingSupportsContext { get; init; }

    public bool SupportsElicitation { get; init; }

    public bool SupportsTasks { get; init; }

    public bool TasksSupportsList { get; init; }

    public bool TasksSupportsCancel { get; init; }

    public static bool TryRead(JsonElement capabilities, out McpClientCapabilityState state, out string error)
    {
        state = Empty;
        error = string.Empty;

        if (capabilities is not { ValueKind: JsonValueKind.Object })
        {
            error = "Client capabilities must be a JSON object.";
            return false;
        }

        var supportsRoots = false;
        var rootsListChanged = false;
        if (capabilities.TryGetProperty("roots"u8, out var rootsElement))
        {
            if (rootsElement is not { ValueKind: JsonValueKind.Object })
            {
                error = "Client roots capability must be a JSON object.";
                return false;
            }

            supportsRoots = true;
            rootsListChanged = ReadOptionalBoolean(rootsElement, "listChanged"u8, "Client roots.listChanged", out error) is { IsValid: true, Value: true };
            if (error.Length != 0)
            {
                return false;
            }
        }

        var supportsSampling = false;
        var samplingSupportsTools = false;
        var samplingSupportsContext = false;
        if (capabilities.TryGetProperty("sampling"u8, out var samplingElement))
        {
            if (samplingElement is not { ValueKind: JsonValueKind.Object })
            {
                error = "Client sampling capability must be a JSON object.";
                return false;
            }

            supportsSampling = true;
            samplingSupportsTools = HasObjectProperty(samplingElement, "tools"u8, "Client sampling.tools", out error);
            if (error.Length != 0)
            {
                return false;
            }

            samplingSupportsContext = HasObjectProperty(samplingElement, "context"u8, "Client sampling.context", out error);
            if (error.Length != 0)
            {
                return false;
            }
        }

        var supportsElicitation = false;
        if (capabilities.TryGetProperty("elicitation"u8, out var elicitationElement))
        {
            if (elicitationElement is not { ValueKind: JsonValueKind.Object })
            {
                error = "Client elicitation capability must be a JSON object.";
                return false;
            }

            supportsElicitation = true;
        }

        var supportsTasks = false;
        var tasksSupportsList = false;
        var tasksSupportsCancel = false;
        if (capabilities.TryGetProperty("tasks"u8, out var tasksElement))
        {
            if (tasksElement is not { ValueKind: JsonValueKind.Object })
            {
                error = "Client tasks capability must be a JSON object.";
                return false;
            }

            supportsTasks = true;
            tasksSupportsList = HasObjectProperty(tasksElement, "list"u8, "Client tasks.list", out error);
            if (error.Length != 0)
            {
                return false;
            }

            tasksSupportsCancel = HasObjectProperty(tasksElement, "cancel"u8, "Client tasks.cancel", out error);
            if (error.Length != 0)
            {
                return false;
            }
        }

        state = new McpClientCapabilityState
        {
            SupportsRoots = supportsRoots,
            RootsListChanged = rootsListChanged,
            SupportsSampling = supportsSampling,
            SamplingSupportsTools = samplingSupportsTools,
            SamplingSupportsContext = samplingSupportsContext,
            SupportsElicitation = supportsElicitation,
            SupportsTasks = supportsTasks,
            TasksSupportsList = tasksSupportsList,
            TasksSupportsCancel = tasksSupportsCancel
        };

        return true;
    }

    private static OptionalBoolean ReadOptionalBoolean(JsonElement parent, ReadOnlySpan<byte> propertyName, string displayName, out string error)
    {
        error = string.Empty;

        if (!parent.TryGetProperty(propertyName, out var element))
        {
            return new OptionalBoolean(isValid: true, value: false);
        }

        if (element is not { ValueKind: JsonValueKind.True or JsonValueKind.False })
        {
            error = $"{displayName} must be a boolean when supplied.";
            return new OptionalBoolean(isValid: false, value: false);
        }

        return new OptionalBoolean(isValid: true, value: element.GetBoolean());
    }

    private static bool HasObjectProperty(JsonElement parent, ReadOnlySpan<byte> propertyName, string displayName, out string error)
    {
        error = string.Empty;

        if (!parent.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element is not { ValueKind: JsonValueKind.Object })
        {
            error = $"{displayName} must be a JSON object when supplied.";
            return false;
        }

        return true;
    }

    private readonly struct OptionalBoolean
    {
        public OptionalBoolean(bool isValid, bool value)
        {
            IsValid = isValid;
            Value = value;
        }

        public bool IsValid { get; }

        public bool Value { get; }
    }
}
