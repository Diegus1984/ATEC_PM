#!/usr/bin/env python3
"""
Popola gamma_distinta per quadri IRB 8700 (IRC5).

Varianti:
  - 108  IRB 8700-800/3.50
  - 109  IRB 8700-550/4.20

Fonti online (ABB):
  - Manipolatore: Product manual spare parts 3HAC052854-001 (ManualsLib)
  - Quadro IRC5:   Product manual IRC5 spare parts 3HAC047136-001 rev.AH

Uso:
  python tools/import_irb8700_800_distinta.py --quadro 109 --apply
  python tools/import_irb8700_800_distinta.py --quadro 108 --apply
  python tools/import_irb8700_800_distinta.py --all --apply
"""
from __future__ import annotations

import argparse
import sys

import pymysql

from catalog_description import CATALOG_DESCRIPTIONS, build_description

QUADRO_IDS = {
    108: "IRB 8700-800/3.50",
    109: "IRB 8700-550/4.20",
}

# DSQC 668 — codice ALT (stesso slot Axis Computer del primario 3HAC029157-001)
DSQC668_ALT = ("Schede", "Axis Computer", "3HAC028179-001", 1, 253)

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

# (sezione, slot, codice ABB, qty, nome catalogo se da creare, category_name)
BOM = [
    # --- Schede quadro IRC5 (3HAC047136-001 §7.1, large robot) ---
    ("Schede", "Panel Board", "3HAC024488-001", 1, None, "Schede"),
    ("Schede", "Axis Computer", "3HAC029157-001", 1, None, "Schede"),
    ("Schede", "Alimentatore Computer", "3HAC026253-001", 1, None, "Schede"),
    ("Schede", "Power distribution unit", "3HAC026254-001", 1, None, "Schede"),
    ("Schede", "Customer I/O Power Supply", "3HAC14178-1", 1, None, "Schede"),
    ("Schede", "Misura", "3HAC031851-001", 1, None, "Schede"),
    ("Schede", "Batteria SMB", "3HAC044075-001", 1, None, "Schede"),
    # --- Manipolatore electrical (3HAC052854-001 §1) ---
    ("Schede", "RMU102", "3HAC043904-001", 1, None, "Schede"),
    ("Schede", "Brake release DSQC1052", "3HAC065021-001", 1,
     "DSQC1052 brake release — 3HAC065021-001", "Schede"),
    ("Schede", "Battery holder", "3HAC044161-001", 1,
     "Battery holder — 3HAC044161-001", "Schede"),
    ("Schede", "Push button guard", "3HAC6499-1", 1,
     "Push button guard — 3HAC6499-1", "Schede"),
    # --- Azionamenti IRC5 (8700: 1 MDU + 2 ADU + bleeder 4kW) ---
    ("Azionamenti", "Main Drive Unit", "3HAC029818-001", 1, None, "Azionamenti"),
    ("Azionamenti", "Additional Drive Unit", "3HAC030923-001", 2, None, "Azionamenti"),
    ("Azionamenti", "Bleeder 4 kW", "3HAC050878-001", 1,
     "Bleeder 4 kW IRB 8700 — 3HAC050878-001", "Azionamenti"),
    # --- Kit cavi (3HAC047136-001 §7.3, IRB 8700 richiede 2 cavi potenza) ---
    ("Kit Cavi", "Cavo potenza", "3HAC026787-001", 2, None, "Kit Cavi"),
    ("Kit Cavi", "Harness CP/CS", "3HAC022957-001", 1, None, "Kit Cavi"),
    ("Kit Cavi", "Cable harness manipolatore", "3HAC050792-001", 1,
     "Cable harness — 3HAC050792-001", "Kit Cavi"),
    # --- Motori (3HAC052854-001 §8) ---
    ("Motori", "Asse 1", "3HAC058949-003", 1,
     "Motore asse 1 IRB 8700 — 3HAC058949-003", "Motori"),
    ("Motori", "Asse 2", "3HAC058949-003", 1,
     "Motore asse 2 IRB 8700 — 3HAC058949-003", "Motori"),
    ("Motori", "Asse 3", "3HAC058949-003", 1,
     "Motore asse 3 IRB 8700 — 3HAC058949-003", "Motori"),
    ("Motori", "Asse 4", "3HAC058950-003", 1,
     "Motore asse 4 IRB 8700 — 3HAC058950-003", "Motori"),
    ("Motori", "Asse 5", "3HAC058949-003", 1,
     "Motore asse 5 IRB 8700 — 3HAC058949-003", "Motori"),
    ("Motori", "Asse 6", "3HAC058951-003", 1,
     "Motore asse 6 IRB 8700 — 3HAC058951-003", "Motori"),
    # --- Ventole (stesso set IRB 7600 quadro IRC5) ---
    ("Ventole", "Azionamenti", "4414/2HHP", 1, None, "Ventole"),
    ("Ventole", "Cabinet interno", "9GA0924P4G03", 1, None, "Ventole"),
    ("Ventole", "RACK/CPU/PC", "9GA0924P4G03", 1, None, "Ventole"),
]

SOURCE_NOTE = "Import da manuali ABB 3HAC052854-001 + 3HAC047136-001 (web)"


def connect():
    return pymysql.connect(**DB)


def load_categories(cur) -> dict[str, int]:
    cur.execute("SELECT id, name FROM quote_categories")
    return {name: cid for cid, name in cur.fetchall()}


