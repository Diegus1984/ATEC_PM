import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

PRIMARY = 351  # 3HAC029157-001
ALT = 253      # 3HAC028179-001

cur.execute("""
    SELECT q.id, q.system_key, r.modello
    FROM gamma_distinta d
    JOIN gamma_quadro q ON q.id = d.quadro_id
    JOIN gamma_robot r ON r.id = q.robot_id
    WHERE d.product_id = %s AND d.is_alternate = 0
      AND d.sezione = 'Schede' AND d.slot = 'Axis Computer'
""", (PRIMARY,))
primary_quadri = {r[0]: r for r in cur.fetchall()}

cur.execute("""
    SELECT DISTINCT d.quadro_id FROM gamma_distinta d
    WHERE d.product_id = %s AND d.is_alternate = 1
      AND d.sezione = 'Schede' AND d.slot = 'Axis Computer'
""", (ALT,))
alt_quadri = {r[0] for r in cur.fetchall()}

missing_alt = [primary_quadri[qid] for qid in primary_quadri if qid not in alt_quadri]
only_alt = []
cur.execute("""
    SELECT DISTINCT d.quadro_id FROM gamma_distinta d
    WHERE d.product_id = %s AND d.is_alternate = 1
""", (ALT,))
for (qid,) in cur.fetchall():
    if qid not in primary_quadri:
        cur.execute("SELECT system_key FROM gamma_quadro WHERE id=%s", (qid,))
        only_alt.append((qid, cur.fetchone()[0]))

print(f"Quadri con 668 primario ({PRIMARY}): {len(primary_quadri)}")
print(f"Quadri con 668 ALT ({ALT}): {len(alt_quadri)}")
print(f"Primario SENZA riga ALT: {len(missing_alt)}")
for r in missing_alt[:15]:
    print(" ", r)
if len(missing_alt) > 15:
    print(f"  ... +{len(missing_alt)-15}")
print(f"Solo ALT senza primario: {len(only_alt)}")
for r in only_alt[:10]:
    print(" ", r)

c.close()
