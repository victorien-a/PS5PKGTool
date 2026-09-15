# PS5 PKG Tool CLI

A cross-platform command line front end for the PS5 PKG Tool engine. It runs on
Linux, macOS and Windows, because it depends only on the portable projects
(`PS5PKGTool.Core`, `.Ffpfsc`, `.Ufs2`) and not on the WinForms UI.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build PS5PKGTool.Cli/PS5PKGTool.Cli.csproj
```

To produce a self-contained binary:

```bash
dotnet publish PS5PKGTool.Cli/PS5PKGTool.Cli.csproj \
  -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true
```

## Usage

```
ps5pkgtool <command> [options]
```

| Command | Description |
| --- | --- |
| `scan <path>...` | Scan folders for dumps, packages and images |
| `info <path>` | Show detailed metadata for a single item |
| `files <path>` | List the files inside a dump, package or image |
| `extract <path> <dir>` | Extract a package, image or dump into a directory |
| `convert <pkg> <out>` | Convert a Sony `.pkg` into an exfat, ffpkg or ffpfsc image |
| `split <pkg> <dir>` | Split a package into verifiable pieces plus a manifest |
| `merge <manifest> <out>` | Rebuild a package from split pieces |
| `validate <pkg>` | Run structural acceptance checks against a package |
| `build <dir> <out.pkg>` | Build a debug package from a loose dump |
| `version` | Show version and runtime information |

Supported inputs: loose dumps (`sce_sys/param.json`), Sony `.pkg`, `.ffpfsc`,
`.ffpkg` and `.exfat` images.

### Options

- `--recursive`, `-r` — recurse into subdirectories (`scan`)
- `--limit N` — show only the first N files (`files`)
- `--json` — emit JSON instead of a table (`scan`, `info`, `files`, `validate`)
- `--to <exfat|ffpkg|ffpfsc>` — conversion target (`convert`)
- `--overwrite` — replace an existing output (`convert`)
- `--piece-size SIZE` — piece size such as `4GB` or `700MB` (`split`)
- `--content-id ID` — override the content ID; read from `sce_sys/param.json` when omitted (`build`)
- `--passcode VALUE` — package passcode (`build`, `extract`)

### Examples

```bash
ps5pkgtool scan ~/PS5 --recursive
ps5pkgtool info ~/PS5/game.pkg
ps5pkgtool files ~/PS5/game.pkg --limit 20
ps5pkgtool extract ~/PS5/game.pkg ~/out
ps5pkgtool convert ~/PS5/game.pkg ~/out/game.exfat --to exfat
ps5pkgtool split ~/PS5/game.pkg ~/pieces --piece-size 4GB
ps5pkgtool merge ~/pieces/game.ps5split.json ~/PS5/game.pkg
ps5pkgtool validate ~/PS5/game.pkg
ps5pkgtool build ~/PS5/GameAlpha ~/out/game.pkg
```

`validate` exits non-zero if any check fails, so it can gate a script:

```bash
ps5pkgtool validate game.pkg || echo "not structurally ready"
```

`--json` writes machine readable output on stdout while progress goes to stderr,
so it pipes cleanly:

```bash
ps5pkgtool scan ~/PS5 -r --json | jq '.games[] | .titleId'
```

## Notes

- `extract` reads the engine's merged view of a container. For a Sony package
  that unions the inner PFS with the CNT container, so metadata such as
  `sce_sys/param.json` is included.
- Writing a UFS filesystem to a raw device (`UFS2Tool.DriveIO`) is Windows only
  and is not exposed by this CLI.
