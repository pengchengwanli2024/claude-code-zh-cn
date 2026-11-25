"""Launch the native CLI and display offline translations of visible console labels."""
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
import argparse
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys

from offline import Translator, default_model

PLUGIN = Path(__file__).resolve().parents[1] / "plugins" / "zh-ui"
DECORATION = re.compile(r"^[\s│┃▎>❯›✔✓√⏵•✻✳]*(?:\d+[.)]\s+)?")
VERSION = re.compile(r"^(Welcome to Claude Code) (v[\d.]+)$")


def load_labels():
    source = (PLUGIN / "locales" / "zh-CN.js").read_text(encoding="utf-8")
    catalog = json.loads(source.removeprefix("export default "))
    return {en.strip(): zh.strip() for group in catalog.values() for en, zh in group.values()}


def visible_labels(lines, labels, include_unknown=False):
    """Join wrapped rows only for a complete known phrase. Never infer an approval scope."""
    clean = [DECORATION.sub("", line).strip().rstrip("│").strip() for line in lines]
    longest = max(map(len, labels))
    result = []
    i = 0
    while i < len(clean):
        line = clean[i]
        i += 1
        if not line:
            continue
        phrase = line
        best = (line, i) if line in labels else None
        end = i
        while end < len(clean) and clean[end] and len(phrase) < longest:
            phrase += " " + clean[end]
            end += 1
            if phrase in labels:
                best = phrase, end
        if best:
            en, i = best
            result.append((en, labels[en]))
            continue
        version = VERSION.fullmatch(line)
        if version:
            title, number = version.groups()
            result.append((line, labels[title] + " " + number))
            continue
        # An explicit separator delimits independent hints or menu columns.
        for cell in re.split(r"\s+·\s+|\s{2,}", line):
            cell = cell.strip()
            if cell in labels:
                result.append((cell, labels[cell]))
            elif include_unknown and re.search(r"[A-Za-z]", cell):
                result.append((cell, None))
    return list(dict.fromkeys(result))


class Coord(ctypes.Structure):
    _fields_ = [("x", ctypes.c_short), ("y", ctypes.c_short)]


class Rect(ctypes.Structure):
    _fields_ = [(field, ctypes.c_short) for field in ("left", "top", "right", "bottom")]


class BufferInfo(ctypes.Structure):
    _fields_ = [("size", Coord), ("cursor", Coord), ("attributes", wintypes.WORD),
                ("window", Rect), ("maximum", Coord)]


