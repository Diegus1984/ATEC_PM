import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005", database="atec_pm", charset="utf8mb4")
cur = c.cursor()
cur.execute("""
    SELECT p.id, p.name, LENGTH(p.description_rtf)
    FROM quote_products p
    JOIN quote_categories c2 ON c2.id = p.category_id
    JOIN quote_groups g ON g.id = c2.group_id
    WHERE g.price_list_id = 4 AND c2.name = 'Robot'
    ORDER BY p.name
""")
rows = cur.fetchall()
baseline = "Descrizione sintetica da completare"
pending = []
for pid, name, ln in rows:
    cur.execute("SELECT description_rtf FROM quote_products WHERE id=%s", (pid,))
    desc = cur.fetchone()[0] or ""
    if baseline in desc:
        pending.append(name)
print("Total:", len(rows))
print("Pending baseline:", len(pending))
for n in pending:
    print(n)
c.close()
