"""LoRA Studio 起動スクリプト (run.bat から呼ばれます)"""

import sys

if sys.version_info < (3, 10):
    sys.exit("LoRA Studio には Python 3.10 以上が必要です")

from lora_studio.gui import main

if __name__ == "__main__":
    main()
