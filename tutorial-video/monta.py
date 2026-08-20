"""Monta un tutorial: voce concatenata + video della GUI, più i sottotitoli.

Uso:  python monta.py <copione> <cartella-video> <marks.json> <nome-uscita>
"""
import glob, json, os, shutil, subprocess, sys

BASE = os.path.dirname(os.path.abspath(__file__))
# ffmpeg: dal PATH se c'è, altrimenti dove lo mette winget (Gyan.FFmpeg).
FFMPEG = shutil.which("ffmpeg") or next(iter(glob.glob(os.path.expandvars(
    r"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg*\ffmpeg*\bin\ffmpeg.exe"))), "ffmpeg")

copione_nome = sys.argv[1]
video_dir = sys.argv[2]
marks_file = sys.argv[3]
uscita = sys.argv[4]

copione = json.load(open(os.path.join(BASE, copione_nome + ".json"), encoding="utf-8"))
durate = json.load(open(os.path.join(BASE, "durate-" + copione_nome + ".json"), encoding="utf-8"))
marks = json.load(open(os.path.join(BASE, marks_file), encoding="utf-8"))
offset = marks["offsetMs"] / 1000.0

video = [f for f in os.listdir(os.path.join(BASE, video_dir)) if f.endswith(".webm")][0]
video = os.path.join(BASE, video_dir, video)

# 1 ── voce unica, ANCORATA ai tempi reali dei passi
#
# Non basta incollare le clip una dopo l'altra: se durante la registrazione un passo
# è durato più del suo parlato (un selettore lento, una pagina che ci mette), tutti i
# passi successivi slittano e la voce resta indietro rispetto a ciò che si vede.
# `marks.atMs` dice a che istante ogni passo è partito davvero: fra una clip e la
# successiva si mette esattamente il silenzio che serve per ricadere su quell'istante.
tempi = {m["id"]: m["atMs"] / 1000.0 for m in marks["marks"]}
padded = []
avvisi = []
for i, d in enumerate(durate):
    inizio = tempi.get(d["id"])
    prossimo = tempi.get(durate[i + 1]["id"]) if i + 1 < len(durate) else None
    gap = 0.0
    if inizio is not None and prossimo is not None:
        gap = (prossimo - inizio) - d["seconds"]
    if gap < -0.05:
        avvisi.append("%s: il parlato dura %.1fs oltre il suo passo" % (d["id"], -gap))
        gap = 0.0
    # WAV e non MP3: concatenando MP3 ogni giunzione aggiunge il ritardo dell'encoder
    # (~50 ms), che su sedici clip diventa un secondo di sfasamento a fine video.
    out = os.path.join(BASE, "audio_pad_%02d.wav" % i)
    subprocess.run([FFMPEG, "-y", "-v", "error", "-i", d["file"],
                    "-af", "apad=pad_dur=%.3f" % max(0.0, gap),
                    "-ar", "48000", "-ac", "1", "-c:a", "pcm_s16le", out], check=True)
    padded.append(out)

lista = os.path.join(BASE, "lista_" + copione_nome + ".txt")
with open(lista, "w", encoding="utf-8") as f:
    for p in padded:
        f.write("file '%s'\n" % p.replace("\\", "/"))
voce = os.path.join(BASE, "voce-" + copione_nome + ".wav")
subprocess.run([FFMPEG, "-y", "-v", "error", "-f", "concat", "-safe", "0",
                "-i", lista, "-c", "copy", voce], check=True)
for a in avvisi:
    print("AVVISO:", a)


def ts(sec):
    h = int(sec // 3600)
    m = int((sec % 3600) // 60)
    s = sec % 60
    ms = min(999, round((s - int(s)) * 1000))
    return "%02d:%02d:%02d,%03d" % (h, m, int(s), ms)


srt = os.path.join(BASE, uscita + ".srt")
with open(srt, "w", encoding="utf-8") as f:
    for i, d in enumerate(durate, 1):
        # stessi istanti dell'audio: i tempi reali dei passi, non la somma delle durate
        t = tempi.get(d["id"], 0.0)
        say = next(c["say"] for c in copione if c["id"] == d["id"])
        words, lines, cur = say.split(), [], ""
        for w in words:
            if len(cur) + len(w) + 1 > 90:
                lines.append(cur); cur = w
            else:
                cur = (cur + " " + w).strip()
        if cur:
            lines.append(cur)
        f.write("%d\n%s --> %s\n%s\n\n" % (i, ts(t), ts(t + d["seconds"]), "\n".join(lines)))

out = os.path.join(BASE, uscita + ".mp4")
subprocess.run([FFMPEG, "-y", "-v", "error", "-stats",
                "-ss", "%.3f" % offset, "-i", video,
                "-i", voce,
                "-map", "0:v:0", "-map", "1:a:0",
                "-c:v", "libx264", "-preset", "slow", "-crf", "21",
                "-pix_fmt", "yuv420p", "-r", "25",
                "-c:a", "aac", "-b:a", "160k",
                "-movflags", "+faststart", out], check=True)

print("\nFATTO: %s  (%.1f MB)" % (out, os.path.getsize(out) / 1024 / 1024))
print("SRT:   %s" % srt)
