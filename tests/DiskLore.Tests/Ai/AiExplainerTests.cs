using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using Xunit;

namespace DiskLore.Tests.Ai;

public class AiExplainerTests
{
    private static string TempCache() => Path.Combine(Path.GetTempPath(), "disklore-expl-" + Guid.NewGuid().ToString("N") + ".json");

    private static (ScanNode, AiRequestItem) Item(string name)
    {
        var root = new ScanNode(Path.GetFullPath("scanroot"), null, true);
        var d = new ScanNode(name, root, true) { Size = 2L << 30 };
        root.AddChild(d);
        return (d, new AiRequestItem("scanroot\\" + name, d.Size, 10, [], [], [], null, null));
    }

    private static string Answers(int count, string badge = "check", double confidence = 0.5)
        => "[" + string.Join(",", Enumerable.Range(0, count).Select(_ => $"{{\"badge\":\"{badge}\",\"summary\":\"s\",\"confidence\":{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}")) + "]";

    [Fact]
    public void Prompt_contains_items_schema_and_instruction()
    {
        var (_, item) = Item("Foo");
        var prompt = AiExplainer.BuildPrompt([item]);
        Assert.Contains("\"path\":\"scanroot\\\\Foo\"", prompt);
        Assert.Contains("Reply with only the JSON array.", prompt);
        Assert.Contains("app|check|safe|leftover|user|unknown", prompt);
    }

    [Fact]
    public async Task Batches_of_five_and_one_failing_batch_does_not_stop_the_rest()
    {
        var cachePath = TempCache();
        var cache = new AiCache(cachePath);
        var calls = 0;
        var provider = new FakeProvider((s, u, ct) =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("boom");
            return Task.FromResult(Answers(5));
        });
        var explainer = new AiExplainer(cache, provider);

        var items = Enumerable.Range(0, 12).Select(i => Item("F" + i)).ToList();
        var result = await explainer.ExplainAsync(items, CancellationToken.None);

        Assert.Equal(3, calls);
        Assert.Equal(7, result.Count);   // batch 1 failed (5), batch 2 answered 5, batch 3 had 2 items and 5 answers by order -> 2
        Assert.Equal(7, cache.Count);
        Assert.Equal(AiExplainer.SystemPrompt, provider.Calls[0].System);
        File.Delete(cachePath);
    }

    [Fact]
    public async Task Answers_are_clamped_and_written_to_the_cache()
    {
        var cachePath = TempCache();
        var cache = new AiCache(cachePath);
        var explainer = new AiExplainer(cache, new FakeProvider((s, u, ct) => Task.FromResult(Answers(1, "critical", 0.99))));
        var (node, item) = Item("Clampy");

        var result = await explainer.ExplainAsync([(node, item)], CancellationToken.None);

        var e = Assert.Single(result).Value;
        Assert.Equal(Badge.Check, e.Badge);            // Critical is rule-only
        Assert.Equal(0.9, e.Confidence);               // 0.1-0.9
        Assert.Equal(ExplanationSource.AI, e.Source);
        Assert.NotNull(cache.Get(node.FullPath));
        File.Delete(cachePath);
    }

    [Fact]
    public async Task Last_error_is_reported_and_cleared_by_the_next_good_batch()
    {
        var calls = 0;
        var explainer = new AiExplainer(new AiCache(TempCache()), new FakeProvider((s, u, ct) =>
            ++calls == 1 ? throw new InvalidOperationException("API key rejected") : Task.FromResult(Answers(1))));

        await explainer.ExplainAsync(Enumerable.Range(0, 5).Select(i => Item("A" + i)).ToList(), CancellationToken.None);
        Assert.Equal("API key rejected", explainer.LastError);

        await explainer.ExplainAsync([Item("B")], CancellationToken.None);
        Assert.Null(explainer.LastError);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var explainer = new AiExplainer(new AiCache(TempCache()), new FakeProvider((p, u, ct) => throw new OperationCanceledException(ct)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => explainer.ExplainAsync([Item("A")], cts.Token));
    }

    [Fact]
    public void Availability_and_names_follow_the_provider()
    {
        var ready = new AiExplainer(new AiCache(TempCache()), new FakeProvider(ready: true));
        var notReady = new AiExplainer(new AiCache(TempCache()), new FakeProvider(ready: false));
        Assert.True(ready.IsAvailable);
        Assert.False(notReady.IsAvailable);
        Assert.Equal("Fake AI (test)", ready.ProviderName);
        Assert.Equal("Fake AI", ready.ProviderShortName);
    }
}
