import os
import sys
import tempfile
import textwrap
import threading
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from lora_studio import config  # noqa: E402
from lora_studio.process import LineSplitter, ProcessRunner, parse_epoch, parse_progress  # noqa: E402


class ParseTest(unittest.TestCase):
    def test_training_step(self):
        p = parse_progress("steps:  12%|█▏        | 120/1000 [01:23<10:12,  1.44it/s, avr_loss=0.0912]")
        self.assertTrue(p.is_training_step)
        self.assertEqual((p.current, p.total), (120, 1000))
        self.assertEqual(p.remaining, "10:12")
        self.assertEqual(p.rate, "1.44 it/s")
        self.assertAlmostEqual(p.loss, 0.0912)
        self.assertAlmostEqual(p.fraction, 0.12)

    def test_seconds_per_iteration_and_unknown(self):
        p = parse_progress("steps:   0%|          | 1/2000 [00:02<1:10:00,  2.31s/it, avr_loss=nan]")
        self.assertEqual(p.rate, "2.31 s/it")
        self.assertEqual(p.remaining, "1:10:00")
        p = parse_progress("steps:   0%|          | 0/2000 [00:00<?, ?it/s]")
        self.assertEqual((p.remaining, p.rate, p.loss), ("", "", None))

    def test_non_training_progress(self):
        p = parse_progress("100%|██████████| 20/20 [00:05<00:00,  3.90it/s]")
        self.assertFalse(p.is_training_step)
        self.assertIsNone(parse_progress("INFO loading model"))

    def test_epoch(self):
        self.assertEqual(parse_epoch("epoch 3/10"), (3, 10))
        self.assertIsNone(parse_epoch("skipping epoch 1/10"))


class LineSplitterTest(unittest.TestCase):
    def test_cr_and_crlf(self):
        s = LineSplitter()
        out = s.feed(b"a\r\nb\rc\n")
        self.assertEqual(out, [("a", False), ("b", True), ("c", False)])

    def test_waits_for_split_crlf(self):
        s = LineSplitter()
        self.assertEqual(s.feed(b"x\r"), [])
        self.assertEqual(s.feed(b"\ny"), [("x", False)])
        self.assertEqual(s.flush(), [("y", False)])

    def test_multibyte_split_across_chunks(self):
        s = LineSplitter()
        data = "学習中\n".encode("utf-8")
        self.assertEqual(s.feed(data[:4]), [])
        self.assertEqual(s.feed(data[4:]), [("学習中", False)])


FAKE_LAUNCH = textwrap.dedent('''
    import runpy, sys
    args = sys.argv[1:]
    while args and args[0].startswith("--"):
        args = args[2:]
    sys.argv = args
    runpy.run_path(args[0], run_name="__main__")
''')

FAKE_TRAIN = textwrap.dedent('''
    import sys, time
    assert "--dataset_config" in sys.argv
    print("\\nepoch 1/2\\n")
    for i in range(1, 5):
        sys.stderr.write(f"\\rsteps:  {i*25}%|##| {i}/4 [00:0{i}<00:0{4-i},  1.00it/s, avr_loss=0.{i}]")
        sys.stderr.flush()
        time.sleep(0.02)
    sys.stderr.write("\\n")
    print("epoch 2/2")
    print("学習完了")
''')


class ProcessRunnerTest(unittest.TestCase):
    def test_runs_fake_training_end_to_end(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            sd = root / "sd-scripts"
            (sd / "accelerate" / "commands").mkdir(parents=True)
            (sd / "accelerate" / "__init__.py").write_text("")
            (sd / "accelerate" / "commands" / "__init__.py").write_text("")
            (sd / "accelerate" / "commands" / "launch.py").write_text(FAKE_LAUNCH)
            (sd / config.TRAIN_SCRIPT).write_text(FAKE_TRAIN, encoding="utf-8")
            py = config.venv_python(sd)
            py.parent.mkdir(parents=True)
            if os.name == "nt":
                import shutil
                shutil.copy(sys.executable, py)
            else:
                py.symlink_to(sys.executable)
            images = root / "img"
            images.mkdir()
            (images / "a.png").write_bytes(b"")
            model = root / "m.safetensors"
            model.write_text("")
            cfg = config.TrainingConfig(sd_scripts_dir=str(sd), pretrained_model=str(model),
                                        image_dir=str(images), output_dir=str(root / "out"), trigger_word="sks")
            self.assertEqual(config.validate(cfg)[0], [])
            cmd = config.prepare_training(cfg)

            lines, done = [], threading.Event()
            result = {}
            runner = ProcessRunner(cmd, sd, on_output=lambda t, o: lines.append((t, o)),
                                   on_exit=lambda code: (result.setdefault("code", code), done.set()))
            runner.start()
            self.assertTrue(done.wait(30), lines)
            self.assertEqual(result["code"], 0, lines)
            steps = [parse_progress(t) for t, _ in lines if parse_progress(t)]
            self.assertEqual([p.current for p in steps], [1, 2, 3, 4])
            self.assertTrue(all(p.is_training_step for p in steps))
            self.assertIn(("学習完了", False), lines)
            self.assertIn((1, 2), [parse_epoch(t) for t, _ in lines])

    def test_stop_kills_process(self):
        done = threading.Event()
        runner = ProcessRunner([sys.executable, "-c", "import time; print('start', flush=True); time.sleep(60)"],
                               ".", on_output=lambda t, o: None, on_exit=lambda code: done.set())
        runner.start()
        runner.stop()
        self.assertTrue(done.wait(10))
        self.assertTrue(runner.stopped)


if __name__ == "__main__":
    unittest.main()
