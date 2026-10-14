# File Checksum Tool

A cross-platform desktop tool built with **.NET 10 + Avalonia UI** for generating, verifying and merging checksum files.
It supports CRC64, MD5, SHA-1, SHA-256, SHA-384, SHA-512, SHA3-256, SHA3-384, SHA3-512 and CRC-32 (both `.crc32` and `.sfv`), and is fully interoperable with every format HashCheck understands.
Runs on **Windows / macOS / Linux** with a modern UI that follows the system light/dark mode, offers manual theme switching, and ships with multi-language support including automatic detection of the system language.

<p align="center">
  <img src="icon/logo.png" width="120" alt="ChecksumTool" />
</p>

## Author's Note

The code of this project was implemented with AI assistance. Human contributions were limited to UI product design, software logic, design concepts, requirement descriptions and testing feedback.

## Screenshots

| Verify (Light) | Verify (Dark) |
| --- | --- |
| ![Verify Light](docs/screenshots/01-verify-light.png) | ![Verify Dark](docs/screenshots/02-verify-dark.png) |

| Generate | Merge |
| --- | --- |
| ![Generate](docs/screenshots/03-generate-light.png) | ![Merge](docs/screenshots/04-merge-light.png) |

| Settings | Dropdown |
| --- | --- |
| ![Settings](docs/screenshots/05-settings-light.png) | ![Dropdown](docs/screenshots/07-settings-menu-light.png) |

| Multi-language (English) | Verification in Progress |
| --- | --- |
| ![English](docs/screenshots/06-verify-english.png) | ![Running](docs/screenshots/09-verify-running.png) |

> Screenshots are rendered headlessly with Avalonia via `tools/CheckSumTool.Preview` and can serve as a UI regression baseline.

---

## Features

| Panel | Capabilities |
| --- | --- |
| **Verify** | Drag in or pick checksum files (`.crc64`, `.md5`, `.sha1`, `.sha256`, `.sha384`, `.sha512`, `.sha3-256`, `.sha3-384`, `.sha3-512`, `.crc32` or `.sfv`); contents are parsed automatically and each entry is compared against its associated file |
| **Generate** | Drag in or pick regular files, check one or more output formats (11 in total, including CRC-32 `.crc32` and `.sfv`) and generate them in the order they are checked |
| **Merge** | Drag in or pick checksum files of the same algorithm, de-duplicate / sort and export; both `.crc32` and `.sfv` are recognized as the same CRC-32 algorithm |

### Key Behaviors

