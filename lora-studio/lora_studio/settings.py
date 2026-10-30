"""前回の入力内容 (settings.json) の保存と読み込み"""

import json
import os
from pathlib import Path


def load_settings(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def save_settings(path, data):
    path = Path(path)
    tmp = path.with_suffix(".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(tmp, path)
