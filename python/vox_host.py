#!/usr/bin/env python3
"""
vox_host — long-lived VOX export host for the SDVX Chart Editor.

The editor starts this once and keeps it running. It imports ksh2vox from the
converter directory given on the command line and calls its functions
directly (convert_song, assign_song_id, ...) — no CLI round trip, no launcher,
no addon install.

    python -E vox_host.py --converter <dir containing ksh2vox.py>

(-E, not -I: -I also hides the user site-packages, where a system Python
usually has Pillow — measured: Pillow 13 invisible under -I.)

PROTOCOL — newline-delimited JSON, UTF-8, one object per line.

    request   {"id": 1, "method": "export_vox", "params": {...}}
    response  {"id": 1, "result": {...}}
              {"id": 1, "error": {"kind": "...", "message": "...", "detail": ...}}
    event     {"event": "progress", "id": 1, "name": "chart_done", "fields": {...}}
              {"event": "ready", "host": "...", "python": "...", ...}

Methods: ping, version, export_vox, shutdown.

Rules carried over from the launcher bridge (converter CLAUDE.md §14.4a), each
of which cost a debugging round there:
  * stdout is protocol ONLY. sys.stdout is rebound to stderr at import, file
    descriptor 1 itself is pointed at stderr (so a child process that inherits
    it cannot write into the protocol either), and every call additionally
    runs under redirect_stdout — ksh2vox prints a lot.
  * SystemExit is caught inside a method. Uncaught it would end the host.
  * radar.py is loaded from the SAME folder as ksh2vox.py and handed over via
    sys.modules; loaded by file path, `import radar` would otherwise find
    nothing (and radar values silently become 0) or some unrelated module.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import math
import os
import shutil
import sys
import time
import traceback

HOST_VERSION = "0.1.0"
PROTOCOL = 1
MIN_PYTHON = (3, 10)            # ksh2vox uses PEP 604 annotations (str | None)
RECOMMENDED_CONVERTER = "0.16.0"


def _utf8(stream, **kw) -> None:
    with contextlib.suppress(AttributeError, ValueError, OSError):
        stream.reconfigure(encoding="utf-8", **kw)


# The protocol is UTF-8 on every platform. Before Python 3.15 a piped stream on
# Windows uses the ANSI code page: a Japanese title would raise
# UnicodeEncodeError and the call would never be answered.
_utf8(sys.stdin, errors="replace")
_utf8(sys.stdout, errors="backslashreplace", newline="\n")
_utf8(sys.stderr, errors="backslashreplace")


def _claim_stdout():
    """Return a private writer on the real stdout and point everything else
    (sys.stdout AND file descriptor 1) at stderr."""
    try:
        sys.stdout.flush()
        fd = os.dup(1)
        os.dup2(2, 1)
        proto = open(fd, "w", encoding="utf-8", errors="backslashreplace",
                     newline="\n", buffering=1, closefd=True)
    except (OSError, ValueError, AttributeError):
        proto = sys.stdout              # no real fds (embedded / test harness)
    sys.stdout = sys.stderr
    return proto


_PROTO = _claim_stdout()


class HostError(Exception):
    """An error the editor tells apart by `kind`, not by its English text."""

    def __init__(self, kind: str, message: str, detail=None):
        super().__init__(message)
        self.kind = kind
        self.detail = detail


def _finite(obj):
    if isinstance(obj, float):
        return obj if math.isfinite(obj) else None
    if isinstance(obj, dict):
        return {k: _finite(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [_finite(v) for v in obj]
    return obj


def _send(obj: dict) -> None:
    """The only writer of the real stdout. NaN/Infinity go out as null."""
    kw = {"ensure_ascii": False, "separators": (",", ":"), "allow_nan": False}
    try:
        line = json.dumps(obj, **kw)
    except ValueError:
        line = json.dumps(_finite(obj), **kw)
    except TypeError:
        line = json.dumps(_finite(json.loads(json.dumps(obj, default=str))), **kw)
    _PROTO.write(line + "\n")
    _PROTO.flush()


# ── converter loading ───────────────────────────────────────────────────────
_CONVERTER_DIR: str | None = None
_KSH2VOX = None
_LOAD_ERROR: str | None = None


def _exec_from_source(name: str, path: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None:
        raise ImportError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    with open(path, "rb") as fh:
        code = compile(fh.read(), path, "exec", dont_inherit=True)
    sys.modules[name] = module
    try:
        exec(code, module.__dict__)
    except BaseException:
        sys.modules.pop(name, None)
        raise
    return module


def _load_converter(directory: str):
    """Import ksh2vox (+ its optional radar.py sibling) from `directory`."""
    path = os.path.join(directory, "ksh2vox.py")
    if not os.path.isfile(path):
        raise HostError("converter_missing", f"ksh2vox.py not found in {directory}",
                        directory)
    radar_path = os.path.join(directory, "radar.py")
    try:
        with contextlib.redirect_stdout(sys.stderr):
            if os.path.isfile(radar_path):
                _exec_from_source("radar", radar_path)
            else:
                sys.modules["radar"] = None     # makes `import radar` fail cleanly
            return _exec_from_source("ksh2vox", path)
    except HostError:
        raise
    except SyntaxError as e:
        raise HostError("converter_incompatible",
                        f"ksh2vox.py does not compile on Python "
                        f"{sys.version.split()[0]}: {e}", directory) from e
    finally:
        # ksh2vox holds its own reference as _radar; a global "radar" left
        # behind would be found by unrelated imports.
        sys.modules.pop("radar", None)


def _converter():
    if _KSH2VOX is None:
        raise HostError("converter_missing",
                        _LOAD_ERROR or "no converter directory given (--converter)",
                        _CONVERTER_DIR)
    return _KSH2VOX


def _version_tuple(v: str) -> tuple:
    out = []
    for part in str(v).split("."):
        digits = "".join(ch for ch in part if ch.isdigit())
        out.append(int(digits) if digits else 0)
    return tuple(out)


# ── stdout capture → warnings ───────────────────────────────────────────────
class _Capture(io.TextIOBase):
    """Tee for ksh2vox's print() output: forwards to stderr (for the editor's
    log) and keeps the lines so warnings can be returned as data."""

    def __init__(self):
        self.lines: list[str] = []
        self._buf = ""

    def writable(self) -> bool:
        return True

    def write(self, s: str) -> int:
        sys.stderr.write(s)
        self._buf += s
        while "\n" in self._buf:
            line, self._buf = self._buf.split("\n", 1)
            self.lines.append(line.rstrip("\r"))
        return len(s)

    def flush(self) -> None:
        sys.stderr.flush()

    def all_lines(self) -> list[str]:
        return self.lines + ([self._buf] if self._buf else [])


_WARN_MARKERS = ("[WARN]", "[warn]", "[skip]")
_ERROR_MARKERS = ("[ERROR]",)


def _classify(lines: list[str]) -> tuple[list[str], list[str]]:
    warnings, errors = [], []
    for raw in lines:
        line = raw.strip()
        if any(m in line for m in _ERROR_MARKERS):
            errors.append(line)
        elif any(m in line for m in _WARN_MARKERS):
            warnings.append(line)
    return warnings, errors


# ── methods ─────────────────────────────────────────────────────────────────
def m_ping(params: dict, ctx) -> dict:
    return {"pong": True, "protocol": PROTOCOL, "host": HOST_VERSION}


def m_version(params: dict, ctx) -> dict:
    """Host, Python and converter facts — the same checks as
    `ksh2vox.py --self-check`, as data."""
    info = {
        "host": HOST_VERSION,
        "protocol": PROTOCOL,
        "python": sys.version.split()[0],
        "python_executable": sys.executable,
        "python_ok": sys.version_info[:2] >= MIN_PYTHON,
        "converter_dir": _CONVERTER_DIR,
        "converter_loaded": _KSH2VOX is not None,
        "converter_error": _LOAD_ERROR,
        "recommended_converter": RECOMMENDED_CONVERTER,
    }
    ffmpeg = shutil.which("ffmpeg")
    info["ffmpeg"] = ffmpeg
    try:
        with contextlib.redirect_stdout(sys.stderr):
            from PIL import Image  # noqa: F401
            import PIL
        info["pillow"] = getattr(PIL, "__version__", "?")
    except Exception:
        info["pillow"] = None
    k = _KSH2VOX
    if k is not None:
        radar = getattr(k, "_radar", None)
        info.update({
            "converter_version": getattr(k, "__version__", None),
            "converter_outdated": _version_tuple(getattr(k, "__version__", "0"))
                                  < _version_tuple(RECOMMENDED_CONVERTER),
            "radar_loaded": radar is not None,
            "radar_version": getattr(radar, "__version__", None) if radar else None,
            "template_found": os.path.isfile(getattr(k, "TEMPLATE", "")),
            "max_song_id": getattr(k, "MAX_SONG_ID", None),
            "has_progress": "progress" in getattr(k.convert_song, "__code__").co_varnames,
        })
        check = getattr(k, "_check_positions", None)
        if callable(check):
            try:
                with contextlib.redirect_stdout(sys.stderr):
                    bad = check()
                info["positions_ok"] = not bad
                if bad:
                    info["positions_errors"] = list(bad)
            except Exception as e:      # diagnostic only
                info["positions_ok"] = None
                info["positions_errors"] = [str(e)]
    return info


def _int_param(params: dict, key: str):
    v = params.get(key)
    if v is None:
        return None
    if isinstance(v, bool) or not isinstance(v, (int, float)) or int(v) != v:
        raise HostError("bad_params", f"{key} must be an integer", key)
    return int(v)


def _apply_settings(k, params: dict) -> dict:
    """Set ksh2vox's module-level options for THIS call. Always every one of
    them: the module lives as long as the host, so a value left over from the
    previous export would otherwise apply silently."""
    fmt = params.get("audio_format") or "2dx"
    if fmt not in ("2dx", "s3v"):
        raise HostError("bad_params", "audio_format must be '2dx' or 's3v'", "audio_format")
    channels = params.get("audio_channels") or "mono"
    if channels not in ("mono", "stereo"):
        raise HostError("bad_params", "audio_channels must be 'mono' or 'stereo'",
                        "audio_channels")
    radar_names = params.get("radar_names") or "hyphen"
    if radar_names not in ("hyphen", "underscore"):
        raise HostError("bad_params", "radar_names must be 'hyphen' or 'underscore'",
                        "radar_names")
    k.AUDIO_FORMAT = fmt
    k.AUDIO_CHANNELS = 1 if channels == "mono" else 2
    k.WMA_ENCODER_CMD = params.get("wma_encoder") or None
    k.RADAR_TAG_STYLE = radar_names
    k.EMIT_MAX_EXSCORE = bool(params.get("max_exscore", False))
    custom_tag = params.get("custom_tag", True) is not False
    tag_id = _int_param(params, "custom_tag_id")
    setter = getattr(k, "set_custom_tag", None)
    if callable(setter):                 # ksh2vox >= 0.15.0
        if tag_id is None:
            setter(custom_tag)
        else:
            setter(custom_tag, tag_id)
    return {"audio_format": fmt, "audio_channels": channels,
            "custom_tag": custom_tag if callable(setter) else None}


def _list_files(directory: str) -> list[str]:
    out = []
    for root, _dirs, files in os.walk(directory):
        for f in files:
            out.append(os.path.join(root, f))
    return sorted(out)


def m_export_vox(params: dict, ctx) -> dict:
    """Convert ONE song folder to VOX.

    params:
      folder          song folder with .kson/.ksh + audio + jacket (required)
      out             music output root; the song lands in <out>/<id>_<ascii>/ (required)
      merged_db       path of music_db.merged.xml; omitted/null = the converter's
                      default (<out>/../others/music_db.merged.xml); false = none
      id              explicit song id (bypasses the import map)
      base_db         music_db.json/.xml, or a list of them (plugin json + the game's
                      data/others/music_db.xml): ids start one above the highest real
                      id over all of them (ksh2vox 0.20.1+; older takes one path)
      start_id        lowest id to hand out; with base_db too, the higher floor wins
      map             import_map.json path; default next to the merged db
      force           convert even if the import map says it is unchanged
      audio_format    "2dx" (default) | "s3v";  audio_channels "mono" | "stereo"
      wma_encoder     command template for s3v;  custom_tag (bool), custom_tag_id
      radar_names     "hyphen" | "underscore";   max_exscore (bool)
    """
    k = _converter()
    folder = params.get("folder") or ""
    out = params.get("out") or ""
    if not folder or not os.path.isdir(folder):
        raise HostError("bad_params", f"song folder not found: {folder}", "folder")
    if not out:
        raise HostError("bad_params", "out (music output root) is required", "out")
    folder = os.path.abspath(folder)
    out = os.path.abspath(out)

    settings = _apply_settings(k, params)
    merged = params.get("merged_db", None)
    if merged is False:
        merged = None
    elif not merged:
        merged = k._default_merged_path(out)
    else:
        merged = os.path.abspath(merged)

    explicit_id = _int_param(params, "id")
    start_id = _int_param(params, "start_id")
    base_db = params.get("base_db") or None
    bases = [base_db] if isinstance(base_db, str) else list(base_db or [])
    force = bool(params.get("force", False))

    capture = _Capture()
    produced: dict = {}
    events: list[str] = []

    def on_event(event, **fields):
        events.append(event)
        if event == "song_done":
            produced.update(fields)
        ctx.event("progress", name=event, fields=fields)

    # ksh2vox < 0.13.0 (e.g. the submodule at GitHub v0.12.0) has no progress=
    # parameter and no import map. Explicit ids still work there; everything
    # else needs a newer converter, said plainly instead of an AttributeError.
    with_progress = "progress" in k.convert_song.__code__.co_varnames

    def convert(sid):
        if with_progress:
            return k.convert_song(folder, sid, out, merged, progress=on_event)
        return k.convert_song(folder, sid, out, merged)

    t0 = time.monotonic()
    map_path = None
    skipped = False
    is_new = None
    with contextlib.redirect_stdout(capture):
        if explicit_id is not None:
            sid = explicit_id
            entry = convert(sid)
        else:
            floors = []
            if bases:
                for b in bases:
                    if not isinstance(b, str) or not os.path.isfile(b):
                        raise HostError("bad_params", f"base_db not found: {b}", "base_db")
                if hasattr(k, "max_real_id"):
                    # 0.20.1+: custom songs (nautica-tagged, merged dbs, our own entries) never count
                    floors.append(k.max_real_id(bases, own_db=merged) + 1)
                elif len(bases) == 1:
                    floors.append(k._max_existing_id(bases[0]) + 1)
                else:
                    raise HostError("converter_too_old",
                                    f"ksh2vox {getattr(k, '__version__', '?')} takes one base_db "
                                    f"(several need 0.20.1+)", getattr(k, "__version__", None))
            if start_id is not None:
                floors.append(start_id)
            if floors:
                floor = max(floors)
            else:
                raise HostError("id_required",
                                "give id, base_db or start_id — the converter needs "
                                "to know which song ids are safe", "id")
            missing = [f for f in ("default_map_path", "load_import_map", "is_up_to_date",
                                   "assign_song_id", "record_import", "save_import_map",
                                   "song_ascii_id", "map_key")
                       if not hasattr(k, f)]
            if missing:
                raise HostError("converter_too_old",
                                f"ksh2vox {getattr(k, '__version__', '?')} has no import map "
                                f"(needs 0.14.0+); pass an explicit id or update the converter",
                                getattr(k, "__version__", None))
            if floor > k.MAX_SONG_ID:
                raise HostError("id_exhausted",
                                f"no free song id: the first safe id {floor} is above "
                                f"the game limit {k.MAX_SONG_ID}", str(floor))
            map_path = params.get("map") or (k.default_map_path(merged) if merged else None)
            if map_path is None:
                raise HostError("id_required",
                                "without a merged db there is nowhere to remember the "
                                "id — pass id explicitly", "id")
            map_path = os.path.abspath(map_path)
            imap = k.load_import_map(map_path)
            merged_ids = set(k._read_existing_entries(merged)) if merged else None
            # 0.20.1+: a remembered id below the floor is reassigned, and the old entry/folder retired
            floor_aware = "floor_id" in k.is_up_to_date.__code__.co_varnames
            up_to_date = (k.is_up_to_date(imap, folder, out, merged_ids, floor_id=floor) if floor_aware
                          else k.is_up_to_date(imap, folder, out, merged_ids))
            if not force and up_to_date:
                sid = imap["songs"][k.map_key(folder)]["id"]
                skipped = True
                entry = True
                ascii_id = imap["songs"][k.map_key(folder)].get("ascii", "")
                produced["song_dir"] = os.path.join(out, f"{sid:04d}_{ascii_id}")
                on_event("song_skipped", index=0, total=1,
                         folder=os.path.basename(folder), song_id=sid)
            else:
                if "merged_path" in k.assign_song_id.__code__.co_varnames:
                    sid, is_new = k.assign_song_id(imap, folder, floor, out_root=out, merged_path=merged)
                else:
                    sid, is_new = k.assign_song_id(imap, folder, floor)
                entry = convert(sid)
                if entry:
                    header = k.chart_header(os.path.join(
                        folder, k.pick_charts(os.listdir(folder))[0]))
                    k.record_import(imap, folder, sid,
                                    f"{sid:04d}_{k.song_ascii_id(header, sid)}")
                    k.save_import_map(map_path, imap)

    warnings, errors = _classify(capture.all_lines())
    if not entry:
        raise HostError("convert_failed",
                        errors[0] if errors else "the converter produced no song",
                        {"errors": errors, "warnings": warnings,
                         "log": capture.all_lines()[-50:]})
    song_dir = produced.get("song_dir")
    if not song_dir and os.path.isdir(out):
        # no song_done event (converter without progress=): the folder is <id>_<ascii>
        cands = [os.path.join(out, d) for d in os.listdir(out)
                 if d.startswith(f"{sid:04d}_") and os.path.isdir(os.path.join(out, d))]
        if cands:
            song_dir = max(cands, key=os.path.getmtime)
    files = _list_files(song_dir) if song_dir and os.path.isdir(song_dir) else []
    vox = [f for f in files if f.lower().endswith(".vox")]
    if not skipped and not vox:
        warnings.append("no .vox was written — no chart had a known difficulty")
    return {
        "song_id": sid,
        "is_new": is_new,
        "skipped": skipped,
        "song_dir": song_dir,
        "files": files,
        "merged_db": merged,
        "map": map_path,
        "converter_version": getattr(k, "__version__", None),
        "settings": settings,
        "warnings": warnings,
        "errors": errors,
        "elapsed_s": round(time.monotonic() - t0, 3),
    }


def m_shutdown(params: dict, ctx) -> dict:
    ctx.shutdown = True
    return {"bye": True}


METHODS = {
    "ping": m_ping,
    "version": m_version,
    "export_vox": m_export_vox,
    "shutdown": m_shutdown,
}


class _Ctx:
    def __init__(self, req_id):
        self.req_id = req_id
        self.shutdown = False

    def event(self, event_type: str, /, **fields) -> None:
        # positional-only: progress events carry a field called `name`
        _send({**fields, "event": event_type, "id": self.req_id})


def _handle(line: str) -> bool:
    """Handle one request line. Returns False when the host should exit."""
    try:
        req = json.loads(line)
    except ValueError as e:
        _send({"id": None, "error": {"kind": "bad_request", "message": f"not JSON: {e}"}})
        return True
    if not isinstance(req, dict):
        _send({"id": None, "error": {"kind": "bad_request", "message": "not an object"}})
        return True
    req_id = req.get("id")
    method = req.get("method")
    params = req.get("params") or {}
    fn = METHODS.get(method)
    if fn is None:
        _send({"id": req_id, "error": {"kind": "unknown_method",
                                       "message": f"unknown method: {method}"}})
        return True
    if not isinstance(params, dict):
        _send({"id": req_id, "error": {"kind": "bad_params", "message": "params must be an object"}})
        return True
    ctx = _Ctx(req_id)
    try:
        with contextlib.redirect_stdout(sys.stderr):
            result = fn(params, ctx)
        _send({"id": req_id, "result": result})
    except HostError as e:
        _send({"id": req_id, "error": {"kind": e.kind, "message": str(e), "detail": e.detail}})
    except SystemExit as e:
        # ksh2vox's argparse paths exit; never let that end the host.
        _send({"id": req_id, "error": {"kind": "converter_exit",
                                       "message": f"the converter called exit({e.code})"}})
    except KeyboardInterrupt:
        _send({"id": req_id, "error": {"kind": "interrupted", "message": "interrupted"}})
    except Exception as e:
        tb = traceback.format_exc()
        print(tb, file=sys.stderr)
        _send({"id": req_id, "error": {"kind": "internal",
                                       "message": f"{type(e).__name__}: {e}",
                                       "detail": tb[-4000:]}})
    return not ctx.shutdown


def main(argv: list[str]) -> int:
    global _CONVERTER_DIR, _KSH2VOX, _LOAD_ERROR
    args = list(argv)
    if "--converter" in args:
        i = args.index("--converter")
        if i + 1 < len(args):
            _CONVERTER_DIR = os.path.abspath(args[i + 1])
    if sys.version_info[:2] < MIN_PYTHON:
        _LOAD_ERROR = (f"Python {sys.version.split()[0]} is too old; ksh2vox needs "
                       f"{MIN_PYTHON[0]}.{MIN_PYTHON[1]}+")
    elif _CONVERTER_DIR:
        try:
            _KSH2VOX = _load_converter(_CONVERTER_DIR)
        except HostError as e:
            _LOAD_ERROR = str(e)
        except BaseException as e:      # SystemExit at import included
            _LOAD_ERROR = f"importing ksh2vox failed: {type(e).__name__}: {e}"
            traceback.print_exc(file=sys.stderr)
    _send({"event": "ready", "host": HOST_VERSION, "protocol": PROTOCOL,
           "python": sys.version.split()[0],
           "converter_dir": _CONVERTER_DIR,
           "converter_version": getattr(_KSH2VOX, "__version__", None),
           "error": _LOAD_ERROR})
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        if not _handle(line):
            break
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
