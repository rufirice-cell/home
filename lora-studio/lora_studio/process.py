"""外部プロセスの実行と、tqdm 出力からの進捗解析"""

import os
import re
import signal
import subprocess
import threading
from dataclasses import dataclass

_PROGRESS_RE = re.compile(
    r"(?P<current>\d+)/(?P<total>\d+)\s*\[(?P<elapsed>[\d:]+)<(?P<remaining>[\d:?]+)"
    r"(?:,\s*(?P<rate>\?|[\d.]+)\s*(?P<unit>it/s|s/it))?"
)
_LOSS_RE = re.compile(r"avr_loss=(?P<loss>[-+\d.eE]+|nan|inf)")
_EPOCH_RE = re.compile(r"^\s*epoch (?P<epoch>\d+)/(?P<total>\d+)\s*$")


@dataclass
class Progress:
    current: int
    total: int
    elapsed: str = ""
    remaining: str = ""
    rate: str = ""
    loss: float | None = None
    is_training_step: bool = False

    @property
    def fraction(self):
        return self.current / self.total if self.total else 0.0


def parse_progress(line):
    """tqdm の進捗行を解析する。進捗行でなければ None"""
    m = _PROGRESS_RE.search(line)
    if not m:
        return None
    rate = ""
    if m.group("rate") and m.group("rate") != "?":
        rate = f"{m.group('rate')} {m.group('unit')}"
    loss = None
    lm = _LOSS_RE.search(line)
    if lm:
        try:
            loss = float(lm.group("loss"))
        except ValueError:
            pass
    return Progress(
        current=int(m.group("current")),
        total=int(m.group("total")),
        elapsed=m.group("elapsed"),
        remaining="" if m.group("remaining") == "?" else m.group("remaining"),
        rate=rate,
        loss=loss,
        is_training_step=line.lstrip().startswith("steps:"),
    )


def parse_epoch(line):
    """sd-scripts の "epoch 3/10" 行を解析して (3, 10) を返す"""
    m = _EPOCH_RE.match(line)
    return (int(m.group("epoch")), int(m.group("total"))) if m else None


def _decode(data):
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        try:
            return data.decode("cp932")  # Windows のコマンドが返すメッセージ用
        except UnicodeDecodeError:
            return data.decode("utf-8", errors="replace")


class LineSplitter:
    """バイト列を行に分割する。\\r で終わる行 (tqdm の上書き更新) は is_overwrite=True で返す"""

    def __init__(self):
        self._buf = b""

    def feed(self, data):
        self._buf += data
        lines = []
        while True:
            positions = [i for i in (self._buf.find(b"\n"), self._buf.find(b"\r")) if i >= 0]
            if not positions:
                break
            i = min(positions)
            if self._buf[i:i + 1] == b"\r":
                if i + 1 >= len(self._buf):
                    break  # 次が \n かどうか分かるまで待つ
                if self._buf[i + 1:i + 2] == b"\n":
                    lines.append((_decode(self._buf[:i]), False))
                    self._buf = self._buf[i + 2:]
                else:
                    lines.append((_decode(self._buf[:i]), True))
                    self._buf = self._buf[i + 1:]
            else:
                lines.append((_decode(self._buf[:i]), False))
                self._buf = self._buf[i + 1:]
        return lines

    def flush(self):
        rest, self._buf = self._buf.rstrip(b"\r"), b""
        return [(_decode(rest), False)] if rest else []


class ProcessRunner:
    """サブプロセスを起動し、出力を 1 行ずつコールバックへ渡す (コールバックは別スレッドから呼ばれる)"""

    def __init__(self, cmd, cwd, on_output, on_exit, env=None):
        self.cmd = [str(c) for c in cmd]
        self.cwd = str(cwd)
        self.on_output = on_output
        self.on_exit = on_exit
        self.env = env
        self.proc = None
        self.stopped = False

    def start(self):
        env = dict(os.environ if self.env is None else self.env)
        env.update(PYTHONUNBUFFERED="1", PYTHONIOENCODING="utf-8", PYTHONUTF8="1")
        kwargs = {}
        if os.name == "nt":
            kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW
        else:
            kwargs["start_new_session"] = True
        self.proc = subprocess.Popen(
            self.cmd, cwd=self.cwd, env=env,
            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            **kwargs,
        )
        threading.Thread(target=self._read_output, daemon=True).start()

    def _read_output(self):
        splitter = LineSplitter()
        stream = self.proc.stdout
        while True:
            chunk = stream.read1(65536)
            if not chunk:
                break
            for text, overwrite in splitter.feed(chunk):
                self.on_output(text, overwrite)
        for text, overwrite in splitter.flush():
            self.on_output(text, overwrite)
        stream.close()
        self.on_exit(self.proc.wait())

    def is_running(self):
        return self.proc is not None and self.proc.poll() is None

    def stop(self):
        """プロセスツリーごと終了させる (accelerate は子プロセスで学習を動かすため)"""
        if not self.is_running():
            return
        self.stopped = True
        if os.name == "nt":
            subprocess.run(
                ["taskkill", "/PID", str(self.proc.pid), "/T", "/F"],
                capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW,
            )
        else:
            try:
                os.killpg(self.proc.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
