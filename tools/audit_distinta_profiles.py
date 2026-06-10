#!/usr/bin/env python3
"""Audit distinta post-correzione: confronto schede vs profili manuali."""
import pymysql

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

conn = pymysql.connect(**DB)
cur = conn.cursor()

print("=== OmniCore (devono essere VUOTI) ===")
cur.execute(
    """
    SELECT r.modello, COUNT(DISTINCT q.id) quadri,
           SUM(CASE WHEN d.id IS NULL THEN 0 ELSE 1 END) righe
    FROM gamma_robot r
    JOIN gamma_quadro q ON q.robot_id = r.id
    LEFT JOIN gamma_distinta d ON d.quadro_id = q.id
    WHERE r.id >= 26 AND q.controllore = 'OmniCore'
    GROUP BY r.modello
    ORDER BY r.modello
    """
)
for row in cur.fetchall():
    status = "OK" if row[2] == 0 else "ERRORE"
    print(f"  [{status}] {row[0]}: {row[1]} quadri, {row[2]} righe distinta")

print("\n=== IRC5 nuovi: schede vs ref quadro 98 ===")
cur.execute(
    """
    SELECT q.id FROM gamma_quadro q
    JOIN gamma_robot r ON r.id = q.robot_id
    WHERE r.modello = 'IRB 6700' LIMIT 1
    """
)
q6700 = cur.fetchone()[0]
cur.execute(
    """
    SELECT d.slot, p.code, d.is_alternate
    FROM gamma_distinta d JOIN quote_products p ON p.id = d.product_id
    WHERE d.quadro_id = %s AND d.sezione = 'Schede'
    ORDER BY d.slot, d.is_alternate
    """,
    (q6700,),
)
sc6700 = cur.fetchall()
cur.execute(
    """
    SELECT d.slot, p.code, d.is_alternate
    FROM gamma_distinta d JOIN quote_products p ON p.id = d.product_id
    WHERE d.quadro_id = 98 AND d.sezione = 'Schede'
    ORDER BY d.slot, d.is_alternate
    """
)
sc98 = cur.fetchall()
match = sc6700 == sc98
print(f"  IRB 6700 schede == ref 6640: {match}")
if not match:
    print("  6700:", sc6700)
    print("  ref:", sc98)

cur.execute(
    """
    SELECT COUNT(*) FROM gamma_distinta d
    JOIN gamma_quadro q ON q.id = d.quadro_id
    WHERE q.id = %s AND d.sezione = 'Motori'
    """,
    (q6700,),
)
print(f"  IRB 6700 motori (devono essere 0): {cur.fetchone()[0]}")

print("\n=== IRB 6660: non deve avere schede M2004 ===")
cur.execute(
    """
    SELECT p.code FROM gamma_distinta d
    JOIN quote_products p ON p.id = d.product_id
    JOIN gamma_quadro q ON q.id = d.quadro_id
    JOIN gamma_robot r ON r.id = q.robot_id
    WHERE r.modello = 'IRB 6660' AND d.sezione = 'Schede' AND d.is_alternate = 0
    LIMIT 3
    """
)
codes = [r[0] for r in cur.fetchall()]
m2004 = any("3HAC020929" in c or "3HAC12815" in c for c in codes)
print(f"  Primi codici: {codes}")
print(f"  Contiene M2004 (ERRORE): {m2004}")

cur.execute(
    """
    SELECT COUNT(*) FROM gamma_distinta d
    JOIN gamma_quadro q ON q.id = d.quadro_id
    JOIN gamma_robot r ON r.id = q.robot_id
    WHERE r.id >= 26
    """
)
print(f"\nTotale righe distinta robot>=26: {cur.fetchone()[0]}")

conn.close()
