#!/usr/bin/env python3
"""
Popola gamma_distinta manipolatore + cavi + motori per IRB 6700 (IRC5).

Fonti ABB (ricerca online / PDF):
  - Product manual IRB 6700: 3HAC044266-001 rev.AB
  - Spare parts (motori identificati §7.1; ordine da 3HAC044268-001)
  - IRC5 spare parts cavi quadro: 3HAC047136-001 rev.AH §7.3
  - Circuit diagram: 3HAC043446-005

Schede/Azionamenti quadro IRC5: gia applicati da apply_irc5_controller_profiles.py
Questo script aggiunge solo componenti manipolatore verificati + corregge SMB 633C.

Uso:
  python tools/import_irb6700_distinta.py --quadro 133
  python tools/import_irb6700_distinta.py --all --apply
"""
from __future__ import annotations

import argparse
import sys

import pymysql

from catalog_description import CATALOG_DESCRIPTIONS, build_description

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

QUADRO_IDS: dict[int, str] = {
    131: "IRB 6700-300/2.70",
    132: "IRB 6700-245/3.00",
    133: "IRB 6700-235/2.65",
    134: "IRB 6700-205/2.80",
    135: "IRB 6700-200/2.60",
    136: "IRB 6700-175/3.05",
    137: "IRB 6700-155/2.85",
    138: "IRB 6700-150/3.20",
}

# Reach /2.60 e /2.85: motori Type B diversi (3HAC044266-001 §7.1)
SHORT_REACH_QUADROS = {135, 137}

SOURCE_NOTE = (
    "Import da manuali ABB 3HAC044266-001 + 3HAC047136-001 + 3HAC044268-001 (web)"
)

# (sezione, slot, codice, qty, nome catalogo se da creare, categoria)
COMMON_BOM = [
    # --- Manipolatore electrical (3HAC044266-001 §4.4) ---
    ("Schede", "SMB unit", "3HAC043904-001", 1,
     "DSQC633C SMB unit — 3HAC043904-001", "Schede"),
    ("Schede", "Brake release unit", "3HAC046642-001", 1,
     "Brake release unit IRB 6700 — 3HAC046642-001", "Schede"),
    ("Schede", "Batteria SMB manipolatore", "3HAC043118-001", 1,
     "Battery pack SMB IRB 6700 — 3HAC043118-001", "Schede"),
    # --- Azionamenti quadro (3HAC047136-001 bleeder 2600-7600) ---
    ("Azionamenti", "Bleeder 2 kW", "3HAC032586-001", 1,
     "Bleeder 2 kW IRC5 — 3HAC032586-001", "Azionamenti"),
    # --- Kit cavi (3HAC044266-001 §2.6.1 + 3HAC047136-001 §7.3, default 7 m) ---
    ("Kit Cavi", "Cavo potenza", "3HAC026787-001", 1, None, "Kit Cavi"),
    ("Kit Cavi", "Cavo segnale", "3HAC068917-001", 1,
     "Robot cable signal 7 m — 3HAC068917-001", "Kit Cavi"),
    ("Kit Cavi", "Harness CP/CS", "3HAC022957-001", 1, None, "Kit Cavi"),
    ("Kit Cavi", "Cable harness manipolatore", "3HAC058040-001", 1,
     "Cable harness axis 1-6 IRB 6700 — 3HAC058040-001", "Kit Cavi"),
    # --- Ventole IRC5 (3HAC047136-001, stesso set IRB 6640/7600) ---
    ("Ventole", "Azionamenti", "4414/2HHP", 1, None, "Ventole"),
    ("Ventole", "Cabinet interno", "9GA0924P4G03", 1, None, "Ventole"),
    ("Ventole", "RACK/CPU/PC", "9GA0924P4G03", 1, None, "Ventole"),
]

# Motori Type B (primario) + Type A (ALT) — 3HAC044266-001 §7.1 tabella reach standard
MOTORS_STANDARD = [
    ("Motori", "Asse 1", "3HAC055433-001", "3HAC045060-001"),
    ("Motori", "Asse 2", "3HAC055434-001", "3HAC045061-001"),
    ("Motori", "Asse 3", "3HAC055435-001", "3HAC045063-001"),
    ("Motori", "Asse 4", "3HAC055436-001", "3HAC045064-001"),
    ("Motori", "Asse 5", "3HAC055436-001", "3HAC045064-001"),
    ("Motori", "Asse 6", "3HAC055445-001", "3HAC045066-001"),
]