class ConsoleReader:
    def __init__(self):
        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        self.api.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                         wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        self.api.CreateFileW.restype = wintypes.HANDLE
        self.api.GetConsoleScreenBufferInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(BufferInfo)]
        self.api.ReadConsoleOutputCharacterW.argtypes = [wintypes.HANDLE, wintypes.LPWSTR, wintypes.DWORD,
                                                        Coord, ctypes.POINTER(wintypes.DWORD)]
        self.api.CloseHandle.argtypes = [wintypes.HANDLE]

    def rows(self):
        # Open the currently active buffer on each tick (the TUI can switch buffers).
        handle = self.api.CreateFileW("CONOUT$", 0x80000000, 3, None, 3, 0, None)
        if handle == ctypes.c_void_p(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            info = BufferInfo()
            if not self.api.GetConsoleScreenBufferInfo(handle, ctypes.byref(info)):
                raise ctypes.WinError(ctypes.get_last_error())
            rect = info.window
            width = rect.right - rect.left + 1
            buf = ctypes.create_unicode_buffer(width + 1)
            count = wintypes.DWORD()
            rows = []
            for y in range(rect.top, rect.bottom + 1):
                if not self.api.ReadConsoleOutputCharacterW(handle, buf, width, Coord(rect.left, y),
                                                            ctypes.byref(count)):
                    raise ctypes.WinError(ctypes.get_last_error())
                rows.append(buf[:count.value].replace("\x00", " ").rstrip())
            return rows
        finally:
            self.api.CloseHandle(handle)


def run_viewer(executable, model_directory=None):
    import tkinter as tk
    from tkinter.scrolledtext import ScrolledText

    labels = load_labels()
    reader = ConsoleReader()
    reader.rows()  # Fail before starting Claude if this is not an attached console.
    translator = Translator(model_directory) if model_directory else None
    window = tk.Tk()
    window.title("Claude Code · 中文对照")
    window.geometry("510x560")
    window.minsize(350, 240)
    tk.Label(window, text="当前终端的中英文对照", anchor="w", font=("Microsoft YaHei UI", 14, "bold"),
             padx=16, pady=10).pack(fill="x")
    text = ScrolledText(window, wrap="word", padx=14, pady=8, relief="flat",
                        font=("Microsoft YaHei UI", 11), state="disabled")
    text.pack(fill="both", expand=True)
    text.tag_configure("source", foreground="#555555", spacing3=8)
    text.tag_configure("translation", foreground="#14385c", spacing1=4)
    # Inherit the terminal handles directly. All keys and permission choices go to Claude.
    child = subprocess.Popen([str(executable), "--plugin-dir", str(PLUGIN)])
    previous = None
    pool = ThreadPoolExecutor(max_workers=1) if translator else None
    pending = None
    cache = OrderedDict()
    machine_error = ""
    last_frame = None

    def paint():
        nonlocal previous, last_frame, pending, machine_error
        if child.poll() is not None:
            window.destroy()
            return
        try:
            frame = reader.rows()
            if frame == last_frame and pending is None:
                window.after(350, paint)
                return
            last_frame = frame
            pairs = visible_labels(frame, labels, include_unknown=bool(translator))
            if pending and pending[1].done():
                lines, future = pending
                pending = None
                try:
                    cache.update(zip(lines, future.result()))
                    while len(cache) > 512:
                        cache.popitem(last=False)
                except Exception as error:
                    machine_error = "机器翻译暂不可用：" + str(error)
            unknown = [en for en, zh in pairs if zh is None and en not in cache]
            if unknown and not pending and not machine_error:
                pending = unknown, pool.submit(translator.batch, unknown)
            message = "当前可见行没有命中词条。"
        except OSError:
            last_frame = None
            pairs, message = [], "暂时读不到终端画面，将自动重试。"
        display = []
        for en, zh in pairs:
            if zh is None:
                zh = "机译 · " + cache[en] if en in cache else machine_error or "正在本地翻译…"
            display.append((en, zh))
        pairs = display
        current = pairs, message
        if current != previous:
            text.configure(state="normal")
            text.delete("1.0", "end")
            for en, zh in pairs:
                text.insert("end", zh + "\n", "translation")
                text.insert("end", en + "\n", "source")
            if not pairs:
                text.insert("end", message)
            text.configure(state="disabled")
            previous = current
        window.after(350, paint)

    # The console broadcasts Ctrl+C to both processes; Claude handles its own interrupts.
    original_handler = signal.signal(signal.SIGINT, lambda *_: None)
    try:
        window.after(0, paint)
        window.mainloop()
        return child.wait()  # Closing only the glossary leaves Claude usable.
    finally:
        signal.signal(signal.SIGINT, original_handler)
        if pool:
            pool.shutdown(wait=True, cancel_futures=True)


def main():
    parser = argparse.ArgumentParser(description="启动 Claude Code 与离线中文对照窗口")
    parser.add_argument("--claude", metavar="EXE", help="官方 claude.exe 的完整路径")
    parser.add_argument("--model", type=Path, help="用于翻译未收录文字的本地模型目录")
    parser.add_argument("--dictionary-only", action="store_true", help="仅使用内置词典")
    args = parser.parse_args()
    if os.name != "nt":
        parser.exit(2, "此对照窗口需要 Windows 控制台。\n")
    path = args.claude or shutil.which("claude.exe") or Path.home() / ".local/bin/claude.exe"
    if not Path(path).is_file() or Path(path).suffix.lower() != ".exe":
        parser.exit(2, "找不到 claude.exe。请用 --claude 指定原生可执行文件。\n")
    try:
        model = args.model or default_model()
        if args.model and not (model / "model/model.bin").is_file():
            parser.exit(2, "指定的模型目录缺少 model/model.bin。\n")
        active_model = model if not args.dictionary_only and (model / "model/model.bin").is_file() else None
        return run_viewer(Path(path).resolve(), active_model)
    except (OSError, ImportError, RuntimeError) as error:
        parser.exit(1, f"对照窗口启动失败。请在 Windows 终端使用带 Tk 的 Python 3.9+。\n{error}\n")


if __name__ == "__main__":
    sys.exit(main())
