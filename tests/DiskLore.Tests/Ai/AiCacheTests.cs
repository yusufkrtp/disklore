using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using Xunit;

namespace DiskLore.Tests.Ai;

public class AiCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "disklore-cache-test-" + Guid.NewGuid().ToString("N"));

    private string CacheFile => Path.Combine(_dir, "ai-cache.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (Exception) { }
    }

    private static Explanation Sample(string title = "Thing") => new(
        title, Badge.Leftover, "Owner", "Summary", "What", "Why", "If", "Safer", null, ExplanationSource.AI, 0.6, null);

    [Fact]
    public void Set_and_get_round_trip()
    {
        var cache = new AiCache(CacheFile);
        var path = Path.Combine(Path.GetTempPath(), "somewhere", "Big");
        cache.Set(path, Sample());
        var e = cache.Get(path);
        Assert.NotNull(e);
        Assert.Equal("Thing", e!.Title);
        Assert.Equal(Badge.Leftover, e.Badge);
        Assert.Equal(ExplanationSource.AI, e.Source);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Lookup_ignores_trailing_separator_and_case_on_case_insensitive_platforms()
    {
        var cache = new AiCache(CacheFile);
        var path = Path.Combine(Path.GetTempPath(), "somewhere", "Big");
        cache.Set(path, Sample());
        Assert.NotNull(cache.Get(path + Path.DirectorySeparatorChar));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Assert.NotNull(cache.Get(path.ToUpperInvariant()));
        else
            Assert.Null(cache.Get(path.ToUpperInvariant()));
    }

    [Fact]
    public void Profile_prefix_is_replaced_by_a_token()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(profile, "Stuff", "Data");
        var key = AiCache.Normalize(path);
        Assert.StartsWith("%userprofile%".ToLowerInvariant(), key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(profile.ToLowerInvariant(), key.ToLowerInvariant());
    }

    [Fact]
    public void Persists_across_instances()
    {
        var path = Path.Combine(Path.GetTempPath(), "x", "Y");
        new AiCache(CacheFile).Set(path, Sample("Persisted"));
        var again = new AiCache(CacheFile);
        Assert.Equal("Persisted", again.Get(path)?.Title);
        Assert.Equal(1, again.Count);
    }

    [Fact]
    public void Corrupt_file_starts_empty_and_is_renamed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(CacheFile, "{ this is not json");
        var cache = new AiCache(CacheFile);
        Assert.Equal(0, cache.Count);
        Assert.True(File.Exists(CacheFile + ".bad"));
        cache.Set(Path.Combine(Path.GetTempPath(), "a"), Sample());
        Assert.Equal(1, new AiCache(CacheFile).Count);
    }

    [Fact]
    public void Clear_empties_the_file()
    {
        var cache = new AiCache(CacheFile);
        cache.Set(Path.Combine(Path.GetTempPath(), "a"), Sample());
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, new AiCache(CacheFile).Count);
    }
}
