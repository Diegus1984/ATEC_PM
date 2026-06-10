# Import distinta Gamma Robot da manuali ABB (runbook)

Guida per ripetere il lavoro fatto su **IRB 8700** su altri robot/quadri.

## Prerequisiti

- Python 3 + `pymysql` (`pip install pymysql`)
- MySQL locale con DB `atec_pm` (credenziali negli script: `localhost`, user `root`)
- Robot e quadro già presenti in `gamma_robot` / `gamma_quadro` (id quadro da usare negli script)

Esegui sempre dalla **root del repo**:

```powershell
cd C:\Users\diego\Desktop\ATEC_PM_CSharp_v5\ATEC_PM
```

---

## Fase 1 — Ricerca online (manualistica ABB)

Per ogni robot cercare:

| Cosa | Documento tipico | Dove |
|------|------------------|------|
| Ricambi manipolatore | Product manual, Spare parts `3HACxxxxxx-001` | [ManualsLib](https://www.manualslib.com) o ABB Library |
| Schede quadro IRC5 | IRC5 spare parts `3HAC047136-001` §7.1 Controller parts | ABB Library |
| Cavi | Stesso manuale IRC5 §7.3 Manipulator cables | — |

Annotare codici `3HAC*` / `DSQC*` per sezioni: **Schede**, **Azionamenti**, **Kit Cavi**, **Motori**, **Ventole**.

Confrontare con un robot **della stessa famiglia** già popolato da manuale (es. IRB 7600 quadro 105, IRB 8700 quadro 108).

**Non clonare distinta da robot simili.** Le schede quadro IRC5 seguono classi drive system (manuale 3HAC047136-001 §2.6.2), ma manipolatore, cavi e motori cambiano per ogni famiglia.

```powershell
python tools/query_7600_distinta.py
python tools/explore_irb8700_distinta.py
```

## Profili quadro IRC5 (solo Schede + Azionamenti)

Dal manuale **3HAC047136-001 rev.AH §2.6.2**:

| Classe robot | MDU | Profilo | Quadro ref DB |
|--------------|-----|---------|---------------|
| Fino a IRB 1600-1660 | DSQC 406 MDU-430A | `irc5_small` | quadro 6 (IRB 1200) |
| Medio/grandi (2600, 4600, 6640, 6700, 7600…) | DSQC 663 MDU-790A | `irc5_medium_large` | quadro 98 (IRB 6640 IRC5) |
| IRB 8700 (MDU + 2 ADU + bleeder 4 kW) | DSQC 663 + 2×664 | `irc5_heavy` | quadro 108 (IRB 8700) |

Applicare **solo** schede quadro controllore (non motori/cavi manipolatore):

```powershell
python tools/purge_cloned_distinta.py --all-new --apply   # rimuove distinta template errata
python tools/apply_irc5_controller_profiles.py --apply  # IRC5: Schede+Azionamenti verificati
```

Robot **OmniCore** (1010, 6710, 7710…): richiedono manuale spare parts OmniCore dedicato — **non** usare profilo IRC5.

---

## Fase 2 — Verifica catalogo (`quote_products`)

```powershell
python tools/match_irb8700_catalog.py
python tools/search_8700_catalog.py
```

Per un robot nuovo: duplicare uno di questi script, cambiare la lista `CODES` / `BOM`, rieseguire.

---

## Fase 3 — Import distinta

Script attuale:

| Robot | Script | Quadri |
|-------|--------|--------|
| IRB 8700 | `import_irb8700_800_distinta.py` | 108, 109 |
| IRB 6700 | `import_irb6700_distinta.py` | 131–138 |
| IRB 2600 / 2600ID | `import_irb2600_distinta.py` | 120–123 |
| IRB 4600 | `import_irb4600_distinta.py` | 124–127 |
| IRB 6660 | `import_irb6660_distinta.py` | 128–130 |
| IRB 6600 IRC5 | `import_irb6600_irc5_distinta.py` | 188–190 (dopo setup) |

```powershell
# IRB 8700
python tools/import_irb8700_800_distinta.py --all --apply

# IRB 6700 (tutte le 8 varianti)
python tools/import_irb6700_distinta.py --all --apply

# IRB 2600 + 2600ID (4 quadri)
python tools/import_irb2600_distinta.py --all --apply

# IRB 4600 (4 varianti)
python tools/import_irb4600_distinta.py --all --apply

# IRB 6660 (3 varianti IRC5)
python tools/import_irb6660_distinta.py --all --apply

# IRB 6600 IRC5 (setup quadri + profilo cabinet + distinta manipolatore)
python tools/import_irb6600_irc5_distinta.py --setup --apply
python tools/import_irb6600_irc5_distinta.py --all --apply
```

**Per un altro robot:** copiare `tools/import_irb8700_800_distinta.py` → es. `tools/import_irb6700_distinta.py`, aggiornare:

- `QUADRO_IDS` (id → nome quadro)
- lista `BOM` (sezione, slot, codice, qty, nome se da creare, categoria)
- `DSQC668_ALT` se serve (o altre coppie primario/ALT)
- `SOURCE_NOTE` con riferimento manuale ABB

Lo script è **idempotente**: righe già presenti vengono saltate.

---

## Fase 4 — Descrizioni catalogo (tabella 1×2)

Vedi anche `memory/popolamento_descrizioni_catalogo.md`.

1. Aggiungere testi in `tools/catalog_description.py` → dizionario `CATALOG_DESCRIPTIONS` (chiave = codice prodotto).
2. Applicare al DB:

```powershell
python tools/apply_schede_descriptions.py --apply
```

3. Verificare prodotti senza descrizione:

```powershell
python tools/check_missing_desc.py
```

---

## Fase 5 — Codici alternativi (ALT)

Se la stessa board ha due codici ABB (es. DSQC 668):

```powershell
python tools/fix_dsqc668_alt_rows.py --apply
```

Per altre board: usare lo stesso pattern (riga `is_alternate=1` nello script import o script dedicato).

---

## Fase 6 — Verifica in app

1. Avviare server + client ATEC PM
2. **Commerciale → Gamma Robot** → robot → quadro
3. Tab distinta: controllare sezioni e badge **ALT**
4. Doppio click su componente → `QuoteProductDialog` con descrizione 1×2

---

## Script IRB 8700 (riferimento rapido)

| Script | Scopo |
|--------|--------|
| `migrate_all_missing_abb_robots.py` | Inserisce robot/quadri ABB mancanti (**senza** distinta) |
| `purge_cloned_distinta.py` | Rimuove distinta clonata per errore |
| `apply_irc5_controller_profiles.py` | Schede+Azionamenti IRC5 da profili manuali |
| `explore_irb8700_distinta.py` | Stato quadro, confronto robot simili |
| `match_irb8700_catalog.py` | Match codici manuali ↔ catalogo |
| `import_irb8700_800_distinta.py` | **Import principale** distinta IRB 8700 |
| `import_irb6700_distinta.py` | **Import manipolatore** IRB 6700 (da manuali 3HAC044266/268) |
| `import_irb2600_distinta.py` | **Import manipolatore** IRB 2600/2600ID (3HAC035504/049106) |
| `import_irb4600_distinta.py` | **Import manipolatore** IRB 4600 (3HAC033453/049108) |
| `import_irb6660_distinta.py` | **Import manipolatore** IRB 6660 (3HAC028197/049112) |
| `import_irb6600_irc5_distinta.py` | **Setup + import** IRB 6600 IRC5 (3HAC023082/047136) |
| `catalog_description.py` | Template HTML descrizioni |
| `apply_schede_descriptions.py` | Scrive descrizioni su DB |
| `fix_dsqc668_alt_rows.py` | Righe ALT DSQC 668 mancanti |
| `check_missing_desc.py` | Prodotti importati senza descrizione |
| `migrate_irb8700.py` | Migrazione struttura robot/quadro (una tantum) |

---

## Cosa dire a Cursor / Claude per il prossimo robot

Esempio prompt:

> Per **IRB 6700-235/2.65** (quadro id=XXX): cerca online il manuale spare parts ABB, estrai schede IRC5 e componenti manipolatore, verifica il catalogo `quote_products`, copia/adatta `import_irb8700_800_distinta.py`, importa distinta con `--apply`, aggiungi descrizioni 1×2 in `catalog_description.py` e applica con `apply_schede_descriptions.py`. Segui `memory/gamma_distinta_import.md`.
