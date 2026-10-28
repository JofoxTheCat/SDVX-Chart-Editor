"""Builds the release packages: a self-contained Windows zip and a self-contained Linux tarball.

    python tools/publish_release.py                    # both platforms, version from Directory.Build.props
    python tools/publish_release.py --rid linux-x64    # one platform
    python tools/publish_release.py --version 0.1.1

Output: release/SDVXEditor-<version>-<rid>/ plus .zip (Windows) / .tar.gz (Linux) beside it.
release/ is git-ignored.

Each package carries what the VOX export needs besides ffmpeg:
- the converter files from the submodule, in external/SDVX-Chart-Converter/ (found by
  VoxExporter.FindConverterDir from the app folder upwards);
- Windows only: python/runtime/ (embeddable Python + Pillow, fetched by tools/fetch_python.ps1).
  Linux uses the system python3 (PythonLocator), which needs Pillow.

The Linux tarball is written with Python's tarfile so the executable keeps its 0755 mode;
a zip made on Windows would lose it.
"""
from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import tarfile
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DESKTOP = REPO / "src" / "SDVXEditor.Desktop" / "SDVXEditor.Desktop.csproj"
CONVERTER = REPO / "external" / "SDVX-Chart-Converter"
PY_RUNTIME = REPO / "python" / "runtime"
RELEASE = REPO / "release"

# What vox_host.py imports from the converter (ksh2vox + its radar siblings + template), plus its licence.
CONVERTER_FILES = ["ksh2vox.py", "radar.py", "radar_model.py", "radar_model.json", "template_v13.vox",
                   "LICENSE", "requirements.txt"]
OPTIONAL_CONVERTER_FILES = {"radar_model.py", "radar_model.json", "requirements.txt"}

RIDS = ["win-x64", "linux-x64"]


def props_version() -> str:
    text = (REPO / "Directory.Build.props").read_text(encoding="utf-8")
    m = re.search(r"<Version>([^<]+)</Version>", text)
    if not m:
        sys.exit("no <Version> in Directory.Build.props")
    return m.group(1).strip()


def converter_version() -> str:
    m = re.search(r'^__version__\s*=\s*"([^"]+)"', (CONVERTER / "ksh2vox.py").read_text(encoding="utf-8"), re.M)
    return m.group(1) if m else "?"


def publish(rid: str, version: str, out: Path) -> None:
    if out.exists():
        shutil.rmtree(out)
    cmd = ["dotnet", "publish", str(DESKTOP), "-c", "Release", "-r", rid, "--self-contained", "true",
           f"-p:Version={version}", "-o", str(out)]
    print("+", " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True)


def copy_converter(out: Path) -> None:
    dst = out / "external" / "SDVX-Chart-Converter"
    dst.mkdir(parents=True, exist_ok=True)
    for name in CONVERTER_FILES:
        src = CONVERTER / name
        if not src.exists():
            if name in OPTIONAL_CONVERTER_FILES:
                continue
            sys.exit(f"converter file missing: {src} (run: git submodule update --init)")
        shutil.copy2(src, dst / name)


def copy_python_runtime(out: Path) -> None:
    if not (PY_RUNTIME / "python.exe").exists():
        sys.exit("python/runtime is missing - run tools/fetch_python.ps1 first (or pass --no-python)")
    shutil.copytree(PY_RUNTIME, out / "python" / "runtime",
                    ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))


def write_texts(out: Path, rid: str, version: str) -> None:
    shutil.copy2(REPO / "LICENSE", out / "LICENSE.txt")
    shutil.copy2(REPO / "THIRD-PARTY-NOTICES.txt", out / "THIRD-PARTY-NOTICES.txt")
    readme = (REPO / "tools" / "release" / "README.txt").read_text(encoding="utf-8")
    platform = "Windows" if rid.startswith("win") else "Linux"
    readme = readme.replace("{version}", version).replace("{platform}", platform)
    # keep only the block for this platform
    keep, drop = ("[windows]", "[linux]") if platform == "Windows" else ("[linux]", "[windows]")
    lines, mode = [], None
    for line in readme.splitlines():
        tag = line.strip().lower()
        if tag in ("[windows]", "[linux]", "[end]"):
            mode = tag
            continue
        if mode == drop:
            continue
        lines.append(line)
    newline = "\r\n" if platform == "Windows" else "\n"
    (out / "README.txt").write_bytes((newline.join(lines) + newline).encode("utf-8"))


def make_zip(folder: Path) -> Path:
    archive = folder.parent / (folder.name + ".zip")  # with_suffix would cut at "0.1.0"
    archive.unlink(missing_ok=True)
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(folder.rglob("*")):
            z.write(p, Path(folder.name) / p.relative_to(folder))
    return archive


def make_tarball(folder: Path, executables: set[str]) -> Path:
    archive = folder.parent / (folder.name + ".tar.gz")
    archive.unlink(missing_ok=True)

    def mode(info: tarfile.TarInfo) -> tarfile.TarInfo:
        rel = info.name.split("/", 1)[1] if "/" in info.name else ""
        info.uid = info.gid = 0
        info.uname = info.gname = ""
        if info.isdir() or rel in executables:
            info.mode = 0o755
        else:
            info.mode = 0o644
        return info

    with tarfile.open(archive, "w:gz", compresslevel=9) as t:
        t.add(folder, arcname=folder.name, filter=mode)
    return archive


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--version", default=None, help="default: <Version> of Directory.Build.props")
    ap.add_argument("--rid", action="append", choices=RIDS, help="platform(s); default: all")
    ap.add_argument("--no-python", action="store_true", help="Windows: do not bundle python/runtime")
    ap.add_argument("--no-archive", action="store_true", help="leave the folders, skip zip/tar.gz")
    args = ap.parse_args()

    version = args.version or props_version()
    if not (CONVERTER / "ksh2vox.py").exists():
        sys.exit("external/SDVX-Chart-Converter is empty - run: git submodule update --init")
    print(f"SDVX Chart Editor {version}, ksh2vox {converter_version()}")

    RELEASE.mkdir(exist_ok=True)
    for rid in args.rid or RIDS:
        out = RELEASE / f"SDVXEditor-{version}-{rid}"
        publish(rid, version, out)
        copy_converter(out)
        if rid.startswith("win") and not args.no_python:
            copy_python_runtime(out)
        write_texts(out, rid, version)
        if args.no_archive:
            print(f"{rid}: {out}")
            continue
        archive = make_zip(out) if rid.startswith("win") else make_tarball(out, {"SDVXEditor", "createdump"})
        print(f"{rid}: {archive} ({archive.stat().st_size / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
