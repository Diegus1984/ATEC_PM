import pymysql

c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

print("=== Quadro 108 ===")
cur.execute("SELECT * FROM gamma_quadro WHERE id=108")
print(cur.fetchone())

print("\n=== Distinta quadro 108 ===")
cur.execute("SELECT COUNT(*) FROM gamma_distinta WHERE quadro_id=108")
print("count:", cur.fetchone()[0])

print("\n=== Prodotti catalogo con 8700 nel codice/nome ===")
cur.execute("""
    SELECT p.id, p.code, p.name, p.is_active
    FROM quote_products p
    WHERE p.code LIKE '%8700%' OR p.name LIKE '%8700%' OR p.name LIKE '%IRB 8700%'
    ORDER BY p.code
""")
prods = cur.fetchall()
print(f"trovati: {len(prods)}")
for r in prods[:30]:
    print(" ", r)
if len(prods) > 30:
    print(f"  ... +{len(prods)-30}")

print("\n=== Distinta storica: righe con 8700 in raw/note/code (qualsiasi quadro) ===")
cur.execute("""
    SELECT d.id, d.quadro_id, q.system_key, d.code_raw, d.product_id, p.code, d.sezione, d.slot
    FROM gamma_distinta d
    LEFT JOIN gamma_quadro q ON q.id = d.quadro_id
    LEFT JOIN quote_products p ON p.id = d.product_id
    WHERE d.code_raw LIKE '%8700%' OR d.note LIKE '%8700%' OR d.raw_text LIKE '%8700%'
       OR p.code LIKE '%8700%' OR p.name LIKE '%8700%'
       OR q.system_key LIKE '%8700%'
    LIMIT 50
""")
rows = cur.fetchall()
print(f"trovati: {len(rows)}")
for r in rows:
    print(" ", r)

print("\n=== Confronto: quadro simile heavy payload (7600/6700/6650) ===")
cur.execute("""
    SELECT q.id, r.modello, q.system_key, q.payload, q.area_lavoro,
           (SELECT COUNT(*) FROM gamma_distinta d WHERE d.quadro_id=q.id) AS n
    FROM gamma_quadro q
    JOIN gamma_robot r ON r.id = q.robot_id
    WHERE r.modello LIKE '%7600%' OR r.modello LIKE '%6700%' OR r.modello LIKE '%6650%'
       OR r.modello LIKE '%IRB 6600%' OR r.modello LIKE '%IRB 7600%'
    ORDER BY n DESC
    LIMIT 10
""")
for r in cur.fetchall():
    print(" ", r)

print("\n=== Sezioni usate in distinta (sample IRB 1600 IRC5) ===")
cur.execute("""
    SELECT DISTINCT d.sezione FROM gamma_distinta d
    JOIN gamma_quadro q ON q.id=d.quadro_id
    JOIN gamma_robot r ON r.id=q.robot_id
    WHERE r.modello LIKE '%1600%' AND q.controllore='IRC5'
    LIMIT 20
""")
print([x[0] for x in cur.fetchall()])

c.close()
