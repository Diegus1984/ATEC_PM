import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

codes = [
    "3HAC058949", "3HAC058950", "3HAC058951", "3HAC049837", "3HAC049875",
    "3HAC026787", "3HAC022957", "3HAC022723", "3HAC14940", "3HAC11440",
    "3HAC050878", "3HAC065021", "3HAC050792", "3HAC044161", "3HAC6499",
    "DSQC1052", "4414/2HHP", "9GA0924P4G03",
]
for code in codes:
    cur.execute(
        "SELECT id, code, name FROM quote_products WHERE code LIKE %s OR name LIKE %s LIMIT 3",
        (f"%{code}%", f"%{code}%"),
    )
    rows = cur.fetchall()
    print(f"{code}: {rows if rows else 'NONE'}")

cur.execute(
    "SELECT id, name FROM quote_categories WHERE name IN ('Schede','Azionamenti','Kit Cavi','Motori','Ventole')"
)
print("categories:", cur.fetchall())

c.close()
