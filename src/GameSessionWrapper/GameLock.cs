namespace GameSessionWrapper;

internal sealed class GameLock : IDisposable
{
    private readonly FileStream _stream;

    private GameLock(FileStream stream)
    {
        _stream = stream;
    }

    public static GameLock Acquire(string directory, string gameId)
    {
        Directory.CreateDirectory(directory);
        var canonicalGameId = gameId.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalGameId)))[..16];
        var path = Path.Combine(directory, $"{hash}.lock");
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new GameLock(stream);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Game '{gameId}' is already wrapped by another process.", ex);
        }
    }

    public void Dispose() => _stream.Dispose();
}
