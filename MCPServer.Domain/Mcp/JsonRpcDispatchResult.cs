namespace MCPServer.Domain.Mcp;

public readonly struct JsonRpcDispatchResult
{
    private readonly JsonRpcResponse _response;

    private JsonRpcDispatchResult(JsonRpcResponse response, bool hasResponse)
    {
        _response = response;
        HasResponse = hasResponse;
    }

    public bool HasResponse { get; }

    public JsonRpcResponse Response
    {
        get
        {
            if (!HasResponse)
            {
                throw new InvalidOperationException("The dispatch result does not contain a JSON-RPC response.");
            }

            return _response;
        }
    }

    public static JsonRpcDispatchResult NoResponse => default;

    public static JsonRpcDispatchResult Respond(JsonRpcResponse response)
    {
        return new JsonRpcDispatchResult(response, hasResponse: true);
    }
}
