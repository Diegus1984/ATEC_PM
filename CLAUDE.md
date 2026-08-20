# ATEC PM — INDICE MASTER (leggi questo per primo)

> Questo file viene caricato **in automatico** a inizio sessione. È l'UNICO punto
> d'ingresso: non leggere a caso gli altri `.md` — usa la tabella qui sotto per andare
> diretto al documento giusto in base a cosa stai facendo.

## 👉 Su cosa stai lavorando?

| Area | Leggi PRIMA (in ordine) |
|------|-------------------------|
| **Client WEB** — `atec-pm-web/` (React + Vite + shadcn) | 1) [atec-pm-web/HANDOFF.md](atec-pm-web/HANDOFF.md) — stato + regole + prossimi passi · 2) [BLOCKS-RULES.md](atec-pm-web/BLOCKS-RULES.md) layout pagine · 3) [DESIGN-RULES.md](atec-pm-web/DESIGN-RULES.md) tema/token · 4) [WEB-MIGRATION.md](atec-pm-web/WEB-MIGRATION.md) storico migrazione |
| **Server / API** — `ATEC.PM.Server/` | Controller in `ATEC.PM.Server/Controllers/` + DTO in `ATEC.PM.Shared/DTOs/` (leggi il contratto reale prima di scrivere client) |
| **DB / migrazioni** | **Una migrazione = un file** in `ATEC.PM.Server/Migrations/MNNN_Cosa.cs` (`IMigrazione`): si crea il file e basta — niente costanti da alzare, niente elenchi, `DbService.cs` non si tocca. Le applica `MigrationRunner` all'avvio (sotto lock MySQL, una sola istanza alla volta), scoprendole dall'assembly; se una fallisce **il server non parte** e l'errore resta scritto in `schema_migrations` (`success`, `error_text`, `duration_ms`). Le sole eccezioni sono le pulizie marcate `Facoltativa`. Le **viste** non stanno nelle migrazioni: le riallinea `EnsureViews` a ogni avvio. Attrezzi condivisi in `Migrations/AiutiMigrazione.cs`. MySQL con Dapper (no EF) |
| **Segnalazioni / Bug** | **`python tools/segnalazioni.py <ID>`** per leggere ticket e scaricare subito gli screenshot allegati in `_bug_atts/`. `python tools/segnalazioni.py --aperte` per l'elenco aperto. |

> **Client WPF retired (20/07/2026).** Sorgenti in `backups/ATEC.PM.Client_retired_20260720/`. Non è più in `ATEC.PM.sln`. Il client ufficiale è solo web.

## 🗂️ Mappa dei documenti (cosa sta dove)

**Web** (`atec-pm-web/`):
- `HANDOFF.md` — **punto d'ingresso web**: stato modulo per modulo, come avviare, regole, roadmap
- `BLOCKS-RULES.md` — regole layout pagine (fedeltà ai blocchi shadcn, recipe copia-incolla)
- `DESIGN-RULES.md` — preset/tema/token (radix-vega, neutral, Inter, radius 0.625rem)
- `WEB-MIGRATION.md` — storico migrazione WPF → web
- `README.md` — avvio rapido e struttura cartelle

**Progetto (root `ATEC_PM/`):**
- `CLAUDE.md` (questo) / `AGENTS.md` — indice master (Claude Code / Codex). **Tienili allineati.**
- `TODO.md`, `BUGS.md` — cose aperte
- `GUIDA-SERVER-LAN.md` — **deploy sul server aziendale** (ATEC-FC 192.168.2.150): installazione, aggiornamenti, backup, problemi tipici. Script in `deploy/`, avvio da `carica-installazione.bat` / `aggiorna-server.bat`
- Memoria automatica: `~/.claude/projects/.../memory/MEMORY.md` — caricata in automatico, indice delle note persistenti

## 🐛 Segnalazioni e Ticket (Accesso Diretto)

Per consultare le segnalazioni inserite dagli utenti sul gestionale senza perdere tempo:
```powershell
# Leggi il ticket e scarica automaticamente screenshot/allegati in _bug_atts/
python tools/segnalazioni.py <ID>
# oppure:
.\tools\leggi-segnalazione.ps1 <ID>

# Mostra tutte le segnalazioni aperte / in corso
python tools/segnalazioni.py --aperte

# Mostra le ultime N segnalazioni
python tools/segnalazioni.py -n 20
```

## ⚙️ Avvio rapido

```powershell
# API + SPA (dev)
dotnet run --project ATEC.PM.Server      # API → http://localhost:5150 (Release serve anche la SPA)
cd atec-pm-web; npm run dev               # SPA → http://localhost:5173 (proxy /api → 5150)
```

> Nota shell: Node non è nel PATH. Per npm/tsc/eslint: `$env:Path = "C:\Program Files\nodejs;" + $env:Path`.

## Stack

- **UI**: React 19 + Vite 7 + TypeScript + shadcn (`atec-pm-web`)
- **Backend**: ASP.NET Core 8 Web API · **DB**: MySQL con Dapper (no EF Core)
- **Shared**: `ATEC.PM.Shared` (DTO + PermissionEngine)

## Comandi

- `dotnet build ATEC.PM.sln` — Server + Shared (+ stub Web)
- `dotnet run --project ATEC.PM.Server` — API (e SPA in Release)
