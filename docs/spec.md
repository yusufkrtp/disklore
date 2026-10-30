# DiskLore â€” a disk usage explorer that explains what it finds

> Implementation details (project layout, interfaces, UI framework) are defined in [architecture.md](architecture.md), which wins where the two differ.

## Goal

A native Windows disk usage tool with TreeSize-style basics (scan a drive or
folder, size-sorted tree, percentages) whose main feature is explanation: every
file and folder gets a badge and a plain-English note. The note says what the
item is, which program owns it, why it is this big, whether Windows needs it,
what happens if you delete it, and the safer way to shrink it. The aim is to
let the owner decide with confidence what to delete on a 930 GB C: drive
that has 79 GB free.

## Context

- A typical case: a nearly full system drive (hundreds of GB used, little free)
  full of folders with names that say nothing about what they are or whether
  they can go.
- The `claude` CLI (Claude Code) may be installed and logged in; when it is,
  DiskLore can use it for folders its rules don't know. It is optional.
- Developer machines are full of **junctions** and symlinks (git worktrees,
  package managers, OneDrive), so the scanner and the delete code must never
  follow name-surrogate reparse points.

## Decisions

| Topic | Decision | Why |
|---|---|---|
| Explanation source | Built-in knowledge base (rules) first; Claude CLI for unknowns | Instant, offline and reliable for known folders; AI only fills gaps |
| Claude trigger | Automatic for unknown items >= 1 GB after the scan, batched, cached; "Ask Claude" button for the rest | Big mystery folders are exactly the use case |
| Form | Native desktop app, .NET 10 + Avalonia (changed from WPF on 30 Oct) | TreeSize feel, fast, admin elevation, shell integration; Avalonia also builds for Linux and macOS at almost no extra cost |
| Platforms | Windows first; Linux and macOS run with scanning, rules, Claude, views and delete; registry/Restart Manager/Explorer features are Windows only | User asked for Linux/macOS if it is not a big effort |
| Distribution | Git repo with real commits; ready to publish on GitHub: README, LICENSE (MIT), SECURITY.md, privacy note on what goes to Claude, CI that builds win-x64, linux-x64, osx-x64 and osx-arm64 artifacts on a tag | User wants it publishable and usable on other computers |
| Scan | Parallel managed enumeration (no MFT in v1) | Works on every file system; MFT is a later upgrade |
| Layout | Badge + one-line note in every row; full explanation in a right-side detail panel | Scan the list at a glance, details one click away |
| Delete | Full: Recycle Bin **and** permanent delete. No hard lock on critical items | User wants full control |
| Critical delete guard | Red dialog showing consequences + safer alternative; the user must type the item's name | Blocks misclicks, still allows deliberate deletes |
| UI language | English | |
| Theme | Light + Dark, picked automatically from Windows at first launch; extra color themes selectable in Settings | |
| Extra views (v1) | Largest files, file type breakdown, treemap, scan-to-scan diff | All four chosen |
| Smart detection (v1) | Uninstalled-app leftovers, link to installed program, in-use check, last-modified age | All four chosen |
| Startup | Drive picker cards; Explorer right-click "Open in DiskLore" (toggle in Settings) | |
| Name | DiskLore | |

## Badges (safety levels)

| Badge | Meaning | Example |
|---|---|---|
| CRITICAL | Windows or boot breaks if removed | `Windows\System32`, `Program Files\WindowsApps` |
| SHRINK | Needed, but can be shrunk through an official method | `WinSxS` (DISM), `hiberfil.sys` (powercfg), `pagefile.sys` |
| APP | Belongs to an installed program; remove it through the uninstaller | `Program Files\Unity` |
| CHECK | Mixed contents; look inside before deleting | `ProgramData`, `AppData\Roaming` |
| SAFE | Cache, temp files or regenerable data | `npm-cache`, `Temp`, Unity `Library`, `.godot` |
| LEFTOVER | Program no longer installed; leftover data | `AppData\Local\OldVendor` |
| USER | Your own files; DiskLore cannot judge them | `Videos`, `Documents` |
| UNKNOWN | No rule matched yet; Claude is asked or can be asked | |

AI-written explanations always carry a small "AI" tag plus a confidence value, so
they are never confused with curated rules.

## Explanation model