# Reach /2.60 e /2.85 — colonne separate in §7.1
MOTORS_SHORT_REACH = [
    ("Motori", "Asse 1", "3HAC055442-001", "3HAC051321-001"),
    ("Motori", "Asse 2", "3HAC055443-001", "3HAC051323-001"),
    ("Motori", "Asse 3", "3HAC055443-001", "3HAC051323-001"),
    ("Motori", "Asse 4", "3HAC055449-001", "3HAC045762-001"),
    ("Motori", "Asse 5", "3HAC055436-001", "3HAC045064-001"),
    ("Motori", "Asse 6", "3HAC055438-001", "3HAC045067-001"),
]

CABLE_HARNESS_ALT = ("Kit Cavi", "Cable harness manipolatore", "3HAC042840-001", 1)


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
    """Rimuove 3HAC043904-001 dallo slot Misura (clone 6640 errato) e lo mette su SMB unit."""
    cur.execute(
        """
        SELECT d.id FROM gamma_distinta d
        JOIN quote_products p ON p.id = d.product_id
        WHERE d.quadro_id = %s AND d.sezione = 'Schede' AND d.slot = 'Misura'
          AND p.code = '3HAC043904-001'
        """,
        (quadro_id,),
    )
    rows = cur.fetchall()
    if rows:
        print(f"  FIX rimuove 3HAC043904-001 da slot Misura ({len(rows)} righe)")
        if apply:
            for (did,) in rows:
                cur.execute("DELETE FROM gamma_distinta WHERE id=%s", (did,))


def build_bom(quadro_id: int) -> list[tuple]:
    motors = MOTORS_SHORT_REACH if quadro_id in SHORT_REACH_QUADROS else MOTORS_STANDARD
    bom: list[tuple] = list(COMMON_BOM)
    for sezione, slot, primary, alt in motors:
        display = f"Motore {slot.lower()} IRB 6700 Type B — {primary}"
        bom.append((sezione, slot, primary, 1, display, "Motori"))
    return bom, motors


def import_quadro(cur, quadro_id: int, apply: bool) -> tuple[int, int]:
    if quadro_id not in QUADRO_IDS:
        print(f"ERRORE: quadro {quadro_id} non in QUADRO_IDS")
        return 0, 1

    label = QUADRO_IDS[quadro_id]
    motor_profile = "short_reach" if quadro_id in SHORT_REACH_QUADROS else "standard"
    print(f"\n=== Quadro {quadro_id}: {label} (motori {motor_profile}) ===")
    print(f"Modalita: {'APPLY' if apply else 'PREVIEW'}\n")

    fix_smb_misplacement(cur, quadro_id, apply)

    categories = load_categories(cur)
    bom, motors = build_bom(quadro_id)
    to_insert: list[tuple] = []
    skipped = 0

    for sezione, slot, code, qty, create_name, cat_name in bom:
        display = create_name or code
        prod = ensure_product(cur, code, display, cat_name, categories, apply)
        pid, _, _ = prod

        if distinta_exists(cur, quadro_id, sezione, slot, code, 0):
            print(f"  = SKIP {sezione}/{slot} {code}")
            skipped += 1
            continue

        print(f"  + DISTINTA {sezione:12} | {slot:28} | {code:22} x{qty} | prod={pid}")
        to_insert.append((sezione, slot, code, qty, pid if pid > 0 else None, 0))

    # Motori Type A (ALT)
    for sezione, slot, primary, alt in motors:
        if distinta_exists(cur, quadro_id, sezione, slot, alt, 1):
            print(f"  = SKIP ALT {sezione}/{slot} {alt}")
            skipped += 1
            continue
        prod = ensure_product(cur, alt, f"Motore {slot.lower()} IRB 6700 Type A — {alt}",
                              "Motori", categories, apply)
        pid = prod[0]
        print(f"  + DISTINTA ALT {sezione:12} | {slot:28} | {alt:22} x1 | prod={pid}")
        to_insert.append((sezione, slot, alt, 1, pid if pid > 0 else None, 1))

    # Cable harness ALT (3HAC042840-001 sostituito da 3HAC058040-001)
    sezione, slot, alt_code, alt_qty = CABLE_HARNESS_ALT
    if not distinta_exists(cur, quadro_id, sezione, slot, alt_code, 1):
        prod = ensure_product(cur, alt_code,
                              f"Cable harness axis 1-6 IRB 6700 (legacy) — {alt_code}",
                              "Kit Cavi", categories, apply)
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
        total = cur.fetchone()[0]
        print(f"OK — distinta quadro {quadro_id}: {total} righe totali")

    return len(to_insert), 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--quadro", type=int, help="ID quadro gamma (es. 133)")
    parser.add_argument("--all", action="store_true", help="Tutti gli 8 quadri IRB 6700")
    args = parser.parse_args()
    apply = args.apply

    if args.all:
        quadro_ids = sorted(QUADRO_IDS.keys())
    elif args.quadro:
        quadro_ids = [args.quadro]
    else:
        quadro_ids = [133]

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
