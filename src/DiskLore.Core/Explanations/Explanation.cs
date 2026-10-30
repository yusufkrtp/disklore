namespace DiskLore.Core.Explanations;

public sealed record Explanation(
    string Title,
    Badge Badge,
    string? Owner,
    string Summary,             // one line for the tree row
    string WhatItIs,
    string WhyBig,              // static text; the UI adds SubtreeStats facts below it
    string IfDeleted,
    string? SaferAlternative,
    SaferAction? Action,
    ExplanationSource Source,
    double Confidence,          // 1.0 for rules
    string? RuleId);
