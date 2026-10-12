using System.Buffers.Binary;
using System.IO.Compression;

namespace Playdex.Core;

/// <summary>
/// The offline installer: Playdex-Setup.exe with a zip appended after the exe, then a 16-byte trailer (the zip's length,
/// 8 bytes little endian, and "PDXSETUP"). The zip holds the app folder under app/ and, optionally, the Full Screen
/// Experience package under fse/. Windows ignores data after an exe's image, so the exe runs as it is. Without a
/// trailer the setup is the online one and downloads the app from the release.
/// </summary>
public static class SetupPayload
{
    public const string AppFolder = "app";
    public const string FseFolder = "fse";

    private const int TrailerLength = 16;
    private static ReadOnlySpan<byte> Magic => "PDXSETUP"u8;

    /// <summary>How long the exe itself is, and the payload after it (0 without one).</summary>
    public static (long ExecutableLength, long PayloadLength) Measure(string path)
    {
        using var file = File.OpenRead(path);
        return Measure(file);
    }

    private static (long ExecutableLength, long PayloadLength) Measure(FileStream file)
    {
        if (file.Length < TrailerLength)
        {
            return (file.Length, 0);
        }
        Span<byte> trailer = stackalloc byte[TrailerLength];
        file.Seek(-TrailerLength, SeekOrigin.End);
        file.ReadExactly(trailer);
        var length = BinaryPrimitives.ReadInt64LittleEndian(trailer[..8]);
        if (!trailer[8..].SequenceEqual(Magic) || length <= 0 || length > file.Length - TrailerLength)
        {
            return (file.Length, 0);
        }
        return (file.Length - TrailerLength - length, length);
    }

    /// <summary>
    /// Write <paramref name="output"/>: the exe part of <paramref name="setupExecutable"/> (any payload it has is
    /// dropped), then <paramref name="appDirectory"/> under app/ and <paramref name="fseFiles"/> under fse/.
    /// </summary>
    public static void Bundle(string setupExecutable, string appDirectory, IEnumerable<string> fseFiles, string output)
    {
        if (!File.Exists(Path.Combine(appDirectory, AppUpdate.AppExecutable)))
        {
            throw new InvalidOperationException($"{appDirectory} has no {AppUpdate.AppExecutable}.");
        }
        var zipPath = output + ".payload.zip";
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(appDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(appDirectory, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, $"{AppFolder}/{relative}", CompressionLevel.Optimal);
                }
                foreach (var file in fseFiles)
                {
                    zip.CreateEntryFromFile(file, $"{FseFolder}/{Path.GetFileName(file)}", CompressionLevel.NoCompression);
                }
            }
            CopyExecutable(setupExecutable, output);
            using var target = new FileStream(output, FileMode.Append, FileAccess.Write);
            using (var payload = File.OpenRead(zipPath))
            {
                payload.CopyTo(target);
            }
            Span<byte> trailer = stackalloc byte[TrailerLength];
            BinaryPrimitives.WriteInt64LittleEndian(trailer[..8], new FileInfo(zipPath).Length);
            Magic.CopyTo(trailer[8..]);
            target.Write(trailer);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>Copy the exe part of <paramref name="path"/>, without its payload: the uninstaller kept with the app.</summary>
    public static void CopyExecutable(string path, string destination)
    {
        using var source = File.OpenRead(path);
        var (executableLength, _) = Measure(source);
        source.Seek(0, SeekOrigin.Begin);
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write);
        var buffer = new byte[81920];
        var left = executableLength;
        while (left > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read == 0)
            {
                throw new EndOfStreamException("The setup ended early.");
            }
            target.Write(buffer, 0, read);
            left -= read;
        }
    }

    /// <summary>
    /// Extract the payload of <paramref name="path"/> into <paramref name="destination"/> (app/ and fse/ below it);
    /// false when it has none.
    /// </summary>
    public static bool TryExtract(string path, string destination)
    {
        using var file = File.OpenRead(path);
        var (executableLength, payloadLength) = Measure(file);
        if (payloadLength == 0)
        {
            return false;
        }
        Directory.CreateDirectory(destination);
        var zipPath = Path.Combine(destination, "payload.zip");
        using (var zipFile = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        {
            file.Seek(executableLength, SeekOrigin.Begin);
            var buffer = new byte[81920];
            var left = payloadLength;
            while (left > 0)
            {
                var read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (read == 0)
                {
                    throw new EndOfStreamException("The setup's payload ended early.");
                }
                zipFile.Write(buffer, 0, read);
                left -= read;
            }
        }
        try
        {
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The setup's payload contains a path outside its folder.");
                }
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        finally
        {
            File.Delete(zipPath);
        }
        if (!File.Exists(Path.Combine(destination, AppFolder, AppUpdate.AppExecutable)))
        {
            throw new InvalidDataException($"The setup's payload has no {AppUpdate.AppExecutable}.");
        }
        return true;
    }
}
