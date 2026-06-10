#!/usr/bin/env python3
"""
Popola Cat. Preventivi → Gamma Ricambi → (categoria) Robot con una scheda per ogni modello in gamma_robot.

Formato richiesto dall'utente:
  - tabella HTML 2 righe × 2 colonne (50/50)
  - (0,0) descrizione robot
  - (0,1) placeholder immagine robot (vuoto)
  - (1,0) descrizione cabinet/controller
  - (1,1) placeholder immagine cabinet (vuoto)

NOTE:
  - Il prodotto è creato nel listino Gamma Ricambi (price_list_id=4) nel gruppo 'Manipolazione'.
  - La descrizione robot qui è "baseline" (range payload/reach dal DB). Puoi incollare/raffinare
    testo dal web senza cambiare il formato 2×2.

Uso:
  python tools/populate_robot_catalog_gamma.py              # preview
  python tools/populate_robot_catalog_gamma.py --apply      # scrive su DB
"""

from __future__ import annotations

import argparse
import re

import pymysql

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
    # 2 righe, 2 colonne al 50% (immutabile: l'utente inserirà immagini nelle celle 0,1 e 1,1).
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


def ensure_robot_category(cur, apply: bool) -> int:
    # Gamma Ricambi: price_list_id=4; gruppo 'Manipolazione'
    cur.execute(
        """
        SELECT c.id
        FROM quote_categories c
        JOIN quote_groups g ON g.id = c.group_id
        WHERE g.price_list_id = 4 AND g.name = 'Manipolazione' AND c.name = 'Robot'
        LIMIT 1
        """
    )
    row = cur.fetchone()
    if row:
        return int(row[0])

    cur.execute(
        "SELECT id FROM quote_groups WHERE price_list_id=4 AND name='Manipolazione' LIMIT 1"
    )
    row = cur.fetchone()
    if not row:
        raise RuntimeError("Gruppo Gamma Ricambi/Manipolazione non trovato (quote_groups)")
    group_id = int(row[0])

    print("  + CREA categoria 'Robot' in Gamma Ricambi/Manipolazione")
    if not apply:
        return -1

    # sort_order: 0 per stare in cima al gruppo
    cur.execute(
        """
        INSERT INTO quote_categories (group_id, name, sort_order)
        VALUES (%s, 'Robot', 0)
        """,
        (group_id,),
    )
    return int(cur.lastrowid)


def find_product_by_name_in_category(cur, category_id: int, name: str) -> tuple[int, str] | None:
    cur.execute(
        """
        SELECT id, code
        FROM quote_products
        WHERE category_id=%s AND name=%s
        LIMIT 1
        """,
        (category_id, name),
    )
    row = cur.fetchone()
    return (int(row[0]), row[1] or "") if row else None


def get_robot_quadro_stats(cur, robot_id: int) -> dict:
    cur.execute(
        """
        SELECT
            COUNT(*) AS quadri,
            GROUP_CONCAT(DISTINCT controllore ORDER BY controllore SEPARATOR ',') AS controllori,
            GROUP_CONCAT(DISTINCT generazione ORDER BY generazione SEPARATOR ',') AS generazioni,
            MIN(CAST(payload AS DECIMAL(10,2))) AS payload_min,
            MAX(CAST(payload AS DECIMAL(10,2))) AS payload_max,
            MIN(CAST(area_lavoro AS DECIMAL(10,2))) AS reach_min,
            MAX(CAST(area_lavoro AS DECIMAL(10,2))) AS reach_max
        FROM gamma_quadro
        WHERE robot_id=%s AND is_active=1
        """,
        (robot_id,),
    )
    r = cur.fetchone() or (0, None, None, None, None, None, None)
    return dict(
        quadri=int(r[0] or 0),
        controllori=(r[1] or ""),
        generazioni=(r[2] or ""),
        payload_min=r[3],
        payload_max=r[4],
        reach_min=r[5],
        reach_max=r[6],
    )


