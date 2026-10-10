using static Playdex.Desktop.NativeDesktop;

namespace Playdex.Desktop;

/// <summary>The Windows open-file dialog (GetOpenFileName), modal to the Playdex window. UI thread only.</summary>
internal static unsafe class FilePicker
{
    /// <param name="filter">Pairs of label and pattern, e.g. ("Executables (*.exe)", "*.exe").</param>
    /// <returns>The chosen file, or null when cancelled.</returns>
    public static string? PickFile(nint owner, string title, params (string Label, string Pattern)[] filter)
    {
        var filterText = string.Concat(filter.Select(f => $"{f.Label}\0{f.Pattern}\0")) + "\0";
        var buffer = new char[32768];
        fixed (char* file = buffer)
        fixed (char* filterPointer = filterText)
        fixed (char* titlePointer = title)
        {
            var dialog = new OpenFileName
            {
                StructSize = (uint)sizeof(OpenFileName),
                Owner = owner,
                Filter = filterPointer,
                FilterIndex = 1,
                File = file,
                MaxFile = (uint)buffer.Length,
                Title = titlePointer,
                Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir,
            };
            return GetOpenFileName(&dialog) ? new string(file) : null;
        }
    }
}
