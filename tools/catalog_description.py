"""
Helper per generare description_rtf (HTML tabella 1x2 50/50) del catalogo Gamma Ricambi.
Vedi memory/popolamento_descrizioni_catalogo.md
"""

TABLE_PREFIX = (
    '<table style="border-collapse: collapse; width: 100%;"><tbody><tr>'
    '<td style="width: 50%; vertical-align: top;">'
)
TABLE_MIDDLE = '</td><td style="width: 50%; vertical-align: top;">'
TABLE_SUFFIX = '<p>&nbsp;</p></td></tr></tbody></table>'


def build_description(
    title: str,
    code: str,
    sigla: str,
    body: str,
    costruttore: str = "ABB",
    famiglia: str | None = "IRC5",
) -> str:
    meta_lines = [
        f"Codice commerciale: <strong>{code}</strong>",
    ]
    if sigla:
        meta_lines.append(f"Sigla: <strong>{sigla}</strong>")
    meta_lines.append(f"Costruttore: {costruttore}")
    if famiglia:
        meta_lines.append(f"Famiglia controller: {famiglia}")

    meta_html = "<br>".join(meta_lines)
    return (
        f"{TABLE_PREFIX}"
        f"<p><strong>{title}</strong></p>"
        f"<p>{meta_html}</p>"
        f"<p>{body}</p>"
        f"{TABLE_MIDDLE}{TABLE_SUFFIX}"
    )


# Prodotti creati da import distinta IRB 8700-800/3.50 (tools/import_irb8700_800_distinta.py)
IRB8700_IMPORTED_DESCRIPTIONS: dict[str, str] = {
    "3HAC065021-001": build_description(
        title="Unità rilascio freno ABB DSQC1052",
        code="3HAC065021-001",
        sigla="DSQC1052",
        body=(
            "Unità di rilascio freno (Brake Release Unit) montata sul manipolatore IRB 8700. "
            "Consente il rilascio manuale dei freni degli assi in sicurezza per spostamenti "
            "in manutenzione; include DSQC1052 e cablaggio associato. "
            "Documentazione ABB: Product manual spare parts 3HAC052854-001."
        ),
        famiglia="IRC5",
    ),
    "3HAC044161-001": build_description(
        title="Supporto batteria SMB IRB 8700",
        code="3HAC044161-001",
        sigla="",
        body=(
            "Staffa/supporto (battery holder) per il pacco batteria tampone SMB del manipolatore "
            "ABB IRB 8700. Alloggia la batteria di backup della scheda di misura seriale sul braccio. "
            "Documentazione ABB: Product manual spare parts 3HAC052854-001, sezione Electrical parts."
        ),
        famiglia="IRC5",
    ),
    "3HAC6499-1": build_description(
        title="Protezione pulsante IRB 8700",
        code="3HAC6499-1",
        sigla="",
        body=(
            "Protezione (push button guard) per il pulsante di rilascio freno sul manipolatore "
            "ABB IRB 8700. Evita azionamenti accidentali durante il normale funzionamento. "
            "Documentazione ABB: Product manual spare parts 3HAC052854-001, sezione Electrical parts."
        ),
        famiglia="IRC5",
    ),
    "3HAC050878-001": build_description(
        title="Bleeder 4 kW IRB 8700",
        code="3HAC050878-001",
        sigla="Bleeder",
        body=(
            "Unità bleeder da 4 kW per controller IRC5 abbinato al manipolatore ABB IRB 8700. "
            "Dissipa l'energia rigenerativa prodotta dagli assi in frenata quando il bus DC "
            "supera la soglia di tensione ammessa. "
            "Documentazione ABB: Product manual IRC5 spare parts 3HAC047136-001, sezione Controller parts."
        ),
        famiglia="IRC5",
    ),
    "3HAC050792-001": build_description(
        title="Cablaggi elettrici manipolatore IRB 8700",
        code="3HAC050792-001",
        sigla="Cable harness",
        body=(
            "Harness di cablaggio elettrico (cable harness) del manipolatore ABB IRB 8700. "
            "Raccorda le unità elettriche sul braccio (SMB, rilascio freno, batteria tampone) "
            "con il connettore principale del robot. "
            "Documentazione ABB: Product manual spare parts 3HAC052854-001, sezione Electrical parts."
        ),
        famiglia="IRB 8700",
    ),
    "3HAC058949-003": build_description(
        title="Motore rotante asse 1–3/5 IRB 8700",
        code="3HAC058949-003",
        sigla="Motore asse 1–3/5",
        body=(
            "Servomotore in corrente alternata rotante con pignone (Graphite White) per gli assi 1, 2, 3 e 5 "
            "del manipolatore ABB IRB 8700. Fornisce coppia e movimento tramite il riduttore dell'asse; "
            "integra il resolver per la retroazione di posizione. "
            "Variante colore ABB Orange: 3HAC058949-004. "
            "Documentazione ABB: Product manual spare parts 3HAC052854-001, sezione Motors."
        ),
        famiglia="Manipolatori ABB",
    ),
    "3HAC058950-003": build_description(
        title="Motore rotante asse 4 IRB 8700",
        code="3HAC058950-003",
        sigla="Motore asse 4",
        body=(
            "Servomotore in corrente alternata rotante con pignone (Graphite White) per l'asse 4 "
            "del manipolatore ABB IRB 8700. Trasmette la coppia al riduttore primario dell'asse polso; "
            "include resolver per il controllo di posizione. "
            "Variante colore ABB Orange: 3HAC049837-003. "
            "Documentazione ABB: Product manual spare parts 3HAC052854-001, sezione Motors."
        ),
        famiglia="Manipolatori ABB",
    ),
    "3HAC058951-003": build_description(
        title="Motore rotante asse 6 IRB 8700",
        code="3HAC058951-003",
        sigla="Motore asse 6",
        body=(
            "Servomotore in corrente alternata rotante con pignone (Graphite White) per l'asse 6 "
            "del manipolatore ABB IRB 8700. Aziona il polso finale del robot tramite il riduttore dell'asse 6; "
            "integra resolver per la misura di posizione. "
            "Variante colore ABB Orange: 3HAC049875-004. "
            "Documentazione ABB: Product manual spare parts 3HAC052854-001, sezione Motors."
        ),
        famiglia="Manipolatori ABB",
    ),
}

