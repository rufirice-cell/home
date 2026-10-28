"""setup.bat から呼ばれるインストール確認スクリプト"""

import sys

print(f"Python {sys.version.split()[0]}")
try:
    import torch
except ImportError:
    sys.exit("[ERROR] PyTorch is not installed")
if torch.cuda.is_available():
    props = torch.cuda.get_device_properties(0)
    print(f"torch {torch.__version__} / GPU: {props.name} ({props.total_memory / 1024**3:.1f} GB) / bf16: {torch.cuda.is_bf16_supported()}")
else:
    print(f"[WARNING] torch {torch.__version__}: CUDA GPU is not available. Check the NVIDIA driver.")

for name in ("accelerate", "bitsandbytes", "onnxruntime", "tkinter", "PIL"):
    try:
        __import__(name)
        print(f"{name}: OK")
    except Exception as e:
        print(f"[WARNING] {name}: {e}")
