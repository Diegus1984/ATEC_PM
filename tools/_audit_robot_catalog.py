import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005", database="atec_pm", charset="utf8mb4")
cur = c.cursor()

cur.execute("SELECT id, name FROM quote_price_lists")
print("\n=== Price lists ===")
for r in cur.fetchall():
    print(r)

cur.execute("""
    SELECT pl.name, c.id, c.name, g.name
    FROM quote_categories c
    JOIN quote_groups g ON g.id = c.group_id
    JOIN quote_price_lists pl ON pl.id = g.price_list_id
    WHERE c.name LIKE '%Robot%' OR c.name LIKE '%Manipol%' OR g.name LIKE '%Robot%'
    ORDER BY pl.name, c.name
""")
print("\n=== Categorie/gruppi Robot/Manipol ===")
for r in cur.fetchall():
    print(r)

cur.execute("""
    SELECT pl.name, COUNT(*)
    FROM quote_products p
    JOIN quote_categories c ON c.id = p.category_id
    JOIN quote_groups g ON g.id = c.group_id
    JOIN quote_price_lists pl ON pl.id = g.price_list_id
    WHERE p.name LIKE 'IRB %' OR p.code LIKE 'IRB%'
    GROUP BY pl.name
""")
print("\n=== Prodotti IRB per listino ===")
for r in cur.fetchall():
    print(r)

cur.execute("""
    SELECT p.id, p.code, p.name, pl.name, c.name, LENGTH(p.description_rtf)
    FROM quote_products p
    JOIN quote_categories c ON c.id = p.category_id
    JOIN quote_groups g ON g.id = c.group_id
    JOIN quote_price_lists pl ON pl.id = g.price_list_id
    WHERE p.name LIKE 'IRB %'
    ORDER BY p.name LIMIT 30
""")
print("\n=== Sample prodotti IRB ===")
for r in cur.fetchall():
    print(r)


cur.execute("""
    SELECT c.name, COUNT(*),
           SUM(CASE WHEN p.description_rtf IS NOT NULL AND p.description_rtf LIKE '%<table%' THEN 1 ELSE 0 END)
    FROM quote_products p
    JOIN quote_categories c ON c.id = p.category_id
    JOIN quote_groups g ON g.id = c.group_id
    WHERE g.price_list_id = 4
    GROUP BY c.name
    ORDER BY c.name
""")
print("\n=== Prodotti per categoria (con desc tabella) ===")
for r in cur.fetchall():
    print(r)

cur.execute("SELECT id, modello FROM gamma_robot WHERE is_active=1 ORDER BY modello")
robots = cur.fetchall()
print(f"\n=== gamma_robot: {len(robots)} modelli ===")

# Match robot modello -> quote_products
cur.execute("""
    SELECT p.id, p.code, p.name, c.name, LENGTH(p.description_rtf)
    FROM quote_products p
    JOIN quote_categories c ON c.id = p.category_id
    JOIN quote_groups g ON g.id = c.group_id
    WHERE g.price_list_id = 4 AND c.name = 'Robot'
    ORDER BY p.name
""")
robot_products = cur.fetchall()
print(f"\n=== Categoria Robot: {len(robot_products)} prodotti ===")
for r in robot_products[:15]:
    print(r)
if len(robot_products) > 15:
    print("...")

# sample description
if robot_products:
    pid = robot_products[0][0]
    cur.execute("SELECT description_rtf FROM quote_products WHERE id=%s", (pid,))
    desc = cur.fetchone()[0]
    print("\n=== Sample desc (first 800 chars) ===")
    print((desc or "")[:800])

# unmatched gamma_robot
cur.execute("""
    SELECT p.name FROM quote_products p
    JOIN quote_categories c ON c.id = p.category_id
    JOIN quote_groups g ON g.id = c.group_id
    WHERE g.price_list_id = 4 AND c.name = 'Robot'
""")
prod_names = {r[0].upper().strip() for r in cur.fetchall()}
missing = []
for rid, modello in robots:
    if modello.upper() not in prod_names and not any(modello.upper() in n for n in prod_names):
        missing.append(modello)
print(f"\n=== gamma_robot senza prodotto Robot (euristica): {len(missing)} ===")
for m in missing[:20]:
    print(" ", m)

c.close()
