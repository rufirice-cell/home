"""学習設定の定義と、sd-scripts 用のコマンド・データセット設定の生成"""

import json
import math
import os
import re
import shlex
import shutil
import subprocess
from dataclasses import asdict, dataclass, fields
from pathlib import Path

from . import dataset

TRAIN_SCRIPT = "sdxl_train_network.py"
TAGGER_SCRIPT = os.path.join("finetune", "tag_images_by_wd14_tagger.py")

OPTIMIZERS = ["AdamW8bit", "AdamW", "Adafactor", "Lion8bit", "Prodigy"]
LR_SCHEDULERS = ["cosine", "cosine_with_restarts", "constant", "constant_with_warmup", "linear"]
MIXED_PRECISIONS = ["bf16", "fp16"]
SAVE_PRECISIONS = ["fp16", "bf16", "float"]
ATTENTIONS = ["sdpa", "xformers", "none"]

# プリセットで上書きしてよい項目 (パスやデータセット固有の値は含めない)
PATH_AND_DATASET_KEYS = {
    "sd_scripts_dir", "pretrained_model", "vae", "image_dir", "output_dir", "output_name",
    "trigger_word", "class_word",
}


class ConfigError(ValueError):
    pass


@dataclass
class TrainingConfig:
    # --- パス ---
    sd_scripts_dir: str = ""
    pretrained_model: str = ""
    vae: str = ""
    image_dir: str = ""
    output_dir: str = ""
    output_name: str = "my_sdxl_lora"
    # --- データセット ---
    trigger_word: str = ""
    class_word: str = ""
    num_repeats: int = 10
    resolution: int = 1024
    enable_bucket: bool = True
    min_bucket_reso: int = 512
    max_bucket_reso: int = 2048
    shuffle_caption: bool = False
    keep_tokens: int = 1
    # --- 学習 ---
    batch_size: int = 1
    max_train_epochs: int = 10
    save_every_n_epochs: int = 2
    network_dim: int = 16
    network_alpha: int = 8
    unet_lr: float = 1e-4
    text_encoder_lr: float = 1e-5
    network_train_unet_only: bool = True
    optimizer_type: str = "AdamW8bit"
    lr_scheduler: str = "cosine"
    lr_warmup_steps: int = 0
    noise_offset: float = 0.0
    min_snr_gamma: float = 5.0
    seed: int = 42
    # --- メモリ・速度 ---
    mixed_precision: str = "bf16"
    save_precision: str = "fp16"
    full_bf16: bool = False
    fp8_base: bool = False
    gradient_checkpointing: bool = True
    cache_latents: bool = True
    cache_latents_to_disk: bool = True
    cache_text_encoder_outputs: bool = True
    attention: str = "sdpa"
    max_data_loader_n_workers: int = 1
    # --- サンプル画像・その他 ---
    sample_prompt: str = ""
    sample_every_n_epochs: int = 0
    extra_args: str = ""

    @classmethod
    def from_dict(cls, data):
        """dict から生成する。未知のキーは無視し、文字列は各フィールドの型に変換する"""
        kwargs = {}
        for f in fields(cls):
            if f.name in data:
                kwargs[f.name] = _coerce(f.name, f.type, data[f.name])
        return cls(**kwargs)

    def to_dict(self):
        return asdict(self)

    def updated(self, overrides):
        merged = self.to_dict()
        merged.update(overrides)
        return TrainingConfig.from_dict(merged)

    # --- 派生値 ---
    @property
    def python_exe(self):
        return venv_python(self.sd_scripts_dir)

    @property
    def work_dir(self):
        """生成した dataset.toml などを置くフォルダ"""
        return Path(self.output_dir) / "_lora_studio"

    @property
    def class_tokens(self):
        """キャプションファイルが無い画像に使われるテキスト"""
        return " ".join(t for t in (self.trigger_word.strip(), self.class_word.strip()) if t)


