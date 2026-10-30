# DiskLore architecture

DiskLore is a disk usage explorer that explains what it finds. This document is
the contract between the parts of the code base. Change it together with the
code when a contract changes.

Product spec: [spec.md](spec.md).

## Stack

- .NET 10 (`net10.0`), C# latest, nullable enabled, warnings as errors off.
- UI: Avalonia (latest stable, Fluent theme) + CommunityToolkit.Mvvm.
  Windows is the primary platform; Linux and macOS build and run with the
  Windows-only features switched off (see [Platform](#platform)).
- Tests: xUnit.
- The .NET 10 SDK (see `global.json`).

## Layout

```
DiskLore.sln
src/DiskLore.Core/            no UI, cross-platform; Windows code behind OperatingSystem.IsWindows()
  Model/                      ScanNode, ScanResult, ScanProgress, SubtreeStats
  Scanning/                   Scanner, SubtreeAnalyzer
  Explanations/               Badge, Explanation, SaferAction, IExplainer, ExplanationService
  Rules/                      RuleSet, RuleLoader, PathPattern, rule DTOs
  Knowledge/*.json            embedded rule files (EmbeddedResource)
  Snapshots/                  SnapshotStore, Snapshot, SnapshotDiff
  Ai/                         AiExplainer, AiCoordinator, AiCache, AiRequestItem, AiResponseParser
  Ai/Providers/               IAiProvider, AiProviders, Claude Code / Codex / Gemini CLI, Anthropic, OpenAI-compatible
  Security/                   ISecretStore, SecretStore (DPAPI / Keychain / libsecret / 0600 file)
  Platform/                   IPlatformServices, PlatformServices.Create(), Windows/, Unix/
  Programs/                   InstalledProgram, IInstalledProgramProvider, ProgramMatcher, LeftoverHeuristic
src/DiskLore.App/             Avalonia app (exe name: DiskLore)
  App.axaml(.cs), Program.cs
  Services/IAppContext.cs, Services/DeleteFlow.cs, Services/SettingsStore.cs
  ViewModels/                 MainViewModel (implements IAppContext), TreeRowViewModel, ...
  Views/                      MainWindow, StartView, TreeView (tree-grid), DetailPanel
  Views/Tabs/                 LargestFilesView, FileTypesView, TreemapView, ChangesView
  Controls/TreemapControl.cs
  Dialogs/                    ConfirmDeleteDialog, InUseDialog, CriticalDeleteDialog
  Themes/                     Light.axaml, Dark.axaml, Midnight.axaml, Forest.axaml, Ember.axaml, Rose.axaml, Ocean.axaml, HighContrast.axaml
tests/DiskLore.Tests/
```

## Core contracts

All of these live in namespace `DiskLore.Core` (sub-namespaces follow the folder).

### Model

```csharp
public sealed class ScanNode
{
    public string Name { get; }                 // root node: the full root path
    public ScanNode? Parent { get; }
    public bool IsDirectory { get; }
    public bool IsReparsePoint { get; }         // junction/symlink: never followed, Size = 0
    public string? LinkTarget { get; }
    public long Size { get; }                   // logical bytes; directories: subtree sum
    public long FileCount { get; }              // directories: subtree file count; files: 1
    public long FolderCount { get; }            // directories: subtree folder count
    public DateTime LastWriteUtc { get; }       // directories: newest in subtree
    public bool AccessDenied { get; }           // directory could not be listed
    public IReadOnlyList<ScanNode> Children { get; }   // sorted by Size descending after scan
    public string FullPath { get; }             // built from parents on demand
    public string Extension { get; }            // lower-case with dot, "" for none/directories
    public int Depth { get; }
}
```

Mutators are `internal` (Core has `InternalsVisibleTo("DiskLore.Tests")`), plus
one public method used after deletes:

```csharp
public static class ScanTree
{
    // Detaches node from its parent and subtracts Size/FileCount/FolderCount up the chain.
    public static void Remove(ScanNode node);
}
```

```csharp
public sealed record ScanProgress(long FilesScanned, long FoldersScanned, long BytesScanned, string CurrentPath);

public sealed class ScanResult
{
    public ScanNode Root { get; }
    public DateTime StartedUtc { get; }
    public TimeSpan Elapsed { get; }
    public IReadOnlyList<string> UnreadablePaths { get; }
    public bool IsDriveRoot { get; }
}

public interface IScanner
{
    Task<ScanResult> ScanAsync(string rootPath, IProgress<ScanProgress>? progress, CancellationToken ct);
}
```

The tree is only exposed after the scan completes. The UI shows progress
numbers while scanning, not a partial tree.

```csharp
public sealed record NamedSize(string Name, long Size);

public sealed class SubtreeStats
{
    public IReadOnlyList<NamedSize> TopChildren { get; }     // up to 5, direct children
    public IReadOnlyList<NamedSize> TopExtensions { get; }   // up to 5, whole subtree, "(none)" for no extension
    public long FileCount { get; }
    public NamedSize? LargestFile { get; }                   // Name = path relative to node
    public DateTime? OldestWriteUtc { get; }
    public DateTime? NewestWriteUtc { get; }
}

public static class SubtreeAnalyzer
{
    public static SubtreeStats Analyze(ScanNode node);
    public static IReadOnlyList<NamedSize> ExtensionBreakdown(ScanNode node, int max = 50);
    public static IReadOnlyList<ScanNode> LargestFiles(ScanNode root, int count = 100);
}
```

### Explanations

```csharp
public enum Badge { Critical, Shrink, App, Check, Safe, Leftover, User, Unknown }
public enum ExplanationSource { Rule, InstalledProgram, Heuristic, AI, Inherited, None }

public sealed record SaferAction(
    string Kind,      // "command" | "settingsUri" | "url" | "openPath"
    string Label,     // button text, e.g. "Clean up component store"
    string Value);    // the command line, URI, URL or path

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

public interface IExplainer
{
    // Synchronous, cheap, no I/O beyond what the explainer cached at construction.
    Explanation? Explain(ScanNode node);
}

public sealed class ExplanationService
{
    // Order: RuleSet -> ProgramMatcher -> AiCache -> LeftoverHeuristic -> Inherited -> Unknown.
    public ExplanationService(RuleSet rules, ProgramMatcher programs, LeftoverHeuristic leftovers, AiCache aiCache);
    public Explanation Explain(ScanNode node);   // never null; Badge.Unknown + Source.None when nothing matches
    public void Invalidate(ScanNode node);       // drop memoized result (after AI answer arrives)
    public event Action<ScanNode>? ExplanationChanged;
}
```

**Inheritance.** If a rule has `inherit: true`, descendants with no explanation
of their own get `Source = Inherited`. They keep the parent's badge and a title
of the form `Part of <parent title>`. Results are memoized per node in a
`ConditionalWeakTable`.

### Rules

Rule files live in `src/DiskLore.Core/Knowledge/*.json`, are embedded as
resources, and are loaded by `RuleLoader.LoadEmbedded()`. A user override file
`<AppData>/rules.user.json` has the same schema, is loaded after them and wins
on equal ids.

```json
{
  "rules": [
    {
      "id": "win.winsxs",
      "match": { "path": "%WINDIR%\\WinSxS" },
      "kind": "folder",
      "platforms": ["windows"],
      "inherit": true,
      "priority": 100,
      "title": "Windows component store (WinSxS)",
      "badge": "shrink",
      "owner": "Windows",
      "summary": "Windows components and update backups; shrink with DISM, never delete",
      "whatItIs": "...",
      "whyBig": "...",
      "ifDeleted": "...",
      "saferAlternative": "...",
      "action": { "kind": "command", "label": "Clean up component store", "value": "Dism.exe /Online /Cleanup-Image /StartComponentCleanup" }
    }
  ]
}
```

`match` has exactly one primary key:

| Key | Meaning |
|---|---|
| `path` | A full path pattern. Tokens: `%WINDIR%` `%SYSTEMDRIVE%` `%PROGRAMFILES%` `%PROGRAMFILESX86%` `%PROGRAMDATA%` `%LOCALAPPDATA%` `%APPDATA%` `%USERPROFILE%` `%TEMP%` `%HOME%` (Unix home; also `~`). Globs: `*` = one segment, `**` = any number of segments. `%USERPROFILE%` also matches other profiles under the users folder (`C:\Users\*`, `/home/*`, `/Users/*`). |
| `name` | A file or folder name anywhere; may contain `*`. |
| `extension` | Files only, e.g. `.vhdx`. |

Optional qualifiers:

| Key | Meaning |
|---|---|
| `sibling` | A name that must exist next to the node, e.g. `Library` with sibling `ProjectSettings`. |
| `parentName` | Name of the direct parent. |
| `under` | A path pattern that must be an ancestor. |
| `minSize` | Bytes. |

Matching is case-insensitive on Windows and macOS, and case-sensitive on Linux.

**Precedence:** `path` > `name` with qualifiers > `name` > `extension`; then
higher `priority`; then the longer pattern. `platforms` defaults to all;
values are `windows`, `linux` and `macos`.

```csharp
public sealed class RuleSet : IExplainer
{
    public static RuleSet Load(string? userRulesPath);   // embedded + optional user file
    public int Count { get; }
    public Explanation? Explain(ScanNode node);
}
```

### Programs and leftovers

```csharp
public sealed record InstalledProgram(
    string DisplayName, string? Publisher, string? InstallLocation, string? Version,
    string? UninstallString, bool IsStoreApp, string? PackageFamilyName);

public interface IInstalledProgramProvider { IReadOnlyList<InstalledProgram> GetInstalled(); }

public sealed class ProgramMatcher : IExplainer        // folder at/under InstallLocation, or Store package folder -> Badge.App
{
    public ProgramMatcher(IReadOnlyList<InstalledProgram> programs);
    public InstalledProgram? FindOwner(ScanNode node);
    public Explanation? Explain(ScanNode node);
}

public sealed class LeftoverHeuristic : IExplainer
{
    // Direct children of AppData\Local, AppData\Roaming, ProgramData, Program Files, Program Files (x86)
    // that match no installed program (fuzzy name/publisher/location) -> Badge.Leftover, Source.Heuristic,
    // Confidence 0.4-0.7. AppData/ProgramData children that clearly match an installed program by name
    // -> Badge.App, Source.InstalledProgram, "Data of <program>". Windows only.
    public LeftoverHeuristic(IReadOnlyList<InstalledProgram> programs);
    public Explanation? Explain(ScanNode node);
}
```

**Windows provider:**

- The registry Uninstall keys: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`,
  the same key under `WOW6432Node`, and the same key under HKCU.
- Store apps: the folder names under `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages`.
  Do **not** use WinRT, so Core stays on `net10.0`.

The Unix providers return an empty list.

### Platform

```csharp
public sealed record DriveEntry(string RootPath, string Label, string Format, long TotalBytes, long FreeBytes);
public sealed record LockingProcess(int Pid, string Name, string? AppName);
public sealed record DeleteResult(bool Success, string? Error);

public interface IPlatformServices
{
    string AppDataDirectory { get; }                 // Windows: %LOCALAPPDATA%\DiskLore, Linux: ~/.local/share/DiskLore, macOS: ~/Library/Application Support/DiskLore
    IReadOnlyList<DriveEntry> GetDrives();
    IInstalledProgramProvider Programs { get; }
    bool SupportsLockDetection { get; }
    IReadOnlyList<LockingProcess> FindLockingProcesses(IReadOnlyList<string> filePaths);   // Restart Manager on Windows
    DeleteResult MoveToTrash(string path);
    DeleteResult DeletePermanently(string path);
    void RevealInFileManager(string path);
    void OpenUri(string uri);
    void RunCommandElevated(string commandLine);     // Windows: PowerShell -Verb RunAs -NoExit; Unix: open a terminal is not required, copy-to-clipboard is the fallback (return false via SupportsElevatedCommands)
    bool SupportsElevatedCommands { get; }
    bool IsElevated { get; }
    void RestartElevated(string? scanPath);          // Windows only
    bool SupportsShellIntegration { get; }
    bool IsShellIntegrationEnabled();
    void SetShellIntegration(bool enabled, string exePath);   // HKCU Directory\shell + Drive\shell "Open in DiskLore"
}

public static class PlatformServices { public static IPlatformServices Create(); }
```

### Snapshots

```csharp
public sealed record SnapshotInfo(string FilePath, string RootPath, DateTime TakenUtc, long TotalSize);

public sealed class Snapshot
{
    public string RootPath { get; }
    public DateTime TakenUtc { get; }
    public IReadOnlyDictionary<string, long> Sizes { get; }   // key: path relative to root, "" = root; folders + files >= 50 MB
}

public sealed class SnapshotStore
{
    public SnapshotStore(string directory);            // <AppData>/snapshots
    public SnapshotInfo Save(ScanResult result);       // keeps the newest 10 per root
    public IReadOnlyList<SnapshotInfo> List(string rootPath);   // newest first
    public Snapshot Load(SnapshotInfo info);
}

public sealed record DiffEntry(string RelativePath, long OldSize, long NewSize) { public long Delta => NewSize - OldSize; }

public static class SnapshotDiff
{
    // Folders only, |Delta| >= minDelta, sorted by Delta descending. New and removed folders included.
    public static IReadOnlyList<DiffEntry> Compare(Snapshot older, ScanNode currentRoot, long minDelta = 50L * 1024 * 1024);
}
```

The file format is `.dlsnap`: gzip of a small binary format (magic `DLSN`,
version 1, root path, ticks, count, then (relative path, size) pairs).

### AI explainer

```csharp
public sealed record AiRequestItem(
    string Path,                 // user profile replaced with %USERPROFILE% / ~
    long Size, long FileCount,
    IReadOnlyList<NamedSize> TopChildren,
    IReadOnlyList<NamedSize> TopExtensions,
    IReadOnlyList<string> SampleNames,   // up to 30 file/folder names, no contents
    string? InstalledProgramMatch,
    string? ParentTitle);

public sealed class AiCache
{
    public AiCache(string filePath);                    // <AppData>/ai-cache.json
    public Explanation? Get(string fullPath);           // IExplainer-style lookup by normalized path
    public void Set(string fullPath, Explanation e);
    public void Clear();
    public int Count { get; }
}

public sealed class AiExplainer
{
    public AiExplainer(AiCache cache, IAiProvider provider);
    public bool IsAvailable { get; }                    // provider.IsReady
    public string ProviderName { get; }                 // provider.Info.DisplayName
    // Batches of up to 5 items per provider call. Returns full path -> explanation. Writes every answer to the cache.
    public Task<IReadOnlyDictionary<string, Explanation>> ExplainAsync(IReadOnlyList<(ScanNode Node, AiRequestItem Item)> items, CancellationToken ct);
}
```

```csharp
public sealed class AiCoordinator
{
    public AiCoordinator(AiExplainer explainer, ExplanationService explanations);
    public bool IsAvailable { get; }
    // Unknown (Source.None) folders and files with Size >= thresholdBytes, largest first, at most maxItems.
    public IReadOnlyList<ScanNode> SelectCandidates(ScanNode root, long thresholdBytes, int maxItems);
    // Builds AiRequestItems, asks the provider in batches, calls ExplanationService.Invalidate on answered nodes.
    public Task<int> ExplainAsync(IReadOnlyList<ScanNode> nodes, IProgress<string>? status, CancellationToken ct);
}
```

The app runs `SelectCandidates(root, settings.AiThresholdBytes, settings.AiMaxCallsPerScan * 5)` then
`ExplainAsync` after each scan when auto mode is on, and `ExplainAsync([node])` for the "Ask AI" button.

`SizeFormat.Format(long)` / `SizeFormat.FormatDelta(long)` in `DiskLore.Core.Model` is the only size formatter; every view uses it.

### AI providers

The user picks one provider in Settings. Three kinds:

| Id | Name | Kind | How it connects |
|---|---|---|---|
| `claude-code` | Claude Code (Claude Pro/Max) | SubscriptionCli | `claude -p` (existing flags) |
| `codex-cli` | Codex CLI (ChatGPT Plus/Pro) | SubscriptionCli | `codex exec` non-interactive, read-only sandbox |
| `gemini-cli` | Gemini CLI (Google account) | SubscriptionCli | `gemini -p` / piped stdin, JSON output |
| `anthropic-api` | Anthropic API | ApiKey | `POST https://api.anthropic.com/v1/messages` |
| `openai-api` | OpenAI API | ApiKey | OpenAI-compatible, base `https://api.openai.com/v1` |
| `gemini-api` | Google Gemini API | ApiKey | OpenAI-compatible, base `https://generativelanguage.googleapis.com/v1beta/openai` |
| `openrouter-api` | OpenRouter | ApiKey | OpenAI-compatible, base `https://openrouter.ai/api/v1` |
| `ollama` | Ollama (local) | Local | OpenAI-compatible, base `http://localhost:11434/v1`, no key |
| `lmstudio` | LM Studio (local) | Local | OpenAI-compatible, base `http://localhost:1234/v1`, no key |
| `openai-compatible` | Custom (OpenAI-compatible) | ApiKey | user base URL, optional key |

```csharp
namespace DiskLore.Core.Ai.Providers;

public enum AiProviderKind { SubscriptionCli, ApiKey, Local }

public sealed record AiProviderInfo(
    string Id, string DisplayName, AiProviderKind Kind, string Description,
    string? DefaultModel,                       // null = the CLI's/server's own default, or the user must pick
    IReadOnlyList<string> SuggestedModels,
    bool NeedsApiKey, bool ApiKeyOptional, bool NeedsBaseUrl, string? DefaultBaseUrl,
    string? SetupUrl);                          // install / get-a-key page

public sealed record AiProviderSettings(string? Model, string? BaseUrl);

public sealed record AiTestResult(bool Ok, string Message, TimeSpan Elapsed);

public interface IAiProvider
{
    AiProviderInfo Info { get; }
    bool IsReady { get; }                       // CLI found / key stored / model + base URL set
    string StatusText { get; }                  // "Found: C:\...\claude.exe", "Not installed", "No API key saved", ...
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);   // raw model text
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);  // GET /models for HTTP providers; SuggestedModels for CLIs
    Task<AiTestResult> TestAsync(CancellationToken ct);                 // tiny prompt, reports latency or the error
}

public static class AiProviders
{
    public static IReadOnlyList<AiProviderInfo> All { get; }
    public static AiProviderInfo Get(string id);
    public static IAiProvider Create(string id, AiProviderSettings settings, ISecretStore secrets);
    public static string? DetectDefault();      // first installed CLI in order claude-code, codex-cli, gemini-cli; else null
}
```

```csharp
namespace DiskLore.Core.Security;

public interface ISecretStore
{
    string BackendName { get; }                 // "Windows DPAPI", "macOS Keychain", "libsecret", "File (0600)"
    bool IsSecure { get; }                      // false only for the 0600 file fallback
    string? Get(string name);                   // name: "apikey:<providerId>"
    void Set(string name, string value);
    void Remove(string name);
}

public static class SecretStore { public static ISecretStore Create(string appDataDirectory); }
```

API keys never go into `settings.json`, logs, exceptions or the AI cache.
Settings keeps only the provider id and per-provider `{ model, baseUrl }`.

**Safety clamp:** an AI explanation can never carry `Critical` or `Shrink`, and
an item under the Windows directory, Program Files or the system root files is
never given `Safe`; such answers are downgraded to `Check`. Every AI answer
has `Source = AI` and `Confidence` from the model, clamped to 0.1-0.9.

## App contracts

```csharp
namespace DiskLore.App.Services;

public interface IAppContext
{
    ScanResult? Current { get; }
    event EventHandler? CurrentChanged;              // new scan loaded or tree changed after delete
    ScanNode? Selected { get; set; }
    event EventHandler? SelectedChanged;
    ExplanationService Explanations { get; }
    IPlatformServices Platform { get; }
    SnapshotStore Snapshots { get; }
    void Navigate(ScanNode node);                    // select + expand path in the tree tab and switch to it
    Task<bool> DeleteAsync(ScanNode node, bool permanent);   // runs DeleteFlow, updates tree, raises CurrentChanged
    Task AskClaudeAsync(ScanNode node);              // manual "Ask Claude"
}
```

- `MainViewModel` implements `IAppContext`.
- Each tab view (`LargestFilesView`, `FileTypesView`, `TreemapView`,
  `ChangesView`) exposes `public IAppContext? AppContext { get; set; }` and
  builds its own view model from it. `MainWindow` sets the property.
- `DeleteFlow.RunAsync(Window owner, IAppContext ctx, ScanNode node, Explanation e, bool permanent)`
  runs the in-use check, then the right confirm dialog, then the platform delete,
  then `ScanTree.Remove`, then appends a line to `<AppData>/deletions.log`.
  It returns `true` when the item was deleted.
- `SettingsStore` saves to `<AppData>/settings.json`. It holds the theme id,
  the AI auto mode, the AI threshold bytes (default 1 GiB), the AI model,
  the AI max calls per scan (default 8), whether to show the Last changed
  column, and the shell integration state.

## Badge colors

Every theme defines brushes named `Badge.Critical`, `Badge.Shrink`,
`Badge.App`, `Badge.Check`, `Badge.Safe`, `Badge.Leftover`, `Badge.User` and
`Badge.Unknown`.

Themes also define these keys:

- `App.Background`, `App.Surface`, `App.SurfaceAlt`, `App.Border`
- `App.Text`, `App.TextMuted`, `App.Accent`, `App.Bar`, `App.BarTrack`

Views use only these keys, never hard-coded colors.

## Build and test

```
dotnet build DiskLore.sln -c Debug
dotnet test tests/DiskLore.Tests
dotnet run --project src/DiskLore.App -- "C:\some\folder"
```
