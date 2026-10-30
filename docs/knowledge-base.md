# Knowledge base

The knowledge base is a set of JSON files in `src/DiskLore.Core/Knowledge/`.
They are embedded in the app at build time and loaded at startup. A user file,
`rules.user.json` in the app data directory (see [privacy.md](privacy.md)), has
the same schema and wins over the embedded rules on equal ids.

## File layout

One file per area, for example `windows.json`, `dev-tools.json`, `browsers.json`,
`games.json`, `linux.json`, `macos.json`. Each file looks like this:

```json
{
  "rules": [
    {
      "id": "dev.npm-cache",
      "match": { "path": "%LOCALAPPDATA%\\npm-cache" },
      "kind": "folder",
      "platforms": ["windows"],
      "priority": 100,
      "title": "npm cache",
      "badge": "safe",
      "owner": "npm",
      "summary": "Downloaded packages; npm fetches them again when needed",
      "whatItIs": "...",
      "whyBig": "...",
      "ifDeleted": "...",
      "saferAlternative": "Run `npm cache clean --force`.",
      "action": { "kind": "command", "label": "Clean npm cache", "value": "npm cache clean --force" }
    }
  ]
}
```

The full schema (match keys, qualifiers, precedence, path tokens and globs,
`inherit`, `platforms`, `action` kinds) is defined in
[architecture.md](architecture.md#rules). In short:

- `id` is unique, lower-case and dotted: `<area>.<name>`.
- `match` has exactly one of `path`, `name` or `extension`, plus optional
  `sibling`, `parentName`, `under` and `minSize`.
- `badge` is one of `critical`, `shrink`, `app`, `check`, `safe`, `leftover`,
  `user`, `unknown`.
- Text fields are plain sentences, no markdown.
- An `extension` rule knows only the file type: if the file sits in an installed
  program or under a more cautious inherited rule, that verdict wins, so a type
  rule can raise caution but never lower it.

## Writing good rules

- **Be conservative.** When in doubt, pick the more cautious badge. A wrong SAFE
  can cost someone data; a wrong CHECK only costs a click.
- **SAFE only when harmless.** Use it for caches, temp files and data the program
  recreates on its own. Say in `ifDeleted` what regenerates and what it costs
  (re-download, rebuild time, a new login).
- **Never guess paths.** Only add a path you have seen on a real system or in the
  vendor's documentation. Prefer a specific `path` rule over a loose `name` rule;
  use `name` with `sibling` or `parentName` when a generic name needs context.
- **Offer the official way to shrink** in `saferAlternative` and, where a
  command exists, in `action` (DISM, powercfg, `npm cache clean`, a settings
  URI). Never put a command in `action` that deletes files without asking.
- **Use SHRINK** for things the system needs but that have an official cleanup,
  and **CRITICAL** only when removal breaks the OS or boot.
- **Explain the size.** `whyBig` says why the folder grows; DiskLore adds the
  live facts (top children, extensions) under it.
- Set `platforms` when the path only exists on one OS.
- Link a source (vendor documentation) in the pull request description.

## Validation

`dotnet test` loads every embedded rule file and checks it: valid JSON, known
badge and kind values, unique ids, exactly one primary `match` key, non-empty
text fields and well-formed path patterns. Run it before opening a pull request.
