# LoRA Studio

**LoRA Studio** は、SDXL 用の LoRA 学習を GUI で手軽に行うための Windows 向けツールです。
学習エンジンには [kohya-ss/sd-scripts](https://github.com/kohya-ss/sd-scripts) を使い、コマンドを打たなくても次の操作ができます。

- 画像フォルダの選択と、画像・キャプションの一覧表示
- **WD14 Tagger による自動キャプション付け**（トリガーワードの自動付与、既存キャプションの保護に対応）
- キャプションの確認・手直し（画像プレビュー付き）
- 学習パラメータの設定と、**VRAM 8GB（RTX 3060 Ti）向けプリセット**
- 学習の進捗表示（ステップ数・エポック・loss・残り時間・速度）と停止

---

## 目次

1. [動作環境](#動作環境)
2. [セットアップ手順](#セットアップ手順)
3. [使い方](#使い方)
4. [VRAM 8GB 向けプリセット](#vram-8gb-向けプリセット)
5. [主なパラメータの目安](#主なパラメータの目安)
6. [できあがった LoRA の使い方](#できあがった-lora-の使い方)
7. [トラブルシューティング](#トラブルシューティング)
8. [フォルダ構成](#フォルダ構成)
9. [開発者向け](#開発者向け)

---

## 動作環境

| 項目 | 推奨 / 必須 |
| --- | --- |
| OS | Windows 10 / 11（64bit） |
| GPU | NVIDIA GeForce RTX 3060 Ti（VRAM 8GB）以上。RTX 20 / 30 / 40 シリーズで動作する想定 |
| NVIDIA ドライバ | 最新版を推奨（CUDA 12.4 対応の 550 以降） |
| メインメモリ | 16GB 以上（32GB あると安定します） |
| ストレージ | 空き 30GB 以上（sd-scripts と PyTorch で約 10GB、SDXL モデル 1 つで約 7GB、ほかにキャッシュ） |
| Python | **3.10.x（64bit）**。3.11 でも動く見込みですが、sd-scripts 公式の推奨は 3.10 です |
| Git | 必須（sd-scripts の取得に使います） |

> RTX 20 シリーズ（Turing）は bf16 に対応していません。プリセット適用後に「混合精度」を `fp16` に、「full_bf16」をオフにしてください。

---

## セットアップ手順

### 1. Python 3.10 をインストール

1. <https://www.python.org/downloads/windows/> から **Python 3.10.x の「Windows installer (64-bit)」** をダウンロードします。
2. インストーラの最初の画面で **「Add python.exe to PATH」にチェック** を入れます。
3. 「Customize installation」を選んだ場合は **「tcl/tk and IDLE」にチェックが入っていること** を確認します（GUI 表示に必要です）。

### 2. Git をインストール

<https://git-scm.com/download/win> からインストーラを取得し、既定の設定のままインストールします。

### 3. NVIDIA ドライバを更新

[NVIDIA ドライバのダウンロード](https://www.nvidia.com/ja-jp/drivers/) から、お使いの GPU 向けの最新ドライバを入れてください（Game Ready / Studio どちらでも可）。

### 4. LoRA Studio を配置

このリポジトリの `lora-studio` フォルダを、**日本語や空白を含まない短いパス** に置きます。

```text
良い例: C:\AI\lora-studio
悪い例: C:\Users\山田\Desktop\新しいフォルダー (2)\lora-studio
```

> 日本語・空白・括弧を含むパスは、sd-scripts や bat ファイルが正しく動かない原因になります。

### 5. setup.bat を実行

`lora-studio` フォルダの **`setup.bat` をダブルクリック** します。次の処理が自動で行われます（回線速度によって 10〜30 分程度）。

1. `sd-scripts` フォルダに kohya-ss/sd-scripts（動作確認済みの v0.12.3）を取得
2. `sd-scripts\venv` に Python 仮想環境を作成
3. PyTorch 2.6.0（CUDA 12.4 版）をインストール
4. sd-scripts の依存パッケージをインストール
5. 自動キャプション用の ONNX Runtime（GPU 版）をインストール

最後に次のような確認結果が出れば成功です。

```text
torch 2.6.0+cu124 / GPU: NVIDIA GeForce RTX 3060 Ti (8.0 GB) / bf16: True
accelerate: OK
bitsandbytes: OK
onnxruntime: OK
tkinter: OK
PIL: OK
```

> `CUDA GPU is not available` と出る場合は、NVIDIA ドライバを更新してから `setup.bat` をもう一度実行してください（途中まで完了した処理はスキップされます）。

<details>
<summary>sd-scripts の別バージョンを使いたい場合</summary>

`setup.bat` は既定で動作確認済みの **v0.12.3** を使います。最新の開発版（main ブランチ）など別のバージョンを使いたい場合は、コマンドプロンプトで環境変数を指定してから実行します。

```bat
cd /d C:\AI\lora-studio
set SD_SCRIPTS_REF=main
setup.bat
```

`SD_SCRIPTS_REF` にはブランチ名・タグ名・コミット ID を指定できます。バージョンを変えたあとは依存パッケージも入れ直されます。

</details>

### 6. SDXL のベースモデルを用意

学習のベースにする SDXL モデル（`.safetensors`）を用意します。どこに置いても構いません。

- 汎用: [stabilityai/stable-diffusion-xl-base-1.0](https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0) の `sd_xl_base_1.0.safetensors`
- イラスト系: Illustrious XL、Pony Diffusion V6 XL、Animagine XL などの SDXL 系モデル

> **LoRA は、学習に使ったベースモデル（またはその派生モデル）で使うと最もよく効きます。** 普段画像生成に使っているモデル系統に合わせて選びましょう。

### 7. 起動

**`run.bat` をダブルクリック** すると LoRA Studio が起動します。

---

## 使い方

画面は 4 つのタブに分かれています。左から順に進めれば学習できます。

### 1. 環境設定

| 項目 | 内容 |
| --- | --- |
| sd-scripts フォルダ | 通常は自動入力（`lora-studio\sd-scripts`）のままで OK |
| ベースモデル | 手順 6 で用意した SDXL モデルの `.safetensors` |
| VAE（任意） | 通常は空欄。モデル内蔵の VAE が使われます |

**「環境チェック」** を押して、すべて `[OK]` になることを確認します。

### 2. データセット

1. **画像フォルダ** を選択します（フォルダ直下の png / jpg / webp / bmp が対象。サブフォルダは見ません）。
2. **トリガーワード** を入力します。LoRA を呼び出すための合言葉で、既存の単語と被らない造語がおすすめです（例: `sks_girl`、`mychar_v1`）。
3. **繰り返し回数** を決めます（下の目安を参照）。
4. **「キャプション生成」** を押すと、WD14 Tagger が各画像に `画像名.txt` のタグを書き込みます。
   - 初回はモデル（数百 MB）を自動ダウンロードします。
   - 「既存のキャプションを上書きする」がオフなら、既に `.txt` がある画像はそのまま残ります。
   - 「トリガーワードを先頭に追加」がオンなら、全キャプションの先頭にトリガーワードが入ります。
5. 下の **「キャプション確認・編集」** で画像を選ぶと、プレビューとキャプションが表示されます。必要に応じて手直しし、**保存（Ctrl+S）** します。別の画像を選ぶと自動保存されます。

> **キャプションのコツ（キャラクター LoRA の場合）**
> LoRA に覚えさせたい特徴（髪色・目の色・特徴的な服など）は **キャプションから消す** と、トリガーワードにその特徴が結び付きます。
> 逆に、背景・ポーズ・表情など「毎回変えたい要素」はタグとして残します。
> 「除外タグ」欄に `blonde hair, blue eyes` のように入れておくと、自動キャプション時にまとめて除外できます。

**画像の準備の目安**

- 枚数: 15〜50 枚程度（キャラクターなら 20 枚前後でも十分学習できます）
- 解像度: 長辺 1024px 以上が理想。小さすぎる画像は避ける
- 構図・服装・背景にバリエーションを持たせる。似た画像ばかりだと過学習しやすくなります
- 透過 PNG は背景が黒くなることがあるので、白背景などに変換しておくと安全です

### 3. 学習設定

1. **プリセット** を選んで **「プリセットを適用」** を押します（初回起動時は「VRAM 8GB 標準」が自動適用済みです）。
2. **出力フォルダ** と **出力名**（LoRA のファイル名）を指定します。
3. 必要に応じて各パラメータを調整します。項目にマウスを乗せると説明が表示されます。

「設定をファイルに保存...」で設定を JSON に保存し、あとで読み込めます。学習のたびに出力フォルダの `_lora_studio\config.json` にも自動保存されるので、過去の学習設定を再現したいときはそれを読み込んでください。

### 4. 学習実行

「学習内容の確認」で総ステップ数と目安時間を確認し、**「▶ 学習開始」** を押します。

- 最初の数分はモデルの読み込みと、画像・テキストのキャッシュ作成が行われます（状態欄に「学習準備中」と表示）。
- 学習が始まると、ステップ・エポック・平均 loss・経過 / 残り時間・速度がリアルタイムに更新されます。
- **「■ 停止」** で中断できます。それまでに保存されたエポックの LoRA は残ります。
- 「コマンドをコピー」で、実行される sd-scripts のコマンドをクリップボードにコピーできます（手動実行や不具合報告用）。

学習が終わると、出力フォルダに次のファイルができます。

```text
output\
├─ my_sdxl_lora-000002.safetensors   ← 2 エポック目の途中保存
├─ my_sdxl_lora-000004.safetensors
├─ ...
├─ my_sdxl_lora.safetensors          ← 最終エポック
└─ _lora_studio\                     ← 学習に使った設定 (dataset.toml / command.txt / config.json)
```

途中保存されたファイルを生成で試して、**一番よく効いて、かつ破綻していないエポックを選ぶ** のがおすすめです（最終エポックが最良とは限りません）。

---

## VRAM 8GB 向けプリセット

RTX 3060 Ti（VRAM 8GB）で SDXL LoRA を学習するためのプリセットを 3 つ用意しています（`presets` フォルダの JSON）。

| プリセット | 解像度 | 主な省メモリ設定 | VRAM 使用量の目安 | 用途 |
| --- | --- | --- | --- | --- |
| **VRAM 8GB 標準（推奨）** | 1024 | U-Net のみ学習、Text Encoder 出力キャッシュ、Gradient Checkpointing、AdamW8bit、SDPA | 約 7GB | 通常はこれ |
| **VRAM 8GB 省メモリ** | 1024 | 標準 ＋ `fp8_base` ＋ `full_bf16` | 約 5〜6GB | 標準で Out of Memory になる場合、ほかのアプリと同時に使う場合 |
| **VRAM 8GB お試し** | 768 | 標準と同じ（Dim 8、4 エポック） | 約 5GB | 環境の動作確認、短時間で結果を見たいとき |

VRAM 使用量は、画像の内容、ドライバ、ほかのアプリの使用状況によって変わる目安です。

### プリセットに含まれる省メモリ設定

| 設定 | 効果 |
| --- | --- |
| U-Net のみ学習（`network_train_unet_only`） | 2 つの Text Encoder を学習しないため、メモリと時間を大きく削減。SDXL ではこれでも十分効果が出ます |
| Text Encoder 出力をキャッシュ（`cache_text_encoder_outputs`） | Text Encoder の出力を事前計算し、学習中は GPU から外す。8GB では必須級。キャプションのシャッフルとは併用不可 |
| Gradient Checkpointing | 計算を少し増やす代わりにメモリを大幅に削減。8GB では必須 |
| Latent をキャッシュ（ディスク保存） | VAE によるエンコードを事前に行い、学習中は VAE を使わない。2 回目以降の学習はキャッシュを再利用して速く始まります |
| AdamW8bit | オプティマイザの状態を 8bit で保持して省メモリ |
| SDPA | PyTorch 標準の省メモリ Attention。xformers の追加インストールは不要 |
| `fp8_base`（省メモリのみ） | ベースモデルを FP8 で保持し、U-Net の使用メモリをほぼ半減。画質がわずかに落ちることがあります |
| `full_bf16`（省メモリのみ） | 勾配なども bf16 で扱いメモリを削減 |

### 学習時間の目安（RTX 3060 Ti、1024px）

- 1 ステップあたりおよそ 1.5〜3 秒
- 画像 20 枚 × 繰り返し 10 回 × 10 エポック = 2,000 ステップ → **約 1〜2 時間**

「学習実行」タブに、設定から計算した目安時間が表示されます。

---

## 主なパラメータの目安

| パラメータ | 目安 | 説明 |
| --- | --- | --- |
| 繰り返し回数 × 画像枚数 × エポック数 | 合計 1,500〜3,000 ステップ | 少ないと覚えず、多すぎると過学習（絵が硬くなる・崩れる） |
| エポック数 | 10 | 2 エポックごとに保存し、後で良いものを選ぶ |
| Network Dim | 16〜32 | 大きいほど細部まで覚えるが、ファイルが大きく過学習しやすい。画風なら 32 前後も |
| Network Alpha | Dim の半分 | 学習の強さの補正 |
| 学習率（U-Net） | 1e-4（AdamW8bit） | 崩れるなら 5e-5 に下げる。覚えが悪いなら 2e-4 に上げる |
| Min SNR Gamma | 5 | 学習を安定させる設定。通常はそのまま |
| バッチサイズ | 1 | 8GB では 1 を推奨 |

**画像枚数ごとの繰り返し回数の目安**（10 エポックの場合）

| 画像枚数 | 繰り返し回数 |
| --- | --- |
| 10 枚 | 20 |
| 20 枚 | 10 |
| 50 枚 | 4 |
| 100 枚 | 2 |

---

## できあがった LoRA の使い方

出力された `.safetensors` を、お使いの画像生成ツールの LoRA フォルダへコピーします。

| ツール | コピー先 |
| --- | --- |
| Stable Diffusion WebUI（AUTOMATIC1111）/ Forge | `models\Lora` |
| ComfyUI | `models\loras` |

プロンプトに LoRA とトリガーワードを入れて生成します（例: `<lora:my_sdxl_lora:0.8>, sks_girl, 1girl, smile`）。
効きすぎる場合は LoRA の強さ（上の例の `0.8`）を 0.5〜0.7 程度に下げてください。

---

## トラブルシューティング

### `CUDA out of memory` / `OutOfMemoryError` が出る

1. 「VRAM 8GB 省メモリ」プリセットを適用する
2. 解像度を 768 または 896 に下げる
3. ブラウザ（ハードウェアアクセラレーション）・動画再生・ゲームなど GPU を使うアプリを閉じる
4. ほかの画像生成ツール（WebUI / ComfyUI）を終了する

### 学習が極端に遅い（1 ステップ 10 秒以上）

VRAM があふれてメインメモリ（共有 GPU メモリ）が使われている可能性があります。タスクマネージャー →「パフォーマンス」→「GPU」で **共有 GPU メモリ使用量** が増えていないか確認してください。増えていれば「省メモリ」プリセットにするか、解像度を下げます。

> NVIDIA コントロールパネル →「3D 設定の管理」→「CUDA - システムメモリ フォールバック ポリシー」を「フォールバックしない」にすると、遅くなる代わりにすぐ Out of Memory エラーで止まるので、原因が分かりやすくなります。

### 平均 loss が `nan` になる

- 学習率を下げる（1e-4 → 5e-5）
- 混合精度を `fp16` にしている場合は `bf16` に戻す（RTX 30 シリーズ以降）

### 自動キャプションが遅い / `CUDAExecutionProvider` の警告が出る

ONNX Runtime が GPU を使えず CPU で動いています。動作はしますが遅くなります。`sd-scripts\venv\Scripts\python.exe -m pip install --upgrade onnxruntime-gpu` で更新すると改善することがあります。

### `bitsandbytes` 関連のエラーが出る

「オプティマイザ」を `AdamW8bit` から `Adafactor` に変更すると、bitsandbytes を使わずに学習できます。

### 起動しない / `tkinter` が無いと言われる

Python を「tcl/tk and IDLE」付きで入れ直し、`sd-scripts\venv` フォルダを削除してから `setup.bat` を再実行してください。

### 「キャプションの無い画像が○枚あります」と出る

キャプション（`.txt`）が無い画像は「トリガーワード＋クラスワード」で学習されます。トリガーワードが空だと学習できないため、キャプションを生成するかトリガーワードを入力してください。

### それでも解決しない場合

画面下のログに、sd-scripts のエラーメッセージがそのまま表示されます。最後の方の `Error` や `Traceback` 付近を確認してください。出力フォルダの `_lora_studio\command.txt` には実行したコマンドが保存されているので、コマンドプロンプトから直接実行して確認することもできます。

---

## フォルダ構成

```text
lora-studio\
├─ setup.bat            初回セットアップ (sd-scripts の取得と依存関係のインストール)
├─ run.bat              LoRA Studio を起動
├─ main.py              起動スクリプト
├─ check_install.py     setup.bat から呼ばれるインストール確認
├─ lora_studio\         本体
│   ├─ gui.py           画面 (tkinter)
│   ├─ config.py        学習設定、入力チェック、sd-scripts のコマンド生成
│   ├─ captioning.py    WD14 Tagger による自動キャプション
│   ├─ dataset.py       画像・キャプションファイルの操作
│   ├─ process.py       学習プロセスの実行と進捗の解析
│   ├─ presets.py       プリセットの読み込み
│   └─ settings.py      前回の入力内容の保存
├─ presets\             学習プリセット (JSON。自作プリセットを追加可能)
├─ tests\               単体テスト
├─ sd-scripts\          (setup.bat が作成) kohya-ss/sd-scripts と仮想環境
└─ settings.json        (自動作成) 前回の入力内容
```

### プリセットの自作

`presets` フォルダに JSON ファイルを追加すると、プリセット一覧に表示されます（ファイル名順）。既存のファイルをコピーして編集するのが簡単です。

```json
{
  "name": "表示名",
  "description": "説明文",
  "config": {
    "network_dim": 32,
    "network_alpha": 16,
    "unet_lr": 0.0001
  }
}
```

`config` には学習パラメータだけを書きます（パス・トリガーワードなど、データセット固有の項目は書けません）。書かなかった項目は現在の値のまま残ります。

---

## 開発者向け

- GUI は Python 標準の tkinter のみで作られており、追加パッケージは不要です（画像プレビューに Pillow を使いますが、sd-scripts の依存関係に含まれます）。
- 学習は `accelerate launch` 経由で `sdxl_train_network.py` を、データセットは `--dataset_config`（TOML）で渡しています。
- 単体テストの実行:

```bat
sd-scripts\venv\Scripts\python.exe -m unittest discover -s tests -v
```

## ライセンス・注意事項

- 学習エンジンの [kohya-ss/sd-scripts](https://github.com/kohya-ss/sd-scripts) は Apache License 2.0 です。
- ベースモデルや WD14 Tagger のモデルは、それぞれのライセンスに従って利用してください。
- 学習に使う画像の権利、生成物の取り扱いには十分注意してください。
