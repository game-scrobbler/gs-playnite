# Vendored Playnite Toolbox

`Toolbox.exe` from the Playnite 11 desktop build, checked in so CI can produce the `.pext2`
extension package.

| | |
|---|---|
| Version | `Toolbox 1.0.0+7ca94f37d35657a15c037d62df15b327a0417b88` |
| Taken from | Playnite **11.0.11.0** (devel channel) |
| SHA-256 | `d551a59aa3b122cc6b04874f643ba26bfbebcd68424e5caebcd4a3b405348d9f` |
| Size | 896,205 bytes |
| License | MIT — see `LICENSE.txt` (Playnite is MIT; this repo is GPL-3.0, which MIT is compatible with) |

## Why it is vendored

Playnite 11 ships on the devel channel and is **not** published as a GitHub release asset — every
`https://github.com/JosefNemec/Playnite/releases/download/<version>/Playnite<version>.zip` URL 404s
and the releases API stops at 10.x. The Playnite 10 pipeline downloaded Toolbox that way; on this
branch there is nothing to download, so the binary is committed instead.

## Why not just zip the output

A `.pext2` is a plain zip, but `Toolbox.exe pack` deliberately **omits the assemblies Playnite
itself ships** — `Playnite.SDK.dll`, `ByteAether.Ulid.dll`, `CommunityToolkit.Mvvm.dll`. A
`Compress-Archive` of `bin\Release\net10.0-windows` includes them, and an extension that carries
its own copy of the host's assemblies reintroduces exactly the version skew the plugin is trying to
avoid. Do not replace the pack step with a plain zip.

## Notes

- It is a framework-dependent single-file app: it needs a .NET 10 runtime on PATH, which
  `actions/setup-dotnet` with `10.0.x` provides. It needs no other file from the Playnite install.
- `pack`'s second argument is documented as a destination *directory* but is opened as a *file*.
  Passing a directory fails with `UnauthorizedAccessException`. Pass the full `.pext2` path.
- The same binary generates the Fluent localization classes:
  `Toolbox.exe ftlgen "Localization" "Localization"`.

## Updating

Replace `Toolbox.exe` from the matching Playnite build whenever the `Playnite.SDK` package
reference in `GsPlugin.csproj` moves to a different alpha, then refresh the version, hash and size
above. Keep the two in step — the packer and the SDK come from the same build.
