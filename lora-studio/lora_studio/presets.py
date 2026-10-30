"""presets/*.json の読み込み"""

import json
from dataclasses import dataclass, fields
from pathlib import Path

from .config import PATH_AND_DATASET_KEYS, TrainingConfig


@dataclass
class Preset:
    name: str
    description: str
    values: dict
    path: Path


def load_presets(preset_dir):
    """ファイル名順にプリセットを読み込む。壊れたファイルは (ファイル名, エラー) として返す"""
    allowed = {f.name for f in fields(TrainingConfig)} - PATH_AND_DATASET_KEYS
    presets, errors = [], []
    for path in sorted(Path(preset_dir).glob("*.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            values = data["config"]
            unknown = set(values) - allowed
            if unknown:
                raise ValueError(f"未対応の項目: {', '.join(sorted(unknown))}")
            TrainingConfig.from_dict(values)  # 型チェック
            presets.append(Preset(data["name"], data.get("description", ""), values, path))
        except (OSError, ValueError, KeyError, TypeError) as e:
            errors.append((path.name, str(e)))
    return presets, errors
