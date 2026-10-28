"""WD14 Tagger (sd-scripts 同梱) による自動キャプション"""

from dataclasses import dataclass
from pathlib import Path

from . import dataset
from .config import TAGGER_SCRIPT, venv_python

TAGGER_MODELS = [
    "SmilingWolf/wd-swinv2-tagger-v3",
    "SmilingWolf/wd-vit-tagger-v3",
    "SmilingWolf/wd-convnext-tagger-v3",
    "SmilingWolf/wd-eva02-large-tagger-v3",
    "SmilingWolf/wd-v1-4-convnext-tagger-v2",
]


@dataclass
class CaptionOptions:
    model: str = TAGGER_MODELS[0]
    threshold: float = 0.35
    undesired_tags: str = ""
    overwrite: bool = False
    add_trigger: bool = True
    batch_size: int = 1


def build_tagger_command(sd_scripts_dir, image_dir, opts):
    # Tagger 側で model_dir/<repo_id> に保存されるので、モデルごとのフォルダ分けは不要
    model_dir = Path(sd_scripts_dir) / "wd14_tagger_model"
    cmd = [
        str(venv_python(sd_scripts_dir)),
        TAGGER_SCRIPT,
        str(Path(image_dir).resolve()),
        "--repo_id", opts.model,
        "--model_dir", str(model_dir),
        "--onnx",
        "--batch_size", str(opts.batch_size),
        "--thresh", format(opts.threshold, "g"),
        "--caption_extension", dataset.CAPTION_EXTENSION,
        "--remove_underscore",
        "--max_data_loader_n_workers", "0",
    ]
    if opts.undesired_tags.strip():
        cmd += ["--undesired_tags", opts.undesired_tags.strip()]
    return cmd


def finish_captioning(image_dir, snapshot, opts, trigger_word):
    """Tagger 実行後の後処理: 既存キャプションの復元とトリガーワード付与。トリガーを付けた件数を返す"""
    if not opts.overwrite:
        dataset.restore_captions(snapshot)
    if opts.add_trigger and trigger_word.strip():
        return dataset.apply_trigger_word_to_folder(image_dir, trigger_word, create_missing=False)
    return 0
