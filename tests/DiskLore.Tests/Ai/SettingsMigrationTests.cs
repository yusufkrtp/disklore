using DiskLore.App.Services;
using DiskLore.Core.Ai.Providers;
using Xunit;

namespace DiskLore.Tests.Ai;

public class SettingsMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "disklore-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    [Fact]
    public void Old_AiModel_moves_into_the_claude_code_provider_settings()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"ThemeId":"dark","AiAuto":false,"AiThresholdBytes":2147483648,"AiModel":"haiku","AiMaxCallsPerScan":3}""");

        var s = SettingsStore.Load(path);

        Assert.Equal("claude-code", s.AiProviderId);
        Assert.Equal("haiku", s.GetProviderSettings("claude-code").Model);
        Assert.Null(s.AiModel);
        Assert.False(s.AiAuto);
        Assert.Equal(2147483648, s.AiThresholdBytes);
        Assert.Equal(3, s.AiMaxCallsPerScan);

        // Saved again, the legacy key is gone and the new shape round-trips.
        s.Save(path);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("AiModel", text);
        var again = SettingsStore.Load(path);
        Assert.Equal("claude-code", again.AiProviderId);
        Assert.Equal("haiku", again.GetProviderSettings("claude-code").Model);
    }

    [Fact]
    public void New_settings_have_no_provider_yet_and_keep_per_provider_options()
    {
        var path = Path.Combine(_dir, "settings.json");
        var fresh = SettingsStore.Load(path);
        Assert.Null(fresh.AiProviderId);
        Assert.Equal(new AiProviderSettings(null, null), fresh.GetProviderSettings("ollama"));

        fresh.AiProviderId = "ollama";
        fresh.SetProviderSettings("ollama", new AiProviderSettings("llama3", "http://localhost:11434/v1"));
        fresh.Save(path);

        var back = SettingsStore.Load(path);
        Assert.Equal("ollama", back.AiProviderId);
        Assert.Equal(new AiProviderSettings("llama3", "http://localhost:11434/v1"), back.GetProviderSettings("ollama"));
    }

    [Fact]
    public void An_explicit_provider_is_kept_when_the_legacy_model_is_present()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"AiProviderId":"codex-cli","AiModel":"sonnet"}""");
        var s = SettingsStore.Load(path);
        Assert.Equal("codex-cli", s.AiProviderId);
        Assert.Equal("sonnet", s.GetProviderSettings("claude-code").Model);
    }
}
