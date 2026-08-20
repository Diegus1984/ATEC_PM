-- ═══════════════════════════════════════════════════════════════════════════
-- PROPOSTA DI ASSEGNAZIONE  fase di anagrafica  ->  sezione di costo
-- Segnalazione #42 · preparata il 06/08/2026
--
-- ✅ GIA' APPLICATA IN PRODUZIONE il 07/08/2026 su richiesta di Diego. NON RIESEGUIRE:
--    gli UPDATE sono idempotenti, ma il file resta come traccia di cosa e' stato deciso.
--    Backup di sicurezza: C:\ATEC_Backups\atec_pm_prima_fasi_sezioni_20260806.sql
--
--    Creata anche la sezione mancante «Ufficio Acquisti» (id 74, IN_SEDE, gruppo GESTIONE,
--    reparto ACQ), quindi le fasi 33/34/35 sono state assegnate e NON sono piu' in sospeso.
--
--    Esito: da 37 fasi scollegate (25 is_default) a 14 (2 is_default). Le 2 rimaste sono
--    la 22 e la 39, che aspettano una decisione di Paolo — vedi in fondo.
--    Verificato sulla commessa C260805.500: le 8 ore di «Project Management» sono passate
--    da «Fasi senza sezione costo» a «Program Manager» (360 -> 720 EUR su quella sezione),
--    e NESSUN totale si e' mosso. E' una riattribuzione, non un cambio di importi.
-- ═══════════════════════════════════════════════════════════════════════════
--
-- PERCHE' SERVE
-- Delle 54 fasi in anagrafica, 37 non hanno una sezione di costo: le ore imputate
-- su quelle fasi non si possono attribuire ne' a «in sede» ne' a «da cliente», e nel
-- Bilancio restano fuori dalla ripartizione. Qui ci sono le 25 che pesano davvero,
-- cioe' quelle `is_default` che nascono su OGNI commessa nuova (17 commesse a testa).
--
-- DA DOVE VENGONO QUESTI ACCOPPIAMENTI — non sono inventati
-- 21 delle 25 avevano gia' una sezione, persa quando le sezioni di costo sono state
-- ricreate da zero (le fasi puntavano agli id 1..12, le sezioni di oggi partono da 41:
-- e' lo stesso incidente bonificato con la migrazione v70).
-- La mappatura ORIGINALE e' stata recuperata dal backup `atec_pm_manual_20260324_095548.sql`
-- e tradotta nelle sezioni di oggi. Dove il backup non aiuta, la proposta e' marcata.
--
-- COME LEGGERLA
--   [ORIGINALE] = era cosi' prima, ricostruito dal backup. Alta confidenza.
--   [PROPOSTA]  = non c'era, l'ho deciso io per analogia. Da confermare.
--   [DA DECIDERE] = in fondo, commentate: non hanno una casa evidente. Servono a Paolo.
--
-- COME APPLICARLA
--   1. Paolo legge e corregge.
--   2. Si esegue sul DB di PRODUZIONE dopo un backup.
--   3. Si controlla il conteggio finale in fondo.
-- Nessuna riga tocca dati di commessa: si scrive solo `phase_templates`, che e' anagrafica.
-- Le ore gia' imputate non si spostano, cambia come vengono RIPARTITE da qui in avanti.

START TRANSACTION;

-- ── GESTIONE · Progettazione Ufficio Tecnico Meccanico (id 44, SEDE) ──────────
UPDATE phase_templates SET cost_section_template_id = 44 WHERE id = 27; -- [ORIGINALE] Progettazione 3D
UPDATE phase_templates SET cost_section_template_id = 44 WHERE id = 28; -- [ORIGINALE] Messa in tavola
UPDATE phase_templates SET cost_section_template_id = 44 WHERE id = 29; -- [ORIGINALE] Distinta base
UPDATE phase_templates SET cost_section_template_id = 44 WHERE id = 31; -- [ORIGINALE] Studio layout

-- ── GESTIONE · Progettazione Ufficio Tecnico Elettrico (id 45, SEDE) ─────────
UPDATE phase_templates SET cost_section_template_id = 45 WHERE id = 1;  -- [ORIGINALE] Progettazione Elettrica

-- ── GESTIONE · Robot Studio - Cella Simulazioni (id 42, SEDE) ────────────────
UPDATE phase_templates SET cost_section_template_id = 42 WHERE id = 23; -- [ORIGINALE] Simulazione RobotStudio

-- ── GESTIONE · Program Manager (id 73, SEDE) ─────────────────────────────────
UPDATE phase_templates SET cost_section_template_id = 73 WHERE id = 37; -- [ORIGINALE] Project Management

-- ── GESTIONE · Sviluppo SW Back Office (id 54, SEDE) ─────────────────────────
-- [PROPOSTA] Le tre programmazioni non hanno mai avuto una sezione. Sono lavoro di
-- software fatto in ufficio, prima di andare in campo: questa e' l'unica sezione che
-- le rappresenta. Se in ATEC la programmazione PLC/HMI/Safety e' considerata parte
-- del preschieramento, vanno invece sulla 56 (Commissioning PLC/HMI, SITO PILOTA).
UPDATE phase_templates SET cost_section_template_id = 54 WHERE id = 15; -- [PROPOSTA] Programmazione PLC
UPDATE phase_templates SET cost_section_template_id = 54 WHERE id = 16; -- [PROPOSTA] Programmazione HMI
UPDATE phase_templates SET cost_section_template_id = 54 WHERE id = 17; -- [PROPOSTA] Programmazione Safety

-- ── SITO PILOTA · Allestimento Meccanico / Elettrico (id 55, SEDE) ───────────
-- Tutte queste stavano su «ATEC INSTALLATORI IN_SEDE», che nella struttura nuova
-- corrisponde al gruppo SITO PILOTA. La sezione piu' vicina e' l'allestimento.
UPDATE phase_templates SET cost_section_template_id = 55 WHERE id = 2;  -- [ORIGINALE] Cablaggio quadro elettrico
UPDATE phase_templates SET cost_section_template_id = 55 WHERE id = 3;  -- [ORIGINALE] Montaggio elettrico IN ATEC
UPDATE phase_templates SET cost_section_template_id = 55 WHERE id = 4;  -- [ORIGINALE] Preinstallazione elettrica IN ATEC
UPDATE phase_templates SET cost_section_template_id = 55 WHERE id = 6;  -- [ORIGINALE] Collaudo Hardware
UPDATE phase_templates SET cost_section_template_id = 55 WHERE id = 38; -- [ORIGINALE] Collaudo finale IN ATEC

-- ── SITO PILOTA · Commissioning PLC / HMI (id 56, SEDE) ──────────────────────
-- [PROPOSTA] Non aveva sezione, ma il suo gemello «in CANTIERE» stava su
-- ATEC COMMISSIONING lato cliente: qui la versione in sede.
UPDATE phase_templates SET cost_section_template_id = 56 WHERE id = 18; -- [PROPOSTA] Commissioning PLC IN ATEC

-- ── SITO PILOTA · Commissioning Robot (id 57, SEDE) ──────────────────────────
-- L'originale diceva «ATEC INSTALLATORI IN_SEDE» (troppo generico: allora non
-- esisteva una sezione commissioning in sede). Oggi esiste ed e' il posto giusto.
UPDATE phase_templates SET cost_section_template_id = 57 WHERE id = 24; -- [ORIGINALE, affinato] Commissioning Robot IN ATEC

