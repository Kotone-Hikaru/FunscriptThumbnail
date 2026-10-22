# Funscript Thumbnail for Windows Explorer

**English** · [简体中文](README.zh-CN.md)

A Windows Shell thumbnail provider (`IThumbnailProvider`) that renders an **eroscripts-style heatmap** for `.funscript` files — directly in the Explorer **Details pane header icon**, the **Large / Extra large icon** views, and **file tooltips**. No extra app, no preview pane required.

![Details pane](docs/screenshot-details-pane.png)

---

## Why this exists

`.funscript` files are plain JSON with no embedded thumbnail, so Explorer shows a blank generic icon. Existing solutions are *preview handlers*, which only paint the **Preview pane** — the Details pane icon box, the icon views and the tooltips stay empty.

This project is a **thumbnail provider** instead, so the heatmap shows up exactly where you look for it while browsing a folder. The rendering is a faithful port of the [eroscripts](https://eroscripts.com) renderer, including its colour mapping, statistics and layout.

---

## Features

| | |
|---|---|
| **eroscripts visual style** | Speed → colour via the same OKLCH mapping, the same gradient background, the same `Duration / Actions / Max Speed / Avg Speed` readout. |
| **Multi-axis scripts** | v1.0 `axes` and v2.x `channels` are rendered as one stacked row per axis (`L0 L1 L2 R0 R1 R2 …`), each with its own statistics — matching the eroscripts site layout. |
| **Dark theme by default** | Background calibrated pixel-for-pixel against eroscripts dark mode (`#1A1A1A` page, `79,79,79` title strip, `26,26,26` graph strip). |
| **Real Windows integration** | Registered machine-wide, one entry in **Settings → Apps**, uninstallable from there. |
| **Crash-safe** | Hosted in the Windows isolated thumbnail surrogate (`dllhost.exe`), not inside `explorer.exe`. A malformed script cannot take down your shell. |
| **Tolerant parser** | Handles the quirks found in real-world scripts: `metadata.duration` as float instead of int, string POS values, unsorted / duplicated actions, negative timestamps, missing metadata. |
| **No dependencies** | Pure .NET Framework 4.8 + GDI+. No NuGet packages at runtime. |

### Multi-axis

![Multi axis](docs/screenshot-multi-axis.png)

### Single axis

![Single axis](docs/screenshot-single-axis.png)

A light-background variant is also available (see [Command line](#command-line-tool)):

![Light theme](docs/screenshot-light-theme.png)

---

## Install

1. Download **[FunscriptThumbnail.zip](https://github.com/Kotone-Hikaru/FunscriptThumbnail/releases/latest)** (or use `dist/` from a clone) and extract it anywhere.
2. Double-click **`install.cmd`** — it elevates itself via UAC.
3. Done. Open a folder containing `.funscript` files.

Uninstalling does **not** need the `.cmd` files: **Settings → Apps → Installed apps → "Funscript 缩略图" → Uninstall**.

### Requirements

| | |
|---|---|
| OS | Windows 10 (1903+) or Windows 11, **64-bit** |
| Runtime | **.NET Framework 4.8** — bundled with Windows 10 1903+ and Windows 11, nothing to install |
| Privileges | Administrator, **only during install/uninstall** |

`install.cmd` verifies the .NET Framework version and the presence of the payload before touching anything, and reports a clear error instead of failing silently.

### What installation changes

- `C:\ProgramData\FunscriptThumbnail\bin-<timestamp>\` — the DLL, the tool, an icon, plus a self-contained uninstaller copy
- `HKLM\SOFTWARE\Classes\CLSID\{6E3A2F41-6B1D-4C3E-9F27-2B8D5A7C1E90}` — the COM class
- `HKLM\SOFTWARE\Classes\.funscript\ShellEx\{e357fccd-…}` and the same key under `SystemFileAssociations` — the thumbnail hook
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FunscriptThumbnail` — the entry in Settings → Apps
- `HKLM\SOFTWARE\Classes\.funscript` `TypeOverlay` / `Treatment` — suppresses the little shortcut arrow overlay and the frame around the thumbnail (only if you had no value of your own)

It deliberately **does not** touch your `.funscript` file association or default "open with" program.

---

## Build from source

```
git clone https://github.com/Kotone-Hikaru/FunscriptThumbnail.git
cd FunscriptThumbnail
build.cmd
```

`build.cmd` locates the .NET SDK, builds `FunscriptThumbnail.sln` in Release, and copies the binaries into `dist\`. Requires the **.NET SDK** (any version that supports `net48`; tested with 10.0.x) — the .NET Framework runtime alone is not enough to compile.

Or open `FunscriptThumbnail.sln` in Visual Studio 2019/2022.

### Layout

```
src/FunscriptThumbnail/     the COM thumbnail provider (the DLL Explorer loads)
  ThumbnailProvider.cs        IThumbnailProvider + IInitializeWithStream
  HeatmapRenderer.cs          GDI+ port of the eroscripts SVG renderer
  Stats.cs                    Duration / Actions / Max Speed / Avg Speed
  SpeedColor.cs               speed -> OKLCH -> sRGB, bit-identical to the site
  Funscript.cs                tolerant parser + axis normalisation
  MiniJson.cs                 dependency-free JSON reader (depth-capped)
  Diag.cs                     opt-in trace log
src/FunscriptThumbTool/     installer / uninstaller / diagnostic CLI
dist/                       prebuilt, ready-to-install payload
docs/                       screenshots and the visual overview page
```

---

## Command line tool

`FunscriptThumbTool.exe` is both the installer and a diagnostic utility:

```
status                                   registration state, DLL path, hosting mode
install [--in-process]                   register machine-wide (needs admin)
uninstall [--quiet]                      remove registration + files
render <in.funscript> <out.png> [size]   render with the exact code Explorer uses
shelltest <file> <out.png> [size]        ask the Windows Shell for the thumbnail
colors                                   print the speed -> colour table
```

`render … --light` produces the light-background variant. `shelltest` is the honest end-to-end check: it goes through `IShellItemImageFactory`, the same path Explorer takes.

---

## How it works

1. Explorer asks the Shell for a thumbnail of a `.funscript` file.
2. The Shell reads the `IThumbnailProvider` CLSID from `HKLM\…\.funscript\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}` and instantiates our class in the isolated surrogate `dllhost.exe`.
3. `IInitializeWithStream::Initialize` receives the file stream; the provider reads it whole, detects UTF-8/UTF-16 BOM, parses the JSON and normalises the actions.
4. `GetThumbnail(cx)` lays the script out at a logical width of 690 px — one row per axis, plus the statistics header — then scales so the longest side equals the requested `cx`, and returns an HBITMAP.
5. The Shell caches the result per file path in `thumbcache_*.db`.

Two implementation details worth knowing if you modify this:

- **`CompositingQuality.HighSpeed`, never `HighQuality`.** GDI+'s high-quality compositor blends alpha in *linear light* (gamma-corrected), which visibly washes out the 20 %-opacity heatmap. Browsers — and therefore eroscripts — blend in sRGB. `HighSpeed` matches the browser.
- **Semi-transparent planes are composited in floating point before drawing.** GDI+ quantises alpha to 8 bits, so `0.3 × 204 + 0.7 × 26` lands on 80 instead of 79. Pre-blending the plane onto the background reproduces the site's exact values.

---

## Supported input

| | |
|---|---|
| Format | `.funscript` (JSON), single-axis and multi-axis |
| Multi-axis | v1.0 `axes` array, v2.x `channels` map, `metadata.axes` |
| Axis name mapping | `stroke→L0`, `surge→L1`, `sway→L2`, `twist→R0`, `roll→R1`, `pitch→R2`, `suck→A1` |
| Chapters | `metadata.chapters` (rendered as a bar when present) |
| Robustness | float `metadata.duration`, string POS, unsorted or duplicated actions, negative times |

---

## Limitations

- **A single-axis script looks like a thin strip in a square icon box.** The layout is 690 × 52 (an eroscripts original proportion) and Windows letterboxes thumbnails instead of cropping them, so in a 256 × 256 box the strip fills only its middle. Multi-axis scripts (roughly square) fill the box nicely. This is the direct consequence of matching the site's aspect ratio.
- Explorer caches thumbnails **by file path**. Files you viewed before installing keep their blank icon until the cache is cleared — run `dist\clear-cache.cmd`, or simply copy the file to a new name.
- This is a thumbnail provider, **not** a preview handler. If you also want a large rendering in the Preview pane, that needs a separate `IPreviewHandler` (the Preview pane shows whatever your currently installed preview handler produces).
- Windows 10 1903+ / 11 only — it relies on `SHCreateItemFromParsingName`, the isolated surrogate and the modern Settings app.

---

## Credits

Rendering style, colour mapping, layout and the statistics algorithms are ported from **[Eroscripts/funlib](https://github.com/Eroscripts/funlib)** (`src/rendering/svg.ts`, `src/utils/converter.ts`, `src/utils/manipulations.ts`), MIT licensed.

OKLCH → sRGB conversion follows **[colorizr](https://github.com/gilbarbara/colorizr)**, MIT licensed.

The idea of showing `.funscript` metadata inside Explorer comes from **[martinAlt335/FunscriptWindowsPreviewExtension](https://github.com/martinAlt335/FunscriptWindowsPreviewExtension)** (a preview handler). This project is an independent implementation targeting the thumbnail interface; it shares no code with it.

See [THIRD_PARTY_NOTICES.txt](dist/THIRD_PARTY_NOTICES.txt).

## License

[MIT](LICENSE)
