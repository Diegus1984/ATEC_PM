import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005", database="atec_pm", charset="utf8mb4")
cur = c.cursor()

cur.execute("""
    SELECT g.id, g.name, pl.name, g.sort_order
    FROM quote_groups g
    JOIN quote_price_lists pl ON pl.id = g.price_list_id
    WHERE pl.name = 'Gamma Ricambi'
""")
print("Gruppi Gamma Ricambi:", cur.fetchall())

cur.execute("SELECT id, modello FROM gamma_robot WHERE is_active=1 ORDER BY modello")
robots = [r[1] for r in cur.fetchall()]
print(f"\nModelli gamma_robot ({len(robots)}):")
for m in robots:
    print(" ", m)

# prodotti AT con descrizione tabella per famiglia IRB
cur.execute("""
    SELECT DISTINCT SUBSTRING_INDEX(p.name, ' -', 1) AS famiglia,
           MAX(CASE WHEN p.description_rtf LIKE '%<table%' OR p.description_rtf LIKE '%<figure class=\"table\"%' THEN 1 ELSE 0 END) AS has_fmt
    FROM quote_products p
    JOIN quote_categories c ON c.id = p.category_id
    JOIN quote_groups g ON g.id = c.group_id
    WHERE g.price_list_id = 1 AND p.name LIKE 'IRB %'
    GROUP BY famiglia
    ORDER BY famiglia
""")
print("\nFamiglie IRB in Automation Technology:")
for r in cur.fetchall():
    print(r)

c.close()
