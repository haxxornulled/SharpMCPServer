using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.AgentRouter.Hosting.Tests.Testing;

internal static class TestFin
{
    public static T Success<T>(Fin<T> fin)
    {
        return fin.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    public static Error Failure<T>(Fin<T> fin)
    {
        return fin.Match(
            Succ: static _ => throw new InvalidOperationException("Expected Fin failure but received success."),
            Fail: static error => error);
    }
}
