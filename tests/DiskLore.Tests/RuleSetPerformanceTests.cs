using System.Diagnostics;
using System.Text;
using DiskLore.Core.Model;
using DiskLore.Core.Rules;
using Xunit;
using Xunit.Abstractions;
using static DiskLore.Tests.TestTree;

namespace DiskLore.Tests;

public class RuleSetPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Explain_200kNodes_With300Rules_IsFast()
    {
        var abs = AbsRoot.Replace('\\', '/');
        var sb = new StringBuilder("{ \"rules\": [");
        int n = 0;
        void Add(string match, string extra = "")
        {
            if (n++ > 0) sb.Append(',');
            sb.Append($$"""{ "id": "r{{n}}", "match": { {{match}} }, "title": "t{{n}}", "badge": "check", "summary": "s" {{extra}} }""");
        }
        for (int i = 0; i < 100; i++) Add($"\"name\": \"literal{i}\"");
        for (int i = 0; i < 40; i++) Add($"\"name\": \"glob{i}*x\"");
        for (int i = 0; i < 40; i++) Add($"\"extension\": \".e{i}\"");
        for (int i = 0; i < 60; i++) Add($"\"path\": \"{abs}/d{i}/sub{i}\"");
        for (int i = 0; i < 20; i++) Add($"\"path\": \"{abs}/**/wild{i}*\"");
        for (int i = 0; i < 20; i++) Add($"\"name\": \"q{i}\", \"parentName\": \"d{i}\", \"sibling\": \"*.csproj\"");
        for (int i = 0; i < 20; i++) Add($"\"name\": \"under{i}\", \"under\": \"**/d{i}\", \"minSize\": 10");
        sb.Append("] }");

        var set = RuleSet.FromJson(sb.ToString());
        Assert.Equal(300, set.Count);
        Assert.Empty(set.Errors);

        var root = Root(AbsRoot);
        var all = new List<ScanNode>(210_000);
        for (int d = 0; d < 2000; d++)
        {
            var dir = Dir(root, "d" + (d % 100));
            all.Add(dir);
            var sub = Dir(dir, "sub" + (d % 70));
            all.Add(sub);
            for (int f = 0; f < 98; f++)
            {
                var name = f % 7 == 0 ? $"file{f}.e{f % 60}" : f % 11 == 0 ? $"literal{f % 120}" : f % 13 == 0 ? $"glob{f % 50}zzx" : $"file{f}.dat";
                all.Add(File(sub, name, f));
            }
        }
        Assert.True(all.Count >= 200_000);

        // Warm up JIT and the thread-static buffers.
        for (int i = 0; i < 1000; i++) set.Explain(all[i]);

        var sw = Stopwatch.StartNew();
        int hits = 0;
        foreach (var node in all) if (set.Explain(node) != null) hits++;
        sw.Stop();

        output.WriteLine($"{all.Count} nodes, {set.Count} rules: {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds * 1000 / all.Count:0.00} us/node), hits={hits}");
        Assert.True(hits > 0);
        // Shared CI runners are several times slower than a desktop; keep a generous ceiling there.
        var limit = TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("CI") is null ? 3 : 20);
        Assert.True(sw.Elapsed < limit, $"took {sw.Elapsed}");
    }
}
