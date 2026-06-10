import pymysql

# Codici da manuali ABB online:
# - Manipolatore 3HAC052854-001 sez.1 Electrical parts
# - Quadro IRC5 3HAC047136-001 sez.7.1 Controller parts (IRB 8700 = large robot, 2x ADU, bleeder 4kW)

CODES = [
    # Schede quadro IRC5 (standard large robot)
    ("Schede", "Panel Board", "3HAC024488-001", 1),
    ("Schede", "Axis Computer", "3HAC029157-001", 1),
    ("Schede", "Alimentatore Computer", "3HAC026253-001", 1),
    ("Schede", "Power distribution unit", "3HAC026254-001", 1),
    ("Schede", "Customer I/O Power Supply", "3HAC14178-1", 1),
    ("Schede", "Misura", "3HAC031851-001", 1),
    ("Schede", "Batteria SMB", "3HAC044075-001", 1),  # 8700 manual vs 16831 on 7600
    # Manipolatore electrical (3HAC052854-001)
    ("Schede", "RMU102", "3HAC043904-001", 1),
    ("Schede", "Brake release DSQC1052", "3HAC065021-001", 1),
    ("Schede", "Cable harness", "3HAC050792-001", 1),
    ("Schede", "Battery holder", "3HAC044161-001", 1),
    ("Schede", "Push button guard", "3HAC6499-1", 1),
    # Azionamenti
    ("Azionamenti", "Main Drive Unit", "3HAC029818-001", 1),
    ("Azionamenti", "Additional Drive Unit", "3HAC030923-001", 2),  # IRB 8700: 2 ADU
    ("Azionamenti", "Bleeder 4 kW", "3HAC050878-001", 1),
    # Kit cavi (IRC5 manual - IRB 8700 listed with 7600/6700 group)
    ("Kit Cavi", "Cavo alimentazione", "3HAC023519-001", 2),  # 8700 needs 2 power cables
    ("Kit Cavi", "Cavo segnale", "3HAC023600-001", 1),
]

c = pymysql.connect(host="localhost", user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

def find_product(code: str):
    base = code.replace("-001", "").replace("-1", "")
    patterns = [code, base, code.replace("-001", "-1")]
    for pat in patterns:
        cur.execute("""
            SELECT id, code, name FROM quote_products
            WHERE code = %s OR code LIKE %s OR name LIKE %s
            ORDER BY CASE WHEN code = %s THEN 0 WHEN code LIKE %s THEN 1 ELSE 2 END
            LIMIT 3
        """, (pat, f"%{pat}%", f"%{pat}%", pat, f"{pat}%"))
        rows = cur.fetchall()
        if rows:
            return rows[0]
    # DSQC number fallback
    if "DSQC" in code.upper() or "3HAC" in code:
        pass
    return None

print("=== Match catalogo ===")
matched = []
missing = []
for sezione, slot, code, qty in CODES:
    prod = find_product(code)
    if prod:
        matched.append((sezione, slot, code, qty, prod[0], prod[1], prod[2]))
        print(f"OK  {code:22} -> id={prod[0]:4} {prod[1]} | {prod[2][:60]}")
    else:
        missing.append((sezione, slot, code, qty))
        print(f"MISS {code:22} ({sezione}/{slot})")

print(f"\nMatched: {len(matched)}, Missing: {len(missing)}")

# Also search motors from manual page 31
motor_codes = [
    "3HAC048180-001",  # from web search snippet axis-1 related
]
print("\n=== Search extra codes ===")
for code in motor_codes + [m[2] for m in missing]:
    cur.execute("""
        SELECT id, code, name FROM quote_products
        WHERE code LIKE %s OR name LIKE %s LIMIT 2
    """, (f"%{code.replace('-001','')}%", f"%{code.replace('-001','')}%"))
    rows = cur.fetchall()
    print(f"  {code}: {rows}")

c.close()
