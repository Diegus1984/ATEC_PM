#!/usr/bin/env python3
"""
Inserisce in gamma_robot + gamma_quadro tutti i robot ABB mancanti (manipolazione 6 assi
+ famiglie correlate).

NON popola gamma_distinta: ogni famiglia richiede manuali spare parts ABB specifici.
Dopo l'inserimento robot/quadri usare:
  - tools/apply_irc5_controller_profiles.py  (solo Schede+Azionamenti IRC5 verificati)
  - tools/import_<robot>_distinta.py         (manipolatore + cavi da manuale dedicato)

Uso:
  python tools/migrate_all_missing_abb_robots.py           # anteprima
  python tools/migrate_all_missing_abb_robots.py --apply
"""
from __future__ import annotations

import argparse
import sys

import pymysql

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

# template_key: solo documentazione interna (classe robot), NON usato per clonare distinta
TEMPLATE = {
    "small": "irc5_small — 3HAC047136-001 §2.6.2",
    "medium": "irc5_medium_large — 3HAC047136-001 §2.6.2",
    "large_irc5": "irc5_medium_large — 3HAC047136-001 §2.6.2",
    "heavy": "irc5_heavy — 3HAC047136-001 §2.6.2 + manuale manipolatore",
    "shelf": "M2004 shelf — manuale 6650S",
    "press": "M2004 press — manuale 6620/6660",
}

