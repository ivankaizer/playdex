using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Playdex.Core;

/// <summary>
/// Starts a packaged app (an Xbox app game) by its app user model id, as the Start menu does, through Windows'
/// ApplicationActivationManager. Source-generated COM, so it works under Native AOT (Playdex.Run).
/// </summary>
public static partial class PackagedApp
{
    private const uint ClsctxAll = 0x17;
    private const int RpcChangedMode = unchecked((int)0x80010106);

    private static readonly Guid _activationManager = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private static readonly StrategyBasedComWrappers _wrappers = new();

    /// <summary>Starts the app and returns the id of the process Windows started for it.</summary>
    public static int Start(string appUserModelId, IEnumerable<string> arguments)
    {
        // Any apartment will do: this thread may already be a UI (STA) thread.
        var initialized = CoInitializeEx(0, 0);
        if (initialized < 0 && initialized != RpcChangedMode)
        {
            Marshal.ThrowExceptionForHR(initialized);
        }
        var iid = typeof(IApplicationActivationManager).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(_activationManager, 0, ClsctxAll, iid, out var instance));
        try
        {
            var manager = (IApplicationActivationManager)_wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
            var commandLine = LaunchOptionsCodec.Format(arguments);
            manager.ActivateApplication(appUserModelId, commandLine.Length == 0 ? null : commandLine, 0, out var processId);
            return (int)processId;
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    internal partial interface IApplicationActivationManager
    {
        void ActivateApplication(string appUserModelId, string? arguments, int options, out uint processId);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