# Prodotti import distinta IRB 2600 (tools/import_irb2600_distinta.py)
IRB2600_IMPORTED_DESCRIPTIONS: dict[str, str] = {
    "3HAC065021-001": build_description(
        title="Unità rilascio freno DSQC1052 IRB 2600",
        code="3HAC065021-001",
        sigla="DSQC1052",
        body=(
            "Set brake release unit Type B per manipolatori IRB 2600 con motori Type B. "
            "Include scheda rilascio freno e cablaggio. "
            "Documentazione ABB: Product manual spare parts 3HAC049106-001 §11."
        ),
        famiglia="IRB 2600",
    ),
    "3HAC065020-001": build_description(
        title="Unità rilascio freno DSQC1050 IRB 2600ID",
        code="3HAC065020-001",
        sigla="DSQC1050",
        body=(
            "Set brake release unit Type A per IRB 2600ID (motori Type A assi 1–3). "
            "Documentazione ABB: Product manual spare parts 3HAC049106-001 §11."
        ),
        famiglia="IRB 2600ID",
    ),
    "3HAC094846-001": build_description(
        title="Cable harness basic IRB 2600",
        code="3HAC094846-001",
        sigla="Cable harness",
        body=(
            "Harness di cablaggio base del manipolatore ABB IRB 2600 (assi 1–6, base/telaio/braccio). "
            "Documentazione ABB: Product manual spare parts 3HAC049106-001 §11 Electrical connections."
        ),
        famiglia="IRB 2600",
    ),
    "3HAC094849-001": build_description(
        title="Cable harness braccio superiore IRB 2600ID",
        code="3HAC094849-001",
        sigla="Cable harness ID",
        body=(
            "Harness di cablaggio del braccio superiore per IRB 2600ID (process upper arm). "
            "Documentazione ABB: Product manual spare parts 3HAC049106-001 §11."
        ),
        famiglia="IRB 2600ID",
    ),
    "3HAC047586-002": build_description(
        title="Motore asse 1 IRB 2600 Type B",
        code="3HAC047586-002",
        sigla="Motore asse 1",
        body=(
            "Servomotore rotante Type B con pignone, colore Graphite White, asse 1 IRB 2600. "
            "Variante ABB Orange: 3HAC047586-003. Type A ALT: 3HAC034644-003. "
            "Documentazione ABB: 3HAC049106-001 §8 Motors."
        ),
        famiglia="IRB 2600",
    ),
    "3HAC047584-002": build_description(
        title="Motore asse 2 IRB 2600 Type B",
        code="3HAC047584-002",
        sigla="Motore asse 2",
        body=(
            "Servomotore rotante Type B con pignone, asse 2 IRB 2600. "
            "Documentazione ABB: 3HAC049106-001 §8 Motors."
        ),
        famiglia="IRB 2600",
    ),
    "3HAC047575-002": build_description(
        title="Motore asse 3 IRB 2600 Type B",
        code="3HAC047575-002",
        sigla="Motore asse 3",
        body=(
            "Servomotore rotante Type B con pignone, asse 3 IRB 2600. "
            "Documentazione ABB: 3HAC049106-001 §8 Motors."
        ),
        famiglia="IRB 2600",
    ),
    "3HAC047574-002": build_description(
        title="Motore assi 4–5 IRB 2600 Type B",
        code="3HAC047574-002",
        sigla="Motore assi 4–5",
        body=(
            "Servomotore rotante Type B con pignone per assi 4 e 5 IRB 2600 / 2600ID. "
            "Documentazione ABB: 3HAC049106-001 §8 Motors."
        ),
        famiglia="IRB 2600",
    ),
    "3HAC17342-1": build_description(
        title="Motore asse 6 IRB 2600",
        code="3HAC17342-1",
        sigla="Motore asse 6",
        body=(
            "Servomotore rotante Type B con pignone per asse 6 IRB 2600 e IRB 2600ID. "
            "Documentazione ABB: 3HAC049106-001 §8 Motors."
        ),
        famiglia="IRB 2600",
    ),
    "3HAC046277-001": build_description(
        title="Serial measurement unit IRB 2600",
        code="3HAC046277-001",
        sigla="SMB",
        body=(
            "Unità di misura seriale (Serial measurement unit) sul manipolatore IRB 2600. "
            "Alternativa a RMU101 3HAC044168-001. "
            "Documentazione ABB: 3HAC049106-001 §11."
        ),
        famiglia="IRB 2600",
    ),
}

