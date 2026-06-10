import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

QUADRO_REF = 105  # IRB 7600
QUADRO_TARGET = 108  # IRB 8700-800/3.50

cur.execute("""
    SELECT d.sezione, d.slot, d.code_raw, p.id, p.code, p.name, d.qty, d.is_alternate, d.note
    FROM gamma_distinta d
    LEFT JOIN quote_products p ON p.id = d.product_id
    WHERE d.quadro_id = %s AND d.is_alternate = 0
    ORDER BY FIELD(d.sezione, 'Schede', 'Azionamenti', 'Kit Cavi', 'Motori', 'Ventole'), d.slot
""", (QUADRO_REF,))
ref = cur.fetchall()
print(f"=== Distinta reference quadro {QUADRO_REF} ({len(ref)} rows) ===")
for r in ref:
    print(r)

c.close()
