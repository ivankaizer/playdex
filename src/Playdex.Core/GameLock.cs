using System.Security.Cryptography;
using System.Text;

namespace Playdex.Core;

/// <summary>
/// One process tracks a game at a time. The holder has an exclusive lock on the first byte of the game's lock file
/// (state\{hash}.lock), which Windows releases when the process ends. The file itself is opened shared, so
/// <see cref="IsHeld"/> can look at the lock without taking it: a check (the backup scheduler's) never makes a start of
/// the game at that instant fail as "already wrapped".
/// </summary>
public sealed class GameLock : IDisposable
{
    private readonly FileStream _stream;

    private GameLock(FileStream stream)
    {
        _stream = stream;
    }

    public static GameLock Acquire(string directory, string gameId)
    {
        Directory.CreateDirectory(directory);
        FileStream? stream = null;
        try
        {
            stream = new FileStream(PathFor(directory, gameId), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, bufferSize: 1);
            if (stream.Length == 0)
            {
                // A byte to lock and to read: IsHeld reads it, and a read of a byte another handle has locked fails.
                stream.WriteByte(0);
                stream.Flush();
            }
            stream.Lock(0, 1);
            return new GameLock(stream);
        }
        catch (IOException ex)
        {
            stream?.Dispose();
            throw new InvalidOperationException($"Game '{gameId}' is already wrapped by another process.", ex);
        }
    }

    /// <summary>Whether a process holds the game's lock. Only reads: it never holds the lock, not even briefly.</summary>
    public static bool IsHeld(string directory, string gameId)
    {
        try
        {
            using var stream = new FileStream(PathFor(directory, gameId), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);
            Span<byte> probe = stackalloc byte[1];
            _ = stream.Read(probe);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            // The byte is locked; or an older Playdex holds the file itself, opened without sharing.
            return true;
        }
    }

    public void Dispose()
    {
        try
        {
            // Unlock before closing: Windows releases the locks of a closed handle only eventually.
            _stream.Unlock(0, 1);
        }
        catch (IOException)
        {
        }
        _stream.Dispose();
    }

    private static string PathFor(string directory, string gameId)
    {
        var canonicalGameId = gameId.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalGameId)))[..16];
        return Path.Combine(directory, $"{hash}.lock");
    }
}
