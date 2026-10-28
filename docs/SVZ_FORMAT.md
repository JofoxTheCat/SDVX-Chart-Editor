# `.svz` — SDVX Chart Editor package

The editor's share / collab package, the counterpart of osu!'s `.osz`: one song folder in one file,
to hand a song to a co-mapper, a tester or another machine. Code: Core `Library/SvzPackage.cs`
(export, manifest), `Library/SongImporter.cs` (import).

## Layout

A plain zip archive (deflate) of **exactly one song folder**:

```
<Song Folder>/
  exh.kson               every chart as .kson
  mxm.kson
  adv.ksh                a chart read from .ksh keeps its original ...
  adv.kson               ... and gets a .kson beside it (same stem, the editor prefers it)
  song.ogg               audio (song + preview), jackets, backgrounds, key sounds,
  jacket.png             sub-folders — everything in the folder
  sounds/clap.wav
  sdvxeditor.json        manifest (optional, see below)
```

- Entry names use `/`, are relative, and start with the folder name. The folder name is the song's identity:
  it is what the editor library and ksh2vox's import map key on.
- Left out on export: hidden files and folders (`.…`, e.g. the editor's `.sdvxeditor-fork.json`, which holds a
  local path), `*.tmp`, `*.partial`, `Thumbs.db`, `desktop.ini`, `.DS_Store`.
- Bookmarks (with label and colour) are inside the charts: kson `impl["sdvx-editor"].bookmarks` as
  `[y, label]` / `[y, label, "#rrggbb"]`, ksh `//#bookmark|#rrggbb|label` comment lines.
- Song sources travel the same way: kson `impl["sdvx-editor"].song_sources` (array of URLs), ksh `//#source|<url>`
  lines; the manifest does not repeat them.

## Manifest `sdvxeditor.json`

UTF-8 JSON, informational only — the charts are the truth.

```json
{
  "format": "sdvx-editor-svz",
  "format_version": 1,
  "editor_version": "0.1.0",
  "folder": "Jumpy Star",
  "title": "Jumpy Star",
  "artist": "…",
  "exported_at": "2026-10-25T12:00:00Z",
  "charts": [
    { "file": "exh.kson", "slot": 2, "difficulty": "EXH", "level": 15, "charter": "mapper1",
      "bookmarks": [ { "y": 7680, "time_ms": 21714, "label": "Part B - mapper2", "color": "#ff66aa" } ] }
  ],
  "collab_notes": "optional free text"
}
```

| Key | Meaning |
|---|---|
| `format` | always `sdvx-editor-svz`; another value refuses the manifest (not the package) |
| `format_version` | 1. A newer number still imports; unknown keys are ignored and a warning says so |
| `slot` | 0 NOV, 1 ADV, 2 EXH, 3 INF-class, 4 MXM |
| `y` / `time_ms` | bookmark pulse (240 per quarter note) and its audio time when exported |
| `color` | `#rrggbb` lower case; absent = default bookmark blue |

Parsing is tolerant: missing keys default, wrong types fall back, `[1,2]` or non-JSON is ignored with a warning.

## Import

`.svz` goes through `SongImporter` exactly like a `.zip` — any zip of a song folder works, with or without a
manifest (a flat archive takes the archive's file name as folder name):

- zip-slip refused (absolute paths, drive letters, `..`, anything resolving outside the staging folder),
- caps: 256 MiB archive, 1 GiB unpacked, 5000 entries, 200× ratio per entry above 1 MiB,
- unpacked into a `.import-*` staging folder, moved into the editor's songs folder when complete,
- the manifest is read and removed from the imported folder; a broken one is a warning.

Export refuses to write a package the importer would refuse (same caps).

**Name clash** (a song folder of the same name already exists in the editor's songs): song select asks
*Replace* (osu!'s "update the set": the old folder is deleted, the package takes its place), *Keep both*
(imported as `<name> 2`, the default, also for Enter) or *Cancel*.

Entry points: song select drag & drop, `--import <file>`, song select right-click → Export → ".svz (share /
collab)", editor File → Export → ".svz (share / collab)…" (packages the folder as saved on disk). Exports go to
`Exports\` beside the editor's songs folder.

## Not done: file association

The editor does not register `.svz` with Windows — that is a system setting. An installer could do it later:
`HKCU\Software\Classes\.svz` → `SDVXEditor.svz`, with `shell\open\command` =
`"<path>\SDVXEditor.exe" --import "%1"` (per user, no admin rights needed), and remove it on uninstall.
