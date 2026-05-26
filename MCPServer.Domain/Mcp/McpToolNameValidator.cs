namespace MCPServer.Domain.Mcp;

public static class McpToolNameValidator
{
    public const int MaxLength = 128;

    public static bool IsValid(string? name)
    {
        if (name is not { Length: > 0 and <= MaxLength })
        {
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var valid = c is (>= 'A' and <= 'Z') or
                        (>= 'a' and <= 'z') or
                        (>= '0' and <= '9') or
                        '_' or '-' or '.';

            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}
