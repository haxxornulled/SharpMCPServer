using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Json.Schema;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Infrastructure.Mcp.JsonSchema;

using CompiledJsonSchema = global::Json.Schema.JsonSchema;

public sealed class JsonSchemaNetToolArgumentValidator : IMcpToolArgumentValidator
{
    private const int MaxErrorMessages = 8;

    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    private readonly ConcurrentDictionary<string, SchemaBuildResult> _schemas = new(StringComparer.Ordinal);

    public Fin<JsonElement> Validate(JsonElement inputSchema, JsonElement? arguments)
    {
        if (inputSchema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Fin.Fail<JsonElement>(Error.New("Tool inputSchema is required."));
        }

        var payload = arguments ?? McpJsonElements.EmptyObject;
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            payload = McpJsonElements.EmptyObject;
        }

        return ValidateValueCore(inputSchema, payload, "Tool inputSchema", "Tool argument");
    }

    public Fin<JsonElement> ValidateRequiredValue(JsonElement schema, JsonElement value, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Fin.Fail<JsonElement>(Error.New($"{subject} is required."));
        }

        return ValidateValueCore(schema, value, $"{subject} schema", subject);
    }

    public Fin<JsonElement> ValidateSchema(JsonElement schema, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        if (schema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Fin.Fail<JsonElement>(Error.New($"{subject} is required."));
        }

        if (schema is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Fail<JsonElement>(Error.New($"{subject} must be a JSON object."));
        }

        if (!McpJsonSchemaDialect.TryValidateSupportedDialect(schema, out var schemaDialectError))
        {
            return Fin.Fail<JsonElement>(Error.New(schemaDialectError));
        }

        var schemaText = schema.GetRawText();
        var build = _schemas.GetOrAdd(schemaText, static text => BuildSchema(text));
        return build is { IsSuccess: true }
            ? Fin.Succ<JsonElement>(schema)
            : Fin.Fail<JsonElement>(Error.New(build.ErrorMessage ?? $"{subject} is not a valid JSON Schema."));
    }

    private Fin<JsonElement> ValidateValueCore(JsonElement schema, JsonElement value, string schemaSubject, string valueSubject)
    {
        if (schema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Fin.Fail<JsonElement>(Error.New($"{schemaSubject} is required."));
        }

        if (schema is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Fail<JsonElement>(Error.New($"{schemaSubject} must be a JSON object."));
        }

        if (!McpJsonSchemaDialect.TryValidateSupportedDialect(schema, out var schemaDialectError))
        {
            return Fin.Fail<JsonElement>(Error.New(schemaDialectError));
        }

        var schemaText = schema.GetRawText();
        var build = _schemas.GetOrAdd(schemaText, static text => BuildSchema(text));
        if (build is not { IsSuccess: true, Schema: { } compiledSchema })
        {
            return Fin.Fail<JsonElement>(Error.New(build.ErrorMessage ?? $"{schemaSubject} is not a valid JSON Schema."));
        }

        EvaluationResults results;
        try
        {
            results = compiledSchema.Evaluate(value, EvaluationOptions);
        }
        catch (JsonSchemaException ex)
        {
            return Fin.Fail<JsonElement>(Error.New($"{schemaSubject} could not be evaluated: {ex.Message}"));
        }
        catch (InvalidOperationException ex)
        {
            return Fin.Fail<JsonElement>(Error.New($"{schemaSubject} could not be evaluated: {ex.Message}"));
        }

        if (!results.IsValid)
        {
            return Fin.Fail<JsonElement>(Error.New(FormatFailure(valueSubject, results)));
        }

        return Fin.Succ<JsonElement>(value);
    }

    private static SchemaBuildResult BuildSchema(string schemaText)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaText);
            var schemaRoot = document.RootElement.Clone();
            var options = new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = new SchemaRegistry()
            };

            return SchemaBuildResult.Success(CompiledJsonSchema.Build(schemaRoot, options));
        }
        catch (JsonSchemaException ex)
        {
            return SchemaBuildResult.Fail($"JSON Schema is invalid: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return SchemaBuildResult.Fail($"JSON Schema is invalid JSON: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return SchemaBuildResult.Fail($"JSON Schema could not be built: {ex.Message}");
        }
    }

    private static string FormatFailure(string subject, EvaluationResults results)
    {
        var builder = new StringBuilder(256);
        builder.Append(subject);
        builder.Append(" does not satisfy JSON Schema");

        var appended = 0;
        AppendErrors(results, builder, ref appended);

        if (appended == 0)
        {
            builder.Append('.');
        }

        return builder.ToString();
    }

    private static void AppendErrors(EvaluationResults results, StringBuilder builder, ref int appended)
    {
        if (appended >= MaxErrorMessages)
        {
            return;
        }

        var hasErrors = results.Errors is { Count: > 0 };
        var hasDetails = results.Details is { Count: > 0 };

        if (!results.IsValid && hasErrors)
        {
            foreach (var error in results.Errors!)
            {
                if (appended >= MaxErrorMessages)
                {
                    return;
                }

                var displayLabel = GetErrorLabel(results, error.Key);

                AppendLocation(builder, appended, results.InstanceLocation.ToString(), displayLabel);
                builder.Append(error.Value);
                appended++;
            }
        }
        else if (!results.IsValid && hasDetails)
        {
            var summaryLabel = GetSummaryLabel(results);
            if (!string.IsNullOrEmpty(summaryLabel))
            {
                AppendLocation(builder, appended, results.InstanceLocation.ToString(), summaryLabel);
                appended++;
            }
        }

        if (!hasDetails)
        {
            return;
        }

        foreach (var detail in results.Details!)
        {
            AppendErrors(detail, builder, ref appended);
            if (appended >= MaxErrorMessages)
            {
                return;
            }
        }
    }

    private static string? GetErrorLabel(EvaluationResults results, string? errorKey)
    {
        if (!string.IsNullOrWhiteSpace(errorKey))
        {
            if (string.Equals(errorKey, "false schema", StringComparison.Ordinal))
            {
                return GetKeywordLabel(GetKeywordToken(results));
            }

            return GetKeywordLabel(errorKey);
        }

        return GetKeywordLabel(GetKeywordToken(results));
    }

    private static string? GetSummaryLabel(EvaluationResults results)
    {
        if (results.Details is not { Count: > 0 })
        {
            return default;
        }

        string? summaryToken = default;
        var bestParentLength = -1;

        foreach (var detail in results.Details!)
        {
            var parentPath = detail.EvaluationPath.ToString();
            foreach (var candidate in results.Details!)
            {
                if (ReferenceEquals(detail, candidate))
                {
                    continue;
                }

                var candidatePath = candidate.EvaluationPath.ToString();
                var relativeToken = GetRelativeKeywordToken(parentPath, candidatePath);
                if (string.IsNullOrEmpty(relativeToken) || parentPath.Length <= bestParentLength)
                {
                    continue;
                }

                summaryToken = relativeToken;
                bestParentLength = parentPath.Length;
            }
        }

        return GetKeywordLabel(summaryToken);
    }

    private static string? GetRelativeKeywordToken(string currentPath, string detailPath)
    {
        if (string.IsNullOrEmpty(detailPath))
        {
            return default;
        }

        if (string.IsNullOrEmpty(currentPath))
        {
            return GetKeywordTokenFromPath(detailPath.TrimStart('/'), requireNumericDescendant: true);
        }

        if (!detailPath.StartsWith(currentPath, StringComparison.Ordinal))
        {
            return default;
        }

        var relativePath = detailPath[currentPath.Length..].TrimStart('/');
        if (string.IsNullOrEmpty(relativePath))
        {
            return default;
        }

        return GetKeywordTokenFromPath(relativePath, requireNumericDescendant: true);
    }

    private static string? GetKeywordTokenFromPath(string path, bool requireNumericDescendant)
    {
        if (string.IsNullOrEmpty(path))
        {
            return default;
        }

        var span = path.AsSpan();
        var slashIndex = span.IndexOf('/');
        if (slashIndex < 0)
        {
            return default;
        }

        var segment = span[..slashIndex];
        if (segment.IsEmpty || IsNumericToken(segment))
        {
            return default;
        }

        var remainder = span[(slashIndex + 1)..].TrimStart('/');
        if (remainder.IsEmpty)
        {
            return default;
        }

        if (requireNumericDescendant)
        {
            var nextSlash = remainder.IndexOf('/');
            var nextSegment = nextSlash < 0 ? remainder : remainder[..nextSlash];
            if (!IsNumericToken(nextSegment))
            {
                return default;
            }
        }

        return segment.ToString();
    }

    private static void AppendLocation(StringBuilder builder, int appended, string? instanceLocation, string? keywordLabel)
    {
        builder.Append(appended == 0 ? ": " : "; ");
        builder.Append(NormalizeInstanceLocation(instanceLocation));
        builder.Append(' ');

        if (!string.IsNullOrEmpty(keywordLabel))
        {
            builder.Append(keywordLabel);
            builder.Append(": ");
        }
    }

    private static string? GetKeywordToken(EvaluationResults results)
    {
        var path = results.EvaluationPath.ToString();
        if (string.IsNullOrEmpty(path))
        {
            path = results.SchemaLocation.ToString();
        }

        if (string.IsNullOrEmpty(path))
        {
            return default;
        }

        var fragmentIndex = path.IndexOf('#');
        if (fragmentIndex >= 0 && fragmentIndex + 1 < path.Length)
        {
            path = path[(fragmentIndex + 1)..];
        }

        var span = path.AsSpan();
        while (!span.IsEmpty)
        {
            var slashIndex = span.LastIndexOf('/');
            ReadOnlySpan<char> segment;

            if (slashIndex >= 0)
            {
                segment = span[(slashIndex + 1)..];
                span = span[..slashIndex];
            }
            else
            {
                segment = span;
                span = ReadOnlySpan<char>.Empty;
            }

            if (segment.IsEmpty)
            {
                continue;
            }

            if (!IsNumericToken(segment))
            {
                return segment.ToString();
            }
        }

        return default;
    }

    private static string? GetKeywordLabel(string? token)
    {
        return token switch
        {
            "additionalProperties" => "additional properties",
            "patternProperties" => "pattern properties",
            "unevaluatedProperties" => "unevaluated properties",
            "dependentRequired" => "dependent required",
            "required" => "required property",
            _ => token
        };
    }

    private static bool IsNumericToken(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty)
        {
            return false;
        }

        foreach (var ch in token)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeInstanceLocation(string? location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return "$";
        }

        if (location[0] != '/')
        {
            return location;
        }

        var builder = new StringBuilder(location.Length + 1);
        builder.Append('$');

        var span = location.AsSpan(1);
        while (!span.IsEmpty)
        {
            var slash = span.IndexOf('/');
            ReadOnlySpan<char> segment;
            if (slash < 0)
            {
                segment = span;
                span = ReadOnlySpan<char>.Empty;
            }
            else
            {
                segment = span[..slash];
                span = span[(slash + 1)..];
            }

            if (segment.Length == 0)
            {
                continue;
            }

            if (IsArrayIndex(segment))
            {
                builder.Append('[');
                builder.Append(segment);
                builder.Append(']');
            }
            else
            {
                builder.Append('.');
                builder.Append(UnescapePointerSegment(segment));
            }
        }

        return builder.ToString();
    }

    private static bool IsArrayIndex(ReadOnlySpan<char> segment)
    {
        for (var i = 0; i < segment.Length; i++)
        {
            if (!char.IsAsciiDigit(segment[i]))
            {
                return false;
            }
        }

        return segment.Length > 0;
    }

    private static string UnescapePointerSegment(ReadOnlySpan<char> segment)
    {
        if (segment.IndexOf('~') < 0)
        {
            return segment.ToString();
        }

        return segment.ToString()
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }

    private readonly struct SchemaBuildResult
    {
        private SchemaBuildResult(CompiledJsonSchema? schema, string? errorMessage, bool isSuccess)
        {
            Schema = schema;
            ErrorMessage = errorMessage;
            IsSuccess = isSuccess;
        }

        public CompiledJsonSchema? Schema { get; }

        public string? ErrorMessage { get; }

        public bool IsSuccess { get; }

        public static SchemaBuildResult Success(CompiledJsonSchema schema)
        {
            return new SchemaBuildResult(schema, default, isSuccess: true);
        }

        public static SchemaBuildResult Fail(string errorMessage)
        {
            return new SchemaBuildResult(default, errorMessage, isSuccess: false);
        }
    }
}
