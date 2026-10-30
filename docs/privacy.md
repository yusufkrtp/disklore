# Privacy

DiskLore has no telemetry, no analytics, no accounts and no update check. It
makes no network connections of its own. The only way data leaves your computer
is the optional AI explainer, described below.

## What is stored locally

Everything lives in the per-user application data directory:

| OS | Directory |
|---|---|
| Windows | `%LOCALAPPDATA%\DiskLore` |
| Linux | `~/.local/share/DiskLore` |
| macOS | `~/Library/Application Support/DiskLore` |

| File | Content |
|---|---|
| `settings.json` | Theme, the chosen AI provider with its model and base URL, AI limits, display options, Explorer integration state. Never an API key. |
| `ai-cache.json` | Explanations that the AI returned, keyed by path, so a folder is not asked twice. |
| `secrets.json` | Windows only: your API keys, each encrypted with Windows DPAPI for your user account. On Linux without a keyring it holds the keys in plain text with permissions only you can read. macOS and Linux with libsecret keep keys in the system keychain instead and do not use this file. |
| `snapshots/` | Size snapshots (`.dlsnap`) of past scans, used for the "what grew" comparison. They hold relative paths and sizes of folders and of files of 50 MB or more, not file contents. The newest 10 per scanned root are kept. |
| `deletions.log` | One line per item you deleted through DiskLore. |
| `rules.user.json` | Your own knowledge-base rules, if you created the file. |

Delete the directory to remove all of it. Scan results themselves are held in
memory only.

## The AI explainer

The explainer is optional and can be turned off in Settings ("Off"). You choose
where the requests go, and the data listed below goes only to that one place:

- **A subscription CLI** (Claude Code, Codex CLI, Gemini CLI): DiskLore starts the
  official CLI that is installed on your computer. It sends the request to its
  vendor under your own account and terms. DiskLore never sees your credentials.
- **An API with your key** (Anthropic, OpenAI, Google Gemini, OpenRouter, or a
  custom OpenAI-compatible server): DiskLore sends the request straight to that
  service with the key you saved, under your account and terms.
- **A local model** (Ollama, LM Studio): the request goes to the server on your
  own computer (`localhost` by default) and nothing leaves it.

API keys are kept in the operating system's secret store (Windows DPAPI, macOS
Keychain, libsecret on Linux), never in `settings.json`, the AI cache, logs or
error messages. The only exception is Linux without a keyring, where they are
kept in `secrets.json` readable only by you; Settings shows which storage is in
use. Remove a key in Settings at any time.

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
