using System.Text;
using Microsoft.Win32;

namespace CenterConsole.Core.Services;

public interface ITarkovMatchWatcher : IDisposable
{
    /// <summary>Raised (on a thread-pool thread) when Tarkov reports a match was found.</summary>
    event EventHandler? MatchFound;

    /// <summary>Starts polling <paramref name="logsFolder"/>, or stops when it is null/empty.</summary>
    void Start(string? logsFolder);
    void Stop();
}

/// <summary>
/// Tails Escape from Tarkov's application log for the "MatchingCompleted" line it writes the moment
/// matchmaking finds a game (the same signal Morphy uses for its "Match found" state). Tarkov writes
/// one log_* session folder per game launch under its Logs directory; only the newest one is watched.
///
/// Deliberately polling instead of FileSystemWatcher: Tarkov keeps the file open and buffers writes, so
/// change notifications are unreliable, and a once-a-second read of only the newly appended bytes costs
/// effectively nothing.
/// </summary>
public sealed class TarkovMatchWatcher : ITarkovMatchWatcher
{
    private const string MatchFoundMarker = "|application|MatchingCompleted";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Tail> _tails = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private string? _root;
    private string? _sessionFolder;

    public event EventHandler? MatchFound;

    public void Start(string? logsFolder)
    {
        lock (_gate)
        {
            StopCore();
            if (string.IsNullOrWhiteSpace(logsFolder))
                return;

            _root = logsFolder;
            // Prime offsets at the current end of every existing log so a match found before
            // CenterConsole started (or before the watcher was re-enabled) doesn't notify again.
            PollCore(primeExisting: true);
            _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
            StopCore();
    }

    private void StopCore()
    {
        _timer?.Dispose();
        _timer = null;
        _root = null;
        _sessionFolder = null;
        _tails.Clear();
    }

    /// <summary>Runs one poll pass. Public so tests can drive it without waiting on the timer.</summary>
    public void Poll()
    {
        bool found;
        lock (_gate)
        {
            if (_root is null)
                return;
            found = PollCore(primeExisting: false);
        }

        if (found)
            MatchFound?.Invoke(this, EventArgs.Empty);
    }

    private bool PollCore(bool primeExisting)
    {
        try
        {
            string? session = FindNewestSessionFolder(_root!);
            if (session is null)
                return false;

            if (!string.Equals(session, _sessionFolder, StringComparison.OrdinalIgnoreCase))
            {
                // A new game launch. Its files are brand new, so read them from the start.
                _sessionFolder = session;
                _tails.Clear();
            }

            bool found = false;
            foreach (string file in Directory.EnumerateFiles(session, "* application*.log"))
                found |= ReadNewLines(file, primeExisting);
            return found;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool ReadNewLines(string file, bool primeExisting)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;

        if (!_tails.TryGetValue(file, out var tail) || length < tail.Offset)
        {
            tail = new Tail { Offset = primeExisting ? length : 0 };
            _tails[file] = tail;
        }

        if (length == tail.Offset)
            return false;

        stream.Seek(tail.Offset, SeekOrigin.Begin);
        var buffer = new byte[Math.Min(length - tail.Offset, 256 * 1024)];
        int read = stream.Read(buffer, 0, buffer.Length);
        tail.Offset += read;

        var chars = new char[tail.Decoder.GetCharCount(buffer, 0, read)];
        tail.Decoder.GetChars(buffer, 0, read, chars, 0);
        string text = tail.Pending + new string(chars);

        // Keep the trailing partial line for the next pass; Tarkov may be mid-write.
        int lastNewline = text.LastIndexOf('\n');
        tail.Pending = lastNewline < 0 ? Truncate(text) : Truncate(text[(lastNewline + 1)..]);
        string complete = lastNewline < 0 ? string.Empty : text[..lastNewline];

        return complete.Contains(MatchFoundMarker, StringComparison.Ordinal);
    }

    private static string Truncate(string pending) => pending.Length > 65536 ? pending[^65536..] : pending;

    private static string? FindNewestSessionFolder(string root) =>
        new DirectoryInfo(root).EnumerateDirectories("log_*")
            .OrderBy(d => d.CreationTimeUtc)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .LastOrDefault()?.FullName;

    /// <summary>Best-effort lookup of "&lt;install dir&gt;\Logs" from the BSG launcher's uninstall entry.</summary>
    public static string? TryDetectLogsFolder()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov");
            if (key?.GetValue("InstallLocation") is string install)
            {
                string logs = Path.Combine(install, "Logs");
                if (Directory.Exists(logs))
                    return logs;
            }
        }
        catch
        {
            // Registry unavailable or denied: the user can still browse to the folder manually.
        }
        return null;
    }

    public void Dispose() => Stop();

    private sealed class Tail
    {
        public long Offset;
        public string Pending = string.Empty;
        public readonly Decoder Decoder = Encoding.UTF8.GetDecoder();
    }
}
