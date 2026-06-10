#!/usr/bin/env python3
"""Applica description_rtf template 1x2 ai prodotti importati IRB 8700."""
import argparse
import sys

import pymysql

from catalog_description import CATALOG_DESCRIPTIONS

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    for code, html in CATALOG_DESCRIPTIONS.items():
        cur.execute(
            "SELECT id, name, description_rtf FROM quote_products WHERE code = %s",
            (code,),
        )
        row = cur.fetchone()
        if not row:
            print(f"SKIP {code}: prodotto non trovato")
            continue
        product_id, name, existing = row
        if existing and "<table" in existing and "width: 50%" in existing:
            print(f"SKIP id={product_id} {code}: descrizione gia presente")
            continue
        print(f"UPDATE id={product_id} {code} | {name}")
        if args.apply:
            cur.execute(
                "UPDATE quote_products SET description_rtf=%s WHERE id=%s",
                (html, product_id),
            )

    if args.apply:
        conn.commit()
        print("OK — descrizioni aggiornate.")
    else:
        print("Preview only. Usa --apply per scrivere.")

    conn.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
