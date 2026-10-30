"""LoRA Studio のメインウィンドウ (tkinter)"""

import os
import queue
import subprocess
import sys
import tkinter as tk
import tkinter.font as tkfont
from dataclasses import fields
from pathlib import Path
from tkinter import filedialog, messagebox, scrolledtext, ttk

from . import __version__, captioning, config, dataset
from .config import ConfigError, TrainingConfig
from .presets import load_presets
from .process import ProcessRunner, parse_epoch, parse_progress
from .settings import load_settings, save_settings

try:
    from PIL import Image, ImageTk
except ImportError:  # プレビューが出ないだけで他の機能は動く
    Image = ImageTk = None

APP_DIR = Path(__file__).resolve().parent.parent
MAX_LOG_LINES = 5000

LABELS = {
    "sd_scripts_dir": "sd-scripts フォルダ",
    "pretrained_model": "ベースモデル (SDXL .safetensors)",
    "vae": "VAE (任意)",
    "image_dir": "画像フォルダ",
    "output_dir": "出力フォルダ",
    "output_name": "出力名 (LoRA ファイル名)",
    "trigger_word": "トリガーワード",
    "class_word": "クラスワード (任意)",
    "num_repeats": "繰り返し回数",
    "resolution": "解像度",
    "enable_bucket": "アスペクト比バケットを使う",
    "min_bucket_reso": "バケット最小解像度",
    "max_bucket_reso": "バケット最大解像度",
    "shuffle_caption": "キャプションをシャッフル",
    "keep_tokens": "先頭固定トークン数",
    "batch_size": "バッチサイズ",
    "max_train_epochs": "エポック数",
    "save_every_n_epochs": "N エポックごとに保存",
    "network_dim": "Network Dim (rank)",
    "network_alpha": "Network Alpha",
    "unet_lr": "学習率 (U-Net)",
    "text_encoder_lr": "学習率 (Text Encoder)",
    "network_train_unet_only": "U-Net のみ学習 (省メモリ)",
    "optimizer_type": "オプティマイザ",
    "lr_scheduler": "学習率スケジューラ",
    "lr_warmup_steps": "ウォームアップステップ数",
    "noise_offset": "Noise Offset",
    "min_snr_gamma": "Min SNR Gamma",
    "seed": "シード",
    "mixed_precision": "混合精度",
    "save_precision": "保存精度",
    "full_bf16": "full_bf16 (省メモリ)",
    "fp8_base": "fp8_base (ベースモデルを FP8 化・大幅省メモリ)",
    "gradient_checkpointing": "Gradient Checkpointing (必須級)",
    "cache_latents": "Latent をキャッシュ",
    "cache_latents_to_disk": "Latent キャッシュをディスクに保存",
    "cache_text_encoder_outputs": "Text Encoder 出力をキャッシュ",
    "attention": "Attention 実装",
    "max_data_loader_n_workers": "DataLoader ワーカー数",
    "sample_prompt": "サンプル画像プロンプト",
    "sample_every_n_epochs": "サンプル生成間隔 (エポック, 0=なし)",
    "extra_args": "追加引数 (上級者向け)",
}

HINTS = {
    "trigger_word": "LoRA を呼び出すための固有の単語。既存の単語と被らない造語がおすすめ (例: sks_girl)",
    "class_word": "対象の種類 (例: 1girl, dog)。キャプションが無い画像に使われます",
    "num_repeats": "1 エポックで各画像を何回学習するか。画像 20 枚なら 10 前後が目安",
    "network_dim": "LoRA の表現力。大きいほど細部を覚えるがファイルが大きく過学習しやすい。キャラ・画風なら 16〜32",
    "network_alpha": "学習の強さの補正。Dim の半分程度が一般的",
    "unet_lr": "大きすぎると絵が崩れ、小さすぎると覚えない。AdamW8bit なら 1e-4 前後",
    "fp8_base": "Out of Memory が出る場合にオン。わずかに画質が落ちることがあります",
    "cache_text_encoder_outputs": "VRAM 8GB では必須級。オンにすると「シャッフル」と Text Encoder 学習は使えません",
    "sample_prompt": "例: sks_girl, 1girl, smile, upper body  (空欄なら生成しません。生成中は学習が止まります)",
    "extra_args": "sdxl_train_network.py にそのまま渡す引数。例: --max_grad_norm 1.0",
}

CHOICES = {
    "optimizer_type": config.OPTIMIZERS,
    "lr_scheduler": config.LR_SCHEDULERS,
    "mixed_precision": config.MIXED_PRECISIONS,
    "save_precision": config.SAVE_PRECISIONS,
    "attention": config.ATTENTIONS,
}
# 一覧外の値も入力できるコンボボックス
EDITABLE_CHOICES = {"optimizer_type", "lr_scheduler"}

TRAIN_GROUPS = [
    ("基本", ["max_train_epochs", "save_every_n_epochs", "batch_size", "resolution",
              "network_dim", "network_alpha", "unet_lr", "text_encoder_lr", "network_train_unet_only",
              "optimizer_type", "lr_scheduler", "lr_warmup_steps"]),
    ("メモリ・速度 (VRAM 8GB 向け)", ["mixed_precision", "save_precision", "attention",
                                     "gradient_checkpointing", "cache_latents", "cache_latents_to_disk",
                                     "cache_text_encoder_outputs", "fp8_base", "full_bf16",
                                     "max_data_loader_n_workers"]),
    ("詳細", ["enable_bucket", "min_bucket_reso", "max_bucket_reso", "shuffle_caption", "keep_tokens",
              "noise_offset", "min_snr_gamma", "seed", "sample_prompt", "sample_every_n_epochs", "extra_args"]),
]

