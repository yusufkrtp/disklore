# Privacy

DiskLore has no telemetry, no analytics, no accounts and no update check. It
makes no network connections of its own. The only way data leaves your computer
is the optional Claude explainer, described below.

## What is stored locally

Everything lives in the per-user application data directory:

| OS | Directory |
|---|---|
| Windows | `%LOCALAPPDATA%\DiskLore` |
| Linux | `~/.local/share/DiskLore` |
| macOS | `~/Library/Application Support/DiskLore` |

| File | Content |
|---|---|
| `settings.json` | Theme, AI mode and limits, display options, Explorer integration state. |
| `ai-cache.json` | Explanations that Claude returned, keyed by path, so a folder is not asked twice. |
| `snapshots/` | Size snapshots (`.dlsnap`) of past scans, used for the "what grew" comparison. They hold relative paths and sizes of folders and of files of 50 MB or more, not file contents. The newest 10 per scanned root are kept. |
| `deletions.log` | One line per item you deleted through DiskLore. |
| `rules.user.json` | Your own knowledge-base rules, if you created the file. |

Delete the directory to remove all of it. Scan results themselves are held in
memory only.

## The Claude explainer

The explainer is optional and can be turned off in Settings. When it runs,
DiskLore starts your locally installed Claude Code CLI (`claude -p`), which sends
the request to Anthropic under your own Claude account and its terms. DiskLore
never sees your credentials.

For each item it asks about, DiskLore sends:

- the path, with your user profile folder replaced by `%USERPROFILE%` (or `~`)
- the total size and the file count
- the names and sizes of the top 5 child items
- the top 5 file extensions by size
- up to 30 file and folder names
- the name of the installed program that matches the folder, if any
- the title of the parent folder's explanation, if any

File contents are never read or sent. Names of files and folders can still be
personal, so turn the feature off if that matters for your drives.
