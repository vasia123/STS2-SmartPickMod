#!/usr/bin/env python3
"""Re-scrape tier lists from Mobalytics and slaythespire-2.com.

Usage:
    python3 scripts/scrape_tiers.py

Writes updated JSON files to data/tier_lists/. Requires `curl` on PATH.
"""

from __future__ import annotations

import datetime as dt
import html as html_lib
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_DIR = ROOT / "data" / "tier_lists"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/131.0.0.0 Safari/537.36"
)
TODAY = dt.date.today().isoformat()


def fetch(url: str) -> str:
    """Fetch a URL via curl with a normal browser UA."""
    out = subprocess.run(
        ["curl", "-sL", "-A", UA, url],
        check=True,
        capture_output=True,
    )
    return out.stdout.decode("utf-8", errors="replace")


# --- slaythespire-2.com -----------------------------------------------------

CHARS = ["Ironclad", "Silent", "Defect", "Regent", "Necrobinder"]
TIER_LETTERS = "SABCDF"


def scrape_sts2com() -> dict:
    html = fetch("https://slaythespire-2.com/card-tier")

    # Locate each character's section by its h2 header.
    char_starts = {}
    for c in CHARS:
        m = re.search(re.escape(c) + r" Card Tier List", html)
        if m:
            char_starts[c] = m.start()
    ordered = sorted(char_starts.items(), key=lambda x: x[1])

    characters: dict[str, dict[str, list[str]]] = {}
    marker_re = re.compile(
        r'<span>([SABCDF])</span><span class="text-\[8px\] font-medium opacity-80">(\d+)</span></div>'
    )
    for i, (c, start) in enumerate(ordered):
        end = ordered[i + 1][1] if i + 1 < len(ordered) else len(html)
        section = html[start:end]
        markers = [
            (m.group(1), int(m.group(2)), m.start(), m.end())
            for m in marker_re.finditer(section)
        ]
        tiers: dict[str, list[str]] = {}
        for j, (tier, count, _ms, mend) in enumerate(markers):
            content_end = markers[j + 1][2] if j + 1 < len(markers) else len(section)
            content = section[mend:content_end]
            cards = [html_lib.unescape(x) for x in re.findall(r'alt="([^"]+)"', content)]
            cards = [c for c in cards if c.strip()]
            seen: set[str] = set()
            cards = [c for c in cards if not (c in seen or seen.add(c))]
            if len(cards) != count:
                print(f"WARN sts2com {c}/{tier}: expected {count}, got {len(cards)}", file=sys.stderr)
            tiers[tier] = cards
        characters[c] = tiers
    return {
        "source": "https://slaythespire-2.com/card-tier",
        "snapshot_date": TODAY,
        "note": "Scraped from slaythespire-2.com (run scripts/scrape_tiers.py to refresh).",
        "characters": characters,
    }


# --- Mobalytics --------------------------------------------------------------

PRELOAD_RE = re.compile(r"window\.__PRELOADED_STATE__\s*=\s*(\{.*?\});", re.DOTALL)


def _extract_preload(html: str) -> dict:
    m = PRELOAD_RE.search(html)
    if not m:
        raise RuntimeError("__PRELOADED_STATE__ not found in Mobalytics HTML")
    return json.loads(m.group(1))


def _doc_data(state: dict) -> dict:
    body = state["sts2State"]["apollo"]["graphqlV2"]["queries"][1]["state"]["data"]
    return body[0]["game"]["documents"]["userGeneratedDocumentBySlug"]["data"]["data"]


def scrape_mobalytics_relics() -> dict:
    html = fetch("https://mobalytics.gg/slay-the-spire-2/tier-lists/relics")
    doc = _doc_data(_extract_preload(html))
    section = doc["tierLists"]["values"][0]
    tiers = {
        s["name"]: [item["name"] for item in (s.get("staticDataItems") or [])]
        for s in section["tierSections"]
    }
    return {"tiers": tiers}


def scrape_mobalytics_cards(prev_path: Path) -> dict:
    """Scrape cards. Map each tier list to a character by overlap with the
    existing JSON's per-character card sets — Mobalytics' API doesn't label
    them by name."""
    html = fetch("https://mobalytics.gg/slay-the-spire-2/tier-lists/cards")
    doc = _doc_data(_extract_preload(html))

    prev = json.loads(prev_path.read_text(encoding="utf-8"))
    sigs = {char: {c for tier in tiers.values() for c in tier} for char, tiers in prev["characters"].items()}

    characters: dict[str, dict[str, list[str]]] = {}
    for tl in doc["tierLists"]["values"]:
        tier_data: dict[str, list[str]] = {}
        all_cards: list[str] = []
        for sec in tl["tierSections"]:
            cards = [it["name"] for it in (sec.get("staticDataItems") or [])]
            tier_data[sec["name"]] = cards
            all_cards.extend(cards)
        scores = {char: sum(1 for c in all_cards if c in sig) for char, sig in sigs.items()}
        best = max(scores, key=scores.get)
        if scores[best] == 0:
            raise RuntimeError(f"could not identify character for tier list {tl['id']}")
        characters[best] = tier_data

    return {
        "source": "https://mobalytics.gg/slay-the-spire-2/tier-lists/cards",
        "snapshot_date": TODAY,
        "note": "Mobalytics STS2 card tier list (run scripts/scrape_tiers.py to refresh).",
        "characters": characters,
    }


# --- writers ----------------------------------------------------------------


def _write_inline_chars(path: Path, payload: dict, char_order: list[str]) -> None:
    """Write Mobalytics-style JSON: header keys + characters with inline tier arrays."""
    lines = ["{"]
    lines.append(f'  "source": {json.dumps(payload["source"], ensure_ascii=False)},')
    lines.append(f'  "snapshot_date": {json.dumps(payload["snapshot_date"], ensure_ascii=False)},')
    lines.append(f'  "note": {json.dumps(payload["note"], ensure_ascii=False)},')
    lines.append('  "characters": {')
    char_blocks = []
    for c in char_order:
        if c not in payload["characters"]:
            continue
        tier_lines = []
        for t in TIER_LETTERS:
            if t in payload["characters"][c]:
                arr = json.dumps(payload["characters"][c][t], ensure_ascii=False)
                tier_lines.append(f'      "{t}": {arr}')
        block = f'    "{c}": {{\n' + ",\n".join(tier_lines) + "\n    }"
        char_blocks.append(block)
    lines.append(",\n".join(char_blocks))
    lines.append("  }")
    lines.append("}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def _write_pretty(path: Path, payload: dict) -> None:
    path.write_text(
        json.dumps(payload, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)

    print("[1/3] Mobalytics relics")
    relics = scrape_mobalytics_relics()
    _write_pretty(DATA_DIR / "mobalytics_relics.json", relics)

    print("[2/3] slaythespire-2.com cards")
    sts2com = scrape_sts2com()
    _write_pretty(DATA_DIR / "slaythespire2_com_cards.json", sts2com)

    print("[3/3] Mobalytics cards")
    moba = scrape_mobalytics_cards(DATA_DIR / "mobalytics_cards.json")
    _write_inline_chars(
        DATA_DIR / "mobalytics_cards.json",
        moba,
        char_order=["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"],
    )

    print("Done. Re-build the mod to embed the new resources.")


if __name__ == "__main__":
    main()