- **Same-directory principle**: generated checksum files are written next to the source files; during verification, associated files are looked up in the checksum file's own directory.
- **Two import methods**: **clicking the drop zone** opens the system file picker directly (multi-select supported); the "Select Folder" button to the right of the drop zone imports a whole directory, expanding its files while **automatically skipping hidden files starting with `.`** (such as `.DS_Store`, `.gitignore`) as well as system clutter like `Thumbs.db` / `desktop.ini` (no prompt is shown).
- **Concurrency**: configurable from **1 to 8** in Settings (default 3). The concurrency budget is shared across "format × file" tasks: with a single file, several algorithms run at once; with multiple files, the leading formats process files in parallel first and remaining budget spills over to later formats. The verify panel works the same way.
- **Ignored items get a toast**: dropping a recognized checksum file into the generate panel pops up a "Checksum file ignored" toast, which does not occupy the bottom status bar.
- **Removal gets a toast**: clicking the × on a list row pops up a toast, and the status bar refreshes to the **current list summary**; once the list is cleared the status bar returns to the default "Ready" text.
- **Completion gets a toast**: verify, generate and merge each pop up a toast when finished (green when verification fully passes, red warning when there are mismatches / missing files).
- **Stop button appears with the task**: only after starting does the "Stop" button appear next to the progress bar; clicking it cancels and the button disappears. Only the percentage is shown beside the progress bar, with no text description.
- **Appending allowed**: while verification has not started, or after it has been stopped, new checksum files can still be appended to the list.
- **Records listed upfront**: after dropping in a checksum file, all records (including unverified ones) are listed immediately, and their status is updated row by row during verification.
- **Cross-format recognition**: all algorithms use the same text line format, distinguished by file extension; CRC-32 uses the same algorithm for `.crc32` and `.sfv`. Among them, `.sfv` output uses the **native SFV format** (file names left-aligned, padded with spaces to the longest name, single space separator, lowercase 8-digit CRC-32), fully interoperable with the `.sfv` files produced by the Windows Explorer extension [HashCheck](https://github.com/gurnec/HashCheck); other formats such as `.crc32` keep the unified `<checksum> *<filename>` line format.
- **Folder drop**: dropping a folder expands the files at that folder level only (no sub-folders).
- **Multi-language**: Simplified Chinese / Traditional Chinese / English and more; switching takes effect immediately.
- **Themes**: the round button at the top right cycles between "Follow system → Light → Dark".

### Default File Naming Rules

**Generate panel** — the file name updates automatically with the file list; once edited manually it is never overwritten automatically (restores automatic naming after the list is cleared).

| Case | Default file name |
| --- | --- |
| No files dropped | Empty (the input box shows the "leave empty to auto-name" hint) |
| Single file `episode01.mp4` | `episode01` |
| Multiple files (`a.mp4` … `z.mp4`) | `a - z` (first - last) |

**Merge panel** — the input box defaults to empty; on export it is auto-named as `first file name - last file name`
(e.g. `collection 1-24.crc64` + `extra-checksums.crc64` → `collection 1-24 - extra-checksums.crc64`). A manually entered name takes precedence.

---

## Download and Install

Download the archive for your platform from [Releases](../../releases). All are **self-contained** builds that require no pre-installed .NET runtime.

| Platform | Architecture | File name |
| --- | --- | --- |
| Windows | x86 (32-bit) | `checksum-tool-win-x86.zip` |
| Windows | x64 | `checksum-tool-win-x64.zip` |
| Windows | arm64 | `checksum-tool-win-arm64.zip` |
| macOS | Intel (x64) | `checksum-tool-mac-x64.zip` |
| macOS | Apple Silicon (arm64) | `checksum-tool-mac-arm64.zip` |
| Linux | x64 | `checksum-tool-linux-x64.zip` |

### First Run

**Windows file association** — on first launch the app automatically registers every checksum file
extension as associable (`.crc64` / `.md5` / `.sha1` / `.sha256` / `.sha384` / `.sha512` /
`.sha3-256` / `.sha3-384` / `.sha3-512` / `.crc32` / `.sfv`; writes only to the current user's
registry; no administrator privileges required). After registration:

- choose the app in "Default apps" or "Open with" to permanently assign these extensions to it;
- **double-click any checksum file** and the app starts, enters the verify panel and verifies immediately;
- it can also be invoked from the command line: `"File Checksum Tool.exe" --verify "D:\checksums.crc64"`.

**Windows** — unzip and double-click `File Checksum Tool.exe`.

**macOS** — unzip to get `File Checksum Tool.dmg`, mount it and drag `File Checksum Tool.app` to "Applications". If you see "damaged" or "cannot verify developer", click "Done", then click "Allow Anyway" in System Settings → Privacy & Security → Security, or run in Terminal:

```bash
xattr -dr com.apple.quarantine "~/Downloads/File Checksum Tool.dmg"
```

After dragging the app to Applications, run:

```bash
xattr -dr com.apple.quarantine "/Applications/File Checksum Tool.app"
```

**Linux** — requires basic graphics libraries such as `libX11`, `libICE`, `libSM`, `libfontconfig` (usually pre-installed on desktop distributions):

```bash
chmod +x checksum-tool
./checksum-tool
```

---

## Usage

### Settings Page

Open it via the gear button at the top right:

| Setting | Description |
| --- | --- |
| **Language** | Simplified Chinese / Traditional Chinese / English and more; the UI refreshes immediately |
| **Concurrency** | 1 to 8, the number of files processed simultaneously; 1–2 recommended for mechanical hard drives, 3–8 for SSD/NVMe |
| **Version updates** | Check the latest GitHub Release; if a new version exists, prompt and open the download page, otherwise show "You are on the latest version" |
| **About** | Open the project repository page |

Settings are saved automatically and persist across restarts.

### 1. Verify Checksum Files

1. **Drag** supported checksum files onto the dashed box, **click** it for multi-select from the system file picker, or use "Select Folder" on the right to import a whole directory.
2. The panel shows the loaded checksum file chips (with type and record count) and **lists every record immediately** (status "Pending"), so you can confirm record counts and file names before verification.
3. Click **Verify** at the top right. The bottom progress bar shows overall progress, while list rows refresh one by one — the row being computed shows **its own per-file progress bar** in the Status column, other rows show "Pending" or the final result.
4. During verification, a **"Stop" button appears** to the right of the progress bar; clicking it cancels and the button disappears. Records not processed when stopped are marked "Cancelled", and new checksum files can be appended at that point.

The result list shows "File / Status" per entry; hover over a file name to see the **expected and actual values** of that record.
Statistics are shown uniformly **at the right of the bottom status bar**, updating in real time with scan progress (Passed / Mismatched / Not Found / N files total):

| Status | Meaning |
| --- | --- |
| · Pending | Record listed, verification not started |
| … Verifying | Currently computing |
| ✓ Passed | Computed value matches the recorded value |
| ✕ Mismatched | Computed value differs from the recorded value |
| ? Associated file not found | The recorded file cannot be found in the checksum file's directory (the progress bar hints likewise) |
| ! Read failed | The file is locked or has no read permission |
| – Cancelled | Verification was stopped; this record was not processed |

### 2. Generate Checksum Files

1. **Drag** regular files onto the dashed box or **click** it for multi-select (recognized checksum files are ignored with a prompt).
2. **The file name has its own row**: for a single file it is that file's name; for multiple files it is "first - last".
   It can also be edited manually, or enter just an extension / full path (entering a full path generates only one file).
3. **Below the file name are the output format checkboxes** (11 in total): CRC-32's `.crc32` and `.sfv` are listed separately, the other 9 algorithms one each;
   **multiple selections** are allowed, and every checked format writes one checksum file per input file (CRC64 is checked by default).
4. Click **Generate** at the top right. While running, the "Stop" button also appears next to the progress bar, and the file name input and format checkboxes are locked, preventing output name changes mid-task.

> Output location follows the source file's directory by default; if the dropped files come from multiple directories, one same-named checksum file is generated **in each directory**.
> Enter an absolute path directly (e.g. `/Users/me/out/checksums`) to generate only one file.
> The UI does not display the output path; hover over the file name input box to see the full path.

### 3. Merge Checksum Files

1. Drag in or pick multiple checksum files of the same type. If files of other algorithms are mixed in, a warning pops up and export is disabled.
2. Below the file name are the **merge options**: check "De-duplicate by file name" and "Sort by file name" (temporarily disabled during export).
3. The export file name can be left empty (auto-named "first - last") or filled in manually; the input box is temporarily locked during export. Click **Export Merged File** at the top right.
   After a successful export a toast pops up at the bottom (auto-dismisses after about 2.6 seconds).

> Success is a green checkmark, warnings are a red exclamation mark.
> All three operations prompt on completion: generate / merge show success prompts, verify picks success or warning based on the result.

---

## Checksum File Format

One record per line:

```
<checksum> *<filename>
```

- The separator is ` *` (space + asterisk); `<checksum>  <filename>` (two spaces) is also accepted.
- File names are **plain file names** (sub-directory relative paths allowed), resolved relative to the checksum file's directory during verification.
- Encoding is UTF-8 (no BOM), line endings are LF.

**CRC64 checksum values** are always written as unsigned decimal integers, matching the output of `crc64ecma.cs`:

```
6967259512934720053 *video.mp4
```

> When reading, 16-digit hexadecimal and `0x`-prefixed forms are additionally accepted for convenience with files produced by other tools; but **writing is always decimal**.

**MD5 checksum values** are 32 hexadecimal digits:

```
900150983cd24fb0d6963f7d28e17f72 *file.bin
```

### CRC64 Algorithm

| Parameter | Value |
| --- | --- |
| Polynomial | `0xC96C5795D7870F42` (reflected) |
| Init | `0xFFFFFFFFFFFFFFFF` |
| RefIn / RefOut | `true` / `true` |
| XorOut | `0xFFFFFFFFFFFFFFFF` |
| Check value (`"123456789"`) | `0x995DC9BBDF1939FA` = `11051210869376104954` |

### Other Algorithm Parameters

The "check value" column below always refers to the result of the standard test data `"123456789"`, which can be used to confirm that this tool and other tools agree.

| Algorithm | Output extension | Checksum form | `"123456789"` check value |
| --- | --- | --- | --- |
| CRC-32 | `.crc32` / `.sfv` | 8 lowercase hex digits | `cbf43926` |
| MD5 | `.md5` | 32 lowercase hex digits | `25f9e794323b453885f5181f1b624d0b` |
| SHA-1 | `.sha1` | 40 lowercase hex digits | `f7c3bc1d808e04732adf679965ccc34ca7ae3441` |
| SHA-256 | `.sha256` | 64 lowercase hex digits | `15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225` |
| SHA-384 | `.sha384` | 96 lowercase hex digits | `eb455d56d2c1a69de64e832011f3393d45f3fa31d6842f21af92d2fe469c499da5e3179847334a18479c8d1dedea1be3` |
| SHA-512 | `.sha512` | 128 lowercase hex digits | `d9e6762dd1c8eaf6d61b3c6192fc408d4d6d5f1176d0c29169bc24e71c3f274ad27fcd5811b313d681f7e55ec02d73d499c95455b6b5bb503acf574fba8ffe85` |
| SHA3-256 | `.sha3-256` | 64 lowercase hex digits | `87cd084d190e436f147322b90e7384f6a8e0676c99d21ef519ea718e51d45f9c` |
| SHA3-384 | `.sha3-384` | 96 lowercase hex digits | `8b90ede4d095409f1a12492c2520599683a9478dc70b7566d23b3e41ece8538c6cde92382a5e38786490375c54672abf` |
| SHA3-512 | `.sha3-512` | 128 lowercase hex digits | `e1e44d20556e97a180b6dd3ed7ae5c465cafd553fa8747dca038fb95635b77a37318f7ddf7aec1f6c3c14bb160ba2497007decf38dd361cab199e3b8c8fe1f5c` |

**CRC-32 (IEEE 802.3) algorithm parameters** — identical to the SFV files produced by PKZip / WinRAR / HashCheck:

| Parameter | Value |
| --- | --- |
| Polynomial | `0x04C11DB7` (bit-reversed equivalent `0xEDB88320`) |
| Init | `0xFFFFFFFF` |
| RefIn / RefOut | `true` / `true` |
| XorOut | `0xFFFFFFFF` |
| Check value (`"123456789"`) | `0xCBF43926` = `3421780262` |

**SHA family** (MD5: see RFC 1321; SHA-1 / SHA-2: see FIPS 180-4; SHA-3: see FIPS 202) — all computed block-by-block in streaming fashion, producing fixed-length hexadecimal strings that are always written lowercase into checksum files; uppercase and `0x`-prefixed forms are accepted when reading.

Example checksum files for each algorithm (same file `video.mp4`):

```
cbf43926 *video.mp4                                        # .crc32 (decimal)
25f9e794323b453885f5181f1b624d0b *video.mp4                # .md5
f7c3bc1d808e04732adf679965ccc34ca7ae3441 *video.mp4        # .sha1
15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225 *video.mp4                              # .sha256
eb455d56d2c1a69de64e832011f3393d45f3fa31d6842f21af92d2fe469c499da5e3179847334a18479c8d1dedea1be3 *video.mp4   # .sha384
d9e6762dd1c8eaf6d61b3c6192fc408d4d6d5f1176d0c29169bc24e71c3f274ad27fcd5811b313d681f7e55ec02d73d499c95455b6b5bb503acf574fba8ffe85 *video.mp4                            # .sha512
87cd084d190e436f147322b90e7384f6a8e0676c99d21ef519ea718e51d45f9c *video.mp4                                # .sha3-256
8b90ede4d095409f1a12492c2520599683a9478dc70b7566d23b3e41ece8538c6cde92382a5e38786490375c54672abf *video.mp4   # .sha3-384
e1e44d20556e97a180b6dd3ed7ae5c465cafd553fa8747dca038fb95635b77a37318f7ddf7aec1f6c3c14bb160ba2497007decf38dd361cab199e3b8c8fe1f5c *video.mp4                            # .sha3-512
```

> Note: CRC-32's `.sfv` does not use the unified line format above, but the native SFV format (see the "Cross-format recognition" entry); the example above applies to `.crc32` only.

**Choosing an algorithm**:

| Scenario | Recommendation |
| --- | --- |
| Interoperability with the CRC64 toolchain; fast everyday verification | CRC64 |
| Interoperability with Windows HashCheck / legacy tools | CRC-32 (`.sfv`) |
| General integrity check, balancing speed and collision resistance | SHA-256 |
| Strong integrity check (downloads, archives) | SHA-512 / SHA3-512 |
| Guarding against transfer errors only, security not a concern | MD5 / CRC family |

> Security note: MD5, SHA-1 and the CRC family are **not resistant to intentional tampering**; use them only to detect transfer errors or compare file versions. Use SHA-256 or stronger when security matters.

---

## Local Development

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
# Run (always use Release: SHA3 is about 5× slower in Debug)
dotnet run --project src/CheckSumTool -c Release

# Run unit tests
dotnet test tests/CheckSumTool.Tests

# Local publish (example: Apple Silicon macOS)
dotnet publish src/CheckSumTool -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/osx-arm64
```

> **Performance note**: SHA-1 / SHA-256 / SHA-384 / SHA-512 / MD5 use the system's native implementations (hardware-accelerated, typically >1 GB/s).
> SHA3-256 / SHA3-384 / SHA3-512 can only use the pure managed Keccak-f on macOS (neither the system nor .NET provides native SHA3),
> measured at about 130–260 MiB/s in Release. **The Debug configuration does not optimize hot loops, making SHA3 about 5× slower**
> (a 400 MB file goes from about 2 seconds to about 10 seconds), so always use `-c Release` or a published build for daily use.

Regenerate the Windows executable icon (multi-size ICO from `icon/logo.png`, requires macOS `sips`):

```bash
python3 tools/make_ico.py
```

> Icon assets live under `icon/`: `logo.png` for Windows / Linux and `logo.icns` for macOS;
> the build packs them into `src/CheckSumTool/Assets/` (where `logo.ico` is generated by the script).

Regenerate documentation screenshots (headless Avalonia rendering, cross-platform):

```bash
dotnet run --project tools/CheckSumTool.Preview -- docs/screenshots
```

### Project Structure

```
.
├── .github/workflows/build.yml        # CI: cross-platform build + automatic Release publishing
├── src/CheckSumTool/
│   ├── Core/                          # Algorithms and checksum file I/O (decoupled from the UI)
│   │   ├── Crc64.cs                   # CRC64-ECMA (equivalent to crc64ecma.cs), slicing-by-8
│   │   ├── HashEngine.cs              # Streaming CRC64 / MD5, progress + cancellation
│   │   ├── ChecksumFile.cs            # .crc64 / .md5 parsing and serialization
│   │   └── HashKind.cs
│   ├── Localization/                  # Three-language string tables and accessors
│   ├── Controls/SelectBox.axaml       # Element-style dropdown selector
│   ├── Helpers/                       # Drag & drop, system file picker
│   ├── Services/SettingsService.cs    # Preference persistence
│   ├── Services/FileAssociation.cs   # Windows file associations (.crc64 / .md5)
│   ├── Services/CommandLine.cs       # Command-line parsing (--verify / double-click)
│   ├── ViewModels/                    # MVVM view models
│   ├── Views/                         # Avalonia UI (including SettingsWindow)
│   └── Styles/Controls.axaml          # Themes and control styles
├── tests/CheckSumTool.Tests/             # xUnit unit tests
├── icon/                              # App icon sources (logo.png / logo.icns)
├── tools/
│   ├── CheckSumTool.Preview/             # Headless screenshot tool (not published)
│   └── make_ico.py                    # Generates multi-size ICO from logo.png
└── docs/screenshots/                  # Documentation screenshots
```

---

## Release Process

**Automatic publishing (default)** — every push to the `main` / `master` branch automatically runs the full
"unit tests → six-platform build → publish Release" pipeline with no extra steps. The Release tag is `v` + UTC timestamp
(e.g. `v202610150511`, matching the version displayed inside the installer), and it is automatically marked as the
repository's Latest Release.

**Publishing a specific version** — push a `v*` tag to publish under that tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

You can also manually trigger `Build & Release` from the Actions page: fill in a version (such as `v1.0.0`) in the `tag` input to create a Release; leave it empty to only build artifacts.

> Pull Requests run tests and builds only, and never publish Releases.

Workflow steps:

1. `unit tests` — runs the xUnit tests on Ubuntu (CRC64 standard check values, format parsing, cancellation logic, default naming, multi-language completeness, etc.).
2. `build` — six RIDs in parallel `dotnet publish`; macOS additionally assembles the `.app` bundle and performs ad-hoc signing.
3. `release` — collects the per-platform zips, creates a GitHub Release and uploads the artifacts.

---

## FAQ

**Q: Why does macOS say "damaged" on double-click?**
No Apple developer signing or notarization is performed. Run `xattr -dr com.apple.quarantine File Checksum Tool.app`.

**Q: Linux startup reports missing `libX11` / `libSkiaSharp`?**
Install the basic graphics libraries, e.g. on Debian/Ubuntu: `sudo apt install libx11-6 libice6 libsm6 libfontconfig1`.

**Q: "Associated file not found" during verification?**
Records in a checksum file are plain file names, and the tool looks them up **in the checksum file's directory**. Make sure the target files are in the same directory as the checksum file.

**Q: Where are settings stored?**
`%APPDATA%\CheckSumTool\settings.json` (Windows), `~/Library/Application Support/CheckSumTool/settings.json` (macOS), `~/.config/CheckSumTool/settings.json` (Linux),
storing theme mode, language, concurrency and merge options. Equivalent to a browser's LocalStorage and not shown in the UI.
During development/testing, the `CHECKSUMTOOL_SETTINGS_DIR` environment variable can redirect this directory.

---

## License

MIT