def _coerce(name, typ, value):
    try:
        if typ is bool:
            if isinstance(value, str):
                return value.strip().lower() in ("1", "true", "yes", "on")
            return bool(value)
        if typ is int:
            if isinstance(value, str):
                value = value.strip()
                return int(float(value)) if re.fullmatch(r"-?\d+(\.0*)?", value) else int(value)
            return int(value)
        if typ is float:
            return float(value.strip() if isinstance(value, str) else value)
        return "" if value is None else str(value)
    except (TypeError, ValueError):
        raise ConfigError(f"「{name}」の値 '{value}' を {typ.__name__} に変換できません") from None


def venv_python(sd_scripts_dir):
    base = Path(sd_scripts_dir) / "venv"
    if os.name == "nt":
        return base / "Scripts" / "python.exe"
    return base / "bin" / "python"


def estimate_steps(cfg, image_count):
    if image_count <= 0 or cfg.batch_size <= 0:
        return 0
    steps_per_epoch = math.ceil(image_count * cfg.num_repeats / cfg.batch_size)
    return steps_per_epoch * cfg.max_train_epochs


def validate(cfg):
    """(errors, warnings) を返す。errors が空なら学習を開始できる"""
    errors, warnings = [], []

    if not cfg.sd_scripts_dir or not (Path(cfg.sd_scripts_dir) / TRAIN_SCRIPT).is_file():
        errors.append(f"sd-scripts フォルダが正しくありません ({TRAIN_SCRIPT} が見つかりません)")
    elif not cfg.python_exe.is_file():
        errors.append(f"sd-scripts の仮想環境が見つかりません: {cfg.python_exe} (setup.bat を実行してください)")

    if not cfg.pretrained_model:
        errors.append("ベースモデルを指定してください")
    elif not Path(cfg.pretrained_model).exists():
        errors.append(f"ベースモデルが見つかりません: {cfg.pretrained_model}")
    if cfg.vae and not Path(cfg.vae).exists():
        errors.append(f"VAE が見つかりません: {cfg.vae}")

    images = dataset.list_images(cfg.image_dir)
    if not cfg.image_dir or not Path(cfg.image_dir).is_dir():
        errors.append("画像フォルダを指定してください")
    elif not images:
        errors.append("画像フォルダに画像 (png/jpg/webp/bmp) がありません")
    else:
        missing = dataset.count_missing_captions(cfg.image_dir)
        if missing and not cfg.class_tokens:
            errors.append(f"キャプションの無い画像が {missing} 枚あります。キャプションを生成するか、トリガーワードを入力してください")
        elif missing:
            warnings.append(f"キャプションの無い画像 {missing} 枚は「{cfg.class_tokens}」で学習されます")

    if not cfg.output_dir:
        errors.append("出力フォルダを指定してください")
    if not cfg.output_name or re.search(r'[\\/:*?"<>|]', cfg.output_name):
        errors.append("出力名が空か、ファイル名に使えない文字を含んでいます")

    for name in ("num_repeats", "resolution", "batch_size", "max_train_epochs", "network_dim", "network_alpha"):
        if getattr(cfg, name) <= 0:
            errors.append(f"「{name}」は 1 以上にしてください")
    if cfg.unet_lr <= 0:
        errors.append("学習率 (U-Net) は 0 より大きくしてください")
    if cfg.resolution % 64:
        errors.append("解像度は 64 の倍数にしてください")

    if cfg.cache_text_encoder_outputs:
        if not cfg.network_train_unet_only:
            errors.append("「Text Encoder 出力をキャッシュ」を使う場合は「U-Net のみ学習」をオンにしてください")
        if cfg.shuffle_caption:
            errors.append("「Text Encoder 出力をキャッシュ」と「キャプションをシャッフル」は同時に使えません")
    if cfg.full_bf16 and cfg.mixed_precision != "bf16":
        errors.append("full_bf16 は混合精度 bf16 のときのみ使えます")
    if cfg.cache_latents_to_disk and not cfg.cache_latents:
        warnings.append("cache_latents_to_disk は cache_latents がオンのときのみ有効です")

    if cfg.resolution > 1024:
        warnings.append("解像度 1024 超は VRAM 8GB では不足する可能性が高いです")
    if cfg.batch_size > 1 and not cfg.fp8_base:
        warnings.append("VRAM 8GB でバッチサイズ 2 以上は不足する可能性があります")
    if not cfg.gradient_checkpointing:
        warnings.append("gradient_checkpointing がオフです。VRAM 8GB ではほぼ確実にメモリ不足になります")

    try:
        shlex.split(cfg.extra_args)
    except ValueError as e:
        errors.append(f"追加引数を解析できません: {e}")

    return errors, warnings


