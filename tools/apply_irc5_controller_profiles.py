#!/usr/bin/env python3
"""
Applica Schede + Azionamenti quadro IRC5 da profili verificati (manuali ABB).

NON clona distinta intera: copia solo sezioni quadro controllore da quadri
legacy curati, mappati per classe drive system (3HAC047136-001 §2.6.2).

Profili:
  irc5_small         — fino a IRB 1600-1660, MDU DSQC 406 (ref quadro 6 IRB 1200)
  irc5_medium_large  — robot medio/grandi, MDU DSQC 663 (ref quadro 98 IRB 6640 IRC5)
  irc5_heavy         — IRB 8700 class, MDU+2 ADU+bleeder 4kW (ref quadro 108, solo quadro)

Kit Cavi / Motori / Ventole manipolatore: NON inclusi — richiedono manuale spare parts
specifico per ogni famiglia robot.

Uso:
  python tools/apply_irc5_controller_profiles.py              # anteprima
  python tools/apply_irc5_controller_profiles.py --apply
  python tools/apply_irc5_controller_profiles.py --robot "IRB 6700" --apply
"""
from __future__ import annotations

import argparse
import sys

import pymysql

DB = dict(host="localhost", port=3306, user="root", password="Atec2005",
          database="atec_pm", charset="utf8mb4")

DSQC668_PRIMARY = 351
DSQC668_ALT = 253
DSQC668_SLOT = ("Schede", "Axis Computer")

PROFILES: dict[str, dict] = {
    "irc5_small": {
        "ref_quadro": 6,
        "manual": "3HAC047136-001 rev.AH §2.6.2 small robots (up to IRB 1600-1660)",
        "sections": ("Schede", "Azionamenti"),
        "exclude_slots": (),
    },
    "irc5_medium_large": {
        "ref_quadro": 98,
        "manual": "3HAC047136-001 rev.AH §2.6.2 medium and large robots",
        "sections": ("Schede", "Azionamenti"),
        "exclude_slots": (),
    },
    "irc5_heavy": {
        "ref_quadro": 108,
        "manual": "3HAC047136-001 rev.AH §2.6.2 IRB 8700 (MDU + 2 ADU + bleeder 4kW)",
        "sections": ("Schede", "Azionamenti"),
        "exclude_slots": (
            "RMU102",
            "Brake release DSQC1052",
            "Battery holder",
            "Push button guard",
        ),
    },
}

# Mappatura modello robot -> profilo IRC5 (solo controllore IRC5, non OmniCore)
ROBOT_PROFILE: dict[str, str] = {
    "IRB 360": "irc5_small",
    "IRB 2600": "irc5_medium_large",
    "IRB 2600ID": "irc5_medium_large",
    "IRB 4600": "irc5_medium_large",
    "IRB 460": "irc5_medium_large",
    "IRB 6600": "irc5_medium_large",
    "IRB 6660": "irc5_medium_large",
    "IRB 6700": "irc5_medium_large",
}


def ensure_dsqc668_alt(cur, quadro_id: int, apply: bool) -> bool:
    sezione, slot = DSQC668_SLOT
    cur.execute(
        """
        SELECT id FROM gamma_distinta
        WHERE quadro_id=%s AND sezione=%s AND slot=%s AND product_id=%s AND is_alternate=1
        LIMIT 1
        """,
        (quadro_id, sezione, slot, DSQC668_ALT),
    )
    if cur.fetchone():
        return False
    cur.execute(
        """
        SELECT id FROM gamma_distinta
        WHERE quadro_id=%s AND sezione=%s AND slot=%s AND product_id=%s AND is_alternate=0
        LIMIT 1
        """,
        (quadro_id, sezione, slot, DSQC668_PRIMARY),
    )
    if not cur.fetchone():
        return False
    if apply:
        cur.execute(
            """
            INSERT INTO gamma_distinta
                (quadro_id, product_id, sezione, slot, code_raw, qty, is_alternate, is_optional, note)
            VALUES (%s,%s,%s,%s,'3HAC028179-001',1,1,0,
                    'Codice alternativo DSQC 668 (3HAC028179-001 vs 3HAC029157-001)')
            """,
            (quadro_id, DSQC668_ALT, sezione, slot),
        )
    return True


