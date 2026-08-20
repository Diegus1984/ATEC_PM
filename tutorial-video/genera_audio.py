import asyncio, json, os, subprocess, sys
import edge_tts

BASE = os.path.dirname(os.path.abspath(__file__))
# Primo argomento = nome del copione (senza estensione). Default: copione.json
NOME = sys.argv[1] if len(sys.argv) > 1 else "copione"
AUDIO = os.path.join(BASE, "audio" if NOME == "copione" else "audio-" + NOME)
# ffprobe: dal PATH se c'è, altrimenti dove lo mette winget (Gyan.FFmpeg).
import glob, shutil
FFPROBE = shutil.which("ffprobe") or next(iter(glob.glob(os.path.expandvars(
    r"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg*\ffmpeg*\bin\ffprobe.exe"))), "ffprobe")
VOICE = "it-IT-DiegoNeural"
RATE = "-4%"

os.makedirs(AUDIO, exist_ok=True)

with open(os.path.join(BASE, NOME + ".json"), encoding="utf-8") as f:
    steps = json.load(f)


def duration(path):
    out = subprocess.run(
        [FFPROBE, "-v", "error", "-show_entries", "format=duration",
         "-of", "default=noprint_wrappers=1:nokey=1", path],
        capture_output=True, text=True, check=True)
    return float(out.stdout.strip())


async def main():
    result = []
    for step in steps:
        path = os.path.join(AUDIO, step["id"] + ".mp3")
        tts = edge_tts.Communicate(step["say"], VOICE, rate=RATE)
        await tts.save(path)
        d = duration(path)
        result.append({"id": step["id"], "file": path, "seconds": round(d, 3)})
        print(f"{step['id']:16s} {d:6.2f}s")
    dur_name = "durate.json" if NOME == "copione" else "durate-" + NOME + ".json"
    with open(os.path.join(BASE, dur_name), "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)
    total = sum(r["seconds"] for r in result)
    print(f"\nTOTALE: {total:.1f}s  ({int(total//60)}m {int(total%60)}s)")


asyncio.run(main())
