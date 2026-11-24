using CenterConsole.Core.Services;

namespace CenterConsole.Tests;

public class TarkovAudioConfigTests
{
    private const string StockConfig =
        "Preamp: -6 dB\r\nInclude: example.txt\r\nFilter  1: ON  PK       Fc   50 Hz   Gain  -3.0 dB  Q  2.00\r\n";

    [Fact]
    public void StockConfig_IsReplacedWithOnlyOurInclude()
    {
        string result = TarkovAudioService.AddInclude(StockConfig);
        Assert.Equal(TarkovAudioService.IncludeLine + "\r\n", result);
    }

    [Fact]
    public void EmptyConfig_GetsOurInclude()
    {
        Assert.Equal(TarkovAudioService.IncludeLine + "\r\n", TarkovAudioService.AddInclude(""));
    }

    [Fact]
    public void CustomConfig_IsKeptAndIncludeAppended()
    {
        const string custom = "Preamp: -2 dB\r\nGraphicEQ: 25 0; 40 1";
        string result = TarkovAudioService.AddInclude(custom);
        Assert.StartsWith(custom + "\r\n", result);
        Assert.EndsWith(TarkovAudioService.IncludeLine + "\r\n", result);
    }

    [Fact]
    public void AddInclude_IsIdempotent()
    {
        string once = TarkovAudioService.AddInclude("Preamp: -2 dB\n");
        Assert.Equal(once, TarkovAudioService.AddInclude(once));
    }

    [Fact]
    public void HasInclude_IgnoresCaseAndWhitespace()
    {
        Assert.True(TarkovAudioService.HasInclude("Preamp: 0 dB\n  include: CENTERCONSOLE-TARKOV.TXT  \n"));
        Assert.False(TarkovAudioService.HasInclude("# Include: centerconsole-tarkov.txt\n"));
    }

    [Fact]
    public void IncludeFile_QuotesPluginPath()
    {
        string text = TarkovAudioService.BuildIncludeFile(@"C:\Program Files\EqualizerAPO\VSTPlugins\CenterConsole\TarkovAudio.dll");
        Assert.Contains("VSTPlugin: Library \"C:\\Program Files\\EqualizerAPO\\VSTPlugins\\CenterConsole\\TarkovAudio.dll\"", text);
    }
}
