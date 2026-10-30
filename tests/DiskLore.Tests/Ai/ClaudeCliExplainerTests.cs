using System.Diagnostics;
using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using DiskLore.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace DiskLore.Tests.Ai;

public class ClaudeCliExplainerTests(ITestOutputHelper output)
{
    private static string TempCache() => Path.Combine(Path.GetTempPath(), "disklore-expl-" + Guid.NewGuid().ToString("N") + ".json");

    private static (ScanNode, AiRequestItem) Item(string name)
    {
        var root = new ScanNode(Path.GetFullPath("scanroot"), null, true);
        var d = new ScanNode(name, root, true) { Size = 2L << 30 };
        root.AddChild(d);
        return (d, new AiRequestItem("scanroot\\" + name, d.Size, 10, [], [], [], null, null));
    }

    [Fact]
    public void Prompt_contains_items_schema_and_instruction()
    {
        var (_, item) = Item("Foo");
        var prompt = ClaudeCliExplainer.BuildPrompt([item]);
        Assert.Contains("\"path\":\"scanroot\\\\Foo\"", prompt);
        Assert.Contains("Reply with only the JSON array.", prompt);
        Assert.Contains("app|check|safe|leftover|user|unknown", prompt);
    }

    [Fact]
    public void Arguments_use_print_mode_without_tools()
    {
        var args = ClaudeCliExplainer.BuildArguments("sonnet");
        Assert.Contains("-p", args);
        Assert.Contains("json", args);
        Assert.Contains("--tools", args);
        Assert.Contains("--no-session-persistence", args);
    }

    [Fact]
    public async Task Batches_of_five_and_one_failing_batch_does_not_stop_the_rest()
    {
        var cachePath = TempCache();
        var cache = new AiCache(cachePath);
        var calls = 0;
        var explainer = new ClaudeCliExplainer(cache, (prompt, ct) =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("boom");
            var json = "[" + string.Join(",", Enumerable.Range(0, 5).Select(_ => "{\"badge\":\"check\",\"summary\":\"s\",\"confidence\":0.5}")) + "]";
            return Task.FromResult(json);
        });

        var items = Enumerable.Range(0, 12).Select(i => Item("F" + i)).ToList();
        var result = await explainer.ExplainAsync(items, CancellationToken.None);

        Assert.Equal(3, calls);
        Assert.Equal(7, result.Count);   // batch 1 failed (5), batch 2 answered 5, batch 3 had 2 items and 5 answers by order -> 2
        Assert.Equal(7, cache.Count);
        File.Delete(cachePath);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var explainer = new ClaudeCliExplainer(new AiCache(TempCache()), (p, ct) => throw new OperationCanceledException(ct));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => explainer.ExplainAsync([Item("A")], cts.Token));
    }

    // Uses the user's Claude subscription: only runs with DISKLORE_MANUAL=1.
    [Fact]
    [Trait("Category", "Manual")]
    public async Task Manual_real_call_explains_a_fake_item()
    {
        if (Environment.GetEnvironmentVariable("DISKLORE_MANUAL") != "1") return;

        var cachePath = TempCache();
        var cache = new AiCache(cachePath);
        var explainer = new ClaudeCliExplainer(cache);
        Assert.True(explainer.IsAvailable);

        var temp = Path.GetTempPath().TrimEnd('\\', '/');
        var root = new ScanNode(Path.GetPathRoot(temp)!, null, true);
        var node = new ScanNode("Temp", root, true) { Size = 3_500_000_000 };
        root.AddChild(node);
        var item = new AiRequestItem(
            AiCoordinator.ReplaceProfile(temp), node.Size, 4200,
            [new("Temp", 2_000_000_000), new("pip", 900_000_000), new("chrome_installer", 600_000_000)],
            [new(".tmp", 1_800_000_000), new(".log", 700_000_000), new(".whl", 500_000_000)],
            ["tmp8f3a.tmp", "pip-unpack-1x2", "setup.log", "chrome_installer.exe", "numba_cache"],
            null, null);

        var sw = Stopwatch.StartNew();
        var result = await explainer.ExplainAsync([(node, item)], CancellationToken.None);
        sw.Stop();

        output.WriteLine($"elapsed {sw.Elapsed.TotalSeconds:0.0} s");
        foreach (var (k, e) in result)
            output.WriteLine($"{k}: [{e.Badge}] {e.Title} | {e.Summary} | conf {e.Confidence} | why: {e.WhyBig} | if deleted: {e.IfDeleted}");
        Assert.Single(result);
        File.Delete(cachePath);
    }
}
