using System.Runtime.InteropServices;
using System.Text.Json;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.PythonBridge.Native;

internal static class NativeBridgeCodec
{
    public static bool TryParseRequest(
        ReadOnlySpan<byte> inputUtf8,
        out AgentRouterBridgeRequest request,
        out string? errorMessage)
    {
        request = default;
        errorMessage = null;

        if (inputUtf8.IsEmpty)
        {
            errorMessage = "agent router request payload is required.";
            return false;
        }

        try
        {
            AgentRouterBridgeRequest? parsedRequest = JsonSerializer.Deserialize(
                inputUtf8,
                NativeBridgeJsonSerializerContext.Default.AgentRouterBridgeRequest);

            if (parsedRequest is not { } value)
            {
                errorMessage = "agent router request payload was empty.";
                return false;
            }

            request = value;
            return true;
        }
        catch (JsonException exception)
        {
            errorMessage = $"agent router request payload is not valid JSON: {exception.Message}";
            return false;
        }
    }

    public static unsafe int SerializeResponse(
        AgentRouterBridgeResponse response,
        byte** outputUtf8,
        int* outputLength)
    {
        if (outputUtf8 is null || outputLength is null)
        {
            return NativeBridgeStatusCodes.InvalidArguments;
        }

        using var writer = new PooledUtf8BufferWriter();
        using (var jsonWriter = new Utf8JsonWriter(writer))
        {
            JsonSerializer.Serialize(
                jsonWriter,
                response,
                NativeBridgeJsonSerializerContext.Default.AgentRouterBridgeResponse);
            jsonWriter.Flush();
        }

        if (writer.WrittenCount <= 0)
        {
            return NativeBridgeStatusCodes.ExecutionFailure;
        }

        var length = writer.WrittenCount;
        var nativeBuffer = NativeMemory.Alloc((nuint)length);

        fixed (byte* source = writer.WrittenSpan)
        {
            Buffer.MemoryCopy(source, nativeBuffer, length, length);
        }

        *outputUtf8 = (byte*)nativeBuffer;
        *outputLength = length;
        return NativeBridgeStatusCodes.Success;
    }
}
