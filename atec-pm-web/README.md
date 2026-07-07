# ATEC PM — Client Web

Client React per ATEC PM. Consuma l'API ASP.NET Core esistente (`ATEC.PM.Server`).

> 🧭 **Riprendi il lavoro da una nuova chat?** Parti da [HANDOFF.md](HANDOFF.md): stato modulo per modulo, regole e prossimi passi.

## Design system

**Regola fondamentale:** UI identica al preset [shadcn/create bIkeymG](https://ui.shadcn.com/create?preset=bIkeymG) (radix-vega). Vedi `DESIGN-RULES.md` (preset/tema/token) e `BLOCKS-RULES.md` (layout pagine fedele a [ui.shadcn.com/blocks](https://ui.shadcn.com/blocks)).

## Prerequisiti

- [Node.js](https://nodejs.org/) 20+ (LTS)
- API in esecuzione su `http://localhost:5150`

## Avvio rapido

```powershell
cd atec-pm-web
copy .env.example .env
npm install
npm run dev
```

Apri `http://localhost:5173`. In dev, Vite fa proxy di `/api`, `/hubs` e `/uploads` verso l'API.

## Struttura

```
src/
├── app/              # Layout applicazione (AppShell, sidebar)
├── components/ui/    # Primitivi shadcn (button, card, input…)
├── features/         # Moduli per dominio (auth, commesse, dashboard…)
└── lib/
    ├── api/          # Client HTTP + tipi DTO
    ├── auth/         # Sessione JWT (localStorage)
    └── signalr/      # Hub project + resource-planner
```

## Aggiungere componenti shadcn

```powershell
npx shadcn@latest add table dialog tabs sheet sidebar
```

## Build produzione (opzione 1: wwwroot sul server)

```powershell
npm run build
npm run copy-to-server
```

Copia `dist/` in `ATEC.PM.Server/wwwroot/`. Per servire la SPA dal server ASP.NET serve anche la configurazione fallback su `index.html` (passo successivo).

## Variabili ambiente

| Variabile | Descrizione |
|-----------|-------------|
| `VITE_API_BASE_URL` | URL API assoluto. Vuoto = stesso origin (proxy in dev). |
| `VITE_DEV_API_PROXY` | Target proxy dev (default `http://localhost:5150`). |

## Prossimo passo (PoC)

- Login funzionante con `/api/auth/login`
- Lista commesse con TanStack Table
- Tipi generati da Swagger: `npx openapi-typescript http://localhost:5150/swagger/v1/swagger.json -o src/lib/api/schema.d.ts`
