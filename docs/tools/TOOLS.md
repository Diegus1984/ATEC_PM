# Attrezzi — script e comandi di servizio

> Tutti gli strumenti a riga di comando del progetto in un posto solo: cosa fanno, come si
> lanciano, dove finisce il risultato. Gli script veri stanno in `tools/` e `deploy/`;
> qui c'è il **manuale d'uso**.

---

## 🐛 Segnalazioni — leggere i ticket degli utenti

Le segnalazioni **vivono solo sul database di PRODUZIONE** (192.168.2.150): in locale la
tabella `bug_reports` è vuota. Questi due attrezzi si collegano in SSH al server, leggono
il ticket e **scaricano gli allegati** (screenshot compresi) in `_bug_atts/`.

```powershell
# Leggi la segnalazione NN + scarica gli allegati in _bug_atts/
python tools/segnalazioni.py 140

# Elenco delle segnalazioni APERTE (stato OPEN / IN_PROGRESS)
python tools/segnalazioni.py --aperte

# Ultime N segnalazioni (default 15)
python tools/segnalazioni.py -n 20

# Stessa cosa in PowerShell
.\tools\leggi-segnalazione.ps1 140
.\tools\leggi-segnalazione.ps1 -Aperte
.\tools\leggi-segnalazione.ps1 -Ultime 20
```

| File | A cosa serve |
|------|--------------|
| `tools/segnalazioni.py` | Lettore principale (Python). SSH + `mysql` 8.4 sul server, allegati in `_bug_atts/` |
| `tools/leggi-segnalazione.ps1` | Stesso lavoro in PowerShell, per chi non ha Python a portata |

> **Serve la chiave SSH** `~/.ssh/atec_vps`. Le credenziali del DB sono dentro gli script:
> non copiarle altrove e non metterle in documenti condivisi.

---

## 🚀 Deploy e collaudo (cartella `deploy/`)

Il manuale completo — installazione da zero, aggiornamenti, backup, problemi tipici — sta in
[../guide/GUIDA-SERVER-LAN.md](../guide/GUIDA-SERVER-LAN.md). Qui solo l'elenco degli attrezzi.

| Script | A cosa serve |
|--------|--------------|
| `aggiorna-server.bat` → `deploy/aggiorna-server.ps1` | **L'aggiornamento normale** della produzione. Si lancia nudo dal terminale, mai dal tool Bash |
| `carica-installazione.bat` → `deploy/carica-installazione.ps1` | Prima installazione / ricarica completa sul server |
| `deploy/install-server.ps1` | Installazione lato server (servizio, cartelle, MySQL) |
| `deploy/applica-aggiornamento.ps1` | Passo lato server dell'aggiornamento (lo chiama `aggiorna-server`) |
| `deploy/disinstalla-server.ps1` | Rimozione del servizio dal server |
| `prova-test.bat` → `deploy/prova-test.ps1` | **Test automatici fuori dal deploy**, dal PC di sviluppo: se sono verdi registra l'impronta dei sorgenti e l'aggiornamento dopo li salta (deploy ~60 s) |
| `deploy/misura-prestazioni.ps1` | Raccoglie le richieste lente (>500 ms) registrate dal middleware |
| `deploy/accendi-slow-log.ps1` | Accende lo slow query log di MySQL sul server |
| `deploy/imposta-credenziali-share.ps1` | Credenziali SMB per la share delle immagini (Server-maga) e per il backup su NAS |
| `deploy/_comune.ps1` | Funzioni condivise dagli altri script (non si lancia da solo) |

---

## 🤖 Gamma Ricambi — import distinte robot ABB (cartella `tools/`)

Script Python di **una tantum**: importano le distinte dei robot dai manuali ABB e
popolano il catalogo Gamma. Il runbook con i prerequisiti e la procedura completa è in
[../guide/GAMMA-IMPORT-DISTINTA.md](../guide/GAMMA-IMPORT-DISTINTA.md); le descrizioni HTML
del catalogo in [../guide/CATALOGO-DESCRIZIONI.md](../guide/CATALOGO-DESCRIZIONI.md).

| Gruppo | File |
|--------|------|
| **Import distinte** | `import_irb2600_distinta.py`, `import_irb4600_distinta.py`, `import_irb6600_irc5_distinta.py`, `import_irb6660_distinta.py`, `import_irb6700_distinta.py`, `import_irb8700_800_distinta.py`, `migrate_irb8700.py`, `migrate_all_missing_abb_robots.py` |
| **Catalogo e descrizioni** | `populate_robot_catalog_gamma.py`, `catalog_description.py`, `apply_schede_descriptions.py`, `robot_web_descriptions.py`, `update_robot_catalog_web_snippets.py`, `apply_irc5_controller_profiles.py` |
| **Controlli / interrogazioni** | `audit_distinta_profiles.py`, `audit_dsqc668.py`, `check_missing_desc.py`, `_audit_robot_catalog.py`, `_audit_robot_catalog2.py`, `_list_robots_pending.py`, `query_7600_distinta.py`, `query_irb8700.py`, `explore_irb8700_distinta.py`, `search_8700_catalog.py`, `match_irb8700_catalog.py`, `match_irb8700_parts.py`, `sample_descriptions.py`, `sample_quadri.py` |
| **Pulizie** | `purge_cloned_distinta.py`, `fix_dsqc668_alt_rows.py` |

> Girano sul **MySQL locale** (`atec_pm`), non sulla produzione. Serve Python 3 + `pymysql`.

---

## 🧰 Altri attrezzi

| Percorso | A cosa serve |
|----------|--------------|
| `tools/CleanupBase64/` | Progetto .NET: ripulisce dal DB le immagini base64 rimaste nei campi RTF |
| `tools/DbFix/` | Progetto .NET di riparazioni una tantum sul database |
| `atec-pm-web/scripts/genera-catalogo.mjs` | Rigenera `src/config/catalogo.gen.ts` dal catalogo permessi (fonte unica: `ATEC.PM.Shared/catalogo-permessi.json`) |
| `detect_python.ps1` (radice workspace) | Trova l'interprete Python utilizzabile sulla macchina |
| `start-web.cmd` (radice workspace) | Avvio rapido della SPA in sviluppo |

---

## ⚙️ Comandi di sviluppo

```powershell
# API (in Release serve anche la SPA) → http://localhost:5150
dotnet run --project ATEC.PM.Server

# SPA in sviluppo → http://localhost:5173 (proxy /api → 5150)
cd atec-pm-web; npm run dev

# Build completa
dotnet build ATEC.PM.sln
```

> **Node non è nel PATH.** Prima di `npm`/`tsc`/`eslint`:
> `$env:Path = "C:\Program Files\nodejs;" + $env:Path`
> Per il type-check del web usare `tsc -b` o `npm run build`: `npx tsc --noEmit` esce 0 **senza controllare**.
