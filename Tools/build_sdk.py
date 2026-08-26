#!/usr/bin/env python3
"""Rebuild the entire Il2Cpp SDK from the dump and validate it compiles.

Usage:
    python Tools/build_sdk.py

Steps:
    1. Parse dump.cs into analysis/type_database.json
    2. Merge curated aliases
    3. Auto-extend aliases for referenced types
    4. Generate a first-pass C# SDK under Sdk/Generated
    5. Prune stale aliases that no longer resolve against the generated SDK
    6. Regenerate the final C# SDK
    7. Verify every alias resolves in the SDK
    8. Build the .NET project

Each step stops the pipeline on failure so bad output is not propagated.
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def run(cmd: list[str], cwd: Path) -> int:
    print(f"\n==> {' '.join(cmd)}")
    result = subprocess.run(cmd, cwd=cwd, check=False, capture_output=True, text=True)
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)
    return result.returncode


def main() -> int:
    steps = [
        [sys.executable, "Tools/dump_analyzer.py"],
        [sys.executable, "Tools/add_aliases.py"],
        [sys.executable, "Tools/auto_alias.py"],
        [sys.executable, "Tools/generate_sdk.py"],
        [sys.executable, "Tools/prune_aliases.py"],
        [sys.executable, "Tools/generate_sdk.py"],
        [sys.executable, "Tools/verify_sdk.py"],
        ["dotnet", "build", "-p:AutoDeploy=false"],
    ]
    for step in steps:
        if run(step, ROOT) != 0:
            print(f"\nBuild SDK failed at step: {' '.join(step)}")
            return 1
    print("\nSDK build succeeded.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