-- ── INSTALLAZIONE CLIENTE · Installazione Meccanica / Elettrica (id 60) ──────
UPDATE phase_templates SET cost_section_template_id = 60 WHERE id = 5;  -- [ORIGINALE] Installazione elettrica in CANTIERE

-- ── INSTALLAZIONE CLIENTE · Commissioning PLC / HMI (id 61) ──────────────────
UPDATE phase_templates SET cost_section_template_id = 61 WHERE id = 19; -- [ORIGINALE] Commissioning PLC in CANTIERE

-- ── INSTALLAZIONE CLIENTE · Commissioning Robot (id 62) ──────────────────────
UPDATE phase_templates SET cost_section_template_id = 62 WHERE id = 25; -- [ORIGINALE] Commissioning Robot in CANTIERE

COMMIT;

-- ── RISOLTA il 07/08/2026: creata la sezione «Ufficio Acquisti» (id 74) ─────
-- Fra le 22 sezioni non ne esisteva una per gli acquisti: erano tutte tecniche o di
-- cantiere, mentre il reparto ACQ esisteva gia' in anagrafica reparti. Creata come
-- IN_SEDE nel gruppo GESTIONE, collegata al reparto ACQ (45,00 EUR/h, K 1,450) come
-- fanno le altre sezioni con il loro reparto.
UPDATE phase_templates SET cost_section_template_id = 74 WHERE id = 33; -- Richiesta offerte fornitori
UPDATE phase_templates SET cost_section_template_id = 74 WHERE id = 34; -- Emissione ordini
UPDATE phase_templates SET cost_section_template_id = 74 WHERE id = 35; -- Solleciti e tracking consegne

