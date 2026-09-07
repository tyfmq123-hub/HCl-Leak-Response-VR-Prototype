"""Generate the canonical stage audio from the reviewed, versioned TTS table.

Requires edge-tts and ffmpeg. Existing .meta/GUID files are never touched.
The manifest binds spoken text to audio SHA256 so stale clips can be detected.
"""
import argparse
import asyncio
import hashlib
import json
import re
import subprocess
import tempfile
from pathlib import Path

import edge_tts

ROOT = Path(__file__).resolve().parents[2]
VOICE = "ko-KR-SunHiNeural"


def cues():
    stage = None
    for line in (ROOT / "docs/audio/FULL_COURSE_TTS_SCRIPT.md").read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            stage = next((s for s in ("PPE", "PreEntry", "LeakResponse") if line.startswith("## " + s + " ")), None)
        match = re.match(r"\| (\d{2}_[^|]+) \| ([^|]+) \|", line)
        if stage and match:
            yield stage, match[1].strip(), match[2].strip()


async def main(ffmpeg):
    limiter = asyncio.Semaphore(2)
    records = []
    manifest_path = ROOT / "docs/audio/narration-manifest.json"
    previous = {}
    if manifest_path.exists():
        previous = {(r["stage"], r["cue"]): r for r in json.loads(manifest_path.read_text(encoding="utf-8"))["cues"]}
    with tempfile.TemporaryDirectory(prefix="hcl-reviewed-tts-") as temp:
        async def generate(stage, name, text):
            async with limiter:
                raw = Path(temp) / f"{stage}-{name}-raw.mp3"
                output = ROOT / f"Assets/Resources/TrainingNarration/{stage}/{name}.mp3"
                old = previous.get((stage, name))
                if old and old["text"] == text and output.exists() and hashlib.sha256(output.read_bytes()).hexdigest() == old["sha256"]:
                    records.append(old)
                    return
                for attempt in range(3):
                    try:
                        await edge_tts.Communicate(text, VOICE, rate="-8%", volume="-4%", pitch="-1Hz").save(str(raw))
                        break
                    except Exception:
                        if attempt == 2:
                            raise
                        await asyncio.sleep(2)
                clean = Path(temp) / f"{stage}-{name}.mp3"
                trim = "silenceremove=start_periods=1:start_duration=0.02:start_threshold=-48dB:start_silence=0.06"
                subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", str(raw),
                                "-af", f"{trim},areverse,{trim},areverse", "-ar", "24000", "-ac", "1",
                                "-codec:a", "libmp3lame", "-b:a", "96k", str(clean)], check=True)
                if clean.stat().st_size < 2000:
                    raise RuntimeError("Empty narration: " + name)
                data = clean.read_bytes()
                output.write_bytes(data)
                records.append({"stage": stage, "cue": name, "text": text,
                                "audio": str(output.relative_to(ROOT)).replace("\\", "/"),
                                "sha256": hashlib.sha256(data).hexdigest()})
                print(f"Generated {stage}/{name}", flush=True)
        await asyncio.gather(*(generate(*cue) for cue in cues()))
    manifest = {"voice": VOICE, "rate": "-8%", "volume": "-4%", "pitch": "-1Hz",
                "edge_silence_seconds": 0.06, "cues": sorted(records, key=lambda r: (r["stage"], r["cue"]))}
    (ROOT / "docs/audio/narration-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Verified generation: {len(records)} clips", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--ffmpeg", required=True)
    args = parser.parse_args()
    asyncio.run(main(args.ffmpeg))
