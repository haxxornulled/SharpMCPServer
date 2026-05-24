using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.AgentRouter.Ssh.Tests.Testing;

internal static class TestFin
{
    public static T Success<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
    }

    public static Error Failure<T>(Fin<T> result)
    {
        return result.Match(
            Succ: _ => throw new InvalidOperationException("Expected failure."),
            Fail: static error => error);
    }
}
