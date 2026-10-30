# Contributing to DiskLore

## Knowledge-base rules

Most contributions are rules that explain a folder or file. Read
[docs/knowledge-base.md](docs/knowledge-base.md) first. If DiskLore gave a wrong
or missing explanation, you can also open an issue with the "Wrong or missing
explanation" template.

## Code

```
dotnet build DiskLore.sln
dotnet test
dotnet run --project src/DiskLore.App
```

- Use the .NET 10 SDK; plain `dotnet` on your PATH is enough.
- The layout and the contracts between the projects are in
  [docs/architecture.md](docs/architecture.md); product decisions are in
  [docs/spec.md](docs/spec.md). Change the document together with the code when a
  contract changes.
- `DiskLore.Core` has no UI dependency and stays cross-platform; Windows-only
  code sits behind `OperatingSystem.IsWindows()`.
- Match the surrounding style: nullable enabled, small focused classes,
  comments only where the reason is not obvious.
- Views use theme brush keys, never hard-coded colors.
- Code, comments, commit messages and documentation are written in English, and
  so is the UI.
- Keep pull requests focused, add or update tests for behavior changes, and make
  sure `dotnet test` passes on your platform.

## Security

Report vulnerabilities privately; see [SECURITY.md](SECURITY.md).

## License

By contributing you agree that your contribution is licensed under the
[MIT License](LICENSE).
