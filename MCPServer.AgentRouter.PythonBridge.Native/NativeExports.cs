using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MCPServer.AgentRouter.PythonBridge.Native;

public static unsafe class NativeExports
{
    [UnmanagedCallersOnly(EntryPoint = "agent_router_run", CallConvs = [typeof(CallConvCdecl)])]
    public static int Run(byte* inputUtf8, int inputLength, byte** outputUtf8, int* outputLength)
    {
        try
        {
            if (outputUtf8 is null || outputLength is null)
            {
                return NativeBridgeStatusCodes.InvalidArguments;
            }

            *outputUtf8 = null;
            *outputLength = 0;

            if (inputUtf8 is null || inputLength < 0)
            {
                return NativeBridgeStatusCodes.InvalidArguments;
            }

            var inputSpan = new ReadOnlySpan<byte>(inputUtf8, inputLength);
            if (!NativeBridgeCodec.TryParseRequest(inputSpan, out var request, out _))
            {
                return NativeBridgeStatusCodes.InvalidArguments;
            }

            var response = NativeBridgeComposer.GetBridgeRuntime()
                .Run(in request, CancellationToken.None);

            return NativeBridgeCodec.SerializeResponse(response, outputUtf8, outputLength);
        }
        catch (OperationCanceledException)
        {
            return NativeBridgeStatusCodes.Cancelled;
        }
        catch (ArgumentException)
        {
            return NativeBridgeStatusCodes.InvalidArguments;
        }
        catch
        {
            return NativeBridgeStatusCodes.ExecutionFailure;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "agent_router_free", CallConvs = [typeof(CallConvCdecl)])]
    public static void Free(byte* ptr)
    {
        if (ptr is not null)
        {
            NativeMemory.Free(ptr);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "agent_router_shutdown", CallConvs = [typeof(CallConvCdecl)])]
    public static int Shutdown()
    {
        NativeBridgeComposer.Shutdown();
        return NativeBridgeStatusCodes.Success;
    }
}