# (modello, note, controller_default, [(suffix, payload, reach, ctrl_override|None, template_key), ...])
ROBOTS: list[tuple] = [
    ("IRB 1010", "Piccolo robot 6 assi OmniCore (brochure ABB 2025)", "OmniCore", [
        ("1.5/0.37", "1.5", "0.37", None, "small"),
    ]),
    ("IRB 1090", "Compatto OmniCore (brochure ABB 2025)", "OmniCore", [
        ("3.5/0.58", "3.5", "0.58", None, "small"),
    ]),
    ("IRB 1300", "Robot compatto ad alte prestazioni OmniCore", "OmniCore", [
        ("11/0.9", "11", "0.9", None, "small"),
        ("10/1.15", "10", "1.15", None, "small"),
        ("7/1.4", "7", "1.4", None, "small"),
        ("12/1.4", "12", "1.4", None, "small"),
    ]),
    ("IRB 1200 Gen2", "Seconda generazione IRB 1200 OmniCore", "OmniCore", [
        ("7/0.7", "7", "0.7", None, "small"),
        ("5/0.9", "5", "0.9", None, "small"),
    ]),
    ("IRB 1520ID", "Robot con dressing integrato (ID)", "OmniCore", [
        ("4/1.50", "4", "1.50", None, "small"),
    ]),
    ("IRB 1660ID", "Evoluzione IRB 1600 con dressing integrato", "OmniCore", [
        ("4/1.55", "4", "1.55", None, "small"),
    ]),
    ("IRB 2600", "Mid-range IRC5/OmniCore — saldatura, tending", "IRC5", [
        ("20/1.65", "20", "1.65", None, "medium"),
        ("12/1.65", "12", "1.65", None, "medium"),
        ("12/1.85", "12", "1.85", None, "medium"),
    ]),
    ("IRB 2600ID", "IRB 2600 con process cabling integrato", "IRC5", [
        ("15/1.85", "15", "1.85", None, "medium"),
    ]),
    ("IRB 4600", "General purpose (famiglia standard, oltre Type C)", "IRC5", [
        ("20/2.50", "20", "2.50", None, "medium"),
        ("40/2.55", "40", "2.55", None, "medium"),
        ("45/2.05", "45", "2.05", None, "medium"),
        ("60/2.05", "60", "2.05", None, "medium"),
    ]),
    ("IRB 6660", "Press tending / pre-machining — 3HAC028207-001", "IRC5", [
        ("130/3.10", "130", "3.10", None, "large_irc5"),
        ("100/3.30", "100", "3.30", None, "large_irc5"),
        ("205/1.90", "205", "1.90", None, "large_irc5"),
    ]),
    ("IRB 6700", "7a gen. large IRC5 — 3HAC044265-001", "IRC5", [
        ("300/2.70", "300", "2.70", None, "large_irc5"),
        ("245/3.00", "245", "3.00", None, "large_irc5"),
        ("235/2.65", "235", "2.65", None, "large_irc5"),
        ("205/2.80", "205", "2.80", None, "large_irc5"),
        ("200/2.60", "200", "2.60", None, "large_irc5"),
        ("175/3.05", "175", "3.05", None, "large_irc5"),
        ("155/2.85", "155", "2.85", None, "large_irc5"),
        ("150/3.20", "150", "3.20", None, "large_irc5"),
    ]),
    ("IRB 5710", "Large robot EV/auto OmniCore — 70-110 kg", "OmniCore", [
        ("110/2.30", "110", "2.30", None, "large_irc5"),
        ("90/2.70", "90", "2.70", None, "large_irc5"),
        ("90/2.30 LID", "90", "2.30", None, "large_irc5"),
        ("70/2.70 LID", "70", "2.70", None, "large_irc5"),
    ]),
    ("IRB 5720", "Large robot 90-180 kg OmniCore", "OmniCore", [
        ("180/2.60", "180", "2.60", None, "large_irc5"),
        ("125/3.00", "125", "3.00", None, "large_irc5"),
        ("155/2.60 LID", "155", "2.60", None, "large_irc5"),
        ("90/3.00 LID", "90", "3.00", None, "large_irc5"),
    ]),
    ("IRB 6710", "Next-gen large 150-210 kg OmniCore", "OmniCore", [
        ("210/2.65", "210", "2.65", None, "large_irc5"),
        ("200/2.95", "200", "2.95", None, "large_irc5"),
        ("175/2.65", "175", "2.65", None, "large_irc5"),
    ]),
    ("IRB 6720", "Next-gen large 170-240 kg OmniCore", "OmniCore", [
        ("240/2.65", "240", "2.65", None, "large_irc5"),
        ("210/2.80", "210", "2.80", None, "large_irc5"),
        ("170/3.10", "170", "3.10", None, "large_irc5"),
    ]),
    ("IRB 6730", "Next-gen large 210-270 kg OmniCore", "OmniCore", [
        ("270/2.70", "270", "2.70", None, "large_irc5"),
        ("240/2.90", "240", "2.90", None, "large_irc5"),
        ("210/3.10", "210", "3.10", None, "large_irc5"),
    ]),
    ("IRB 6740", "Next-gen large 240-310 kg OmniCore", "OmniCore", [
        ("310/2.80", "310", "2.80", None, "large_irc5"),
        ("260/3.00", "260", "3.00", None, "large_irc5"),
        ("240/3.20", "240", "3.20", None, "large_irc5"),
    ]),
    ("IRB 6730S", "Shelf-mounted spot welding OmniCore", "OmniCore", [
        ("270/2.70", "270", "2.70", None, "shelf"),
        ("240/2.90", "240", "2.90", None, "shelf"),
        ("210/3.10", "210", "3.10", None, "shelf"),
    ]),
    ("IRB 6750S", "Shelf-mounted large OmniCore", "OmniCore", [
        ("260/3.00", "260", "3.00", None, "shelf"),
        ("240/3.20", "240", "3.20", None, "shelf"),
    ]),
    ("IRB 6760", "Press tending robot OmniCore (900 p/h)", "OmniCore", [
        ("standard", "200", "2.80", None, "press"),
    ]),
    ("IRB 6790", "Large robot IP69 OmniCore (brochure 2025)", "OmniCore", [
        ("205/2.80", "205", "2.80", None, "heavy"),
    ]),
    ("IRB 7710", "Heavy modular OmniCore 280-500 kg — 3HAC089607", "OmniCore", [
        ("500/3.10", "500", "3.10", None, "heavy"),
        ("430/3.10", "430", "3.10", None, "heavy"),
        ("280/2.85", "280", "2.85", None, "heavy"),
        ("500/2.85 LID", "500", "2.85", None, "heavy"),
        ("430/2.85 LID", "430", "2.85", None, "heavy"),
        ("280/3.10 LID", "280", "3.10", None, "heavy"),
    ]),
    ("IRB 7720", "Heavy modular OmniCore 400-620 kg — 3HAC089607", "OmniCore", [
        ("620/2.90", "620", "2.90", None, "heavy"),
        ("530/3.10", "530", "3.10", None, "heavy"),
        ("510/3.30", "510", "3.30", None, "heavy"),
        ("450/3.50", "450", "3.50", None, "heavy"),
        ("560/2.90 LID", "560", "2.90", None, "heavy"),
        ("480/3.10 LID", "480", "3.10", None, "heavy"),
        ("400/3.30 LID", "400", "3.30", None, "heavy"),
        ("400/3.50 LID", "400", "3.50", None, "heavy"),
    ]),
    # Altre tipologie manipolazione ABB (catalogo esteso)
    ("IRB 460", "Palletizer 4 assi ad alta velocita", "IRC5", [
        ("110/2.40", "110", "2.40", None, "medium"),
    ]),
    ("IRB 910", "SCARA 4 assi", "OmniCore", [
        ("standard", "6", "0.45", None, "small"),
    ]),
    ("IRB 920", "SCARA T-type", "OmniCore", [
        ("12/0.85", "12", "0.85", None, "small"),
    ]),
    ("IRB 930", "SCARA ad alte prestazioni", "OmniCore", [
        ("22/1.20", "22", "1.20", None, "medium"),
    ]),
    ("IRB 360", "Delta picker 3+1 assi", "IRC5", [
        ("1/1130", "1", "1.13", None, "small"),
        ("3/1130", "3", "1.13", None, "small"),
    ]),
    ("IRB 390", "Delta 4 assi ad alte prestazioni", "OmniCore", [
        ("15/1300", "15", "1.30", None, "medium"),
    ]),
    ("IRB 14000", "Collaborativo YuMi dual-arm", "OmniCore", [
        ("0.5/0.50", "0.5", "0.50", None, "small"),
    ]),
]

