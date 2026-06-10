import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

cur.execute("""
    SELECT p.id, p.code, p.name, p.description_rtf
    FROM quote_products p
    JOIN quote_categories cat ON cat.id = p.category_id
    WHERE cat.name = 'Schede' AND p.description_rtf LIKE '%img%'
    LIMIT 3
""")
for r in cur.fetchall():
    print("===", r[0], r[1])
    print(r[3][:2000] if r[3] else None)
    print()

for code in ["3HAC026253", "3HAC024488", "3HAC029157"]:
    cur.execute(
        "SELECT id, code, LEFT(description_rtf,1500) FROM quote_products WHERE code LIKE %s",
        (f"%{code}%",),
    )
    for r in cur.fetchall():
        print(f"--- {code} id={r[0]} ---")
        print(r[2])
        print()

c.close()
