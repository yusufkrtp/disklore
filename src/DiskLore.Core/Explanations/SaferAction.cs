namespace DiskLore.Core.Explanations;

public sealed record SaferAction(
    string Kind,      // "command" | "settingsUri" | "url" | "openPath"
    string Label,     // button text, e.g. "Clean up component store"
    string Value);    // the command line, URI, URL or path
