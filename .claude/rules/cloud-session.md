# Cloud sessions

Applies only when `CLAUDE_CODE_REMOTE=true`. Ignore this file in local sessions.

- Reply to the user in Turkish. Everything that goes into git or GitHub is in English.
- Commits read as written by the repo owner: no AI attribution, no Co-Authored-By, no mention of Claude, agents, sessions or requests. Describe what changed and why.
- This is a Linux VM with only the GitHub copy of the repo. Gitignored files and everything on the Windows machine are absent, including its paths (`C:\Users\...`), `.exe` tools, the phone (adb), the Zen browser bridge, Blender and user-level skills.
- If a task needs something that is absent, say so plainly and do what remains. Do not fake results or skip verification silently.
- Keys come from environment variables. Never print, log or commit their values. If one is missing, name the variable and stop that step.
- Setup output is in `/tmp/cloud-setup.log`. Check it first when a tool seems missing.

## This project

- .NET 10 + Avalonia app. Setup installs the SDK to `~/.dotnet` when `dotnet` is missing (`global.json` wants 10.0.403, rolling forward to the latest feature band) and restores the Core and Tests projects only.
- Verify with `dotnet build DiskLore.sln -c Release` and `dotnet test DiskLore.sln -c Release` (this is what CI runs on Linux too). The full solution restore also pulls the Avalonia desktop project; it builds on Linux but the GUI cannot be launched in the VM (no display).
- `dotnet run --project src/DiskLore.App` needs a desktop session; do not try it here.
- The knowledge base (`src/DiskLore.Core/Knowledge/*.json`) is mostly Windows paths and rules; that is data, not an error on Linux.
- `scripts/publish.ps1` and Windows/macOS packaging are not testable in the VM; `scripts/` has the shell variant for Linux publishing.
- Do not add personal details to files in this public repository.
