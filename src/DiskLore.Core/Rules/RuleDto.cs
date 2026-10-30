namespace DiskLore.Core.Rules;

public sealed class RuleDto
{
    public string Id { get; set; } = "";
    public RuleMatchDto Match { get; set; } = new();
    public string? Kind { get; set; }                  // "folder" | "file"
    public List<string>? Platforms { get; set; }       // "windows" | "linux" | "macos"; null = all
    public bool Inherit { get; set; }
    public int Priority { get; set; }
    public string Title { get; set; } = "";
    public string Badge { get; set; } = "unknown";
    public string? Owner { get; set; }
    public string Summary { get; set; } = "";
    public string WhatItIs { get; set; } = "";
    public string WhyBig { get; set; } = "";
    public string IfDeleted { get; set; } = "";
    public string? SaferAlternative { get; set; }
    public RuleActionDto? Action { get; set; }
}

public sealed class RuleMatchDto
{
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string? Extension { get; set; }
    public string? Sibling { get; set; }
    public string? ParentName { get; set; }
    public string? Under { get; set; }
    public long? MinSize { get; set; }
}

public sealed class RuleActionDto
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class RuleFileDto
{
    public List<RuleDto> Rules { get; set; } = new();
}
