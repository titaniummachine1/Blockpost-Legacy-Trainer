"""Parse a BepInEx capture log and regenerate Sdk/Weapons.cs."""

import argparse
import glob
import os
import re
from pathlib import Path


LINE_RE = re.compile(r"id=(\d+),\s*codename=([^,]+),\s*name=(.*)$")


def escape_csharp_string(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"')


def parse_log(path: str) -> dict[int, tuple[str, str]]:
    weapons: dict[int, tuple[str, str]] = {}
    with open(path, "r", encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\n")
            if "case[" in line and "weapon[" not in line:
                continue
            m = LINE_RE.search(line)
            if not m:
                continue
            wid = int(m.group(1))
            codename = m.group(2).strip()
            name = m.group(3).strip()
            if wid in weapons:
                continue
            weapons[wid] = (codename, name)
    return weapons


def render_weapons_cs(weapons: dict[int, tuple[str, str]], out_path: str) -> None:
    sorted_weapons = sorted(weapons.items(), key=lambda kv: kv[0])
    lines = [
        "namespace BlockpostTrainer.Sdk",
        "{",
        "    /// <summary>",
        "    /// Known weapon / inventory item ids, codenames and display names.",
        "    /// Populated from an in-game dump of GUIInv.OIHNJCKDOIG (NAHLLMJMOED[]) and ACEDGBLFHDK cases.",
        "    /// The runtime source of truth is still GUIInv.AllWeapons; this is a convenience lookup.",
        "    /// </summary>",
        "    public static class Weapons",
        "    {",
        "        public static readonly System.Collections.Generic.Dictionary<int, string> CodenameById = new()",
        "        {",
    ]
    for wid, (codename, _) in sorted_weapons:
        lines.append(f'            [{wid}] = "{escape_csharp_string(codename)}",')
    lines.extend([
        "        };",
        "",
        "        public static readonly System.Collections.Generic.Dictionary<int, string> NameById = new()",
        "        {",
    ])
    for wid, (_, name) in sorted_weapons:
        lines.append(f'            [{wid}] = "{escape_csharp_string(name)}",')
    lines.extend([
        "        };",
        "",
        "        public static readonly System.Collections.Generic.Dictionary<string, int> IdByCodename = new()",
        "        {",
    ])
    for wid, (codename, _) in sorted_weapons:
        lines.append(f'            ["{escape_csharp_string(codename)}"] = {wid},')
    lines.extend([
        "        };",
        "    }",
        "}",
    ])
    Path(out_path).write_text("\n".join(lines) + "\n", encoding="utf-8")


def find_latest_capture(captures_dir: str) -> str:
    files = glob.glob(os.path.join(captures_dir, "net-*.log"))
    if not files:
        raise FileNotFoundError(f"no net-*.log files in {captures_dir}")
    return max(files, key=os.path.getmtime)


def main():
    parser = argparse.ArgumentParser(description="Regenerate Sdk/Weapons.cs from a dump log.")
    parser.add_argument(
        "--log",
        default=None,
        help="BepInEx capture log to parse (defaults to most recent net-*.log).",
    )
    parser.add_argument(
        "--out",
        default="Sdk/Weapons.cs",
        help="Output C# file path.",
    )
    args = parser.parse_args()

    log_path = args.log
    if log_path is None:
        captures_dir = r"C:\Steam\steamapps\common\BLOCKPOST\BepInEx\captures"
        log_path = find_latest_capture(captures_dir)

    weapons = parse_log(log_path)
    render_weapons_cs(weapons, args.out)
    print(f"Parsed {log_path}")
    print(f"Wrote {len(weapons)} weapons to {args.out}")


if __name__ == "__main__":
    main()
