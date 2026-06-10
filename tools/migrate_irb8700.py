"""Sostituisce IRB 8700 nel DB con le 2 configurazioni ufficiali ABB (800/3.50 e 550/4.20)."""
import pymysql

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

QUADRI = [
    {
        "modello_suffix": "800/3.50",
        "payload": "800",
        "area_lavoro": "3.5",
        "note": "ABB IRB 8700-800/3.50 — reach 3.5 m, payload 800 kg (1000 kg polso giù)",
    },
    {
        "modello_suffix": "550/4.20",
        "payload": "550",
        "area_lavoro": "4.2",
        "note": "ABB IRB 8700-550/4.20 — reach 4.2 m, payload 550 kg",
    },
]


def main():
    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    cur.execute("""
        SELECT id FROM gamma_robot
        WHERE is_active = 1 AND modello LIKE '%8700%'
    """)
    old_ids = [r[0] for r in cur.fetchall()]
    if not old_ids:
        print("Nessun IRB 8700 esistente — procedo con insert.")
    else:
        for rid in old_ids:
            cur.execute("SELECT COUNT(*) FROM gamma_quadro WHERE robot_id=%s", (rid,))
            nq = cur.fetchone()[0]
            cur.execute("SELECT COUNT(*) FROM gamma_distinta d JOIN gamma_quadro q ON q.id=d.quadro_id WHERE q.robot_id=%s", (rid,))
            nd = cur.fetchone()[0]
            print(f"Elimino robot id={rid} ({nq} quadri, {nd} righe distinta) — CASCADE")
            cur.execute("DELETE FROM gamma_robot WHERE id=%s", (rid,))

    cur.execute("""
        INSERT INTO gamma_robot (modello, serie, brand, note, is_active)
        VALUES ('IRB 8700', NULL, 'ABB',
                'Serie IRB 8700 — 8ª gen. ABB, heavy payload (fonte: datasheet ABB 2024)', 1)
    """)
    robot_id = cur.lastrowid
    print(f"Inserito robot id={robot_id} modello=IRB 8700")

    for q in QUADRI:
        cur.execute("""
            INSERT INTO gamma_quadro
                (robot_id, controllore, generazione, payload, area_lavoro, os_version, system_key, note, is_active)
            VALUES (%s, 'IRC5', 'IRC5', %s, %s, NULL, %s, %s, 1)
        """, (
            robot_id,
            q["payload"],
            q["area_lavoro"],
            f"IRB 8700-{q['modello_suffix']}",
            q["note"],
        ))
        print(f"  + quadro id={cur.lastrowid}: IRB 8700-{q['modello_suffix']} | {q['payload']} kg | {q['area_lavoro']} m")

    conn.commit()

    cur.execute("""
        SELECT r.id, r.modello, q.id, q.system_key, q.payload, q.area_lavoro, q.note
        FROM gamma_robot r
        JOIN gamma_quadro q ON q.robot_id = r.id
        WHERE r.modello = 'IRB 8700'
        ORDER BY q.payload DESC
    """)
    print("\nVerifica:")
    for row in cur.fetchall():
        print(f"  robot {row[0]} | quadro {row[2]} | {row[3]} | {row[4]} kg | {row[5]} m")

    conn.close()
    print("\nFatto.")


if __name__ == "__main__":
    main()
