using System.Text.Json;
using LanguageExt;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpToolArgumentValidator
{
    Fin<JsonElement> Validate(JsonElement inputSchema, JsonElement? arguments);

    Fin<JsonElement> ValidateRequiredValue(JsonElement schema, JsonElement value, string subject);

    Fin<JsonElement> ValidateSchema(JsonElement schema, string subject);
}
