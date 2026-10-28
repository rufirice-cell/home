"""画像フォルダとキャプションファイルの操作"""

from pathlib import Path

IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}
CAPTION_EXTENSION = ".txt"


def list_images(image_dir):
    """フォルダ直下の画像ファイルを名前順で返す (sd-scripts と同じくサブフォルダは見ない)"""
    path = Path(image_dir) if image_dir else None
    if not path or not path.is_dir():
        return []
    return sorted(
        (p for p in path.iterdir() if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS),
        key=lambda p: p.name.lower(),
    )


def caption_path(image_path):
    return Path(image_path).with_suffix(CAPTION_EXTENSION)


def read_caption(image_path):
    path = caption_path(image_path)
    if not path.is_file():
        return None
    return path.read_text(encoding="utf-8", errors="replace").strip()


def write_caption(image_path, text):
    caption_path(image_path).write_text(text.strip() + "\n", encoding="utf-8")


def count_missing_captions(image_dir):
    return sum(1 for img in list_images(image_dir) if not caption_path(img).is_file())


def split_tags(caption):
    return [t.strip() for t in caption.split(",") if t.strip()]


def add_trigger_word(caption, trigger_word):
    """トリガーワードをキャプションの先頭タグにする (重複していれば先頭へ移動)"""
    trigger = trigger_word.strip()
    if not trigger:
        return caption.strip()
    tags = [t for t in split_tags(caption) if t.lower() != trigger.lower()]
    return ", ".join([trigger] + tags)


def snapshot_captions(image_dir):
    """既存キャプションを {画像パス: 内容} で保存する (自動キャプションで上書きしない場合の退避用)"""
    snapshot = {}
    for img in list_images(image_dir):
        text = read_caption(img)
        if text is not None:
            snapshot[img] = text
    return snapshot


def restore_captions(snapshot):
    for img, text in snapshot.items():
        write_caption(img, text)


def apply_trigger_word_to_folder(image_dir, trigger_word, create_missing=True):
    """フォルダ内の全キャプションにトリガーワードを付与する。変更したファイル数を返す"""
    changed = 0
    for img in list_images(image_dir):
        current = read_caption(img)
        if current is None and not create_missing:
            continue
        new = add_trigger_word(current or "", trigger_word)
        if new != (current or ""):
            write_caption(img, new)
            changed += 1
    return changed
