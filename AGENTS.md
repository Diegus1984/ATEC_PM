# ATEC PM — INDICE MASTER (leggi questo per primo)

> Questo file viene caricato **in automatico** a inizio sessione. È l'UNICO punto
> d'ingresso: non leggere a caso gli altri `.md` — usa la tabella qui sotto per andare
> diretto al documento giusto in base a cosa stai facendo.
> (Gemello di `CLAUDE.md` per Claude Code: tenerli allineati.)

## 👉 Su cosa stai lavorando?

| Area | Leggi PRIMA (in ordine) |
|------|-------------------------|
| **Client WEB** — `atec-pm-web/` (React + Vite + shadcn) | 1) `atec-pm-web/HANDOFF.md` — stato + regole + prossimi passi · 2) `atec-pm-web/BLOCKS-RULES.md` layout pagine · 3) `atec-pm-web/DESIGN-RULES.md` tema/token · 4) `atec-pm-web/WEB-MIGRATION.md` storico migrazione |
| **Server / API** — `ATEC.PM.Server/` | Controller in `ATEC.PM.Server/Controllers/` + DTO in `ATEC.PM.Shared/DTOs/` (leggi il contratto reale prima di scrivere client) |
| **DB / migrazioni** | Migrazioni gestite dal server all'avvio; MySQL (Dapper, no EF). Vedi memoria `MEMORY.md` per stato schema/porte |

> **Client WPF retired (20/07/2026).** Sorgenti in `backups/ATEC.PM.Client_retired_20260720/`. Non è più in `ATEC.PM.sln`. Il client ufficiale è solo web.

## 🗂️ Mappa dei documenti (cosa sta dove)

**Web** (`atec-pm-web/`):
- `HANDOFF.md` — **punto d'ingresso web**: stato modulo per modulo, come avviare, regole, roadmap
- `BLOCKS-RULES.md` — regole layout pagine (fedeltà ai blocchi shadcn, recipe copia-incolla)
- `DESIGN-RULES.md` — preset/tema/token (radix-vega, neutral, Inter, radius 0.625rem)
- `WEB-MIGRATION.md` — storico migrazione WPF → web
- `README.md` — avvio rapido e struttura cartelle

**Progetto (root `ATEC_PM/`):**
- `AGENTS.md` (questo) / `CLAUDE.md` — indice master (Codex / Claude Code). **Tienili allineati.**
- `TODO.md`, `BUGS.md` — cose aperte

## ⚙️ Avvio rapido

```powershell
# API + SPA (dev)
dotnet run --project ATEC.PM.Server      # API → http://localhost:5150 (Release serve anche la SPA)
cd atec-pm-web; npm run dev               # SPA → http://localhost:5173 (proxy /api → 5150)
```

> Nota shell: Node non è nel PATH. Per npm/tsc/eslint: prefissa `C:\Program Files\nodejs` al PATH.

## Stack

- **UI**: React 19 + Vite 7 + TypeScript + shadcn (`atec-pm-web`)
- **Backend**: ASP.NET Core 8 Web API · **DB**: MySQL con Dapper (no EF Core)
- **Shared**: `ATEC.PM.Shared` (DTO + PermissionEngine)

## Comandi

- `dotnet build ATEC.PM.sln` — Server + Shared (+ stub Web)
- `dotnet run --project ATEC.PM.Server` — API (e SPA in Release)
