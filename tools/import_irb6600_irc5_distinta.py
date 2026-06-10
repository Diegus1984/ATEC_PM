#!/usr/bin/env python3
"""
Crea quadri IRC5 per IRB 6600 e popola gamma_distinta manipolatore.

Fonti ABB:
  - Product manual IRB 6600 Type B: 3HAC023082-001 rev.E (§9.2.2 spare parts)
  - IRC5 cavi manipolatore: 3HAC047136-001 rev.V §7.3.1–7.3.3
  - Schede/Azionamenti quadro IRC5: apply_irc5_controller_profiles (irc5_medium_large)

Varianti (stesso manipolatore, payload/reach diversi):
  175/2.55, 225/2.55, 175/2.8

Uso:
  python tools/import_irb6600_irc5_distinta.py --setup --apply   # quadri + profilo IRC5
  python tools/import_irb6600_irc5_distinta.py --all --apply     # distinta manipolatore
"""
from __future__ import annotations

import argparse
import sys

import pymysql

from catalog_description import CATALOG_DESCRIPTIONS
from apply_irc5_controller_profiles import apply_profile_to_quadro

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

ROBOT_MODELLO = "IRB 6600"
IRC5_PROFILE = "irc5_medium_large"

VARIANTS: list[tuple[str, str, str]] = [
    ("175/2.55", "175", "2.55"),
    ("225/2.55", "225", "2.55"),
    ("175/2.8", "175", "2.8"),
]

SOURCE_NOTE = (
    "Import da manuali ABB 3HAC023082-001 + 3HAC047136-001 (web)"
)

COMMON_BOM = [
    ("Schede", "Misura", "3HAC16014-001", 1,
     "SMB unit IRB 6600 — 3HAC16014-001", "Schede"),
    ("Schede", "Batteria SMB", "3HAC16831-001", 1,
     "Battery pack manipolatore — 3HAC16831-001", "Schede"),
    ("Kit Cavi", "Cavo potenza", "3HAC026787-001", 1,
     "Control cable power 7 m — 3HAC047136-001 §7.3.1", "Kit Cavi"),
    ("Kit Cavi", "Cavo segnale", "3HAC7998-001", 1,
     "Control cable signal 7 m — 3HAC047136-001 §7.3.1", "Kit Cavi"),
    ("Kit Cavi", "Harness CP/CS", "3HAC022957-001", 1, None, "Kit Cavi"),
    ("Kit Cavi", "Cable harness manipolatore", "3HAC024385-001", 1,
     "Cable harness axes 1-6 IRB 6600 — 3HAC023082-001 §9.2.2", "Kit Cavi"),
    ("Ventole", "Azionamenti", "4414/2HHP", 1, None, "Ventole"),
    ("Ventole", "Cabinet interno", "9GA0924P4G03", 1, None, "Ventole"),
    ("Ventole", "RACK/CPU/PC", "9GA0924P4G03", 1, None, "Ventole"),
]

MOTORS = [
    ("Motori", "Asse 1", "3HAC15879-2", "3HAC15879-3"),
    ("Motori", "Asse 2", "3HAC21030-1", "3HAC026975-001"),
    ("Motori", "Asse 3", "3HAC15885-2", "3HAC026976-001"),
    ("Motori", "Asse 4", "3HAC15889-2", "3HAC026977-001"),
    ("Motori", "Asse 5", "3HAC17484-10", "3HAC026982-001"),
    ("Motori", "Asse 6", "3HAC15991-4", "3HAC026983-001"),
]

BATTERY_ALT = ("Schede", "Batteria SMB", "3HAC044075-001", 1)
HARNESS_SPLIT_14 = ("Kit Cavi", "Cable harness assi 1-4", "3HAC025503-001", 1,
                    "Cable harness axes 1-4 IRB 6600 — 3HAC023082-001 §9.2.2", "Kit Cavi")
HARNESS_SPLIT_56 = ("Kit Cavi", "Cable harness assi 5-6", "3HAC14140-001", 1,
                    "Cable harness axes 5-6 IRB 6600 — 3HAC023082-001 §9.2.2", "Kit Cavi")


def connect():
    return pymysql.connect(**DB)


def robot_id(cur) -> int:
    cur.execute("SELECT id FROM gamma_robot WHERE modello = %s LIMIT 1", (ROBOT_MODELLO,))
    row = cur.fetchone()
    if not row:
        raise RuntimeError(f"Robot {ROBOT_MODELLO} non trovato in gamma_robot")
    return row[0]


