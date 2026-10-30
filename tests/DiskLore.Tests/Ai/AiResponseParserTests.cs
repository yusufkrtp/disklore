using DiskLore.Core.Ai;
using DiskLore.Core.Explanations;
using Xunit;

namespace DiskLore.Tests.Ai;

public class AiResponseParserTests
{
    private const string One = """
        {"path":"C:\\Data\\Cache","title":"Cache","badge":"safe","owner":"Foo","summary":"Rebuilt cache","whatItIs":"A cache.","whyBig":"Grows.","ifDeleted":"Rebuilt.","saferAlternative":null,"confidence":0.7}
        """;

    [Fact]
    public void Parses_fenced_json_inside_cli_envelope()
    {
        var inner = "```json\n[" + One + "]\n```";
        var envelope = System.Text.Json.JsonSerializer.Serialize(new { type = "result", result = inner });
        var r = AiResponseParser.Parse(envelope);
        var e = Assert.Single(r).Value;
        Assert.Equal(Badge.Safe, e.Badge);
        Assert.Equal(ExplanationSource.AI, e.Source);
        Assert.Equal("Foo", e.Owner);
        Assert.Null(e.Action);
        Assert.Equal(0.7, e.Confidence);
    }

    [Fact]
    public void Accepts_an_items_object_as_well_as_a_bare_array()
    {
        var r = AiResponseParser.Parse("{\"items\": [" + One + "]}");
        Assert.Equal(Badge.Safe, Assert.Single(r).Value.Badge);

        var fenced = AiResponseParser.Parse("Here:" + Environment.NewLine + "```json" + Environment.NewLine
            + "{ \"items\": [" + One + "] }" + Environment.NewLine + "```");
        Assert.Single(fenced);
    }

    [Fact]
    public void Tolerates_leading_prose_and_plain_text()
    {
        var r = AiResponseParser.Parse("Sure, here you go:\n[" + One + "]\nHope it helps.");
        Assert.Single(r);
    }

    [Fact]
    public void Garbage_is_dropped_not_thrown()
    {
        Assert.Empty(AiResponseParser.Parse("not json at all"));
        Assert.Empty(AiResponseParser.Parse(""));
        Assert.Empty(AiResponseParser.Parse("{\"result\":\"[ {broken\"}"));
    }

    [Fact]
    public void Matches_by_path_even_when_order_differs()
    {
        var a = One.Replace("C:\\\\Data\\\\Cache", "%USERPROFILE%\\\\A").Replace("Cache", "AAA");
        var b = One.Replace("C:\\\\Data\\\\Cache", "%USERPROFILE%\\\\B").Replace("Cache", "BBB");
        var expected = new List<(string, string)> { ("%USERPROFILE%\\A", @"C:\Users\x\A"), ("%USERPROFILE%\\B", @"C:\Users\x\B") };
        var r = AiResponseParser.Parse($"[{b},{a}]", expected);
        Assert.Equal("AAA", r[@"C:\Users\x\A"].Title);
        Assert.Equal("BBB", r[@"C:\Users\x\B"].Title);
    }

    [Fact]
    public void Falls_back_to_order_when_path_is_missing_or_wrong()
    {
        var noPath = """{"title":"First","badge":"check","summary":"s","confidence":0.5}""";
        var wrongPath = """{"path":"zzz","title":"Second","badge":"user","summary":"s","confidence":0.5}""";
        var expected = new List<(string, string)> { ("a", @"C:\a"), ("b", @"C:\b") };
        var r = AiResponseParser.Parse($"[{noPath},{wrongPath}]", expected);
        Assert.Equal("First", r[@"C:\a"].Title);
        Assert.Equal("Second", r[@"C:\b"].Title);
    }

    [Fact]
    public void Missing_optional_fields_get_defaults()
    {
        var r = AiResponseParser.Parse("""[{"path":"C:\\Foo\\Bar","badge":"user","summary":"Photos"}]""");
        var e = Assert.Single(r).Value;
        Assert.Equal("Bar", e.Title);
        Assert.Equal("", e.WhyBig);
        Assert.Null(e.SaferAlternative);
        Assert.InRange(e.Confidence, 0.1, 0.9);
    }

    [Fact]
    public void Invalid_badge_or_missing_summary_is_dropped()
    {
        Assert.Empty(AiResponseParser.Parse("""[{"path":"a","badge":"delete-me","summary":"x"}]"""));
        Assert.Empty(AiResponseParser.Parse("""[{"path":"a","badge":"safe"}]"""));
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("shrink")]
    public void Critical_and_shrink_become_check(string badge)
    {
        var r = AiResponseParser.Parse($$"""[{"path":"C:\\Data\\X","badge":"{{badge}}","summary":"s","confidence":0.5}]""");
        Assert.Equal(Badge.Check, Assert.Single(r).Value.Badge);
    }

    [Theory]
    [InlineData(@"C:\\Windows\\Temp\\foo")]
    [InlineData(@"C:\\Program Files\\Thing")]
    [InlineData(@"C:\\Program Files (x86)\\Thing")]
    [InlineData(@"C:\\pagefile.sys")]
    [InlineData(@"C:\\hiberfil.sys")]
    public void Safe_under_system_locations_becomes_check(string path)
    {
        var r = AiResponseParser.Parse($$"""[{"path":"{{path}}","badge":"safe","summary":"s","confidence":0.5}]""");
        Assert.Equal(Badge.Check, Assert.Single(r).Value.Badge);
    }

    [Fact]
    public void Safe_elsewhere_stays_safe_and_user_in_windows_is_untouched()
    {
        var safe = AiResponseParser.Parse("""[{"path":"C:\\Users\\x\\Cache","badge":"safe","summary":"s","confidence":0.5}]""");
        Assert.Equal(Badge.Safe, Assert.Single(safe).Value.Badge);
        var app = AiResponseParser.Parse("""[{"path":"C:\\Windows\\Foo","badge":"app","summary":"s","confidence":0.5}]""");
        Assert.Equal(Badge.App, Assert.Single(app).Value.Badge);
    }

    [Theory]
    [InlineData(5.0, 0.9)]
    [InlineData(0.0, 0.1)]
    [InlineData(-3.0, 0.1)]
    [InlineData(0.42, 0.42)]
    public void Confidence_is_clamped(double given, double expected)
    {
        var r = AiResponseParser.Parse($$"""[{"path":"C:\\Data\\X","badge":"check","summary":"s","confidence":{{given.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]""");
        Assert.Equal(expected, Assert.Single(r).Value.Confidence, 3);
    }

    [Fact]
    public void Summary_is_trimmed_to_90_chars()
    {
        var long_ = new string('x', 200);
        var r = AiResponseParser.Parse($$"""[{"path":"C:\\Data\\X","badge":"check","summary":"{{long_}}","confidence":0.5}]""");
        Assert.Equal(90, Assert.Single(r).Value.Summary.Length);
    }
}