OOM_MARKERS = ("CUDA out of memory", "OutOfMemoryError", "CUBLAS_STATUS_ALLOC_FAILED")


class Tooltip:
    def __init__(self, widget, text):
        self.widget, self.text, self.tip = widget, text, None
        widget.bind("<Enter>", self.show, add="+")
        widget.bind("<Leave>", self.hide, add="+")

    def show(self, _event=None):
        if self.tip:
            return
        x = self.widget.winfo_rootx() + 20
        y = self.widget.winfo_rooty() + self.widget.winfo_height() + 4
        self.tip = tk.Toplevel(self.widget)
        self.tip.wm_overrideredirect(True)
        self.tip.wm_geometry(f"+{x}+{y}")
        tk.Label(self.tip, text=self.text, background="#ffffe0", relief="solid", borderwidth=1,
                 wraplength=420, justify="left", padx=6, pady=3).pack()

    def hide(self, _event=None):
        if self.tip:
            self.tip.destroy()
            self.tip = None


class ScrollableFrame(ttk.Frame):
    def __init__(self, parent):
        super().__init__(parent)
        canvas = tk.Canvas(self, highlightthickness=0)
        bar = ttk.Scrollbar(self, orient="vertical", command=canvas.yview)
        self.inner = ttk.Frame(canvas)
        self.inner.bind("<Configure>", lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        window = canvas.create_window((0, 0), window=self.inner, anchor="nw")
        canvas.bind("<Configure>", lambda e: canvas.itemconfigure(window, width=e.width))
        canvas.configure(yscrollcommand=bar.set)
        canvas.pack(side="left", fill="both", expand=True)
        bar.pack(side="right", fill="y")

        def on_wheel(event):
            canvas.yview_scroll(int(-event.delta / 120) or (-1 if event.delta > 0 else 1), "units")

        self.inner.bind("<Enter>", lambda e: canvas.bind_all("<MouseWheel>", on_wheel))
        self.inner.bind("<Leave>", lambda e: canvas.unbind_all("<MouseWheel>"))


class LoraStudioApp:
    def __init__(self, root):
        self.root = root
        self.settings_path = APP_DIR / "settings.json"
        self.settings = load_settings(self.settings_path)
        self.presets, preset_errors = load_presets(APP_DIR / "presets")
        self.vars = {}
        self.events = queue.Queue()
        self.runner = None
        self.job = None
        self.job_context = {}
        self.images = []
        self.current_image = None
        self.preview_photo = None

        root.title(f"LoRA Studio {__version__} - SDXL LoRA 学習")
        root.geometry("1100x860")
        root.minsize(900, 640)
        root.protocol("WM_DELETE_WINDOW", self.on_close)

        self._create_vars()
        self._build_ui()
        self._load_initial_values()
        for name, err in preset_errors:
            self.log(f"[警告] プリセット {name} を読み込めません: {err}")
        self.refresh_images()
        self.update_summary()
        root.after(100, self._poll_events)

    # ------------------------------------------------------------------ 変数
    def _create_vars(self):
        for f in fields(TrainingConfig):
            self.vars[f.name] = tk.BooleanVar() if f.type is bool else tk.StringVar()
        caption = self.settings.get("caption", {})
        defaults = captioning.CaptionOptions()
        self.cap_model = tk.StringVar(value=caption.get("model", defaults.model))
        self.cap_threshold = tk.StringVar(value=str(caption.get("threshold", defaults.threshold)))
        self.cap_undesired = tk.StringVar(value=caption.get("undesired_tags", defaults.undesired_tags))
        self.cap_overwrite = tk.BooleanVar(value=caption.get("overwrite", defaults.overwrite))
        self.cap_add_trigger = tk.BooleanVar(value=caption.get("add_trigger", defaults.add_trigger))
        self.preset_var = tk.StringVar()
        self.image_count_var = tk.StringVar(value="")
        self.summary_var = tk.StringVar()
        self.status_var = tk.StringVar(value="待機中")
        self.step_var = tk.StringVar(value="- / -")
        self.epoch_var = tk.StringVar(value="- / -")
        self.loss_var = tk.StringVar(value="-")
        self.time_var = tk.StringVar(value="-")
        self.speed_var = tk.StringVar(value="-")
        self.live_line_var = tk.StringVar()

    def _load_initial_values(self):
        try:
            cfg = TrainingConfig.from_dict(self.settings.get("config", {}))
        except ConfigError:
            cfg = TrainingConfig()
        if not cfg.sd_scripts_dir:
            cfg.sd_scripts_dir = str(APP_DIR / "sd-scripts")
        if not cfg.output_dir:
            cfg.output_dir = str(APP_DIR / "output")
        self.set_config(cfg)
        names = [p.name for p in self.presets]
        saved = self.settings.get("preset")
        if names:
            self.preset_var.set(saved if saved in names else names[0])
            self._on_preset_selected()
            if "config" not in self.settings:  # 初回起動時は推奨プリセットを適用
                self.apply_preset(confirm=False)

    def set_config(self, cfg):
        for name, value in cfg.to_dict().items():
            var = self.vars[name]
            if isinstance(var, tk.BooleanVar):
                var.set(bool(value))
            else:
                var.set(format(value, "g") if isinstance(value, float) else str(value))

    def raw_config(self):
        return {name: var.get() for name, var in self.vars.items()}

    def get_config(self):
        return TrainingConfig.from_dict(self.raw_config())

    def caption_options(self):
        try:
            threshold = float(self.cap_threshold.get())
        except ValueError:
            raise ConfigError("しきい値には 0〜1 の数値を入力してください") from None
        if not 0 < threshold < 1:
            raise ConfigError("しきい値には 0〜1 の数値を入力してください")
        return captioning.CaptionOptions(
            model=self.cap_model.get().strip(),
            threshold=threshold,
            undesired_tags=self.cap_undesired.get(),
            overwrite=self.cap_overwrite.get(),
            add_trigger=self.cap_add_trigger.get(),
        )

    # ------------------------------------------------------------------ UI 構築
    def _build_ui(self):
        paned = ttk.PanedWindow(self.root, orient="vertical")
        paned.pack(fill="both", expand=True, padx=6, pady=6)

        self.notebook = ttk.Notebook(paned)
        paned.add(self.notebook, weight=4)
        self.tab_env = ttk.Frame(self.notebook, padding=10)
        self.tab_data = ttk.Frame(self.notebook, padding=10)
        self.tab_train = ttk.Frame(self.notebook, padding=4)
        self.tab_run = ttk.Frame(self.notebook, padding=10)
        self.notebook.add(self.tab_env, text=" 1. 環境設定 ")
        self.notebook.add(self.tab_data, text=" 2. データセット ")
        self.notebook.add(self.tab_train, text=" 3. 学習設定 ")
        self.notebook.add(self.tab_run, text=" 4. 学習実行 ")
        self.notebook.bind("<<NotebookTabChanged>>", lambda e: self.update_summary())

        self._build_env_tab()
        self._build_data_tab()
        self._build_train_tab()
        self._build_run_tab()

        log_frame = ttk.LabelFrame(paned, text="ログ", padding=4)
        paned.add(log_frame, weight=1)
        ttk.Label(log_frame, textvariable=self.live_line_var, foreground="#555").pack(fill="x")
        self.log_text = scrolledtext.ScrolledText(log_frame, height=10, wrap="none", state="disabled",
                                                  font=("Consolas", 9))
        self.log_text.pack(fill="both", expand=True)

    def _path_row(self, parent, row, key, mode, filetypes=None):
        ttk.Label(parent, text=LABELS[key]).grid(row=row, column=0, sticky="w", padx=(0, 8), pady=3)
        entry = ttk.Entry(parent, textvariable=self.vars[key])
        entry.grid(row=row, column=1, sticky="ew", pady=3)
        ttk.Button(parent, text="参照...", command=lambda: self._browse(key, mode, filetypes)).grid(
            row=row, column=2, padx=(6, 0), pady=3)
        if key in HINTS:
            Tooltip(entry, HINTS[key])
        return entry

    def _browse(self, key, mode, filetypes):
        current = self.vars[key].get()
        initial = current if os.path.isdir(current) else (os.path.dirname(current) or str(APP_DIR))
        if mode == "dir":
            path = filedialog.askdirectory(initialdir=initial, title=LABELS[key])
        else:
            path = filedialog.askopenfilename(initialdir=initial, title=LABELS[key],
                                              filetypes=filetypes or [("すべて", "*.*")])
        if path:
            self.vars[key].set(os.path.normpath(path))
            if key == "image_dir":
                self.refresh_images()
            self.update_summary()

    def _field(self, parent, row, key):
        var = self.vars[key]
        if isinstance(var, tk.BooleanVar):
            widget = ttk.Checkbutton(parent, text=LABELS[key], variable=var, command=self.update_summary)
            widget.grid(row=row, column=0, columnspan=2, sticky="w", pady=2)
        else:
            ttk.Label(parent, text=LABELS[key]).grid(row=row, column=0, sticky="w", padx=(0, 8), pady=2)
            if key in CHOICES:
                state = "normal" if key in EDITABLE_CHOICES else "readonly"
                widget = ttk.Combobox(parent, textvariable=var, values=CHOICES[key], state=state, width=24)
            else:
                width = 60 if key in ("sample_prompt", "extra_args") else 26
                widget = ttk.Entry(parent, textvariable=var, width=width)
            widget.grid(row=row, column=1, sticky="w", pady=2)
            widget.bind("<FocusOut>", lambda e: self.update_summary(), add="+")
        if key in HINTS:
            Tooltip(widget, HINTS[key])
        return widget

    def _build_env_tab(self):
        tab = self.tab_env
        tab.columnconfigure(1, weight=1)
        self._path_row(tab, 0, "sd_scripts_dir", "dir")
        self._path_row(tab, 1, "pretrained_model", "file",
                       [("Safetensors", "*.safetensors"), ("Checkpoint", "*.ckpt"), ("すべて", "*.*")])
        self._path_row(tab, 2, "vae", "file", [("Safetensors", "*.safetensors"), ("すべて", "*.*")])

        buttons = ttk.Frame(tab)
        buttons.grid(row=3, column=0, columnspan=3, sticky="w", pady=(10, 4))
        ttk.Button(buttons, text="環境チェック", command=self.check_environment).pack(side="left")
        ttk.Button(buttons, text="sd-scripts フォルダを開く",
                   command=lambda: open_path(self.vars["sd_scripts_dir"].get())).pack(side="left", padx=6)

        help_text = (
            "使い方\n"
            "  1. setup.bat を実行すると、このフォルダ内に sd-scripts と Python 仮想環境が作られます。\n"
            "  2. 学習のベースにする SDXL モデル (例: sd_xl_base_1.0.safetensors や Illustrious / Pony 系) を指定します。\n"
            "  3.「環境チェック」で全項目が OK になれば準備完了です。\n"
            "  4.「データセット」タブで画像フォルダを選び、キャプションを付けます。\n"
            "  5.「学習設定」タブでプリセットを選び、「学習実行」タブで開始します。\n\n"
            "VAE は通常空欄で構いません (モデルに内蔵された VAE を使います)。"
        )
        ttk.Label(tab, text=help_text, justify="left", foreground="#333").grid(
            row=4, column=0, columnspan=3, sticky="w", pady=(16, 0))

    def _build_data_tab(self):
        tab = self.tab_data
        tab.columnconfigure(0, weight=1)
        tab.rowconfigure(2, weight=1)

        top = ttk.Frame(tab)
        top.grid(row=0, column=0, sticky="ew")
        top.columnconfigure(1, weight=1)
        self._path_row(top, 0, "image_dir", "dir")
        ttk.Button(top, text="再読み込み", command=self.refresh_images).grid(row=0, column=3, padx=(6, 0))
        ttk.Label(top, textvariable=self.image_count_var, foreground="#0a5").grid(
            row=1, column=1, sticky="w")
        words = ttk.Frame(top)
        words.grid(row=2, column=0, columnspan=4, sticky="w", pady=(4, 0))
        for col, key in enumerate(("trigger_word", "class_word", "num_repeats")):
            ttk.Label(words, text=LABELS[key]).grid(row=0, column=col * 2, sticky="w", padx=(0 if col == 0 else 16, 6))
            entry = ttk.Entry(words, textvariable=self.vars[key], width=10 if key == "num_repeats" else 22)
            entry.grid(row=0, column=col * 2 + 1)
            entry.bind("<FocusOut>", lambda e: self.update_summary(), add="+")
            Tooltip(entry, HINTS[key])

        cap = ttk.LabelFrame(tab, text="自動キャプション (WD14 Tagger)", padding=8)
        cap.grid(row=1, column=0, sticky="ew", pady=8)
        ttk.Label(cap, text="モデル").grid(row=0, column=0, sticky="w")
        ttk.Combobox(cap, textvariable=self.cap_model, values=captioning.TAGGER_MODELS, width=40).grid(
            row=0, column=1, sticky="w", padx=6)
        ttk.Label(cap, text="しきい値").grid(row=0, column=2, sticky="w", padx=(12, 0))
        thresh = ttk.Entry(cap, textvariable=self.cap_threshold, width=8)
        thresh.grid(row=0, column=3, sticky="w", padx=6)
        Tooltip(thresh, "タグを採用する確信度。小さいほどタグが増えます (0.3〜0.4 が目安)")
        ttk.Label(cap, text="除外タグ").grid(row=1, column=0, sticky="w", pady=(6, 0))
        undesired = ttk.Entry(cap, textvariable=self.cap_undesired, width=60)
        undesired.grid(row=1, column=1, columnspan=3, sticky="w", padx=6, pady=(6, 0))
        Tooltip(undesired, "付けたくないタグをカンマ区切りで (例: simple background, white background)。"
                           "キャラ学習では髪色・目の色など「LoRA に覚えさせたい特徴」を除外すると効果的です")
        opts = ttk.Frame(cap)
        opts.grid(row=2, column=0, columnspan=4, sticky="w", pady=(6, 0))
        ttk.Checkbutton(opts, text="既存のキャプションを上書きする", variable=self.cap_overwrite).pack(side="left")
        ttk.Checkbutton(opts, text="トリガーワードを先頭に追加", variable=self.cap_add_trigger).pack(
            side="left", padx=12)
        btns = ttk.Frame(cap)
        btns.grid(row=3, column=0, columnspan=4, sticky="w", pady=(8, 0))
        self.caption_button = ttk.Button(btns, text="キャプション生成", command=self.start_captioning)
        self.caption_button.pack(side="left")
        ttk.Button(btns, text="トリガーワードのみ付与", command=self.apply_trigger_only).pack(side="left", padx=6)
        ttk.Label(btns, text="※ 初回はモデルのダウンロードに数分かかります", foreground="#777").pack(
            side="left", padx=6)

        editor = ttk.LabelFrame(tab, text="キャプション確認・編集", padding=6)
        editor.grid(row=2, column=0, sticky="nsew")
        editor.columnconfigure(2, weight=1)
        editor.rowconfigure(0, weight=1)
        self.image_list = tk.Listbox(editor, width=34, exportselection=False)
        self.image_list.grid(row=0, column=0, sticky="ns")
        list_bar = ttk.Scrollbar(editor, orient="vertical", command=self.image_list.yview)
        list_bar.grid(row=0, column=1, sticky="ns")
        self.image_list.configure(yscrollcommand=list_bar.set)
        self.image_list.bind("<<ListboxSelect>>", self._on_image_selected)

        right = ttk.Frame(editor)
        right.grid(row=0, column=2, sticky="nsew", padx=(8, 0))
        right.columnconfigure(1, weight=1)
        right.rowconfigure(0, weight=1)
        self.preview_label = ttk.Label(right, text="(画像を選択)", anchor="center", width=30)
        self.preview_label.grid(row=0, column=0, sticky="n")
        self.caption_text = tk.Text(right, height=8, wrap="word", undo=True)
        self.caption_text.grid(row=0, column=1, sticky="nsew", padx=(8, 0))
        self.caption_text.bind("<Control-s>", lambda e: (self.save_current_caption(), "break")[1])
        ttk.Button(right, text="保存 (Ctrl+S)", command=self.save_current_caption).grid(
            row=1, column=1, sticky="e", pady=(4, 0))

    def _build_train_tab(self):
        top = ttk.Frame(self.tab_train, padding=6)
        top.pack(fill="x")
        ttk.Label(top, text="プリセット").grid(row=0, column=0, sticky="w")
        self.preset_combo = ttk.Combobox(top, textvariable=self.preset_var, state="readonly", width=46,
                                         values=[p.name for p in self.presets])
        self.preset_combo.grid(row=0, column=1, sticky="w", padx=6)
        self.preset_combo.bind("<<ComboboxSelected>>", self._on_preset_selected)
        ttk.Button(top, text="プリセットを適用", command=self.apply_preset).grid(row=0, column=2, padx=4)
        self.preset_desc = ttk.Label(top, foreground="#555", wraplength=900, justify="left")
        self.preset_desc.grid(row=1, column=0, columnspan=5, sticky="w", pady=(4, 0))

        out = ttk.Frame(top)
        out.grid(row=2, column=0, columnspan=5, sticky="ew", pady=(8, 0))
        out.columnconfigure(1, weight=1)
        top.columnconfigure(4, weight=1)
        self._path_row(out, 0, "output_dir", "dir")
        ttk.Label(out, text=LABELS["output_name"]).grid(row=1, column=0, sticky="w", pady=3)
        ttk.Entry(out, textvariable=self.vars["output_name"], width=30).grid(row=1, column=1, sticky="w")

        scroll = ScrollableFrame(self.tab_train)
        scroll.pack(fill="both", expand=True)
        inner = scroll.inner
        inner.columnconfigure(0, weight=1)
        inner.columnconfigure(1, weight=1)
        positions = [(0, 0), (0, 1), (1, 0)]
        for (title, keys), (r, c) in zip(TRAIN_GROUPS, positions):
            frame = ttk.LabelFrame(inner, text=title, padding=8)
            frame.grid(row=r, column=c, sticky="nsew", padx=6, pady=6,
                       columnspan=2 if title == "詳細" else 1)
            for i, key in enumerate(keys):
                self._field(frame, i, key)

        bottom = ttk.Frame(self.tab_train, padding=6)
        bottom.pack(fill="x")
        ttk.Button(bottom, text="設定をファイルに保存...", command=self.save_config_file).pack(side="left")
        ttk.Button(bottom, text="設定ファイルを読み込み...", command=self.load_config_file).pack(side="left", padx=6)

    def _build_run_tab(self):
        tab = self.tab_run
        tab.columnconfigure(0, weight=1)
        summary = ttk.LabelFrame(tab, text="学習内容の確認", padding=8)
        summary.grid(row=0, column=0, sticky="ew")
        ttk.Label(summary, textvariable=self.summary_var, justify="left").pack(anchor="w")

        buttons = ttk.Frame(tab)
        buttons.grid(row=1, column=0, sticky="w", pady=10)
        self.start_button = ttk.Button(buttons, text="▶ 学習開始", command=self.start_training)
        self.start_button.pack(side="left")
        self.stop_button = ttk.Button(buttons, text="■ 停止", command=self.stop_job, state="disabled")
        self.stop_button.pack(side="left", padx=6)
        ttk.Button(buttons, text="出力フォルダを開く",
                   command=lambda: open_path(self.vars["output_dir"].get())).pack(side="left", padx=6)
        ttk.Button(buttons, text="コマンドをコピー", command=self.copy_command).pack(side="left")

        stats = ttk.LabelFrame(tab, text="進捗", padding=8)
        stats.grid(row=2, column=0, sticky="ew")
        stats.columnconfigure(1, weight=1)
        rows = [("状態", self.status_var), ("ステップ", self.step_var), ("エポック", self.epoch_var),
                ("平均 loss", self.loss_var), ("経過 / 残り", self.time_var), ("速度", self.speed_var)]
        for i, (label, var) in enumerate(rows):
            ttk.Label(stats, text=label).grid(row=i, column=0, sticky="w", padx=(0, 12))
            ttk.Label(stats, textvariable=var, font=("", 10, "bold")).grid(row=i, column=1, sticky="w")
        self.progress = ttk.Progressbar(stats, maximum=1000)
        self.progress.grid(row=len(rows), column=0, columnspan=2, sticky="ew", pady=(8, 0))

    # ------------------------------------------------------------------ 環境
    def check_environment(self):
        try:
            cfg = self.get_config()
        except ConfigError as e:
            messagebox.showerror("入力エラー", str(e))
            return
        self.log("=== 環境チェック ===")
        results = config.check_environment(cfg)
        for ok, msg in results:
            self.log(("[OK] " if ok else "[NG] ") + msg)
        if all(ok for ok, _ in results):
            messagebox.showinfo("環境チェック", "すべて OK です。")
        else:
            messagebox.showwarning("環境チェック", "NG の項目があります。ログと README を確認してください。")

    # ------------------------------------------------------------------ データセット
    def refresh_images(self):
        self.save_current_caption(silent=True)
        image_dir = self.vars["image_dir"].get()
        self.images = dataset.list_images(image_dir)
        self.image_list.delete(0, "end")
        for img in self.images:
            mark = "  " if dataset.caption_path(img).is_file() else "✕ "
            self.image_list.insert("end", mark + img.name)
        self.current_image = None
        self.caption_text.delete("1.0", "end")
        self.preview_label.configure(image="", text="(画像を選択)")
        if not image_dir:
            self.image_count_var.set("")
        elif not self.images:
            self.image_count_var.set("画像が見つかりません (png / jpg / webp / bmp)")
        else:
            missing = dataset.count_missing_captions(image_dir)
            note = f" / キャプション無し: {missing} 枚 (✕ 印)" if missing else " / 全画像にキャプションあり"
            self.image_count_var.set(f"画像 {len(self.images)} 枚{note}")
        self.update_summary()

    def _on_image_selected(self, _event=None):
        sel = self.image_list.curselection()
        if not sel:
            return
        self.save_current_caption(silent=True)
        img = self.images[sel[0]]
        self.current_image = img
        self.caption_text.delete("1.0", "end")
        self.caption_text.insert("1.0", dataset.read_caption(img) or "")
        self.caption_text.edit_modified(False)
        self._show_preview(img)

    def _show_preview(self, img):
        if Image is None:
            self.preview_label.configure(text=img.name)
            return
        try:
            with Image.open(img) as im:
                im.thumbnail((240, 240))
                self.preview_photo = ImageTk.PhotoImage(im.convert("RGB"))
            self.preview_label.configure(image=self.preview_photo, text="")
        except Exception as e:  # 壊れた画像でも GUI を止めない
            self.preview_label.configure(image="", text=f"プレビュー不可\n{e}")

    def save_current_caption(self, silent=False):
        if self.current_image is None:
            return
        if silent and not self.caption_text.edit_modified():
            return
        text = self.caption_text.get("1.0", "end").strip()
        dataset.write_caption(self.current_image, text)
        self.caption_text.edit_modified(False)
        idx = self.images.index(self.current_image) if self.current_image in self.images else None
        if idx is not None:
            self.image_list.delete(idx)
            self.image_list.insert(idx, "  " + self.current_image.name)
            self.image_list.selection_set(idx)
        if not silent:
            self.live_line_var.set(f"保存しました: {dataset.caption_path(self.current_image).name}")

    def start_captioning(self):
        if self.job:
            return
        try:
            cfg = self.get_config()
            opts = self.caption_options()
        except ConfigError as e:
            messagebox.showerror("入力エラー", str(e))
            return
        if not self.images:
            messagebox.showerror("エラー", "画像フォルダに画像がありません。")
            return
        tagger = Path(cfg.sd_scripts_dir) / config.TAGGER_SCRIPT
        if not tagger.is_file() or not cfg.python_exe.is_file():
            messagebox.showerror("エラー", "sd-scripts または仮想環境が見つかりません。「環境設定」タブを確認してください。")
            return
        existing = len(self.images) - dataset.count_missing_captions(cfg.image_dir)
        if existing and opts.overwrite and not messagebox.askyesno(
                "確認", f"既存のキャプション {existing} 件を上書きします。よろしいですか？"):
            return
        self.save_current_caption(silent=True)
        snapshot = dataset.snapshot_captions(cfg.image_dir)
        cmd = captioning.build_tagger_command(cfg.sd_scripts_dir, cfg.image_dir, opts)
        self.job_context = {"snapshot": snapshot, "opts": opts, "image_dir": cfg.image_dir,
                            "trigger": cfg.trigger_word}
        self.log("=== 自動キャプション開始 ===")
        self._start_process("caption", cmd, cfg.sd_scripts_dir)

    def apply_trigger_only(self):
        image_dir = self.vars["image_dir"].get()
        trigger = self.vars["trigger_word"].get().strip()
        if not trigger:
            messagebox.showerror("エラー", "トリガーワードを入力してください。")
            return
        if not self.images:
            messagebox.showerror("エラー", "画像フォルダに画像がありません。")
            return
        self.save_current_caption(silent=True)
        changed = dataset.apply_trigger_word_to_folder(image_dir, trigger, create_missing=True)
        self.log(f"トリガーワード「{trigger}」を {changed} 件のキャプションに付与しました")
        self.refresh_images()

    # ------------------------------------------------------------------ 学習設定
    def _selected_preset(self):
        return next((p for p in self.presets if p.name == self.preset_var.get()), None)

    def _on_preset_selected(self, _event=None):
        preset = self._selected_preset()
        self.preset_desc.configure(text=preset.description if preset else "")

    def apply_preset(self, confirm=True):
        preset = self._selected_preset()
        if not preset:
            return
        if confirm and not messagebox.askyesno("確認", f"「{preset.name}」の値で学習設定を上書きします。よろしいですか？"):
            return
        raw = self.raw_config()
        raw.update(preset.values)
        self.set_config(TrainingConfig.from_dict(raw))
        self.log(f"プリセット「{preset.name}」を適用しました")
        self.update_summary()

    def save_config_file(self):
        path = filedialog.asksaveasfilename(defaultextension=".json", filetypes=[("JSON", "*.json")],
                                            initialdir=str(APP_DIR), title="設定を保存")
        if not path:
            return
        save_settings(path, {"config": self.raw_config()})
        self.log(f"設定を保存しました: {path}")

    def load_config_file(self):
        path = filedialog.askopenfilename(filetypes=[("JSON", "*.json")], initialdir=str(APP_DIR),
                                          title="設定を読み込み")
        if not path:
            return
        data = load_settings(path)
        try:
            # 学習時に出力される _lora_studio/config.json もそのまま読める
            cfg = TrainingConfig.from_dict(data.get("config", data))
        except ConfigError as e:
            messagebox.showerror("読み込みエラー", str(e))
            return
        self.set_config(cfg)
        self.refresh_images()
        self.log(f"設定を読み込みました: {path}")

    # ------------------------------------------------------------------ 学習実行
    def update_summary(self):
        try:
            cfg = self.get_config()
        except ConfigError as e:
            self.summary_var.set(f"入力エラー: {e}")
            return
        count = len(self.images)
        steps = config.estimate_steps(cfg, count)
        te = "学習しない (U-Net のみ)" if cfg.network_train_unet_only else f"学習する (lr={cfg.text_encoder_lr:g})"
        mem = [n for n in ("fp8_base", "full_bf16", "cache_text_encoder_outputs", "gradient_checkpointing")
               if getattr(cfg, n)]
        lines = [
            f"ベースモデル : {Path(cfg.pretrained_model).name or '(未設定)'}",
            f"画像         : {count} 枚 × 繰り返し {cfg.num_repeats} 回 × {cfg.max_train_epochs} エポック"
            f" ÷ バッチ {cfg.batch_size}  →  約 {steps:,} ステップ",
            f"解像度       : {cfg.resolution}px / Dim {cfg.network_dim} / Alpha {cfg.network_alpha}"
            f" / 学習率 {cfg.unet_lr:g} / {cfg.optimizer_type}",
            f"Text Encoder : {te}",
            f"省メモリ設定 : {', '.join(mem) or 'なし'}",
            f"出力         : {Path(cfg.output_dir) / (cfg.output_name + '.safetensors')}",
            "目安時間     : RTX 3060 Ti・1024px で 1 ステップ約 1.5〜3 秒"
            f" (約 {steps * 1.5 / 60:.0f}〜{steps * 3 / 60:.0f} 分)" if steps else "",
        ]
        self.summary_var.set("\n".join(line for line in lines if line))

    def start_training(self):
        if self.job:
            return
        try:
            cfg = self.get_config()
        except ConfigError as e:
            messagebox.showerror("入力エラー", str(e))
            return
        self.save_current_caption(silent=True)
        errors, warnings = config.validate(cfg)
        if errors:
            messagebox.showerror("学習を開始できません", "\n".join("・" + e for e in errors))
            return
        if warnings and not messagebox.askyesno(
                "確認", "\n".join("・" + w for w in warnings) + "\n\nこのまま学習を開始しますか？"):
            return
        existing = Path(cfg.output_dir) / f"{cfg.output_name}.safetensors"
        if existing.exists() and not messagebox.askyesno(
                "確認", f"{existing.name} は既に存在し、上書きされます。続けますか？"):
            return
        try:
            cmd = config.prepare_training(cfg)
        except OSError as e:
            messagebox.showerror("エラー", f"出力フォルダに書き込めません: {e}")
            return
        self.save_app_settings()
        self.log("=== 学習開始 ===")
        self.log(config.format_command(cmd))
        self.job_context = {"cfg": cfg, "oom": False}
        self.step_var.set("- / -")
        self.epoch_var.set(f"0 / {cfg.max_train_epochs}")
        self.loss_var.set("-")
        self.time_var.set("-")
        self.speed_var.set("-")
        self._start_process("train", cmd, cfg.sd_scripts_dir)

    def copy_command(self):
        try:
            cfg = self.get_config()
            cmd = config.build_train_command(cfg, cfg.work_dir / "dataset.toml", None)
        except (ConfigError, ValueError) as e:
            messagebox.showerror("入力エラー", str(e))
            return
        self.root.clipboard_clear()
        self.root.clipboard_append(config.format_command(cmd))
        self.live_line_var.set("コマンドをクリップボードにコピーしました (dataset.toml は学習開始時に生成されます)")

    # ------------------------------------------------------------------ プロセス管理
    def _start_process(self, job, cmd, cwd):
        self.runner = ProcessRunner(
            cmd, cwd,
            on_output=lambda text, overwrite: self.events.put(("output", text, overwrite)),
            on_exit=lambda code: self.events.put(("exit", code, None)),
        )
        try:
            self.runner.start()
        except OSError as e:
            self.runner = None
            messagebox.showerror("起動エラー", f"プロセスを起動できません: {e}")
            return
        self.job = job
        self.status_var.set("学習準備中 (モデル読み込み・キャッシュ作成)..." if job == "train" else "キャプション生成中...")
        self.progress.configure(value=0)
        self._set_running(True)

    def _set_running(self, running):
        self.start_button.configure(state="disabled" if running else "normal")
        self.caption_button.configure(state="disabled" if running else "normal")
        self.stop_button.configure(state="normal" if running else "disabled")

    def stop_job(self):
        if self.runner and self.runner.is_running():
            if messagebox.askyesno("確認", "実行中の処理を停止しますか？\n(保存済みのエポックの LoRA は残ります)"):
                self.status_var.set("停止中...")
                self.runner.stop()

    def _poll_events(self):
        try:
            for _ in range(500):
                kind, value, extra = self.events.get_nowait()
                if kind == "output":
                    self._handle_output(value, extra)
                else:
                    self._handle_exit(value)
        except queue.Empty:
            pass
        self.root.after(100, self._poll_events)

    def _handle_output(self, text, overwrite):
        if not text.strip():
            return
        progress = parse_progress(text)
        if self.job == "train":
            if any(marker in text for marker in OOM_MARKERS):
                self.job_context["oom"] = True
            epoch = parse_epoch(text)
            if epoch:
                self.epoch_var.set(f"{epoch[0]} / {epoch[1]}")
            if progress and progress.is_training_step:
                self.status_var.set("学習中")
                self.step_var.set(f"{progress.current:,} / {progress.total:,} ({progress.fraction:.1%})")
                self.progress.configure(value=progress.fraction * 1000)
                if progress.loss is not None:
                    self.loss_var.set(f"{progress.loss:.4f}")
                self.time_var.set(f"{progress.elapsed} / {progress.remaining or '?'}")
                self.speed_var.set(progress.rate or "-")
            elif progress:
                self.status_var.set(f"学習準備中 (キャッシュ作成など) {progress.current}/{progress.total}")
        elif self.job == "caption" and progress:
            self.status_var.set(f"キャプション生成中 {progress.current}/{progress.total}")
            self.progress.configure(value=progress.fraction * 1000)
        if overwrite:
            self.live_line_var.set(text.strip()[:200])
        else:
            self.log(text)

    def _handle_exit(self, code):
        job, ctx = self.job, self.job_context
        stopped = self.runner.stopped if self.runner else False
        self.job, self.runner, self.job_context = None, None, {}
        self._set_running(False)
        self.live_line_var.set("")

        if job == "caption":
            added = captioning.finish_captioning(ctx["image_dir"], ctx["snapshot"], ctx["opts"], ctx["trigger"])
            self.refresh_images()
            if stopped:
                self.status_var.set("キャプション生成を停止しました")
            elif code == 0:
                self.progress.configure(value=1000)
                self.status_var.set("キャプション生成 完了")
                msg = "キャプション生成が完了しました。"
                if added:
                    msg += f"\nトリガーワードを {added} 件に付与しました。"
                messagebox.showinfo("完了", msg)
            else:
                self.status_var.set(f"キャプション生成 失敗 (終了コード {code})")
                messagebox.showerror("エラー", "キャプション生成に失敗しました。ログを確認してください。")
            return

        cfg = ctx.get("cfg")
        if stopped:
            self.status_var.set("停止しました")
            self.log("=== 学習を停止しました ===")
        elif code == 0:
            self.progress.configure(value=1000)
            self.status_var.set("完了")
            outputs = sorted(Path(cfg.output_dir).glob(f"{cfg.output_name}*.safetensors"))
            self.log("=== 学習完了 ===")
            for p in outputs:
                self.log(f"  {p}")
            messagebox.showinfo("学習完了", f"学習が完了しました。\n出力: {cfg.output_dir}\n"
                                           f"({len(outputs)} ファイル)")
        else:
            self.status_var.set(f"エラー終了 (終了コード {code})")
            hint = ""
            if ctx.get("oom"):
                hint = ("\n\nVRAM 不足 (Out of Memory) です。\n"
                        "・プリセット「VRAM 8GB 省メモリ」を適用する\n"
                        "・解像度を 768 に下げる\n"
                        "・ブラウザなど GPU を使うアプリを閉じる\nを試してください。")
            messagebox.showerror("学習エラー", "学習が異常終了しました。ログを確認してください。" + hint)

    # ------------------------------------------------------------------ 共通
    def log(self, text):
        self.log_text.configure(state="normal")
        self.log_text.insert("end", text.rstrip() + "\n")
        lines = int(self.log_text.index("end-1c").split(".")[0])
        if lines > MAX_LOG_LINES:
            self.log_text.delete("1.0", f"{lines - MAX_LOG_LINES}.0")
        self.log_text.see("end")
        self.log_text.configure(state="disabled")

    def save_app_settings(self):
        data = {"config": self.raw_config(), "preset": self.preset_var.get(), "caption": {
            "model": self.cap_model.get(),
            "threshold": self.cap_threshold.get(),
            "undesired_tags": self.cap_undesired.get(),
            "overwrite": self.cap_overwrite.get(),
            "add_trigger": self.cap_add_trigger.get(),
        }}
        try:
            save_settings(self.settings_path, data)
        except OSError as e:
            self.log(f"[警告] 設定を保存できません: {e}")

    def on_close(self):
        if self.runner and self.runner.is_running():
            if not messagebox.askyesno("確認", "処理を実行中です。停止して終了しますか？"):
                return
            self.runner.stop()
        self.save_current_caption(silent=True)
        self.save_app_settings()
        self.root.destroy()


def open_path(path):
    if not path or not os.path.exists(path):
        messagebox.showerror("エラー", f"フォルダが存在しません: {path}")
        return
    if os.name == "nt":
        os.startfile(path)
    elif sys.platform == "darwin":
        subprocess.Popen(["open", path])
    else:
        subprocess.Popen(["xdg-open", path])


def _setup_fonts(root):
    families = set(tkfont.families(root))
    family = next((f for f in ("Yu Gothic UI", "Meiryo UI", "Meiryo", "Noto Sans CJK JP") if f in families), None)
    if family:
        for name in ("TkDefaultFont", "TkTextFont", "TkMenuFont", "TkHeadingFont"):
            tkfont.nametofont(name).configure(family=family, size=10)


def main():
    if os.name == "nt":
        try:  # 高 DPI 環境で文字がぼやけないようにする
            import ctypes
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except (AttributeError, OSError):
            pass
    root = tk.Tk()
    _setup_fonts(root)
    style = ttk.Style(root)
    if "vista" in style.theme_names():
        style.theme_use("vista")
    LoraStudioApp(root)
    root.mainloop()
