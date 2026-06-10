import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()
cur.execute("""
    SELECT p.id, p.code, p.name, cat.name,
           CASE WHEN p.description_rtf IS NULL OR p.description_rtf = ''
                OR p.description_rtf NOT LIKE '%width: 50%%' THEN 1 ELSE 0 END AS missing
    FROM quote_products p
    JOIN quote_categories cat ON cat.id = p.category_id
    WHERE p.id >= 632
    ORDER BY p.id
""")
for r in cur.fetchall():
    print(r)

for cat in ['Azionamenti', 'Kit Cavi']:
    cur.execute("""
        SELECT LEFT(p.description_rtf, 500) FROM quote_products p
        JOIN quote_categories c ON c.id = p.category_id
        WHERE c.name = %s AND p.description_rtf LIKE '%%width: 50%%'
        LIMIT 1
    """, (cat,))
    r = cur.fetchone()
    print("===", cat, "===")
    print(r[0] if r else "none")
    print()

c.close()
