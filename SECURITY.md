# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please use GitHub private vulnerability reporting: open the repository's
Security tab and choose "Report a vulnerability". Do not open a public issue for
a security problem. Include the DiskLore version, your OS and steps to
reproduce.

## Safety model

DiskLore can delete files, so its deletion code is built to be conservative:

- It never follows junctions or symlinks. A link is treated as a single item of
  size 0; deleting it removes the link, not the target.
- Critical items need a typed confirmation: you must type the item's name in a
  dialog that lists the consequences and the safer alternative.
- Before deleting, DiskLore checks for processes that hold files open (Windows)
  and tells you which ones.
- Commands that need administrator rights (such as DISM cleanup) are only run
  after DiskLore shows you the exact command, and through the normal elevation
  prompt (UAC). It never elevates silently.
- DiskLore makes no network connections of its own. The optional Claude
  explainer runs your own Claude Code CLI; see [docs/privacy.md](docs/privacy.md).
- AI explanations can never mark an item CRITICAL or SHRINK, and never SAFE
  under the Windows directory or Program Files.
- Every deletion made through DiskLore is appended to `deletions.log`.

The release binaries are not code-signed. Verify downloads against
`SHA256SUMS.txt` from the release page.
