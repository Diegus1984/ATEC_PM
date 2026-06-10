"""
Testi descrizione manipolatore ABB per catalogo Gamma Ricambi (cella 0,0 tabella 2x2).
Fonti: pagine prodotto ABB, product specification e datasheet pubblici.
"""

ROBOT_WEB_DESCRIPTIONS: dict[str, str] = {
    # --- già curati in prima passata ---
    "IRB 5710": (
        "Robot articolato a 6 assi ad alte prestazioni per movimentazione materiali, machine tending e assemblaggio "
        "di precisione. Varianti payload 70-110 kg, reach 2,3-2,7 m; cablaggi LeanID integrati. Fonte: ABB IRB 5710."
    ),
    "IRB 5720": (
        "Robot articolato a 6 assi per material handling e assemblaggio di precisione. Varianti payload 90-180 kg, "
        "reach 2,6-3,0 m; controller OmniCore V250XT e LeanID. Fonte: ABB IRB 5720."
    ),
    "IRB 6600": (
        "Robot articolato a 6 assi per processi industriali: spot welding, material handling, machine tending. "
        "Famiglia con versioni 175-225 kg e reach 2,55-2,8 m. Fonte: ABB 3HAC023933-001."
    ),
    "IRB 6700": (
        "Robot a 6 assi (7a generazione ABB) per alto payload: material handling, machine tending, spot welding. "
        "Varianti 150-300 kg, reach fino a 3,2 m; robusto e a bassa manutenzione. Fonte: ABB 3HAC044265."
    ),
    "IRB 8700": (
        "Robot heavy-payload a 6 assi (8a generazione): fino a 800 kg (1000 kg polso in giu), reach fino a 4,2 m. "
        "Design semplificato per alta affidabilita e velocita superiore nella classe. Fonte: ABB IRB 8700."
    ),
    "IRB 910": (
        "Famiglia SCARA a 4 assi per assemblaggio e pick-and-place ad alta velocita. Varianti tabletop (910SC) e "
        "invertite a soffitto (910INV) per celle compatte. Fonte: ABB 3HAC056431 / IRB 910INV."
    ),
    "IRB 6640": (
        "Robot a 6 assi (modello dismesso) per applicazioni medio-pesanti: material handling, spot welding, cleaning. "
        "Payload fino a 235 kg, reach fino a 3,2 m; protezione Foundry Prime disponibile. Fonte: ABB IRB 6640."
    ),
    "IRB 6660": (
        "Robot a 6 assi per press tending e pre-machining. Struttura rigida a parallelogramma per accuratezza e cicli "
        "brevi; versioni per press tending e lavorazioni (milling, deburring, grinding). Fonte: ABB IRB 6660."
    ),
    "IRB 6400R": (
        "Robot a 6 assi legacy per automazione flessibile in applicazioni pesanti. Payload fino a 200 kg, reach fino "
        "a 3 m; struttura aperta per integrazione periferiche. Fonte: ABB IRB 6400R product manual."
    ),

    # --- OmniCore compatti / nuova generazione ---
    "IRB 1010": (
        "Robot compatto a 6 assi per assemblaggio, pick-and-place e machine tending in spazi ridotti. Payload 1,5 kg, "
        "reach 0,37 m; pensato per elettronica e automazione generale con controller OmniCore. Fonte: ABB IRB 1010."
    ),
    "IRB 1090": (
        "Robot compatto a 6 assi per assemblaggio e movimentazione componenti piccoli. Payload 3,5 kg, reach 0,58 m; "
        "alta ripetibilita e integrazione OmniCore per linee dense. Fonte: ABB IRB 1090."
    ),
    "IRB 1200 Gen2": (
        "Seconda generazione del robot compatto IRB 1200: 6 assi per material handling, machine tending e assemblaggio. "
        "Payload 5-7 kg, reach 0,7-0,9 m; controller OmniCore e design per celle compatte. Fonte: ABB IRB 1200 Gen2."
    ),
    "IRB 1300": (
        "Robot compatto ad alte prestazioni a 6 assi per material handling, machine tending e assemblaggio. "
        "Payload 7-10 kg, reach 0,9-1,4 m; movimenti rapidi e footprint ridotto con OmniCore. Fonte: ABB IRB 1300."
    ),
    "IRB 14000": (
        "Robot micron a 6 assi per assemblaggio e handling di componenti miniaturizzati. Payload 0,5 kg, reach 0,5 m; "
        "progettato per elettronica, medical device e applicazioni ad altissima precisione. Fonte: ABB IRB 14000."
    ),
    "IRB 1520ID": (
        "Robot a 6 assi con dressing integrato (ID) per operazioni in spazi ristretti. Payload 4 kg, reach 1,5 m; "
        "ideale per machine tending e assemblaggio con cablaggi protetti. Fonte: ABB IRB 1520ID."
    ),
    "IRB 1660ID": (
        "Robot a 6 assi con Integrated Dresspack per machine tending e assemblaggio. Payload 4 kg, reach 1,55 m; "
        "cavi integrati nel braccio per maggiore affidabilita. Fonte: ABB IRB 1660ID."
    ),
    "IRB 390": (
        "Robot a 4 assi per palletizzazione ad alta velocita. Payload 15 kg, reach 1,3 m; struttura compatta per "
        "fine linea e imballaggio con controller OmniCore. Fonte: ABB IRB 390."
    ),
    "IRB 920": (
        "Robot SCARA a 4 assi per pick-and-place e assemblaggio ad alta velocita. Payload 12 kg, reach 0,85 m; "
        "controller OmniCore per linee ad alta densita. Fonte: ABB IRB 920."
    ),
    "IRB 930": (
        "Robot SCARA a 4 assi per movimentazione rapida di componenti medi. Payload 22 kg, reach 1,2 m; "
        "adatto a packaging, assemblaggio e machine tending. Fonte: ABB IRB 930."
    ),

    # --- IRC5 compatti / medi ---
    "IRB 120": (
        "Robot compatto a 6 assi per applicazioni leggere: pick-and-place, assemblaggio, machine tending. "
        "Payload 3 kg, reach 0,58 m; compatibile IRC5. Fonte: ABB IRB 120."
    ),
    "IRB 1200": (
        "Robot compatto a 6 assi versatile per material handling, machine tending e assemblaggio. "
        "Payload 5-7 kg, reach 0,7-0,9 m; footprint ridotto e controller IRC5. Fonte: ABB IRB 1200."
    ),
    "IRB 2600": (
        "Robot a 6 assi di medie dimensioni per material handling, machine tending, assemblaggio e spot welding. "
        "Payload 12-20 kg, reach 1,65-1,85 m; TrueMove/QuickMove su IRC5. Fonte: ABB IRB 2600."
    ),
    "IRB 2600ID": (
        "Robot a 6 assi con Integrated Dresspack per operazioni in spazi ristretti. Payload 8-15 kg, reach fino a 2 m; "
        "cavi protetti nel braccio superiore. Fonte: ABB IRB 2600ID."
    ),
    "IRB 4600": (
        "Robot a 6 assi per material handling, machine tending e assemblaggio. Payload 20-60 kg, reach 2,05-2,55 m; "
        "bilanciamento tra velocita, precisione e compattezza. Fonte: ABB IRB 4600."
    ),
    "IRB 4600 Type C": (
        "Variante Type C della famiglia IRB 4600: robot a 6 assi per handling e assemblaggio. "
        "Stesso range payload/reach della serie 4600 con configurazione meccanica Type C. Fonte: ABB IRB 4600."
    ),
    "IRB 460": (
        "Robot palletizzatore a 4 assi per fine linea e imballaggio ad alta velocita. Payload 110 kg, reach 2,4 m; "
        "struttura compatta per palletizing con IRC5. Fonte: ABB IRB 460."
    ),
    "IRB 360": (
        "Robot parallelo FlexPicker a 4 assi per pick-and-place ad altissima velocita. Payload 1-3 kg; "
        "ideale per food, pharma e elettronica. Fonte: ABB IRB 360 FlexPicker."
    ),

    # --- IRC5 / legacy medi-grandi ---
    "IRB 1600 Type A": (
        "Robot a 6 assi versatile per material handling, machine tending, assemblaggio e saldatura. "
        "Payload 4-10 kg, reach 1,2-1,45 m; una delle famiglie piu diffuse in automazione generale. Fonte: ABB IRB 1600."
    ),
    "IRB 1600 Type A/Type 0": (
        "Variante Type A/Type 0 del robot IRB 1600: 6 assi per handling e machine tending. "
        "Configurazione meccanica Type 0 con controller IRC5/M2004. Fonte: ABB IRB 1600."
    ),
    "IRB 1600 Type A/Type0": (
        "Variante Type A/Type0 del robot IRB 1600: 6 assi per handling e machine tending. "
        "Configurazione meccanica Type 0 con controller IRC5/M2004. Fonte: ABB IRB 1600."
    ),
    "IRB 2400": (
        "Robot a 6 assi robusto per material handling, machine tending, saldatura e processi industriali. "
        "Payload 10-16 kg, reach 1,55 m; famiglia storica ABB con ampia base installata. Fonte: ABB IRB 2400."
    ),
    "IRB 2400L": (
        "Variante long-reach della famiglia IRB 2400: 6 assi per handling e machine tending. "
        "Reach esteso fino a 1,8 m mantenendo la robustezza della serie. Fonte: ABB IRB 2400L."
    ),
    "IRB 4400": (
        "Robot a 6 assi per applicazioni pesanti: material handling, machine tending, spot welding. "
        "Payload fino a 60 kg; famiglia legacy con ampia diffusione in automotive. Fonte: ABB IRB 4400."
    ),
    "IRB 4400L": (
        "Variante long-reach della famiglia IRB 4400: 6 assi per handling e processi industriali. "
        "Reach esteso per operazioni su macchine e linee ampie. Fonte: ABB IRB 4400L."
    ),
    "IRB 6600 Type B": (
        "Revisione Type B della famiglia IRB 6600: 6 assi per spot welding, material handling e machine tending. "
        "Meccanica M2004 con controller IRC5; payload 175-225 kg, reach 2,55-2,8 m. Fonte: ABB IRB 6600 Type B."
    ),
    "IRB 6620": (
        "Robot a 6 assi per press tending e material handling. Payload 150 kg, reach 2,2 m; "
        "progettato per cicli rapidi in press automation. Fonte: ABB IRB 6620."
    ),
    "IRB 6650S": (
        "Robot a 6 assi shelf-mounted per applicazioni con montaggio a mensola. Payload 90-125 kg, reach fino a 3,9 m; "
        "ideale per linee compatte e press tending. Fonte: ABB IRB 6650S."
    ),
    "IRB 7600": (
        "Robot a 6 assi per carichi molto pesanti: material handling, palletizing, machine tending. "
        "Payload 150-500 kg, reach 2,3-3,5 m; struttura robusta per industria pesante. Fonte: ABB IRB 7600."
    ),

    # --- OmniCore famiglia 6700+ ---
    "IRB 6710": (
        "Robot a 6 assi di nuova generazione (successore IRB 6700) per alto payload. Payload 150-210 kg, reach 2,65-2,95 m; "
        "motion control OmniCore e design per alta produttivita. Fonte: ABB IRB 6710."
    ),
    "IRB 6720": (
        "Robot a 6 assi ad alte prestazioni per material handling e machine tending. Payload 170-240 kg, reach 2,65-3,1 m; "
        "controller OmniCore e struttura rinforzata. Fonte: ABB IRB 6720."
    ),
    "IRB 6730": (
        "Robot a 6 assi per applicazioni ad alto payload in automotive e industria generale. Payload 210-270 kg, "
        "reach 2,7-3,1 m; OmniCore e manutenzione semplificata. Fonte: ABB IRB 6730."
    ),
    "IRB 6730S": (
        "Variante shelf-mounted della famiglia IRB 6730: 6 assi per montaggio a mensola. Payload 210-270 kg, "
        "reach 2,7-3,1 m; ideale per celle compatte. Fonte: ABB IRB 6730S."
    ),
    "IRB 6740": (
        "Robot a 6 assi top di gamma nella famiglia 6700+ per carichi pesanti. Payload 240-310 kg, reach 2,8-3,2 m; "
        "OmniCore e alta rigidita strutturale. Fonte: ABB IRB 6740."
    ),
    "IRB 6750S": (
        "Robot a 6 assi shelf-mounted per press tending e handling pesante. Payload 240-260 kg, reach 3,0-3,2 m; "
        "montaggio a mensola per massimizzare lo spazio cella. Fonte: ABB IRB 6750S."
    ),
    "IRB 6760": (
        "Robot a 6 assi per press tending ad altissima velocita (fino a 900 pezzi/ora). Payload 200 kg, reach 2,8 m; "
        "ottimizzato per automazione stampi con OmniCore. Fonte: ABB IRB 6760."
    ),
    "IRB 6790": (
        "Robot a 6 assi per press tending e material handling pesante. Payload 205 kg, reach 2,8 m; "
        "famiglia OmniCore per automotive e metal forming. Fonte: ABB IRB 6790."
    ),
    "IRB 7710": (
        "Robot a 6 assi heavy-duty di nuova generazione per carichi estremi. Payload 280-500 kg, reach 2,85-3,1 m; "
        "OmniCore e design per alta uptime in ambienti gravosi. Fonte: ABB IRB 7710."
    ),
    "IRB 7720": (
        "Robot a 6 assi per i carichi piu pesanti nella gamma OmniCore. Payload 400-620 kg, reach 2,9-3,5 m; "
        "per material handling, palletizing e processi industriali pesanti. Fonte: ABB IRB 7720."
    ),

    # --- Legacy S4C ---
    "IRB 140": (
        "Robot compatto a 6 assi legacy per pick-and-place, assemblaggio e machine tending. "
        "Famiglia storica ABB con controller S4C+/M2000. Fonte: ABB IRB 140."
    ),
    "IRB 140 Type C": (
        "Variante Type C del robot compatto IRB 140: 6 assi per handling leggero. "
        "Configurazione meccanica Type C, compatibile IRC5/M2004. Fonte: ABB IRB 140."
    ),
    "IRB 1400": (
        "Robot a 6 assi legacy per material handling, machine tending e assemblaggio. "
        "Famiglia storica con ampia installazione; controller S4C+/M2000. Fonte: ABB IRB 1400."
    ),
    "IRB 6400": (
        "Robot a 6 assi legacy per automazione industriale flessibile. Payload 75-120 kg, reach 2,4-3 m; "
        "famiglia storica con controller S4C. Fonte: ABB IRB 6400."
    ),
    "IRB 6400S": (
        "Variante extended-reach della famiglia IRB 6400: 6 assi per handling pesante. "
        "Payload 120 kg, reach fino a 2,9 m. Fonte: ABB IRB 6400S."
    ),
}
