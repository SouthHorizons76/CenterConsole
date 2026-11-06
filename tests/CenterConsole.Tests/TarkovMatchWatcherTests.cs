using CenterConsole.Core.Services;

namespace CenterConsole.Tests;

public sealed class TarkovMatchWatcherTests : IDisposable
{
    private const string MatchLine = "2026-11-05 18:11:20.335|1.3.0.1.48558|Info|application|MatchingCompleted:487.51 real:495.57 diff:8.05\n";
    private const string OtherLine = "2026-11-05 18:11:21.000|1.3.0.1.48558|Info|application|LocationLoaded:1.0\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "CenterConsoleTarkov_" + Guid.NewGuid());
    private readonly TarkovMatchWatcher _watcher = new();
    private int _matches;

    public TarkovMatchWatcherTests()
    {
        Directory.CreateDirectory(_root);
        _watcher.MatchFound += (_, _) => _matches++;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private string CreateSession(string name)
    {
        string dir = Path.Combine(_root, "log_" + name);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + " application_000.log");
    }

    [Fact]
    public void MatchWrittenBeforeStart_IsIgnored()
    {
        string log = CreateSession("2026.11.05_17-52-25_1.3.0.1.48558");
        File.WriteAllText(log, MatchLine);

        _watcher.Start(_root);
        _watcher.Poll();

        Assert.Equal(0, _matches);
    }

    [Fact]
    public void MatchAppendedAfterStart_RaisesOnce()
    {
        string log = CreateSession("2026.11.05_17-52-25_1.3.0.1.48558");
        File.WriteAllText(log, OtherLine);
        _watcher.Start(_root);

        File.AppendAllText(log, MatchLine);
        _watcher.Poll();
        _watcher.Poll();

        Assert.Equal(1, _matches);
    }

    [Fact]
    public void PartialLine_IsOnlyMatchedOnceComplete()
    {
        string log = CreateSession("2026.11.05_17-52-25_1.3.0.1.48558");
        File.WriteAllText(log, "");
        _watcher.Start(_root);

        File.AppendAllText(log, MatchLine[..40]);
        _watcher.Poll();
        Assert.Equal(0, _matches);

        File.AppendAllText(log, MatchLine[40..]);
        _watcher.Poll();
        Assert.Equal(1, _matches);
    }

    [Fact]
    public void NewGameSession_IsReadFromStart()
    {
        File.WriteAllText(CreateSession("2026.11.05_16-20-41_1.3.0.1.48558"), OtherLine);
        _watcher.Start(_root);

        File.WriteAllText(CreateSession("2026.11.05_17-52-25_1.3.0.1.48558"), OtherLine + MatchLine);
        _watcher.Poll();

        Assert.Equal(1, _matches);
    }

    [Fact]
    public void Disabled_DoesNotRaise()
    {
        string log = CreateSession("2026.11.05_17-52-25_1.3.0.1.48558");
        File.WriteAllText(log, "");
        _watcher.Start(null);

        File.AppendAllText(log, MatchLine);
        _watcher.Poll();

        Assert.Equal(0, _matches);
    }
}
