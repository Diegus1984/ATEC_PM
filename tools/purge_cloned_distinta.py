#!/usr/bin/env python3
"""
Elimina distinta clonata (template) per robot aggiunti in bulk (gamma_robot.id >= 26).

Mantiene robot e quadri; rimuove solo righe gamma_distinta con note template
o tutte le righe distinta dei quadri indicati.

Uso:
  python tools/purge_cloned_distinta.py              # anteprima
  python tools/purge_cloned_distinta.py --apply
  python tools/purge_cloned_distinta.py --apply --all-new   # tutti i quadri robot>=26
"""
from __future__ import annotations

import argparse
import sys

import pymysql

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

TEMPLATE_NOTE = "Distinta template"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument(
        "--all-new",
        action="store_true",
        help="Elimina tutta la distinta dei quadri con robot_id>=26 (default: solo note template)",
    )
    parser.add_argument("--min-robot-id", type=int, default=26)
    args = parser.parse_args()

    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    if args.all_new:
        cur.execute(
            """
            SELECT d.id, q.system_key, r.modello, d.sezione, d.slot
            FROM gamma_distinta d
            JOIN gamma_quadro q ON q.id = d.quadro_id
            JOIN gamma_robot r ON r.id = q.robot_id
            WHERE r.id >= %s
            ORDER BY r.modello, q.system_key, d.id
            """,
            (args.min_robot_id,),
        )
    else:
        cur.execute(
            """
            SELECT d.id, q.system_key, r.modello, d.sezione, d.slot
            FROM gamma_distinta d
            JOIN gamma_quadro q ON q.id = d.quadro_id
            JOIN gamma_robot r ON r.id = q.robot_id
            WHERE r.id >= %s AND d.note LIKE %s
            ORDER BY r.modello, q.system_key, d.id
            """,
            (args.min_robot_id, f"%{TEMPLATE_NOTE}%"),
        )

    rows = cur.fetchall()
    print(f"Righe da eliminare: {len(rows)}")
    by_robot: dict[str, int] = {}
    for _did, _sk, modello, _sez, _slot in rows:
        by_robot[modello] = by_robot.get(modello, 0) + 1
    for modello, count in sorted(by_robot.items()):
        print(f"  {modello}: {count}")

    if not args.apply:
        print("Preview — usa --apply per eliminare.")
        conn.close()
        return 0

    if args.all_new:
        cur.execute(
            """
            DELETE d FROM gamma_distinta d
            JOIN gamma_quadro q ON q.id = d.quadro_id
            JOIN gamma_robot r ON r.id = q.robot_id
            WHERE r.id >= %s
            """,
            (args.min_robot_id,),
        )
    else:
        cur.execute(
            """
            DELETE d FROM gamma_distinta d
            JOIN gamma_quadro q ON q.id = d.quadro_id
            JOIN gamma_robot r ON r.id = q.robot_id
            WHERE r.id >= %s AND d.note LIKE %s
            """,
            (args.min_robot_id, f"%{TEMPLATE_NOTE}%"),
        )

    deleted = cur.rowcount
    conn.commit()
    print(f"Eliminate {deleted} righe distinta.")
    conn.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
