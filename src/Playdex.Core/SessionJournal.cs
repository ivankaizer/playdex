using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

/// <summary>A running session as <see cref="SessionJournal"/> keeps it on disk.</summary>
/// <param name="LastSeenAt">The last time the game was seen running: the session's start, then every ping interval.</param>
/// <param name="Online">Whether the start reached Playdex: the server then has the session and closes it itself.</param>
/// <param name="AnsweredAt">
/// Online, the last time Playdex answered for it (start or ping): the server closes the session
/// <see cref="SessionRecorder.DefaultServerTimeout"/> after that, so play seen later than that is not on the server.
/// </param>
internal sealed record JournalEntry(
    string GameTitle, string GameId, string GameIdProvider, string Platform,
    DateTimeOffset StartedAt, DateTimeOffset LastSeenAt, bool Online, DateTimeOffset? AnsweredAt = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JournalEntry))]
internal sealed partial class JournalJson : JsonSerializerContext;

/// <summary>
/// The sessions still running, on disk: one file per session in a folder next to the offline queue, written when the
/// game starts and at every ping interval, deleted when the session is finished. A file that outlives its session
/// belongs to a process that ended first: a crash, Windows shutting down, Playdex quit while a game ran.
/// <see cref="Recover"/> turns such a session that never reached Playdex into a queued one, from its real start to the
/// last time the game was seen running, so nothing played offline is lost. One that reached Playdex is left to the
/// server, which closes it from its last ping, except for play seen after the server had closed it (its pings had not
/// got through for longer than the server's timeout): that part is queued (<see cref="SessionRecorder.Lost"/>).
/// </summary>
/// <remarks>
/// While its process runs, a session's file is held open without sharing; that is how another process (the app, a
/// game's launcher) tells a live session from one left behind, and Windows lets go of it when the process dies.
/// </remarks>
internal sealed class SessionJournal(string directory)
{
    public string Folder { get; } = directory;

    /// <summary>A session that has just started, on disk; null if it can't be written (it then runs without).</summary>
    public RunningSession? Begin(CaptureGame game, DateTimeOffset startedAt)
    {
        var path = Path.Combine(Folder, $"{Guid.NewGuid():N}.json");
        FileStream? stream = null;
        try
        {
            Directory.CreateDirectory(Folder);
            // Delete is shared so the session can delete its own file before letting go of it (see Complete).
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
            var session = new RunningSession(stream, path, new JournalEntry(
                game.GameTitle, game.GameId, game.GameIdProvider, game.Platform, startedAt, startedAt, Online: false));
            session.Seen(startedAt);
            return session;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stream?.Dispose();
            Log.Error($"journal: could not record {game.GameTitle} as running", ex);
            return null;
        }
    }

    /// <summary>
    /// Hands every session left behind by a process that has ended to <paramref name="recovered"/>, then deletes it:
    /// one that never reached Playdex as a queued session (start to last seen running), one that reached Playdex as the
    /// play seen after the server closed it, if any; one under a second not at all. Sessions of running processes are
    /// left alone.
    /// </summary>
    public void Recover(Action<QueuedSession> recovered)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("journal: could not list the running sessions", ex);
            return;
        }
        foreach (var path in files)
        {
            FileStream stream;
            try
            {
                // Fails while the session's own process holds it, or once it is being deleted.
                stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            using (stream)
            {
                JournalEntry? entry = null;
                try
                {
                    entry = JsonSerializer.Deserialize(stream, JournalJson.Default.JournalEntry);
                }
                catch (JsonException)
                {
                    Log.Info($"journal: dropping the unreadable {Path.GetFileName(path)}");
                }
                if (entry is { Online: false } && (int)(entry.LastSeenAt - entry.StartedAt).TotalSeconds is var seconds and >= 1)
                {
                    recovered(new QueuedSession(
                        entry.GameTitle, entry.GameId, entry.GameIdProvider, entry.Platform, seconds, entry.StartedAt, entry.LastSeenAt));
                    Log.Info($"journal: recovered {entry.GameTitle} ({seconds}s from {entry.StartedAt:O}) left running by an ended process");
                }
                else if (entry is { Online: true, AnsweredAt: { } answered }
                    && SessionRecorder.Lost(
                        new CaptureGame(entry.GameTitle, entry.GameId, entry.GameIdProvider, entry.Platform),
                        answered, entry.LastSeenAt, SessionRecorder.DefaultServerTimeout) is { } lost)
                {
                    recovered(lost);
                    Log.Info($"journal: recovered {lost.DurationInSeconds}s of {entry.GameTitle} from {lost.StartedAt:O}, after Playdex closed it, left running by an ended process");
                }
                // Deleted while still held, so no other process can recover it a second time.
                TryDelete(path);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"journal: could not delete {Path.GetFileName(path)}", ex);
        }
    }
}

/// <summary>One running session's file, held open while it runs. Thread-safe: the ping loop and the finish may race.</summary>
internal sealed class RunningSession(FileStream stream, string path, JournalEntry entry) : IDisposable
{
    private readonly Lock _gate = new();
    private JournalEntry _entry = entry;
    private bool _closed;

    /// <summary>
    /// The game is still running at <paramref name="now"/>; <paramref name="online"/> once its start reached Playdex,
    /// <paramref name="answeredAt"/> the last time Playdex answered for it.
    /// </summary>
    public void Seen(DateTimeOffset now, bool? online = null, DateTimeOffset? answeredAt = null)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }
            _entry = _entry with { LastSeenAt = now, Online = online ?? _entry.Online, AnsweredAt = answeredAt ?? _entry.AnsweredAt };
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(_entry, JournalJson.Default.JournalEntry);
                stream.Position = 0;
                stream.Write(bytes);
                stream.SetLength(bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            catch (IOException ex)
            {
                Log.Error($"journal: could not update {_entry.GameTitle}", ex);
            }
        }
    }

    /// <summary>The session is finished (ended, queued or dropped): its file goes.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"journal: could not delete {Path.GetFileName(path)}", ex);
            }
            stream.Dispose();
        }
    }

    /// <summary>Lets go of the file without finishing the session: a later start of the app recovers it.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            stream.Dispose();
        }
    }
}
