using System.Runtime.CompilerServices;

namespace Playdex.Core.Tests;

internal static class TestLog
{
    /// <summary>Keep Core's log out of the real %LOCALAPPDATA%\Playdex while tests run.</summary>
    [ModuleInitializer]
    internal static void Initialize() => Log.UseSink(_ => { });
}
