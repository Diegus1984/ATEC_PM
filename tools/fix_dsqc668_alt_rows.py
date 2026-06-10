#!/usr/bin/env python3
"""
DSQC 668 ha due codici catalogo (3HAC029157-001 primario, 3HAC028179-001 ALT).
Aggiunge righe distinta ALT mancanti dove esiste già il primario.

Uso: python tools/fix_dsqc668_alt_rows.py --apply
"""
import argparse
import sys

import pymysql

from catalog_description import CATALOG_DESCRIPTIONS

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

PRIMARY_ID = 351
PRIMARY_CODE = "3HAC029157-001"
ALT_ID = 253
ALT_CODE = "3HAC028179-001"
SEZIONE = "Schede"
SLOT = "Axis Computer"
NOTE = "Codice alternativo DSQC 668 (3HAC028179-001 vs 3HAC029157-001)"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    # Aggiorna descrizione prodotto ALT se legacy
    html = CATALOG_DESCRIPTIONS.get(ALT_CODE)
    if html and args.apply:
        cur.execute(
            "UPDATE quote_products SET description_rtf=%s WHERE id=%s",
            (html, ALT_ID),
        )
        print(f"Descrizione aggiornata prodotto {ALT_ID} ({ALT_CODE})")

    cur.execute("""
        SELECT d.quadro_id, q.system_key, r.modello
        FROM gamma_distinta d
        JOIN gamma_quadro q ON q.id = d.quadro_id
        JOIN gamma_robot r ON r.id = q.robot_id
        WHERE d.product_id = %s AND d.is_alternate = 0
          AND d.sezione = %s AND d.slot = %s
    """, (PRIMARY_ID, SEZIONE, SLOT))
    primary_rows = cur.fetchall()

    to_add = []
    for quadro_id, system_key, modello in primary_rows:
        cur.execute("""
            SELECT id FROM gamma_distinta
            WHERE quadro_id = %s AND sezione = %s AND slot = %s
              AND product_id = %s AND is_alternate = 1
            LIMIT 1
        """, (quadro_id, SEZIONE, SLOT, ALT_ID))
        if cur.fetchone() is None:
            to_add.append((quadro_id, system_key, modello))

    print(f"Quadri con primario {PRIMARY_CODE}: {len(primary_rows)}")
    print(f"Righe ALT {ALT_CODE} da aggiungere: {len(to_add)}")
    for quadro_id, system_key, modello in to_add:
        print(f"  + quadro {quadro_id} | {modello} | {system_key}")

    if args.apply and to_add:
        for quadro_id, _, _ in to_add:
            cur.execute("""
                INSERT INTO gamma_distinta
                    (quadro_id, product_id, sezione, slot, code_raw, qty,
                     is_alternate, is_optional, note)
                VALUES (%s, %s, %s, %s, %s, 1, 1, 0, %s)
            """, (quadro_id, ALT_ID, SEZIONE, SLOT, ALT_CODE, NOTE))
        conn.commit()
        print("OK — righe ALT inserite.")

    if not args.apply:
        print("Preview. Usa --apply per scrivere.")

    conn.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