def _toml_str(value):
    # JSON の文字列エスケープは TOML の basic string と互換
    return json.dumps(str(value))


def build_dataset_toml(cfg):
    lines = [
        "[general]",
        f"shuffle_caption = {str(cfg.shuffle_caption).lower()}",
        f"caption_extension = {_toml_str(dataset.CAPTION_EXTENSION)}",
        f"keep_tokens = {cfg.keep_tokens}",
        "",
        "[[datasets]]",
        f"resolution = {cfg.resolution}",
        f"batch_size = {cfg.batch_size}",
        f"enable_bucket = {str(cfg.enable_bucket).lower()}",
    ]
    if cfg.enable_bucket:
        lines += [
            f"min_bucket_reso = {cfg.min_bucket_reso}",
            f"max_bucket_reso = {cfg.max_bucket_reso}",
            "bucket_reso_steps = 64",
        ]
    lines += [
        "",
        "  [[datasets.subsets]]",
        f"  image_dir = {_toml_str(Path(cfg.image_dir).resolve())}",
        f"  num_repeats = {cfg.num_repeats}",
    ]
    if cfg.class_tokens:
        lines.append(f"  class_tokens = {_toml_str(cfg.class_tokens)}")
    return "\n".join(lines) + "\n"


def build_train_command(cfg, dataset_config_path, sample_prompts_path=None):
    """accelerate 経由で sdxl_train_network.py を起動するコマンドを返す"""
    cmd = [
        str(cfg.python_exe), "-m", "accelerate.commands.launch",
        "--num_processes", "1",
        "--num_machines", "1",
        "--mixed_precision", cfg.mixed_precision,
        "--dynamo_backend", "no",
        "--num_cpu_threads_per_process", "1",
        TRAIN_SCRIPT,
        "--pretrained_model_name_or_path", str(cfg.pretrained_model),
        "--dataset_config", str(dataset_config_path),
        "--output_dir", str(cfg.output_dir),
        "--output_name", cfg.output_name,
        "--save_model_as", "safetensors",
        "--network_module", "networks.lora",
        "--network_dim", str(cfg.network_dim),
        "--network_alpha", str(cfg.network_alpha),
        "--learning_rate", _fmt(cfg.unet_lr),
        "--unet_lr", _fmt(cfg.unet_lr),
        "--optimizer_type", cfg.optimizer_type,
        "--lr_scheduler", cfg.lr_scheduler,
        "--lr_warmup_steps", str(cfg.lr_warmup_steps),
        "--max_train_epochs", str(cfg.max_train_epochs),
        "--save_every_n_epochs", str(cfg.save_every_n_epochs),
        "--mixed_precision", cfg.mixed_precision,
        "--save_precision", cfg.save_precision,
        "--seed", str(cfg.seed),
        "--max_data_loader_n_workers", str(cfg.max_data_loader_n_workers),
    ]
    if cfg.network_train_unet_only:
        cmd.append("--network_train_unet_only")
    else:
        cmd += ["--text_encoder_lr", _fmt(cfg.text_encoder_lr)]
    if cfg.max_data_loader_n_workers > 0:
        cmd.append("--persistent_data_loader_workers")
    if cfg.optimizer_type == "Adafactor":
        cmd += ["--optimizer_args", "scale_parameter=False", "relative_step=False", "warmup_init=False"]
    if cfg.vae:
        cmd += ["--vae", str(cfg.vae)]
    if cfg.mixed_precision == "fp16":
        cmd.append("--no_half_vae")  # SDXL の VAE は fp16 だと NaN になりやすい

    flags = [
        ("gradient_checkpointing", "--gradient_checkpointing"),
        ("cache_latents", "--cache_latents"),
        ("cache_text_encoder_outputs", "--cache_text_encoder_outputs"),
        ("full_bf16", "--full_bf16"),
        ("fp8_base", "--fp8_base"),
    ]
    cmd += [flag for name, flag in flags if getattr(cfg, name)]
    if cfg.cache_latents and cfg.cache_latents_to_disk:
        cmd.append("--cache_latents_to_disk")
    if cfg.attention in ("sdpa", "xformers"):
        cmd.append(f"--{cfg.attention}")
    if cfg.noise_offset > 0:
        cmd += ["--noise_offset", _fmt(cfg.noise_offset)]
    if cfg.min_snr_gamma > 0:
        cmd += ["--min_snr_gamma", _fmt(cfg.min_snr_gamma)]
    if sample_prompts_path and cfg.sample_every_n_epochs > 0:
        cmd += [
            "--sample_prompts", str(sample_prompts_path),
            "--sample_every_n_epochs", str(cfg.sample_every_n_epochs),
            "--sample_sampler", "euler_a",
        ]
    cmd += shlex.split(cfg.extra_args)
    return cmd