def get_robot_id(cur, modello: str) -> int | None:
    cur.execute("SELECT id FROM gamma_robot WHERE modello=%s", (modello,))
    row = cur.fetchone()
    return row[0] if row else None


def ensure_robot(cur, modello: str, note: str, apply: bool) -> int | None:
    rid = get_robot_id(cur, modello)
    if rid:
        return rid
    print(f"  + ROBOT {modello}")
    if not apply:
        return -1
    cur.execute(
        "INSERT INTO gamma_robot (modello, brand, note, is_active) VALUES (%s, 'ABB', %s, 1)",
        (modello, note),
    )
    return cur.lastrowid


def quadro_exists(cur, robot_id: int, system_key: str) -> bool:
    cur.execute(
        "SELECT id FROM gamma_quadro WHERE robot_id=%s AND system_key=%s LIMIT 1",
        (robot_id, system_key),
    )
    return cur.fetchone() is not None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    apply = args.apply

    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    robots_added = 0
    quadri_added = 0
    for modello, note, ctrl_default, variants in ROBOTS:
        if get_robot_id(cur, modello):
            robot_id = get_robot_id(cur, modello)
            print(f"\n= {modello} (id={robot_id}) esistente")
        else:
            print(f"\n+ {modello}")
            robot_id = ensure_robot(cur, modello, note, apply)
            if robot_id and robot_id > 0:
                robots_added += 1

        if robot_id is None or robot_id == -1:
            continue

        for suffix, payload, reach, ctrl_override, tmpl_key in variants:
            ctrl = ctrl_override or ctrl_default
            system_key = f"{modello}-{suffix}"
            if quadro_exists(cur, robot_id, system_key):
                continue
            qnote = f"ABB {system_key} — {payload} kg, reach {reach} m (fonte: ABB product range 2025)"
            cls = TEMPLATE.get(tmpl_key, tmpl_key)
            print(f"    + quadro {system_key} [{ctrl}] classe={cls}")
            quadri_added += 1
            if not apply:
                continue
            distinta_note = (
                f"Distinta da compilare — manuale spare parts ABB ({cls})"
            )
            cur.execute(
                """
                INSERT INTO gamma_quadro
                    (robot_id, controllore, generazione, payload, area_lavoro, system_key, note, is_active)
                VALUES (%s, %s, %s, %s, %s, %s, %s, 1)
                """,
                (robot_id, ctrl, ctrl, payload, reach, system_key, f"{qnote}. {distinta_note}"),
            )

    if apply:
        conn.commit()

    cur.execute("SELECT COUNT(*) FROM gamma_robot WHERE is_active=1")
    total_robots = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM gamma_quadro")
    total_quadri = cur.fetchone()[0]

    print(f"\n--- Riepilogo ---")
    print(f"Robot nuovi: {robots_added}")
    print(f"Quadri nuovi: {quadri_added}")
    print(f"Totale DB: {total_robots} robot, {total_quadri} quadri")
    print("Distinta: importare per famiglia da manuali ABB (vedi memory/gamma_distinta_import.md)")
    if not apply:
        print("Preview — usa --apply per scrivere.")

    conn.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
