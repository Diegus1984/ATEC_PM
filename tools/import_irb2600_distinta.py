#!/usr/bin/env python3
"""
Popola gamma_distinta manipolatore + cavi + motori per IRB 2600 / IRB 2600ID (IRC5).

Fonti ABB (ricerca online / PDF):
  - Product manual IRB 2600: 3HAC035504-001 rev.AH
  - Spare parts IRB 2600: 3HAC049106-001 rev.U (§8 Motors, §11 Electrical connections)
  - IRC5 cavi quadro: 3HAC047136-001 rev.AH §7.3
  - Circuit diagram: 3HAC029570-007

Schede/Azionamenti quadro: gia da apply_irc5_controller_profiles.py

Uso:
  python tools/import_irb2600_distinta.py --quadro 120
  python tools/import_irb2600_distinta.py --all --apply
"""
from __future__ import annotations

import argparse
import sys

import pymysql

from catalog_description import CATALOG_DESCRIPTIONS

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

QUADRO_IDS: dict[int, str] = {
    120: "IRB 2600-20/1.65",
    121: "IRB 2600-12/1.65",
    122: "IRB 2600-12/1.85",
    123: "IRB 2600ID-15/1.85",
}

QUADRO_ID_VARIANT = {123}  # IRB 2600ID — motori Type A assi 1-3, harness braccio superiore

SOURCE_NOTE = (
    "Import da manuali ABB 3HAC035504-001 + 3HAC049106-001 + 3HAC047136-001 (web)"
)

# Condiviso (sezione, slot, codice, qty, nome se da creare, categoria)
COMMON_BOM = [
    # Manipolatore — 3HAC049106-001 §11 Electrical connections
    ("Schede", "SMB unit", "3HAC044168-001", 1,
     "RMU101 serial measurement board — 3HAC044168-001", "Schede"),
    # Azionamenti — 3HAC047136-001 bleeder IRB 2600-7600
    ("Azionamenti", "Bleeder 2 kW", "3HAC032586-001", 1,
     "Bleeder 2 kW IRC5 — 3HAC032586-001", "Azionamenti"),
    # Cavi robot — 3HAC035504-001 §2.8.1 default 7 m
    ("Kit Cavi", "Cavo potenza", "3HAC026787-001", 1, None, "Kit Cavi"),
    ("Kit Cavi", "Cavo segnale", "3HAC068917-001", 1,
     "Robot cable signal 7 m — 3HAC068917-001", "Kit Cavi"),
    ("Kit Cavi", "Harness CP/CS", "3HAC022957-001", 1, None, "Kit Cavi"),
    ("Kit Cavi", "Cable harness manipolatore", "3HAC094846-001", 1,
     "Cable harness basic IRB 2600 — 3HAC094846-001", "Kit Cavi"),
    ("Ventole", "Azionamenti", "4414/2HHP", 1, None, "Ventole"),
    ("Ventole", "Cabinet interno", "9GA0924P4G03", 1, None, "Ventole"),
    ("Ventole", "RACK/CPU/PC", "9GA0924P4G03", 1, None, "Ventole"),
]

# IRB 2600 standard — motori Type B (3HAC049106-001 §8, Graphite White -002)
MOTORS_TYPE_B = [
    ("Motori", "Asse 1", "3HAC047586-002", "3HAC034644-003"),
    ("Motori", "Asse 2", "3HAC047584-002", "3HAC031218-003"),
    ("Motori", "Asse 3", "3HAC047575-002", "3HAC030215-003"),
    ("Motori", "Asse 4", "3HAC047574-002", "3HAC030216-003"),
    ("Motori", "Asse 5", "3HAC047574-002", "3HAC030216-003"),
    ("Motori", "Asse 6", "3HAC17342-1", None),
]

# IRB 2600ID — assi 1-3 Type A, 4-6 come Type B (3HAC049106-001 §8)
MOTORS_TYPE_A_ID = [
    ("Motori", "Asse 1", "3HAC034644-003", "3HAC047586-002"),
    ("Motori", "Asse 2", "3HAC031218-003", "3HAC047584-002"),
    ("Motori", "Asse 3", "3HAC030215-003", "3HAC047575-002"),
    ("Motori", "Asse 4", "3HAC047574-002", "3HAC030216-003"),
    ("Motori", "Asse 5", "3HAC047574-002", "3HAC030216-003"),
    ("Motori", "Asse 6", "3HAC17342-1", None),
]

SMB_ALT = ("Schede", "SMB unit", "3HAC046277-001", 1)
BRAKE_STANDARD = ("Schede", "Brake release unit", "3HAC065021-001", 1,
                  "Brake release DSQC1052 Type B — 3HAC065021-001", "Schede")
BRAKE_ID = ("Schede", "Brake release unit", "3HAC065020-001", 1,
            "Brake release DSQC1050 Type A — 3HAC065020-001", "Schede")
HARNESS_ID = ("Kit Cavi", "Cable harness braccio superiore ID", "3HAC094849-001", 1,
              "Cable harness upper arm IRB 2600ID — 3HAC094849-001", "Kit Cavi")


def connect():
    return pymysql.connect(**DB)


