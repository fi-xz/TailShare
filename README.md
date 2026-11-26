# TailShare

Adds a **Tailscale** target to the Windows 11 share sheet. Picking it lists your Taildrop
devices (`tailscale file cp --targets`) and sends the shared files with
`tailscale file cp <files...> <peer>:`. Results show as toasts. Folders are rejected
(Taildrop sends files only).

It also adds **Send with Tailscale** to the Windows 11 top-level File Explorer context menu
(shown when the selection contains at least one file). That's an `IExplorerCommand` served
out-of-proc by `TailShare.exe -Embedding` (`com:ExeServer`), which launches
`TailShare.exe <paths...>` on click, or `--paths-file <list>` for long selections.

WinUI 3 (.NET 10, self-contained incl. Windows App SDK WinUI) + a sparse package for identity;
the exe lives in `C:\Tools\TailShare`.

## Setup

Requires the .NET 10 SDK.

```powershell
# once, elevated, same Windows account: self-signed cert CN=nx, trusted in LocalMachine\TrustedPeople
.\scripts\cert.ps1

.\scripts\build.ps1      # publish -> C:\Tools\TailShare, makeappx /nv, signtool -> out\TailShare.msix
.\scripts\register.ps1   # Add-AppxPackage -ExternalLocation C:\Tools\TailShare
```

Remove: `.\scripts\unregister.ps1 [-RemoveFiles]`

Code-only changes need just `build.ps1` (it re-mirrors the exe); re-run `register.ps1` when
`package/` changes.

Icons follow the light/dark theme. Source art is in `art/` (`icon.png` plated tile,
`icon-light.png` / `icon-dark.png` glyphs for light / dark theme); `scripts\make-icons.ps1`
regenerates `package/Assets` (targetsize + `altform-unplated` / `altform-lightunplated` logos,
`TailShare-light.ico` / `TailShare-dark.ico`), and `build.ps1` indexes them into a
`resources.pri` with makepri, placed both in the package and next to the exe — the shell resolves
the logos of a package with external location against `C:\Tools\TailShare`, so without the copy
there it only finds the plain `Square44x44Logo.png`. (The app's own XAML uses `TailShare.pri`.) Title bar and context-menu icons pick an `.ico` from the app theme
at runtime.

## Debugging

- Log: `%TEMP%\TailShare\TailShare.log` (activation kind, file counts, errors, toasts).
- `C:\Tools\TailShare\TailShare.exe <file|folder>...` runs the same picker/send flow without the
  share sheet (still with package identity, so toasts work).

## Layout

| Path | |
|---|---|
| `package/AppxManifest.xml` | sparse package manifest (share target, runFullTrust, unvirtualizedResources) |
| `src/TailShare/app.manifest` | `<msix>` element binding the exe to the package identity |
| `src/TailShare/Program.cs` | reads `AppInstance.GetActivatedEventArgs()` before XAML starts |
| `src/TailShare/ExplorerCommand.cs` | context-menu `IExplorerCommand` + COM class factory (`-Embedding`, idles out after 2 min) |
| `src/TailShare/ShareFlow.cs` | items → paths → `ReportDataRetrieved` → pick → send → toast → `ReportCompleted` |
| `src/TailShare/Tailscale.cs` | CLI wrapper and `--targets` parser |
| `src/TailShare/PeerPickerWindow.xaml` | device picker / sending progress |
