#!/usr/bin/env python3
"""
Aggiorna descrizioni Robot (Gamma Ricambi) con testi sintetici da fonti web ABB.
Mantiene formato 2x2 (robot + cabinet).

Uso:
  python tools/update_robot_catalog_web_snippets.py           # preview
  python tools/update_robot_catalog_web_snippets.py --apply   # scrive su DB
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import pymysql

sys.path.insert(0, str(Path(__file__).resolve().parent))
from robot_web_descriptions import ROBOT_WEB_DESCRIPTIONS

DB = dict(
    host="localhost",
    port=3306,
    user="root",
    password="Atec2005",
    database="atec_pm",
    charset="utf8mb4",
)


def normalize_spaces(s: str) -> str:
    return re.sub(r"\s+", " ", (s or "").strip())


def html_escape(s: str) -> str:
    return (
        (s or "")
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def build_robot_2x2_description(
    robot_title: str,
    robot_model: str,
    robot_body: str,
    cabinet_title: str,
    cabinet_body: str,
) -> str:
    return (
        '<table style="border-collapse: collapse; width: 100%;"><tbody>'
        "<tr>"
        '<td style="width: 50%; vertical-align: top;">'
        f"<p><strong>{html_escape(robot_title)}</strong></p>"
        f"<p>Modello: <strong>{html_escape(robot_model)}</strong><br>"
        "Costruttore: ABB</p>"
        f"<p>{html_escape(robot_body)}</p>"
        "</td>"
        '<td style="width: 50%; vertical-align: top;"><p>&nbsp;</p></td>'
        "</tr>"
        "<tr>"
        '<td style="width: 50%; vertical-align: top;">'
        f"<p><strong>{html_escape(cabinet_title)}</strong></p>"
        f"<p>{html_escape(cabinet_body)}</p>"
        "</td>"
        '<td style="width: 50%; vertical-align: top;"><p>&nbsp;</p></td>'
        "</tr>"
        "</tbody></table>"
    )


def get_robot_quadro_stats(cur, modello: str) -> dict:
    cur.execute(
        """
        SELECT r.id
        FROM gamma_robot r
        WHERE r.modello=%s AND r.is_active=1
        LIMIT 1
        """,
        (modello,),
    )
    r = cur.fetchone()
    if not r:
        return {}
    robot_id = int(r[0])
    cur.execute(
        """
        SELECT
            GROUP_CONCAT(DISTINCT controllore ORDER BY controllore SEPARATOR ',') AS controllori,
            GROUP_CONCAT(DISTINCT generazione ORDER BY generazione SEPARATOR ',') AS generazioni
        FROM gamma_quadro
        WHERE robot_id=%s AND is_active=1
        """,
        (robot_id,),
    )
    row = cur.fetchone() or ("", "")
    return dict(controllori=row[0] or "", generazioni=row[1] or "")


def cabinet_description_from_controllers(controllori_csv: str, generazioni_csv: str) -> tuple[str, str]:
    controllori = set([normalize_spaces(x) for x in (controllori_csv or "").split(",") if x])
    generazioni = set([normalize_spaces(x) for x in (generazioni_csv or "").split(",") if x])

    if "OmniCore" in controllori or "OmniCore" in generazioni:
        return (
            "Cabinet ABB OmniCore",
            (
                "Controller ABB di nuova generazione (OmniCore) per robot industriali, con funzioni moderne "
                "di programmazione, connettività e motion control. In Gamma Robot il cabinet è modellato a "
                "livello di quadro (Schede/Azionamenti) e può variare per taglia."
            ),
        )
    if "IRC5" in controllori or "IRC5" in generazioni:
        return (
            "Cabinet ABB IRC5",
            (
                "Controller ABB IRC5 (RobotWare) con architettura modulare: computer principale, axis computer, "
                "drive system (MDU/ADU) e I/O."
            ),
        )
    if "M2004" in controllori:
        return (
            "Cabinet ABB M2004 (generazione IRC5)",
            "Cabinet/controller ABB M2004 (famiglia IRC5) usato su molte famiglie IRB 66xx/76xx.",
        )
    if "M2000" in controllori or "S4C+" in generazioni:
        return (
            "Cabinet ABB S4C+ / M2000",
            "Controller ABB di generazione S4C+ (M2000) per manipolatori legacy.",
        )
    return ("Cabinet / Controller", "Descrizione cabinet non classificata automaticamente.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    apply = args.apply

    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    # categoria Robot in Gamma Ricambi
    cur.execute(
        """
        SELECT c.id
        FROM quote_categories c
        JOIN quote_groups g ON g.id = c.group_id
        WHERE g.price_list_id = 4 AND g.name='Manipolazione' AND c.name='Robot'
        LIMIT 1
        """
    )
    row = cur.fetchone()
    if not row:
        raise RuntimeError("Categoria Gamma Ricambi/Robot non trovata. Esegui prima populate_robot_catalog_gamma.py")
    cat_id = int(row[0])

    cur.execute(
        "SELECT id, name FROM quote_products WHERE category_id=%s ORDER BY name",
        (cat_id,),
    )
    products = cur.fetchall()

    updated = 0
    skipped = 0
    for pid, modello in products:
        robot_body = ROBOT_WEB_DESCRIPTIONS.get(modello)
        if not robot_body:
            print(f"SKIP {modello} (id={pid}) - nessun testo in robot_web_descriptions.py")
            skipped += 1
            continue
        stats = get_robot_quadro_stats(cur, modello)
        cabinet_title, cabinet_body = cabinet_description_from_controllers(
            stats.get("controllori", ""), stats.get("generazioni", "")
        )
        desc = build_robot_2x2_description(
            robot_title=f"Manipolatore ABB {modello}",
            robot_model=modello,
            robot_body=robot_body,
            cabinet_title=cabinet_title,
            cabinet_body=cabinet_body,
        )
        print(f"UPDATE {modello} (id={pid})")
        updated += 1
        if apply:
            cur.execute(
                "UPDATE quote_products SET description_rtf=%s WHERE id=%s",
                (desc, pid),
            )

    if apply:
        conn.commit()
    conn.close()
    print(f"OK - aggiornati: {updated}, saltati: {skipped}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

