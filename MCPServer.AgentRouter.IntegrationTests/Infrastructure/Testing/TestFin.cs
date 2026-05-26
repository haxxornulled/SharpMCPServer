using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.AgentRouter.Infrastructure.Tests.Testing;

internal static class TestFin
{
    public static T Success<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    public static Error Failure<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static _ => throw new InvalidOperationException("Expected failure, but operation succeeded."),
            Fail: static error => error);
    }
}