def find_product(cur, code: str) -> tuple[int, str, str] | None:
    base = code.replace("-001", "").replace("-003", "").replace("-1", "")
    for pat in (code, base, code.replace("-001", "-1")):
        cur.execute(
            """
            SELECT id, code, name FROM quote_products
            WHERE code = %s OR code LIKE %s OR name LIKE %s
            ORDER BY CASE WHEN code = %s THEN 0 ELSE 1 END
            LIMIT 1
            """,
            (pat, f"{pat}%", f"%{pat}%", pat),
        )
        row = cur.fetchone()
        if row:
            return row
    return None


def ensure_product(cur, code: str, display_name: str, cat_name: str,
                   categories: dict[str, int], apply: bool) -> tuple[int, str, str]:
    found = find_product(cur, code)
    if found:
        return found

    cat_id = categories.get(cat_name)
    if cat_id is None:
        raise RuntimeError(f"Categoria mancante: {cat_name}")

    print(f"  + CREA prodotto catalogo: {display_name} [{code}]")
    if not apply:
        return (-1, code, display_name)

    description = CATALOG_DESCRIPTIONS.get(code)
    cur.execute(
        """
        INSERT INTO quote_products (category_id, item_type, code, name, description_rtf, is_active)
        VALUES (%s, 'product', %s, %s, %s, 1)
        """,
        (cat_id, code, display_name, description),
    )
    pid = cur.lastrowid
    return (pid, code, display_name)


def distinta_exists(cur, quadro_id: int, sezione: str, slot: str, code_raw: str,
                    is_alternate: int = 0) -> bool:
    cur.execute(
        """
        SELECT id FROM gamma_distinta
        WHERE quadro_id = %s AND sezione = %s AND slot = %s
          AND is_alternate = %s
          AND (code_raw = %s OR code_raw LIKE %s OR product_id IN (
              SELECT id FROM quote_products WHERE code = %s OR code LIKE %s
          ))
        LIMIT 1
        """,
        (quadro_id, sezione, slot, is_alternate, code_raw, f"{code_raw}%",
         code_raw, f"{code_raw}%"),
    )
    return cur.fetchone() is not None


def import_quadro(cur, quadro_id: int, apply: bool) -> tuple[int, int]:
    cur.execute("SELECT system_key FROM gamma_quadro WHERE id=%s", (quadro_id,))
    row = cur.fetchone()
    if not row:
        print(f"ERRORE: quadro {quadro_id} non trovato")
        return 0, 1

    label = QUADRO_IDS.get(quadro_id, row[0])
    print(f"\n=== Quadro {quadro_id}: {label} ===")
    print(f"Modalita: {'APPLY' if apply else 'PREVIEW'}\n")

    categories = load_categories(cur)
    to_insert: list[tuple] = []
    skipped = 0

    for sezione, slot, code, qty, create_name, cat_name in BOM:
        display = create_name or code
        prod = ensure_product(cur, code, display, cat_name, categories, apply)
        pid, _, _ = prod

        if distinta_exists(cur, quadro_id, sezione, slot, code, 0):
            print(f"  = SKIP {sezione}/{slot} {code}")
            skipped += 1
            continue

        print(f"  + DISTINTA {sezione:12} | {slot:28} | {code:22} x{qty} | prod={pid}")
        to_insert.append((sezione, slot, code, qty, pid if pid > 0 else None, 0))

    # DSQC 668 codice alternativo
    sezione, slot, alt_code, alt_qty, alt_pid = DSQC668_ALT
    if not distinta_exists(cur, quadro_id, sezione, slot, alt_code, 1):
        print(f"  + DISTINTA ALT {sezione:12} | {slot:28} | {alt_code:22} x{alt_qty} | prod={alt_pid}")
        to_insert.append((sezione, slot, alt_code, alt_qty, alt_pid, 1))
    else:
        print(f"  = SKIP ALT {sezione}/{slot} {alt_code}")
        skipped += 1

    print(f"Righe da inserire: {len(to_insert)}, skip: {skipped}")

    if apply and to_insert:
        for sezione, slot, code, qty, pid, is_alt in to_insert:
            note = SOURCE_NOTE
            if is_alt:
                note = "Codice alternativo DSQC 668 (3HAC028179-001 vs 3HAC029157-001)"
            cur.execute(
                """
                INSERT INTO gamma_distinta
                    (quadro_id, product_id, sezione, slot, code_raw, qty,
                     is_alternate, is_optional, note)
                VALUES (%s, %s, %s, %s, %s, %s, %s, 0, %s)
                """,
                (quadro_id, pid, sezione, slot, code, qty, is_alt, note),
            )
        cur.execute("SELECT COUNT(*) FROM gamma_distinta WHERE quadro_id=%s", (quadro_id,))
        total = cur.fetchone()[0]
        print(f"OK — distinta quadro {quadro_id}: {total} righe totali")

    return len(to_insert), 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="Esegue INSERT su DB")
    parser.add_argument("--quadro", type=int, choices=[108, 109], help="ID quadro gamma")
    parser.add_argument("--all", action="store_true", help="Importa 108 e 109")
    args = parser.parse_args()
    apply = args.apply

    if args.all:
        quadro_ids = [108, 109]
    elif args.quadro:
        quadro_ids = [args.quadro]
    else:
        quadro_ids = [109]

    conn = connect()
    cur = conn.cursor()
    errors = 0
    for qid in quadro_ids:
        _, err = import_quadro(cur, qid, apply)
        errors += err
    if apply:
        conn.commit()
    elif not apply:
        print("\nEsegui con --apply per scrivere su DB.")
    conn.close()
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
