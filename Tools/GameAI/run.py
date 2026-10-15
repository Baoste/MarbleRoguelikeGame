"""Run from the project root; all generated files default to the project-local .utmp."""
from pathlib import Path
import os
import sys

ROOT = Path(__file__).resolve().parents[2]
TEMP = ROOT / '.utmp/game-ai/temp'
TEMP.mkdir(parents=True, exist_ok=True)
os.environ['TEMP'] = os.environ['TMP'] = str(TEMP)
os.environ['PYTHONDONTWRITEBYTECODE'] = '1'
os.environ.setdefault('TORCH_HOME', str(ROOT / '.utmp/game-ai/torch-cache'))
sys.dont_write_bytecode = True

if __name__ == '__main__':
    from game_ai.cli import main
    main()
