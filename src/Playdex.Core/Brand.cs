namespace Playdex.Core;

/// <summary>Names that appear in files, HTTP requests and generated launchers.</summary>
public static class Brand
{
    public const string Name = "Playdex";

    /// <summary>Generated per-game launchers are named Playdex-&lt;game&gt;-&lt;hash&gt;.exe.</summary>
    public const string LauncherPrefix = Name + "-";

    /// <summary>Each generated launcher has a manifest next to it: Playdex-&lt;game&gt;-&lt;hash&gt;.playdex.json.</summary>
    public const string LauncherManifestExtension = ".playdex.json";

    public const string UserAgent = Name + "/1.0";
}