def fetch_profile_rows(cur, profile_key: str) -> list[tuple]:
    profile = PROFILES[profile_key]
    ref_qid = profile["ref_quadro"]
    sections = profile["sections"]
    exclude = set(profile["exclude_slots"])
    placeholders = ",".join(["%s"] * len(sections))
    cur.execute(
        f"""
        SELECT product_id, sezione, slot, code_raw, qty, is_alternate, is_optional, note
        FROM gamma_distinta
        WHERE quadro_id=%s AND sezione IN ({placeholders})
        ORDER BY id
        """,
        (ref_qid, *sections),
    )
    rows = []
    for row in cur.fetchall():
        if row[2] in exclude:
            continue
        rows.append(row)
    return rows


def apply_profile_to_quadro(
    cur, quadro_id: int, profile_key: str, apply: bool
) -> tuple[int, int]:
    profile = PROFILES[profile_key]
    cur.execute("SELECT COUNT(*) FROM gamma_distinta WHERE quadro_id=%s", (quadro_id,))
    if cur.fetchone()[0] > 0:
        return 0, 0

    src_rows = fetch_profile_rows(cur, profile_key)
    note_base = (
        f"Quadro IRC5 profilo {profile_key} — {profile['manual']} "
        f"(ref quadro {profile['ref_quadro']})"
    )
    inserted = 0
    if apply:
        for r in src_rows:
            note = r[7] or note_base
            if TEMPLATE_MARKER not in note:
                note = note_base
            cur.execute(
                """
                INSERT INTO gamma_distinta
                    (quadro_id, product_id, sezione, slot, code_raw, qty, is_alternate, is_optional, note)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s)
                """,
                (quadro_id, r[0], r[1], r[2], r[3], r[4], r[5], r[6], note),
            )
            inserted += 1
        if ensure_dsqc668_alt(cur, quadro_id, apply=True):
            inserted += 1
    return len(src_rows), inserted


TEMPLATE_MARKER = "Distinta template"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--robot", help="Solo un modello robot (es. IRB 6700)")
    parser.add_argument("--min-robot-id", type=int, default=26)
    args = parser.parse_args()

    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    models = list(ROBOT_PROFILE.keys())
    if args.robot:
        if args.robot not in ROBOT_PROFILE:
            print(f"Robot non mappato: {args.robot}")
            print("Modelli IRC5 supportati:", ", ".join(models))
            conn.close()
            return 1
        models = [args.robot]

    total_preview = 0
    total_inserted = 0
    skipped_omnicore = 0

    for modello in models:
        profile_key = ROBOT_PROFILE[modello]
        cur.execute(
            """
            SELECT q.id, q.system_key, q.controllore
            FROM gamma_quadro q
            JOIN gamma_robot r ON r.id = q.robot_id
            WHERE r.modello = %s AND r.id >= %s
            ORDER BY q.id
            """,
            (modello, args.min_robot_id),
        )
        quadros = cur.fetchall()
        if not quadros:
            continue

        print(f"\n{modello} -> {profile_key} (ref q{PROFILES[profile_key]['ref_quadro']})")
        for qid, system_key, ctrl in quadros:
            if ctrl != "IRC5":
                print(f"  SKIP {system_key} [{ctrl}] — OmniCore, richiede manuale dedicato")
                skipped_omnicore += 1
                continue
            preview, inserted = apply_profile_to_quadro(cur, qid, profile_key, args.apply)
            if preview == 0:
                cur.execute(
                    "SELECT COUNT(*) FROM gamma_distinta WHERE quadro_id=%s", (qid,)
                )
                existing = cur.fetchone()[0]
                print(f"  SKIP {system_key} — distinta gia presente ({existing} righe)")
                continue
            total_preview += preview
            total_inserted += inserted
            action = f"inserite {inserted}" if args.apply else f"previste {preview}"
            print(f"  {system_key}: {action} righe (solo Schede+Azionamenti quadro)")

    if args.apply:
        conn.commit()

    print(f"\n--- Riepilogo ---")
    print(f"Righe {'inserite' if args.apply else 'previste'}: {total_inserted or total_preview}")
    print(f"Quadri OmniCore saltati: {skipped_omnicore}")
    if not args.apply:
        print("Preview — usa --apply per scrivere.")
    print(
        "\nATTENZIONE: Kit Cavi, Motori e Ventole manipolatore vanno importati "
        "per famiglia dal manuale spare parts ABB (es. 3HAC044266 per IRB 6700)."
    )

    conn.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
