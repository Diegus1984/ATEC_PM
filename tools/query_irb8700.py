import pymysql

c = pymysql.connect(
    host="localhost", port=3306, user="root", password="Atec2005",
    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

cur.execute("""
    SELECT r.id, r.modello, r.serie, r.brand, r.note,
           (SELECT COUNT(*) FROM gamma_quadro q WHERE q.robot_id=r.id AND q.is_active=1) AS quadri
    FROM gamma_robot r
    WHERE r.is_active=1
      AND (r.modello LIKE '%8700%' OR r.modello LIKE '%IRB 8700%' OR r.modello LIKE '%IRB8700%')
    ORDER BY r.modello
""")
robots = cur.fetchall()
print("=== ROBOT IRB 8700 nel DB ===")
print(f"Trovati modelli robot: {len(robots)}")

total_quadri = 0
for row in robots:
    rid, modello, serie, brand, note, quadri = row
    total_quadri += quadri
    print(f"\n  id={rid} | {modello} | serie={serie} | brand={brand} | quadri={quadri}")
    if note:
        print(f"    note: {note}")
    cur.execute("""
        SELECT id, controllore, generazione, payload, area_lavoro, os_version, system_key, note,
               (SELECT COUNT(*) FROM gamma_distinta d WHERE d.quadro_id=q.id) AS componenti
        FROM gamma_quadro q
        WHERE q.robot_id=%s AND q.is_active=1
        ORDER BY payload, area_lavoro, generazione, controllore
    """, (rid,))
    for q in cur.fetchall():
        qid, ctrl, gen, payload, area, osv, skey, qnote, comp = q
        parts = []
        if ctrl:
            parts.append(str(ctrl))
        if gen:
            parts.append(f"gen={gen}")
        if payload:
            parts.append(f"{payload}kg")
        if area:
            parts.append(f"{area}m")
        if osv:
            parts.append(f"os={osv}")
        label = " | ".join(parts) if parts else "(senza dettagli)"
        print(f"    · quadro {qid}: {label} | {comp} componenti | key={skey} | note={qnote or ''}")

print(f"\nTotale configurazioni (quadri) IRB 8700: {total_quadri}")

# anche modelli simili se nessun match esatto
if len(robots) == 0:
    cur.execute("""
        SELECT modello, COUNT(*) FROM gamma_robot WHERE is_active=1
        GROUP BY modello ORDER BY modello
    """)
    print("\nNessun IRB 8700 — elenco modelli presenti:")
    for m, cnt in cur.fetchall():
        print(f"  {m} ({cnt})")

c.close()