# Prodotti import distinta IRB 6700 (tools/import_irb6700_distinta.py)
IRB6700_IMPORTED_DESCRIPTIONS: dict[str, str] = {
    "3HAC046642-001": build_description(
        title="Unità rilascio freno IRB 6700",
        code="3HAC046642-001",
        sigla="Brake release unit",
        body=(
            "Unità di rilascio freno montata nel recesso SMB/BU del manipolatore ABB IRB 6700. "
            "Consente il rilascio manuale dei freni degli assi tramite pulsanti 1–6. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §4.4.4."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC043118-001": build_description(
        title="Batteria SMB manipolatore IRB 6700",
        code="3HAC043118-001",
        sigla="Battery pack",
        body=(
            "Pacco batteria tampone per la scheda SMB (Serial Measurement Board) sul telaio "
            "del manipolatore ABB IRB 6700. Include circuiti di protezione; sostituire solo "
            "con ricambio ABB specificato. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §3.4.8."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC058040-001": build_description(
        title="Cablaggi assi 1–6 IRB 6700",
        code="3HAC058040-001",
        sigla="Cable harness",
        body=(
            "Harness di cablaggio elettrico del manipolatore ABB IRB 6700 (assi 1–6). "
            "Sostituisce i codici legacy 3HAC042840-001 e 3HAC069607-001. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §4.4; "
            "spare parts 3HAC044268-001."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC032586-001": build_description(
        title="Bleeder 2 kW IRC5",
        code="3HAC032586-001",
        sigla="Bleeder",
        body=(
            "Unità bleeder da 2 kW per controller IRC5 abbinato a robot medio/grandi "
            "(IRB 2600–7600, incluso IRB 6700). Dissipa energia rigenerativa in frenata. "
            "Documentazione ABB: Product manual IRC5 3HAC047136-001 §7.1 Controller parts."
        ),
        famiglia="IRC5",
    ),
    "3HAC068917-001": build_description(
        title="Cavo segnale robot 7 m",
        code="3HAC068917-001",
        sigla="Robot cable, signals",
        body=(
            "Cavo segnale robot schermato, lunghezza 7 m, per IRB 6700 su IRC5. "
            "Trasferisce dati resolver e alimentazione alla scheda SMB (R1.SMB). "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §2.6.1."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC055433-001": build_description(
        title="Motore asse 1 IRB 6700 Type B",
        code="3HAC055433-001",
        sigla="Motore asse 1",
        body=(
            "Servomotore rotante Type B per asse 1 del manipolatore ABB IRB 6700. "
            "Codice alternativo Type A: 3HAC045060-001. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §7.1; "
            "ordine spare parts 3HAC044268-001."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC055434-001": build_description(
        title="Motore asse 2 IRB 6700 Type B",
        code="3HAC055434-001",
        sigla="Motore asse 2",
        body=(
            "Servomotore rotante Type B per asse 2 del manipolatore ABB IRB 6700. "
            "Codice alternativo Type A: 3HAC045061-001. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §7.1."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC055435-001": build_description(
        title="Motore asse 3 IRB 6700 Type B",
        code="3HAC055435-001",
        sigla="Motore asse 3",
        body=(
            "Servomotore rotante Type B per asse 3 del manipolatore ABB IRB 6700. "
            "Codice alternativo Type A: 3HAC045063-001. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §7.1."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC055436-001": build_description(
        title="Motore assi 4–5 IRB 6700 Type B",
        code="3HAC055436-001",
        sigla="Motore assi 4–5",
        body=(
            "Servomotore rotante Type B per assi 4 e 5 del manipolatore ABB IRB 6700. "
            "Codice alternativo Type A: 3HAC045064-001. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §7.1."
        ),
        famiglia="IRB 6700",
    ),
    "3HAC055445-001": build_description(
        title="Motore asse 6 IRB 6700 Type B",
        code="3HAC055445-001",
        sigla="Motore asse 6",
        body=(
            "Servomotore rotante Type B per asse 6 del manipolatore ABB IRB 6700. "
            "Codice alternativo Type A: 3HAC045066-001. "
            "Documentazione ABB: Product manual IRB 6700 3HAC044266-001 §7.1."
        ),
        famiglia="IRB 6700",
    ),
}

# Prodotti import distinta IRB 4600 (tools/import_irb4600_distinta.py)
IRB4600_IMPORTED_DESCRIPTIONS: dict[str, str] = {
    "3HAC043964-001": build_description(
        title="Cable harness basic IRB 4600",
        code="3HAC043964-001",
        sigla="Cable harness",
        body=(
            "Harness di cablaggio base del manipolatore ABB IRB 4600 (assi 1–6). "
            "Variante aggiornata: 3HAC069651-001. "
            "Documentazione ABB: Product manual spare parts 3HAC049108-001."
        ),
        famiglia="IRB 4600",
    ),
    "3HAC069651-001": build_description(
        title="Cable harness basic IRB 4600 (rev.)",
        code="3HAC069651-001",
        sigla="Cable harness",
        body=(
            "Harness di cablaggio base IRB 4600 — revisione successiva a 3HAC043964-001. "
            "Documentazione ABB: 3HAC049108-001."
        ),
        famiglia="IRB 4600",
    ),
    "3HAC043166-004": build_description(
        title="Motore asse 1 IRB 4600",
        code="3HAC043166-004",
        sigla="Motore asse 1",
        body=(
            "Servomotore rotante con pignone, asse 1 IRB 4600 (Graphite White). "
            "Variante ABB Orange: 3HAC043166-005. "
            "Documentazione ABB: 3HAC049108-001."
        ),
        famiglia="IRB 4600",
    ),
    "3HAC029032-004": build_description(
        title="Motore asse 2 IRB 4600",
        code="3HAC029032-004",
        sigla="Motore asse 2",
        body=(
            "Servomotore rotante con pignone, asse 2 IRB 4600. "
            "Variante: 3HAC029032-009. Documentazione ABB: 3HAC049108-001."
        ),
        famiglia="IRB 4600",
    ),
    "3HAC043569-004": build_description(
        title="Motore asse 3 IRB 4600",
        code="3HAC043569-004",
        sigla="Motore asse 3",
        body=(
            "Servomotore rotante con pignone, asse 3 IRB 4600. "
            "Documentazione ABB: 3HAC049108-001."
        ),
        famiglia="IRB 4600",
    ),
    "3HAC029034-004": build_description(
        title="Motore assi 4–5 IRB 4600",
        code="3HAC029034-004",
        sigla="Motore wrist",
        body=(
            "Servomotore rotante con pignone per assi 4–5 del polso IRB 4600. "
            "Asse 6: 3HAC029034-006. Ricostruzione polso: 3HAC030211-004. "
            "Documentazione ABB: 3HAC049108-001."
        ),
        famiglia="IRB 4600",
    ),
    "3HAC029034-006": build_description(
        title="Motore asse 6 IRB 4600",
        code="3HAC029034-006",
        sigla="Motore asse 6",
        body=(
            "Servomotore rotante con pignone, asse 6 IRB 4600. "
            "Documentazione ABB: 3HAC049108-001."
        ),
        famiglia="IRB 4600",
    ),
}

# Prodotti import distinta IRB 6660 (tools/import_irb6660_distinta.py)
IRB6660_IMPORTED_DESCRIPTIONS: dict[str, str] = {
    "3HAC044259-001": build_description(
        title="Cable harness IRB 6660 press",
        code="3HAC044259-001",
        sigla="Cable harness",
        body=(
            "Harness completo manipolatore IRB 6660-100/3.3 e IRB 6660-130/3.1. "
            "Documentazione ABB: Product manual spare parts 3HAC049112-001 §1."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC028715-001": build_description(
        title="Cable harness IRB 6660-205/1.9",
        code="3HAC028715-001",
        sigla="Cable harness",
        body=(
            "Harness completo manipolatore IRB 6660-205/1.9 (pre-machining). "
            "Documentazione ABB: 3HAC049112-001 §1."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC058993-003": build_description(
        title="Motore asse 1 IRB 6660",
        code="3HAC058993-003",
        sigla="Motore asse 1",
        body=(
            "Servomotore rotante con pignone, asse 1 IRB 6660 (ABB Orange). "
            "Variante Graphite White: 3HAC058993-004. "
            "Documentazione ABB: 3HAC049112-001 §2."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC058991-003": build_description(
        title="Motore assi 2–3 IRB 6660",
        code="3HAC058991-003",
        sigla="Motore assi 2–3",
        body=(
            "Servomotore rotante con pignone per assi 2 e 3 IRB 6660. "
            "Variante: 3HAC058991-004. Documentazione ABB: 3HAC049112-001."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC057547-003": build_description(
        title="Motore asse 4 IRB 6660",
        code="3HAC057547-003",
        sigla="Motore asse 4",
        body=(
            "Servomotore rotante con pignone, asse 4 IRB 6660. "
            "Variante: 3HAC057547-004. Documentazione ABB: 3HAC049112-001 §9."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC058994-003": build_description(
        title="Motore asse 6 IRB 6660",
        code="3HAC058994-003",
        sigla="Motore asse 6",
        body=(
            "Servomotore rotante con pignone, asse 6 IRB 6660-100/3.3 e 130/3.1. "
            "Variante 205/1.9: 3HAC057549-003. Documentazione ABB: 3HAC049112-001 §9."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC057549-003": build_description(
        title="Motore asse 6 IRB 6660-205/1.9",
        code="3HAC057549-003",
        sigla="Motore asse 6",
        body=(
            "Servomotore rotante con pignone, asse 6 IRB 6660-205/1.9. "
            "Variante: 3HAC057549-004. Documentazione ABB: 3HAC049112-001 §9."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC055547-003": build_description(
        title="Wrist unit IRB 6660 (asse 5)",
        code="3HAC055547-003",
        sigla="Wrist unit asse 5",
        body=(
            "Polso completo IRB 6660-100/3.3 e 130/3.1 (Axis Calibration). "
            "Il motore asse 5 non è ricambio singolo in 3HAC049112-001; "
            "sostitutivo per guasto asse 5 §4.6.4 product manual. "
            "Variante Graphite White: 3HAC055547-004."
        ),
        famiglia="IRB 6660",
    ),
    "3HAC058127-005": build_description(
        title="Wrist unit IRB 6660-205/1.9 (asse 5)",
        code="3HAC058127-005",
        sigla="Wrist unit asse 5",
        body=(
            "Polso completo IRB 6660-205/1.9 foundry (Axis Calibration). "
            "Motore asse 5 non disponibile come ricambio singolo; "
            "wrist unit §9 item 6 — 3HAC049112-001. Variante: 3HAC058127-006."
        ),
        famiglia="IRB 6660",
    ),
}

# Prodotti import distinta IRB 6600 IRC5 (tools/import_irb6600_irc5_distinta.py)
IRB6600_IMPORTED_DESCRIPTIONS: dict[str, str] = {
    "3HAC024385-001": build_description(
        title="Cable harness assi 1-6 IRB 6600",
        code="3HAC024385-001",
        sigla="Cable harness",
        body=(
            "Harness completo manipolatore IRB 6600, assi 1-6 (cablaggio non diviso). "
            "Alternativa divisa: 3HAC025503-001 (1-4) + 3HAC14140-001 (5-6). "
            "Documentazione ABB: Product manual 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC025503-001": build_description(
        title="Cable harness assi 1-4 IRB 6600",
        code="3HAC025503-001",
        sigla="Cable harness 1-4",
        body=(
            "Harness manipolatore IRB 6600, divisione al braccio superiore (assi 1-4). "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC14140-001": build_description(
        title="Cable harness assi 5-6 IRB 6600",
        code="3HAC14140-001",
        sigla="Cable harness 5-6",
        body=(
            "Harness manipolatore IRB 6600, assi 5-6 (parte polso). "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC16014-001": build_description(
        title="SMB unit IRB 6600",
        code="3HAC16014-001",
        sigla="SMB",
        body=(
            "Serial Measurement Board (unità di misura) sul manipolatore IRB 6600. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC15879-002": build_description(
        title="Motore asse 1 IRB 6600",
        code="3HAC15879-002",
        sigla="Motore asse 1",
        body=(
            "Motore rotante con pignone, asse 1 IRB 6600. "
            "Variante Foundry Prime: 3HAC15879-003. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC15879-2": build_description(
        title="Motore asse 1 IRB 6600",
        code="3HAC15879-2",
        sigla="Motore asse 1",
        body=(
            "Motore rotante con pignone, asse 1 IRB 6600. "
            "Variante Foundry Prime: 3HAC15879-3. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC15879-3": build_description(
        title="Motore asse 1 IRB 6600 (Foundry Prime)",
        code="3HAC15879-3",
        sigla="Motore asse 1 Foundry Prime",
        body=(
            "Motore rotante con pignone, asse 1 IRB 6600 — protezione Foundry Prime. "
            "Codice standard: 3HAC15879-2. Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC021030-001": build_description(
        title="Motore asse 2 IRB 6600",
        code="3HAC021030-001",
        sigla="Motore asse 2",
        body=(
            "Motore rotante con pignone, asse 2 IRB 6600. "
            "Variante Foundry Prime: 3HAC026975-001. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC15885-002": build_description(
        title="Motore asse 3 IRB 6600",
        code="3HAC15885-002",
        sigla="Motore asse 3",
        body=(
            "Motore rotante con pignone, asse 3 IRB 6600. "
            "Variante Foundry Prime: 3HAC026976-001. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC15889-002": build_description(
        title="Motore asse 4 IRB 6600",
        code="3HAC15889-002",
        sigla="Motore asse 4",
        body=(
            "Motore rotante con pignone, asse 4 IRB 6600. "
            "Variante Foundry Prime: 3HAC026977-001. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC17484-010": build_description(
        title="Motore asse 5 IRB 6600",
        code="3HAC17484-010",
        sigla="Motore asse 5",
        body=(
            "Motore rotante M10, asse 5 IRB 6600. "
            "Variante Foundry Prime: 3HAC026982-001. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
    "3HAC15991-004": build_description(
        title="Motore asse 6 IRB 6600",
        code="3HAC15991-004",
        sigla="Motore asse 6",
        body=(
            "Motore rotante con pignone, asse 6 IRB 6600. "
            "Variante Foundry Prime: 3HAC026983-001. "
            "Documentazione ABB: 3HAC023082-001 §9.2.2."
        ),
        famiglia="IRB 6600",
    ),
}

# Alias retrocompatibilità
IRB8700_SCHEDE_DESCRIPTIONS = IRB8700_IMPORTED_DESCRIPTIONS

# DSQC 668 — codice alternativo (stessa board del 3HAC029157-001)
CATALOG_DESCRIPTIONS: dict[str, str] = {
    **IRB8700_IMPORTED_DESCRIPTIONS,
    **IRB2600_IMPORTED_DESCRIPTIONS,
    **IRB6700_IMPORTED_DESCRIPTIONS,
    **IRB4600_IMPORTED_DESCRIPTIONS,
    **IRB6660_IMPORTED_DESCRIPTIONS,
    **IRB6600_IMPORTED_DESCRIPTIONS,
    "3HAC028179-001": build_description(
        title="Computer dell'asse ABB DSQC 668 (codice alternativo)",
        code="3HAC028179-001",
        sigla="DSQC 668",
        body=(
            "Computer dell'asse (Axis Computer) del controller ABB IRC5 — stessa funzione "
            "e sigla elettronica DSQC 668 del codice primario <strong>3HAC029157-001</strong>. "
            "In distinta Gamma è indicato come alternativa (ALT) allo stesso slot "
            "&laquo;Axis Computer&raquo;. I due codici ABB sono ricambi intercambiabili "
            "sulla stessa applicazione IRC5; per ordini nuovi ABB indica preferibilmente 3HAC029157-001."
        ),
        famiglia="IRC5",
    ),
}