def find_quadro(cur, rid: int, payload: str, reach: str) -> int | None:
    cur.execute(
        """
        SELECT id FROM gamma_quadro
        WHERE robot_id = %s AND controllore = 'IRC5'
          AND payload = %s AND area_lavoro = %s
        LIMIT 1
        """,
        (rid, payload, reach),
    )
    row = cur.fetchone()
    return row[0] if row else None


def ensure_quadri(cur, apply: bool) -> dict[str, int]:
    rid = robot_id(cur)
    quadro_ids: dict[str, int] = {}
    for suffix, payload, reach in VARIANTS:
        system_key = f"{ROBOT_MODELLO}-{suffix}"
        qid = find_quadro(cur, rid, payload, reach)
        if qid:
            print(f"  = quadro esistente {system_key} id={qid}")
            quadro_ids[suffix] = qid
            continue
        print(f"  + CREA quadro {system_key} [IRC5] {payload} kg / {reach} m")
        if not apply:
            quadro_ids[suffix] = -1
            continue
        note = (
            f"ABB {system_key} — {payload} kg, reach {reach} m "
            f"(fonte: 3HAC023933-001 + IRC5 upgrade 3HAC047136-001)"
        )
        cur.execute(
            """
            INSERT INTO gamma_quadro
                (robot_id, controllore, generazione, payload, area_lavoro,
                 system_key, note, is_active)
            VALUES (%s, 'IRC5', 'IRC5', %s, %s, %s, %s, 1)
            """,
            (rid, payload, reach, system_key, note),
        )
        quadro_ids[suffix] = cur.lastrowid
        print(f"    -> id={quadro_ids[suffix]}")
    return quadro_ids


def apply_irc5_profiles(cur, quadro_ids: dict[str, int], apply: bool) -> None:
    print("\n--- Profilo quadro IRC5 (Schede + Azionamenti) ---")
    for suffix, qid in sorted(quadro_ids.items()):
        if qid <= 0:
            continue
        cur.execute("SELECT COUNT(*) FROM gamma_distinta WHERE quadro_id=%s", (qid,))
        existing = cur.fetchone()[0]
        if existing > 0:
            print(f"  SKIP {suffix} (id={qid}) — distinta già presente ({existing} righe)")
            continue
        preview, inserted = apply_profile_to_quadro(cur, qid, IRC5_PROFILE, apply)
        if apply:
            print(f"  OK {suffix} (id={qid}) — inserite {inserted} righe profilo {IRC5_PROFILE}")
        else:
            print(f"  PREVIEW {suffix} (id={qid}) — previste {preview} righe profilo {IRC5_PROFILE}")


def load_categories(cur) -> dict[str, int]:
    cur.execute("SELECT id, name FROM quote_categories")
    return {name: cid for cid, name in cur.fetchall()}


