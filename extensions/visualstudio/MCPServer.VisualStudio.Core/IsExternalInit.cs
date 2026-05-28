#if NET472
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Provides the compiler hook required for init-only properties on .NET Framework targets.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