-- ═══════════════════════════════════════════════════════════════════════════
-- LE 2 CHE RESTANO — servono a Paolo, non le indovino
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 39  Collaudo finale in CANTIERE
--     Il gemello «IN ATEC» va su Allestimento (55). Lato cliente pero' non c'e' nessuna
--     sezione «collaudo»: le quattro di INSTALLAZIONE CLIENTE sono Coordinamento,
--     Installazione, Commissioning PLC/HMI, Commissioning Robot. Candidata piu' probabile
--     la 59 (Coordinamento Attivita' / Capo Cantiere, CLIENTE), ma e' una forzatura.
--
-- ── UN DUBBIO SU UNA CHE HO ASSEGNATO ────────────────────────────────────────
-- 22  Programmazione Robot -> l'originale la metteva su ATEC COMMISSIONING **lato
--     cliente**, il che e' strano per un lavoro d'ufficio: la gemella «Simulazione
--     RobotStudio» stava su Robot Studio, in sede. Sospetto un errore gia' nel dato
--     vecchio. NON l'ho inclusa negli UPDATE sopra: decidere fra
--       42 (Robot Studio - Cella Simulazioni, SEDE)  <- piu' logica
--       62 (Commissioning Robot, CLIENTE)            <- fedele all'originale
-- UPDATE phase_templates SET cost_section_template_id = 42 WHERE id = 22;

-- ═══════════════════════════════════════════════════════════════════════════
-- CONTROLLO — da eseguire dopo
-- ═══════════════════════════════════════════════════════════════════════════
SELECT 'fasi ancora senza sezione' AS controllo, COUNT(*) AS quante
FROM phase_templates pt
LEFT JOIN cost_section_templates cst ON cst.id = pt.cost_section_template_id
WHERE cst.id IS NULL;

SELECT 'di cui is_default (le pesanti)' AS controllo, COUNT(*) AS quante
FROM phase_templates pt
LEFT JOIN cost_section_templates cst ON cst.id = pt.cost_section_template_id
WHERE cst.id IS NULL AND pt.is_default = 1;

-- Prima:  37 fasi senza sezione, di cui 25 is_default.
-- Questo file ne assegna 20 (tutte is_default).
-- Atteso dopo: 17 fasi senza sezione, di cui **5 is_default** — la 22 (lasciata commentata
-- apposta), la 33, la 34, la 35 (acquisti, manca la sezione) e la 39 (collaudo in cantiere).
-- Le altre 12 non sono is_default: non nascono sulle commesse nuove e quasi nessuna e' usata,
-- alcune vanno probabilmente cancellate invece che assegnate.