def find_product(cur, code: str) -> tuple[int, str, str] | None:
    for pat in (code, code.replace("-001", "-1")):
        cur.execute(
            """
            SELECT id, code, name FROM quote_products
            WHERE code = %s
            ORDER BY id
            LIMIT 1
            """,
            (pat,),
        )
        row = cur.fetchone()
        if row:
            return row
    cur.execute(
        """
        SELECT id, code, name FROM quote_products
        WHERE code LIKE %s
        ORDER BY CASE WHEN code = %s THEN 0 ELSE 1 END, LENGTH(code)
        LIMIT 1
        """,
        (f"{code}%", code),
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


def motor_display_name(slot: str, code: str) -> str:
    return f"Motore {slot.lower()} IRB 6600 — {code}"


def import_quadro(cur, quadro_id: int, label: str, apply: bool) -> tuple[int, int]:
    print(f"\n=== Quadro {quadro_id}: {label} ===")
    print(f"Modalità: {'APPLY' if apply else 'PREVIEW'}\n")

    categories = load_categories(cur)
    to_insert: list[tuple] = []
    skipped = 0

    for sezione, slot, code, qty, create_name, cat_name in COMMON_BOM:
        display = create_name or code
        prod = ensure_product(cur, code, display, cat_name, categories, apply)
        pid = prod[0]
        if distinta_exists(cur, quadro_id, sezione, slot, code, 0):
            print(f"  = SKIP {sezione}/{slot} {code}")
            skipped += 1
            continue
        print(f"  + DISTINTA {sezione:12} | {slot:28} | {code:22} x{qty} | prod={pid}")
        to_insert.append((sezione, slot, code, qty, pid if pid > 0 else None, 0))

    for sezione, slot, primary, alt in MOTORS:
        display = motor_display_name(slot, primary)
        prod = ensure_product(cur, primary, display, "Motori", categories, apply)
        pid = prod[0]
        if not distinta_exists(cur, quadro_id, sezione, slot, primary, 0):
            print(f"  + DISTINTA {sezione:12} | {slot:28} | {primary:22} x1 | prod={pid}")
            to_insert.append((sezione, slot, primary, 1, pid if pid > 0 else None, 0))
        else:
            skipped += 1
        if alt and not distinta_exists(cur, quadro_id, sezione, slot, alt, 1):
            prod = ensure_product(cur, alt, f"{motor_display_name(slot, alt)} (Foundry Prime)",
                                  "Motori", categories, apply)
            pid = prod[0]
            print(f"  + DISTINTA ALT {sezione:12} | {slot:28} | {alt:22} x1 | prod={pid}")
            to_insert.append((sezione, slot, alt, 1, pid if pid > 0 else None, 1))
        elif alt:
            skipped += 1

    sezione, slot, alt_code, alt_qty = BATTERY_ALT
    if not distinta_exists(cur, quadro_id, sezione, slot, alt_code, 1):
        prod = ensure_product(cur, alt_code,
                              f"Battery pack manipolatore ALT — {alt_code}", "Schede",
                              categories, apply)
        pid = prod[0]
        print(f"  + DISTINTA ALT {sezione:12} | {slot:28} | {alt_code:22} x{alt_qty} | prod={pid}")
        to_insert.append((sezione, slot, alt_code, alt_qty, pid if pid > 0 else None, 1))
    else:
        skipped += 1

    for harness in (HARNESS_SPLIT_14, HARNESS_SPLIT_56):
        sezione, slot, code, qty, create_name, cat_name = harness
        if not distinta_exists(cur, quadro_id, sezione, slot, code, 1):
            prod = ensure_product(cur, code, create_name or code, cat_name, categories, apply)
            pid = prod[0]
            print(f"  + DISTINTA ALT {sezione:12} | {slot:28} | {code:22} x{qty} | prod={pid}")
            to_insert.append((sezione, slot, code, qty, pid if pid > 0 else None, 1))
        else:
            skipped += 1

    print(f"Righe da inserire: {len(to_insert)}, skip: {skipped}")

    if apply and to_insert:
        for sezione, slot, code, qty, pid, is_alt in to_insert:
            note = SOURCE_NOTE
            if is_alt:
                note = f"Codice alternativo ABB — {note}"
            if "Cable harness assi" in slot:
                note = (
                    "Harness diviso assi 1-4 + 5-6 (alternativa a 3HAC024385-001) — "
                    + note
                )
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


def resolve_quadro_ids(cur) -> dict[int, str]:
    rid = robot_id(cur)
    out: dict[int, str] = {}
    for suffix, payload, reach in VARIANTS:
        qid = find_quadro(cur, rid, payload, reach)
        if qid:
            out[qid] = f"{ROBOT_MODELLO}-{suffix}"
    return out


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--setup", action="store_true",
                        help="Crea quadri IRC5 e applica profilo cabinet")
    parser.add_argument("--all", action="store_true", help="Import distinta su tutti i quadri IRC5")
    parser.add_argument("--quadro", type=int, help="ID quadro specifico")
    args = parser.parse_args()
    apply = args.apply

    conn = connect()
    cur = conn.cursor()

    if args.setup:
        print("=== Setup quadri IRB 6600 IRC5 ===")
        quadro_ids = ensure_quadri(cur, apply)
        apply_irc5_profiles(cur, quadro_ids, apply)
        if apply:
            conn.commit()
        elif not apply:
            print("\nPreview — usa --apply per scrivere su DB.")
        conn.close()
        return 0

    if args.all:
        quadro_map = resolve_quadro_ids(cur)
        if not quadro_map:
            print("Nessun quadro IRC5 per IRB 6600. Esegui prima: --setup --apply")
            conn.close()
            return 1
        quadro_ids = sorted(quadro_map.keys())
    elif args.quadro:
        quadro_ids = [args.quadro]
        quadro_map = {args.quadro: f"quadro {args.quadro}"}
    else:
        quadro_map = resolve_quadro_ids(cur)
        if not quadro_map:
            print("Nessun quadro IRC5. Esegui: python tools/import_irb6600_irc5_distinta.py --setup --apply")
            conn.close()
            return 1
        quadro_ids = [next(iter(quadro_map))]

    errors = 0
    for qid in quadro_ids:
        label = quadro_map.get(qid, str(qid))
        _, err = import_quadro(cur, qid, label, apply)
        errors += err

    if apply:
        conn.commit()
    elif not apply:
        print("\nEsegui con --apply per scrivere su DB.")

    conn.close()
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