def load_categories(cur) -> dict[str, int]:
    cur.execute("SELECT id, name FROM quote_categories")
    return {name: cid for cid, name in cur.fetchall()}


def find_product(cur, code: str) -> tuple[int, str, str] | None:
    base = code.replace("-001", "").replace("-002", "").replace("-003", "").replace("-1", "")
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
    return (cur.lastrowid, code, display_name)


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


def fix_smb_misplacement(cur, quadro_id: int, apply: bool) -> None:
    """3HAC044168-001 e RMU101 sul manipolatore, non come Misura quadro (clone 6640)."""
    cur.execute(
        """
        SELECT d.id FROM gamma_distinta d
        JOIN quote_products p ON p.id = d.product_id
        WHERE d.quadro_id = %s AND d.sezione = 'Schede' AND d.slot = 'Misura'
          AND p.code = '3HAC044168-001'
        """,
        (quadro_id,),
    )
    rows = cur.fetchall()
    if rows:
        print(f"  FIX rimuove 3HAC044168-001 da slot Misura ({len(rows)} righe)")
        if apply:
            for (did,) in rows:
                cur.execute("DELETE FROM gamma_distinta WHERE id=%s", (did,))


def build_bom(quadro_id: int) -> tuple[list[tuple], list[tuple], list[tuple]]:
    bom: list[tuple] = list(COMMON_BOM)
    if quadro_id in QUADRO_ID_VARIANT:
        bom.append(BRAKE_ID)
        bom.append(HARNESS_ID)
        motors = MOTORS_TYPE_A_ID
    else:
        bom.append(BRAKE_STANDARD)
        motors = MOTORS_TYPE_B
    extras: list[tuple] = []
    for sezione, slot, primary, alt in motors:
        display = f"Motore {slot.lower()} IRB 2600 — {primary}"
        bom.append((sezione, slot, primary, 1, display, "Motori"))
    return bom, motors, extras


def import_quadro(cur, quadro_id: int, apply: bool) -> tuple[int, int]:
    if quadro_id not in QUADRO_IDS:
        print(f"ERRORE: quadro {quadro_id} non in QUADRO_IDS")
        return 0, 1

    label = QUADRO_IDS[quadro_id]
    variant = "2600ID" if quadro_id in QUADRO_ID_VARIANT else "2600"
    print(f"\n=== Quadro {quadro_id}: {label} ({variant}) ===")
    print(f"Modalita: {'APPLY' if apply else 'PREVIEW'}\n")

    fix_smb_misplacement(cur, quadro_id, apply)
    categories = load_categories(cur)
    bom, motors, _ = build_bom(quadro_id)
    to_insert: list[tuple] = []
    skipped = 0

    for sezione, slot, code, qty, create_name, cat_name in bom:
        display = create_name or code
        prod = ensure_product(cur, code, display, cat_name, categories, apply)
        pid = prod[0]
        if distinta_exists(cur, quadro_id, sezione, slot, code, 0):
            print(f"  = SKIP {sezione}/{slot} {code}")
            skipped += 1
            continue
        print(f"  + DISTINTA {sezione:12} | {slot:28} | {code:22} x{qty} | prod={pid}")
        to_insert.append((sezione, slot, code, qty, pid if pid > 0 else None, 0))

    for sezione, slot, primary, alt in motors:
        if alt and not distinta_exists(cur, quadro_id, sezione, slot, alt, 1):
            prod = ensure_product(cur, alt, f"Motore {slot.lower()} IRB 2600 ALT — {alt}",
                                  "Motori", categories, apply)
            pid = prod[0]
            print(f"  + DISTINTA ALT {sezione:12} | {slot:28} | {alt:22} x1 | prod={pid}")
            to_insert.append((sezione, slot, alt, 1, pid if pid > 0 else None, 1))
        elif alt:
            skipped += 1

    sezione, slot, alt_code, alt_qty = SMB_ALT
    if not distinta_exists(cur, quadro_id, sezione, slot, alt_code, 1):
        prod = ensure_product(cur, alt_code,
                              f"Serial measurement unit — {alt_code}", "Schede", categories, apply)
        pid = prod[0]
        print(f"  + DISTINTA ALT {sezione:12} | {slot:28} | {alt_code:22} x{alt_qty} | prod={pid}")
        to_insert.append((sezione, slot, alt_code, alt_qty, pid if pid > 0 else None, 1))
    else:
        skipped += 1

    print(f"Righe da inserire: {len(to_insert)}, skip: {skipped}")

    if apply and to_insert:
        for sezione, slot, code, qty, pid, is_alt in to_insert:
            note = SOURCE_NOTE
            if is_alt:
                note = f"Codice alternativo ABB — {note}"
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
        print(f"OK — distinta quadro {quadro_id}: {cur.fetchone()[0]} righe totali")

    return len(to_insert), 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--quadro", type=int, help="ID quadro gamma (es. 120)")
    parser.add_argument("--all", action="store_true", help="Tutti i quadri IRB 2600/2600ID")
    args = parser.parse_args()
    apply = args.apply

    if args.all:
        quadro_ids = sorted(QUADRO_IDS.keys())
    elif args.quadro:
        quadro_ids = [args.quadro]
    else:
        quadro_ids = [120]

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
