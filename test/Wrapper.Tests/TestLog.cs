using System.Runtime.CompilerServices;

namespace Wrapper.Tests;

internal static class TestLog
{
    /// <summary>Keep the shared library's log out of the real local app data folder while tests run.</summary>
    [ModuleInitializer]
    internal static void Initialize() => Log.UseSink(_ => { });
}