def _fmt(value):
    return format(value, "g")


def prepare_training(cfg):
    """作業フォルダに dataset.toml などを書き出し、実行コマンドを返す"""
    work_dir = cfg.work_dir
    work_dir.mkdir(parents=True, exist_ok=True)
    dataset_path = work_dir / "dataset.toml"
    dataset_path.write_text(build_dataset_toml(cfg), encoding="utf-8")

    sample_path = None
    if cfg.sample_prompt.strip() and cfg.sample_every_n_epochs > 0:
        sample_path = work_dir / "sample_prompts.txt"
        prompt = cfg.sample_prompt.strip()
        if " --" not in prompt:
            prompt += f" --w {cfg.resolution} --h {cfg.resolution} --s 28 --l 7"
        sample_path.write_text(prompt + "\n", encoding="utf-8")

    cmd = build_train_command(cfg, dataset_path, sample_path)
    (work_dir / "config.json").write_text(json.dumps(cfg.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")
    (work_dir / "command.txt").write_text(format_command(cmd) + "\n", encoding="utf-8")
    return cmd


def format_command(cmd):
    if os.name == "nt":
        return subprocess.list2cmdline(cmd)
    return shlex.join(cmd)


def check_environment(cfg):
    """[(ok, メッセージ)] を返す"""
    results = []
    sd = Path(cfg.sd_scripts_dir) if cfg.sd_scripts_dir else None
    results.append((bool(sd and (sd / TRAIN_SCRIPT).is_file()), f"sd-scripts: {cfg.sd_scripts_dir or '(未設定)'}"))
    results.append((bool(sd) and cfg.python_exe.is_file(), f"Python 仮想環境: {cfg.python_exe if sd else '(未設定)'}"))
    results.append((bool(sd and (sd / TAGGER_SCRIPT).is_file()), "WD14 Tagger スクリプト"))
    results.append((bool(cfg.pretrained_model) and Path(cfg.pretrained_model).is_file(),
                    f"ベースモデル: {cfg.pretrained_model or '(未設定)'}"))
    if cfg.vae:
        results.append((Path(cfg.vae).is_file(), f"VAE: {cfg.vae}"))
    results.append(gpu_info())
    return results


def gpu_info():
    exe = shutil.which("nvidia-smi")
    if not exe:
        return (False, "nvidia-smi が見つかりません (NVIDIA ドライバを確認してください)")
    try:
        out = subprocess.run(
            [exe, "--query-gpu=name,memory.total,driver_version", "--format=csv,noheader"],
            capture_output=True, text=True, timeout=10,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except (OSError, subprocess.SubprocessError) as e:
        return (False, f"GPU 情報を取得できません: {e}")
    if out.returncode != 0:
        return (False, f"GPU 情報を取得できません: {out.stderr.strip()}")
    return (True, "GPU: " + out.stdout.strip().replace("\n", " / "))