```text
Explanation
  Title            "Visual Studio installer cache"
  Badge            SAFE | SHRINK | ...
  Owner            "Microsoft Visual Studio" (or installed-program match)
  Summary          one line (row text)
  WhatItIs         paragraph
  WhyBig           paragraph; the app appends live facts (top children, top extensions, file count)
  IfDeleted        what breaks or regenerates
  SaferAlternative text + optional action {kind: command|settingsUri|url, value}
  Source           Rule | AI | InstalledProgram | Heuristic
  Confidence       0..1 (AI/heuristic only)
```

**Rule format.** Rules are JSON files in `Knowledge/` and are embedded in the
app. A user override file `%LOCALAPPDATA%\DiskLore\rules.user.json` takes
precedence. Each rule has:

- `id`
- `match`: one of `path` (supports env vars such as `%WINDIR%`,
  `%LOCALAPPDATA%` and `%USERPROFILE%`, plus `*`/`**` globs), `name`
  (folder or file name anywhere, optionally with `parentName` / `requiresSibling`,
  e.g. `Library` is only Unity's when `ProjectSettings` is next to it) or
  `extension`
- `kind`: `folder`, `file` or `any`
- the explanation fields from the model above
- `priority`

Specific path rules beat generic name rules.

**Seed knowledge base.** Target 250+ rules, written by hand with care:

- **Windows:** `WinSxS` (including the note that hard links inflate its
  reported size and that `DISM /AnalyzeComponentStore` gives the real figure),
  `System32`, `SysWOW64`, `Installer` (msi/msp repair cache: never delete by
  hand), `SoftwareDistribution`, `Temp`, `Prefetch`, `Logs`, `LiveKernelReports`,
  `Minidump`/`MEMORY.DMP`, `DriverStore`, `assembly`, `Microsoft.NET`,
  `servicing`, `Fonts`, `Panther`, `Windows.old`, `$Recycle.Bin`,
  `System Volume Information` (restore points, vssadmin), `Recovery`,
  `$WinREAgent`, `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`, `DumpStack.log`,
  `ProgramData\Microsoft\Windows\WER`, Delivery Optimization cache, Windows
  Search index (`Windows.edb`), Defender definitions.
- **App caches and tooling:** npm/yarn/pnpm caches, pip and uv caches,
  `.nuget\packages`, Gradle, Maven `.m2`, Cargo, Go module cache, the
  HuggingFace cache, Ollama models, the PyTorch hub, conda pkgs, VS Package
  Cache, the VS Code extensions/cache, JetBrains caches, Android SDK/AVD
  images, WSL `ext4.vhdx`, Docker `docker_data.vhdx`, Chrome/Edge/Firefox/Zen
  profile caches, Discord, Spotify, Teams, Steam (`steamapps`,
  `shadercache`), Epic, NVIDIA (`DXCache`, `GLCache`, installer leftovers),
  AMD, Adobe caches, OneDrive.
- **Projects:** Unity (`Library`, `Temp`, `Logs`, `obj` with
  `ProjectSettings` sibling; the Unity Hub editors folder), Godot (`.godot`,
  `.import`), `node_modules`, `.venv`/`venv`, `__pycache__`, `bin`/`obj` with
  a `.csproj` sibling, `build`/`dist`, `.git` (explain packs), Blender
  autosave/cache, Python `site-packages`.

**Live "why big" facts.** These are computed for every folder:

- the top 5 children by size
- the top 5 extensions by size
- the file count
- the largest single file
- the newest and oldest modification time

The panel shows them under the rule text, so even an UNKNOWN folder says
"40 GB of it is 3 .vhdx files".

## Claude explainer

- **Candidates:** UNKNOWN items >= 1 GB after a scan finishes, sent in batches
  of up to 5 per `claude -p` call. A scan makes at most 8 calls; the remaining
  items wait for the button. Any UNKNOWN row can also be asked with "Ask Claude".
- **Prompt input:**
  - the path with the user profile replaced by `%USERPROFILE%`
  - the size and file count
  - the top children
  - the top extensions
  - up to 30 sample file names
  - the installed-program match, if any
  - the parent's explanation

  File contents are never sent.
- **Call:** `claude -p --output-format json` with a strict JSON schema instruction.
  The answer is parsed and validated; a failed parse produces "AI answer
  unavailable" instead of a crash.
- **Badge limits:** the AI may only assign SAFE, CHECK, LEFTOVER, APP, USER or
  UNKNOWN. It can never claim CRITICALâ†’SAFE for a path under `%WINDIR%`; rules
  always win.
- **Cache:** `%LOCALAPPDATA%\DiskLore\ai-cache.json`, keyed by the normalized
  path. It can be cleared in Settings, and there is a per-row "Re-ask".
- **Settings:** the threshold (default 1 GB), auto on/off and the model
  (default `sonnet`).

## Smart detection

- **Installed programs:** the registry Uninstall keys (HKLM, HKLM
  WOW6432Node, HKCU) give `DisplayName`, `Publisher`, `InstallLocation`,
  `DisplayVersion` and `UninstallString`. Store apps come from
  `PackageManager` (WinRT; the target framework is
  `net10.0-windows10.0.19041.0`). A folder under an `InstallLocation` gets
  the APP badge and an "Uninstall via Settings" action (`ms-settings:appsfeatures`).
- **Leftovers:** a direct child of `AppData\Local`, `AppData\Roaming`,
  `ProgramData` or `Program Files*` that matches no installed program
  (fuzzy-matched on name, publisher and install location) and no rule gets
  "Possible leftover" (LEFTOVER, heuristic confidence). It never gets
  auto-SAFE.
- **In use:** before any delete, and on demand from the panel, the Restart
  Manager API (`RmStartSession`/`RmRegisterResources`/`RmGetList`) runs on the
  folder's files (the largest 2000 files) and lists the processes that hold
  them open.
- **Age:** the newest modification time in the subtree is shown as "last
  changed 2 years ago". Optional column; sortable.

## Scanner

- **Enumeration:** parallel managed enumeration (`FileSystemEnumerable` /
  `FindFirstFileEx` with large fetch) on a bounded work queue, using all cores.
- **Reparse points:** junctions and symlinks are never followed. They are shown
  as zero-size rows labeled with their target.
- **Access denied:** folders are counted and listed ("312 folders not readable:
  Restart as administrator").
- **Sizes:** logical size in v1. Allocated size and hard-link
  de-duplication are deferred; the WinSxS rule explains the inflation.
- **Live view:** progress numbers (files, folders, bytes, current path) while
  scanning; the tree appears when the scan finishes. The scan can be
  cancelled.
- **Memory:** a compact node model (one string per name, parent index, size,
  counts) so a full C: drive with about 2M entries stays well under 1 GB of RAM.

## Snapshots and diff

- A finished scan can be saved to
  `%LOCALAPPDATA%\DiskLore\snapshots\<drive>-<yyyyMMdd-HHmm>.dlsnap`: a compact
  binary of paths and sizes, gzip-compressed, folders only plus files >= 50 MB.
- The diff view picks an older snapshot and shows, per folder, the change in
  size ("+18.4 GB since 12 Oct"), sorted by growth. Unknown growers can go to
  Claude the same way.
- Saving happens automatically after each full-drive scan. The last 10
  snapshots per drive are kept.

## UI

The main window is laid out top to bottom:

1. Toolbar: drive picker, scan or rescan, "Restart as admin", search, view tabs.
2. Main area, split in two:
   - Left: a virtualized tree-grid (a flattened list with expand/collapse,
     for performance) with the columns Name, Size, % bar, Badge,
     Summary and Last changed (optional).
   - Right: the detail panel. It shows the title, badge, owner and
     the What / Why big (with live facts) / If deleted / Safer
     alternative sections, with action buttons: run the safer
     alternative, Open in Explorer, Ask/Re-ask Claude, Check in-use,
     Delete.
3. Status bar: scanned size, item count, elapsed time, unreadable count.

Other UI details:

- **View tabs:** Tree, Largest files (top 100 with badges), File types (by
  extension, for the selected folder), Treemap (squarified, colored by badge
  or by file type, click to drill down) and Changes (snapshot diff).
- **Startup screen:** drive cards with used/free bars plus "Scan a folderâ€¦".
  A command-line path argument (from the Explorer menu) skips this screen.
- **Context menu:** Open in Explorer, Copy path, Recycle, Delete
  permanently, Ask Claude, Check in-use, Properties.
- **Themes:** Light, Dark and extra color themes (e.g. Midnight, Forest,
  Ember, Rose, Ocean, High Contrast), each a ResourceDictionary swapped at
  runtime. The first launch follows the Windows `AppsUseLightTheme` setting;
  after that the saved choice wins.
- **Explorer integration:** HKCU `Directory\shell\DiskLore` and
  `Drive\shell\DiskLore` run `"<exe>" "%V"`. A Settings toggle adds or
  removes them; no admin is needed. On Windows 11 the entry appears under
  "Show more options" (the modern top-level menu needs a packaged app; out
  of scope).

## Delete flow

1. Run the in-use check. If files are locked, show the list of processes
   with "Delete anyway" or Cancel.
2. **Recycle:** send the item to the Recycle Bin through the shell API
   (`FileSystem.DeleteDirectory`/`DeleteFile` with `SendToRecycleBin`). If the
   item is too big for the bin, Windows' own prompt appears.
3. **Permanent:** a confirm dialog. CRITICAL and SHRINK items get the red
   dialog showing If deleted plus the Safer alternative and a type-the-name box.
4. Afterwards, update the sizes up the tree without a rescan and log the action
   to `%LOCALAPPDATA%\DiskLore\deletions.log`.

## Out of scope (v1)

- MFT reading
- allocated size and hard-link de-duplication
- duplicate-file finder
- network drives tuned for speed
- scheduled scans
- the modern Windows 11 top-level context menu
- localization
- an installer: v1 is a single-file publish into `DiskLore\dist`
- pushing to GitHub (asked separately)

## Implementation plan

**Solution layout** (`DiskLore.sln`):

- `src/DiskLore.Core`: the node model, scanner, aggregation, snapshot
  writer/reader, diff, the rule engine and rule loader, the Explanation model,
  installed-program detection, leftover heuristics, the Restart Manager wrapper,
  delete operations and the Claude explainer (process runner plus cache).
  No WPF.
- `src/DiskLore.Knowledge`: the rule JSON files, embedded as resources.
- `src/DiskLore.App`: WPF. Shell window, tree-grid, detail panel, views,
  treemap control, themes, settings, dialogs and the Explorer registration.
- `tests/DiskLore.Tests`: xUnit tests for the rule matching, aggregation,
  junction skipping (a temp junction fixture), snapshot round-trip/diff,
  Claude JSON parsing and validation (badge clamping), and leftover heuristics.

**Order** (orchestrated lanes after phase 0):

0. Phase 0 (orchestrator): install the .NET 10 SDK, create the solution skeleton,
   and freeze the Core interfaces (`IScanner`, `ScanNode`, `Explanation`,
   `IExplainer`, `IRuleSet`, `ISnapshotStore`) so the lanes can run in parallel.
1. Lane A, scanner + model + snapshots + diff (Core) and tests.
2. Lane B, knowledge base: 250+ rules in JSON, the rule engine and matching
   tests.
3. Lane C, WPF shell: startup screen, tree-grid, detail panel, themes,
   settings, status bar. Works against the frozen interfaces with fake data
   until A lands.
4. Lane D, system integration: installed programs, Store packages, leftover
   heuristic, Restart Manager, delete flow and dialogs, Explorer registration.
5. Lane E, the Claude explainer (CLI runner, batching, cache, validation) and
   the remaining views: Largest files, File types, Treemap, Changes.
6. Merge, real scan of C: and D:, screenshots in both themes, fixes.

## Acceptance criteria

- **Scan:** a full C: scan completes and the total is within 2% of the used
  space Windows reports (minus unreadable and hard-link effects, which are
  shown). Junctions are not double-counted (checked against a folder that contains a junction).
- **Explanations:** every top-level folder of C:, `C:\Windows`, `ProgramData`,
  `%LOCALAPPDATA%` and `%APPDATA%` shows a badge, and fewer than 10% of items
  >= 1 GB end up UNKNOWN after the rules run.
- **Claude:** an UNKNOWN folder >= 1 GB gets an AI explanation automatically,
  and a rescan uses the cache (no second CLI call).
- **Leftovers:** a leftover is detected in a test fixture (a fake
  `AppData\Local\FooVendor` with no installed match).
- **In use:** the in-use check names the process for a file held open by a
  test process.
- **Delete:** the recycle and permanent delete flows work on a temp folder,
  the critical dialog requires the typed name, and sizes update without a
  rescan.
- **Snapshots:** a snapshot is saved, and after a 1 GB dummy file is added
  under a folder, the diff shows +1 GB there.
- **Views and themes:** the treemap renders the C: scan, clicking drills
  down, and the Light, Dark and one extra theme are screenshotted.
- **Explorer menu:** "Open in DiskLore" launches with that folder.
- **Tests:** `dotnet test` is green.

## Open questions

- Exact extra theme palettes; pick tasteful ones and adjust after screenshots.
- Should AI explanations that the user marks "correct" be promoted into
  `rules.user.json` so they become permanent rules? (Cheap to add; leaning yes
  for v1.1.)

