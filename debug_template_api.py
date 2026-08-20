"""Diagnostica: chiama /api/project-templates/tree con un login admin per capire
cosa sta restituendo realmente il server."""
import sys, json
import urllib.request, urllib.error

sys.stdout.reconfigure(encoding="utf-8")
BASE = "http://localhost:5150"

import pymysql
c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()
cur.execute("SELECT username, role FROM users WHERE role IN ('ADMIN','PM') LIMIT 5")
print("Utenti ADMIN/PM disponibili:")
for r in cur.fetchall():
    print(f"  username={r[0]!r}  role={r[1]!r}")
c.close()
print()

# Provo a chiamare l'endpoint senza auth per vedere cosa risponde
print("--- GET /tree senza auth ---")
try:
    req = urllib.request.Request(f"{BASE}/api/project-templates/tree")
    with urllib.request.urlopen(req, timeout=5) as resp:
        print(f"Status: {resp.status}")
        body = resp.read().decode("utf-8")
        print(f"Body: {body[:300]}")
except urllib.error.HTTPError as e:
    print(f"HTTPError {e.code}: {e.read().decode('utf-8', errors='replace')[:300]}")
except Exception as e:
    print(f"Errore: {type(e).__name__}: {e}")
