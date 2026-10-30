import sys
import tempfile
try:
    import tomllib
except ImportError:  # Python 3.10 (sd-scripts の venv)
    import toml as tomllib
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from lora_studio import config  # noqa: E402
from lora_studio.config import ConfigError, TrainingConfig  # noqa: E402
from lora_studio.presets import load_presets  # noqa: E402

APP_DIR = Path(__file__).resolve().parent.parent


def make_env(root):
    """sd-scripts・モデル・画像フォルダの最小構成を作る"""
    sd = root / "sd-scripts"
    (sd / "finetune").mkdir(parents=True)
    (sd / config.TRAIN_SCRIPT).write_text("")
    (sd / config.TAGGER_SCRIPT).write_text("")
    py = config.venv_python(sd)
    py.parent.mkdir(parents=True)
    py.write_text("")
    model = root / "model.safetensors"
    model.write_text("")
    images = root / "images"
    images.mkdir()
    for i in range(3):
        (images / f"{i}.png").write_bytes(b"")
        (images / f"{i}.txt").write_text("1girl, smile", encoding="utf-8")
    return TrainingConfig(sd_scripts_dir=str(sd), pretrained_model=str(model), image_dir=str(images),
                          output_dir=str(root / "out"), trigger_word="sks")


class TrainingConfigTest(unittest.TestCase):
    def test_from_dict_coerces_gui_strings(self):
        cfg = TrainingConfig.from_dict({"network_dim": "32", "unet_lr": "1e-4", "seed": "7.0",
                                        "fp8_base": "true", "unknown": 1})
        self.assertEqual(cfg.network_dim, 32)
        self.assertEqual(cfg.unet_lr, 1e-4)
        self.assertEqual(cfg.seed, 7)
        self.assertTrue(cfg.fp8_base)

    def test_from_dict_rejects_bad_number(self):
        with self.assertRaises(ConfigError):
            TrainingConfig.from_dict({"network_dim": "abc"})

    def test_estimate_steps(self):
        cfg = TrainingConfig(num_repeats=10, max_train_epochs=10, batch_size=1)
        self.assertEqual(config.estimate_steps(cfg, 20), 2000)
        cfg.batch_size = 3
        self.assertEqual(config.estimate_steps(cfg, 20), 67 * 10)
        self.assertEqual(config.estimate_steps(cfg, 0), 0)


class ValidateTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.cfg = make_env(self.root)

    def tearDown(self):
        self.tmp.cleanup()

    def test_valid_config(self):
        errors, _ = config.validate(self.cfg)
        self.assertEqual(errors, [])

    def test_cache_te_conflicts(self):
        cfg = self.cfg.updated({"shuffle_caption": True, "network_train_unet_only": False})
        errors, _ = config.validate(cfg)
        self.assertEqual(len(errors), 2)

    def test_missing_captions_need_trigger(self):
        (self.root / "images" / "0.txt").unlink()
        errors, warnings = config.validate(self.cfg)
        self.assertEqual(errors, [])
        self.assertTrue(any("sks" in w for w in warnings))
        errors, _ = config.validate(self.cfg.updated({"trigger_word": ""}))
        self.assertTrue(any("キャプション" in e for e in errors))

    def test_missing_paths(self):
        errors, _ = config.validate(TrainingConfig())
        self.assertGreaterEqual(len(errors), 4)

    def test_resolution_multiple_of_64(self):
        errors, _ = config.validate(self.cfg.updated({"resolution": 1000}))
        self.assertTrue(any("64" in e for e in errors))


class CommandTest(unittest.TestCase):
    def test_dataset_toml_is_valid(self):
        cfg = TrainingConfig(image_dir=r'C:\データ\my "imgs"', trigger_word="sks", class_word="dog")
        data = tomllib.loads(config.build_dataset_toml(cfg))
        subset = data["datasets"][0]["subsets"][0]
        self.assertTrue(subset["image_dir"].endswith('my "imgs"'))
        self.assertEqual(subset["class_tokens"], "sks dog")
        self.assertEqual(data["datasets"][0]["resolution"], 1024)
        self.assertFalse(data["general"]["shuffle_caption"])

    def test_train_command_8gb_defaults(self):
        cfg = TrainingConfig(sd_scripts_dir="sd", pretrained_model="m.safetensors", output_dir="out")
        cmd = config.build_train_command(cfg, "ds.toml")
        self.assertIn(config.TRAIN_SCRIPT, cmd)
        for flag in ("--network_train_unet_only", "--cache_text_encoder_outputs", "--gradient_checkpointing",
                     "--cache_latents", "--cache_latents_to_disk", "--sdpa"):
            self.assertIn(flag, cmd)
        self.assertNotIn("--text_encoder_lr", cmd)
        self.assertNotIn("--fp8_base", cmd)
        self.assertEqual(cmd[cmd.index("--unet_lr") + 1], "0.0001")

    def test_train_command_options(self):
        cfg = TrainingConfig(network_train_unet_only=False, cache_text_encoder_outputs=False,
                             mixed_precision="fp16", fp8_base=True, optimizer_type="Adafactor",
                             vae="vae.safetensors", sample_every_n_epochs=1, extra_args='--foo "a b"')
        cmd = config.build_train_command(cfg, "ds.toml", "prompts.txt")
        self.assertIn("--text_encoder_lr", cmd)
        self.assertIn("--no_half_vae", cmd)
        self.assertIn("--fp8_base", cmd)
        self.assertIn("relative_step=False", cmd)
        self.assertIn("--sample_prompts", cmd)
        self.assertEqual(cmd[-2:], ["--foo", "a b"])

    def test_prepare_training_writes_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg = make_env(Path(tmp)).updated({"sample_prompt": "sks, 1girl", "sample_every_n_epochs": 2})
            cmd = config.prepare_training(cfg)
            work = cfg.work_dir
            for name in ("dataset.toml", "sample_prompts.txt", "config.json", "command.txt"):
                self.assertTrue((work / name).is_file(), name)
            self.assertIn("--w 1024", (work / "sample_prompts.txt").read_text(encoding="utf-8"))
            self.assertEqual(cmd[cmd.index("--dataset_config") + 1], str(work / "dataset.toml"))


class PresetTest(unittest.TestCase):
    def test_bundled_presets_are_valid_for_8gb(self):
        presets, errors = load_presets(APP_DIR / "presets")
        self.assertEqual(errors, [])
        self.assertGreaterEqual(len(presets), 3)
        with tempfile.TemporaryDirectory() as tmp:
            base = make_env(Path(tmp))
            for preset in presets:
                cfg = base.updated(preset.values)
                errors, _ = config.validate(cfg)
                self.assertEqual(errors, [], preset.name)
                self.assertTrue(cfg.gradient_checkpointing and cfg.cache_text_encoder_outputs, preset.name)
                self.assertTrue(cfg.network_train_unet_only, preset.name)
                self.assertEqual(cfg.batch_size, 1, preset.name)

    def test_preset_must_not_contain_paths(self):
        with tempfile.TemporaryDirectory() as tmp:
            Path(tmp, "bad.json").write_text('{"name": "x", "config": {"image_dir": "C:/"}}', encoding="utf-8")
            presets, errors = load_presets(tmp)
            self.assertEqual(presets, [])
            self.assertEqual(len(errors), 1)


if __name__ == "__main__":
    unittest.main()
