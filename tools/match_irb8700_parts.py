import pymysql
import re

c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

print("=== Distinta IRB 7600 (reference) - sezione Schede ===")
cur.execute("""
    SELECT d.slot, p.code, p.name, d.sezione
    FROM gamma_distinta d
    JOIN gamma_quadro q ON q.id = d.quadro_id
    JOIN gamma_robot r ON r.id = q.robot_id
    LEFT JOIN quote_products p ON p.id = d.product_id
    WHERE r.modello LIKE '%7600%' AND d.sezione = 'Schede' AND d.is_alternate = 0
    LIMIT 25
""")
for r in cur.fetchall():
    print(" ", r)

print("\n=== Sezioni distinta IRB 7600 ===")
cur.execute("""
    SELECT d.sezione, COUNT(*) FROM gamma_distinta d
    JOIN gamma_quadro q ON q.id = d.quadro_id
    JOIN gamma_robot r ON r.id = q.robot_id
    WHERE r.modello LIKE '%7600%'
    GROUP BY d.sezione
""")
for r in cur.fetchall():
    print(" ", r)

print("\n=== Match catalogo: codici 8700 manual (electrical) ===")
codes = [
    "3HAC050792-001", "3HAC044073-001", "3HAC043904-001", "3HAC065021-001",
    "3HAC049785-001", "3HAC044076-001", "3HAC6499-1", "3HAC026331-001",
    "3HAC044161-001", "3HAC044075-001", "3HAC044200-001", "3HAC044205-001",
    "DSQC1052",
]
for code in codes:
    cur.execute("""
        SELECT id, code, name FROM quote_products
        WHERE code LIKE %s OR name LIKE %s OR code LIKE %s
        LIMIT 3
    """, (f"%{code}%", f"%{code}%", f"%{code.replace('-001','')}%"))
    rows = cur.fetchall()
    print(f"  {code}: {rows if rows else 'NON TROVATO'}")

print("\n=== Prodotti catalogo con 8700 o DSQC1052 o RMU102 ===")
cur.execute("""
    SELECT id, code, name FROM quote_products
    WHERE code LIKE '%8700%' OR name LIKE '%8700%'
       OR name LIKE '%RMU102%' OR code LIKE '%RMU102%'
       OR name LIKE '%DSQC1052%' OR code LIKE '%DSQC1052%'
       OR name LIKE '%3HAC050792%' OR code LIKE '%3HAC050792%'
    ORDER BY code, name
""")
for r in cur.fetchall():
    print(" ", r)

print("\n=== Schede DSQC in catalogo (sample) ===")
cur.execute("""
    SELECT p.id, p.code, p.name, c.name AS cat
    FROM quote_products p
    JOIN quote_categories c ON c.id = p.category_id
    WHERE p.name LIKE '%DSQC%' OR p.code LIKE '%DSQC%'
    ORDER BY p.code
    LIMIT 40
""")
for r in cur.fetchall():
    print(" ", r)

c.close()