def cabinet_description_from_controllers(controllori_csv: str, generazioni_csv: str) -> tuple[str, str]:
    controllori = set([normalize_spaces(x) for x in (controllori_csv or "").split(",") if x])
    generazioni = set([normalize_spaces(x) for x in (generazioni_csv or "").split(",") if x])

    # Heuristics: scegliamo una descrizione sintetica in base alle famiglie controller presenti
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
                "drive system (MDU/ADU) e I/O. In Gamma Robot le sezioni Schede/Azionamenti del quadro sono "
                "la parte 'cabinet', mentre cavi/motori restano manipolatore."
            ),
        )

    if "M2004" in controllori:
        return (
            "Cabinet ABB M2004 (generazione IRC5)",
            (
                "Cabinet/controller ABB M2004 (famiglia IRC5). La distinta quadro include schede (Main Computer, "
                "PDU, Axis Computer) e azionamenti (MDU) compatibili con la generazione IRC5."
            ),
        )

    if "M2000" in controllori or "S4C+" in generazioni:
        return (
            "Cabinet ABB S4C+ / M2000",
            (
                "Controller ABB di generazione S4C+ (M2000). La distinta quadro include schede e azionamenti "
                "specifici per questa generazione legacy (diversi da IRC5/OmniCore)."
            ),
        )

    # fallback
    return (
        "Cabinet / Controller",
        "Descrizione cabinet non classificata automaticamente: verifica il tipo controllore nei quadri del robot.",
    )


def format_range(vmin, vmax, unit: str) -> str:
    if vmin is None and vmax is None:
        return "n/d"
    if vmin is None:
        return f"≤ {vmax:g} {unit}"
    if vmax is None:
        return f"≥ {vmin:g} {unit}"
    if float(vmin) == float(vmax):
        return f"{float(vmin):g} {unit}"
    return f"{float(vmin):g}–{float(vmax):g} {unit}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    apply = args.apply

    conn = pymysql.connect(**DB)
    cur = conn.cursor()

    print("=== Gamma Ricambi - Robot catalog ===")
    cat_id = ensure_robot_category(cur, apply)

    cur.execute("SELECT id, modello, note FROM gamma_robot WHERE is_active=1 ORDER BY modello")
    robots = cur.fetchall()

    created = 0
    updated = 0
    skipped = 0

    for rid, modello, note in robots:
        modello = normalize_spaces(modello)
        stats = get_robot_quadro_stats(cur, int(rid))
        payload = format_range(stats["payload_min"], stats["payload_max"], "kg")
        reach = format_range(stats["reach_min"], stats["reach_max"], "m")

        robot_title = f"Manipolatore ABB {modello}"
        robot_body = (
            f"Robot industriale ABB modello {modello}. "
            f"Configurazioni censite in Gamma Robot: {stats['quadri']} (payload {payload}, area lavoro {reach}). "
            "Descrizione sintetica da completare/incollare da scheda ABB online mantenendo questo formato 2×2."
        )
        if note:
            robot_body += " Nota interna: " + normalize_spaces(note)

        cabinet_title, cabinet_body = cabinet_description_from_controllers(
            stats["controllori"], stats["generazioni"]
        )

        desc = build_robot_2x2_description(
            robot_title=robot_title,
            robot_model=modello,
            robot_body=robot_body,
            cabinet_title=cabinet_title,
            cabinet_body=cabinet_body,
        )

        # product keying: name = modello (match con tree Gamma Robot)
        existing = find_product_by_name_in_category(cur, cat_id if cat_id > 0 else 0, modello)
        if existing:
            pid, code = existing
            # aggiorna solo se non ha tabella 2x2
            cur.execute("SELECT description_rtf FROM quote_products WHERE id=%s", (pid,))
            existing_desc = cur.fetchone()[0] or ""
            if "<table" in existing_desc and existing_desc.count("<tr>") >= 2:
                skipped += 1
                continue
            print(f"UPDATE Robot catalog: {modello} (id={pid})")
            updated += 1
            if apply:
                cur.execute(
                    "UPDATE quote_products SET description_rtf=%s WHERE id=%s",
                    (desc, pid),
                )
            continue

        # create new product
        code = modello  # semplice e leggibile (es. 'IRB 5710')
        print(f"CREATE Robot catalog: {modello} [{code}]")
        created += 1
        if apply:
            cur.execute(
                """
                INSERT INTO quote_products (category_id, item_type, code, name, description_rtf, is_active)
                VALUES (%s, 'product', %s, %s, %s, 1)
                """,
                (cat_id, code, modello, desc),
            )

    if apply:
        conn.commit()

    conn.close()
    print("\n--- Riepilogo ---")
    print(f"Creati: {created}")
    print(f"Aggiornati: {updated}")
    print(f"Saltati: {skipped}")
    if not apply:
        print("Preview — usa --apply per scrivere.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

