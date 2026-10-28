import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from lora_studio import captioning, dataset  # noqa: E402


class DatasetTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        for name in ("b.PNG", "a.jpg", "c.webp", "note.txt", "d.gif"):
            (self.dir / name).write_bytes(b"")

    def tearDown(self):
        self.tmp.cleanup()

    def test_list_images(self):
        self.assertEqual([p.name for p in dataset.list_images(self.dir)], ["a.jpg", "b.PNG", "c.webp"])
        self.assertEqual(dataset.list_images(""), [])
        self.assertEqual(dataset.list_images(self.dir / "missing"), [])

    def test_add_trigger_word(self):
        self.assertEqual(dataset.add_trigger_word("1girl, smile", "sks"), "sks, 1girl, smile")
        self.assertEqual(dataset.add_trigger_word("1girl, SKS, smile", "sks"), "sks, 1girl, smile")
        self.assertEqual(dataset.add_trigger_word("", "sks"), "sks")
        self.assertEqual(dataset.add_trigger_word("1girl", ""), "1girl")

    def test_apply_trigger_word_to_folder(self):
        dataset.write_caption(self.dir / "a.jpg", "1girl")
        self.assertEqual(dataset.apply_trigger_word_to_folder(self.dir, "sks", create_missing=False), 1)
        self.assertEqual(dataset.read_caption(self.dir / "a.jpg"), "sks, 1girl")
        self.assertIsNone(dataset.read_caption(self.dir / "b.PNG"))
        self.assertEqual(dataset.apply_trigger_word_to_folder(self.dir, "sks"), 2)
        self.assertEqual(dataset.read_caption(self.dir / "c.webp"), "sks")
        self.assertEqual(dataset.apply_trigger_word_to_folder(self.dir, "sks"), 0)

    def test_finish_captioning_keeps_existing_when_not_overwriting(self):
        dataset.write_caption(self.dir / "a.jpg", "my own caption")
        snapshot = dataset.snapshot_captions(self.dir)
        # Tagger が全画像のキャプションを書いた状態を再現
        for img in dataset.list_images(self.dir):
            dataset.write_caption(img, "1girl, solo")
        opts = captioning.CaptionOptions(overwrite=False, add_trigger=True)
        captioning.finish_captioning(self.dir, snapshot, opts, "sks")
        self.assertEqual(dataset.read_caption(self.dir / "a.jpg"), "sks, my own caption")
        self.assertEqual(dataset.read_caption(self.dir / "b.PNG"), "sks, 1girl, solo")

    def test_tagger_command(self):
        cmd = captioning.build_tagger_command("sd", self.dir, captioning.CaptionOptions(undesired_tags="solo"))
        self.assertIn("--onnx", cmd)
        self.assertEqual(cmd[cmd.index("--undesired_tags") + 1], "solo")
        self.assertEqual(cmd[cmd.index("--thresh") + 1], "0.35")


if __name__ == "__main__":
    unittest.main()
