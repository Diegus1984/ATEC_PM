# 📚 Documentazione ATEC PM — indice

> **Tutti i `.md` del progetto stanno qui dentro.** Fuori da `docs/` restano solo tre file,
> e per un motivo preciso: `CLAUDE.md` e `AGENTS.md` (indice master, caricati in automatico
> a inizio sessione) e `atec-pm-web/README.md` (convenzione npm, avvio rapido della SPA).
>
> **REGOLA: un documento nuovo nasce in `docs/<cartella giusta>/` e si aggiunge a questo indice.**
> Niente `.md` sciolti nella radice del progetto o dentro `atec-pm-web/`.

| Cartella | Cosa ci va |
|----------|------------|
| `docs/` | I tre documenti **vivi** che si consultano ogni giorno |
| `docs/piani/` | Piani di lavoro e specifiche di funzionalità (anche già realizzate) |
| `docs/handoff/` | Consegne di fine sessione, **datate** — foto di un momento |
| `docs/regole/` | Regole che valgono sempre: UI, tema, date, layout |
| `docs/guide/` | Manuali operativi e runbook: come si fa una cosa |
| `docs/tools/` | Manuale degli attrezzi a riga di comando |
| `docs/archivio/` | Superato, generato o storico: si legge, non si aggiorna |

---

## 🔴 Vivi — si aggiornano di continuo

| Documento | Cosa contiene |
|-----------|---------------|
| [HANDOFF-WEB.md](HANDOFF-WEB.md) | **Punto d'ingresso del client web**: stato modulo per modulo, come avviare, regole, roadmap |
| [TODO.md](TODO.md) | Cose da fare, aperte |
| [BUGS.md](BUGS.md) | Difetti noti da sistemare |

## 🧰 Attrezzi

| Documento | Cosa contiene |
|-----------|---------------|
| [tools/TOOLS.md](tools/TOOLS.md) | **Come si leggono le segnalazioni** (`segnalazioni.py`), deploy e collaudo, script Gamma/robot, comandi di sviluppo |

## 📏 Regole (valgono sempre)

| Documento | Cosa contiene |
|-----------|---------------|
| [regole/BLOCKS-RULES.md](regole/BLOCKS-RULES.md) | Layout delle pagine: fedeltà ai blocchi shadcn, recipe copia-incolla |
| [regole/DESIGN-RULES.md](regole/DESIGN-RULES.md) | Preset, tema e token (radix-vega, neutral, Inter, radius 0.625rem) |
| [regole/REGOLA_DATE_INIZIO_FINE.md](regole/REGOLA_DATE_INIZIO_FINE.md) | Coppie di date inizio → fine: fine ≥ inizio, fine disabilitata senza inizio |

## 📖 Guide e runbook

| Documento | Cosa contiene |
|-----------|---------------|
| [guide/GUIDA-SERVER-LAN.md](guide/GUIDA-SERVER-LAN.md) | **Deploy sul server aziendale** (192.168.2.150): installazione, aggiornamenti, backup, problemi tipici |
| [guide/SEZIONI_COSTO_GUIDA.md](guide/SEZIONI_COSTO_GUIDA.md) | Configurazione Sezioni Costo — guida d'uso |
| [guide/ANAGRAFICHE-FASI-SEZIONI.md](guide/ANAGRAFICHE-FASI-SEZIONI.md) | Anagrafiche aggiornate: sezioni di costo e fasi |
| [guide/POPOLAMENTO_DESCRIZIONI_CATALOGO_PREVENTIVI.md](guide/POPOLAMENTO_DESCRIZIONI_CATALOGO_PREVENTIVI.md) | Architettura Commerciale, i 3 cataloghi, runbook descrizioni |
| [guide/CATALOGO-DESCRIZIONI.md](guide/CATALOGO-DESCRIZIONI.md) | Descrizioni HTML del catalogo Gamma Ricambi (template schede) |
| [guide/GAMMA-IMPORT-DISTINTA.md](guide/GAMMA-IMPORT-DISTINTA.md) | Import distinte robot ABB dai manuali (runbook ripetibile) |

## 🗺️ Piani e specifiche

**Commesse / preventivo**
| Documento | Cosa contiene |
|-----------|---------------|
| [piani/PIANO-LAVORO-COMMESSE-V32.md](piani/PIANO-LAVORO-COMMESSE-V32.md) | Piano di lavoro sul gap `Gestione_Commesse_V32.html` → ATEC PM (blocchi 0-7) |
| [piani/ANALISI-GAP-COMMESSE-V32.md](piani/ANALISI-GAP-COMMESSE-V32.md) | Confronto puntuale prototipo → software |
| [piani/BLOCCO5-CALCOLATRICI-SPEC.md](piani/BLOCCO5-CALCOLATRICI-SPEC.md) | Calcolatrici a righe + anagrafica tariffe |
| [piani/BLOCCO6-TRASFERTA-SPEC.md](piani/BLOCCO6-TRASFERTA-SPEC.md) | Gestione Trasferta — specifica di partenza |
| [piani/PIANO-TRASFERTA-PREVENTIVO.md](piani/PIANO-TRASFERTA-PREVENTIVO.md) | Trasferta a righe dentro la sezione di preventivo |
| [piani/PIANO-FASI-MULTISEZIONE.md](piani/PIANO-FASI-MULTISEZIONE.md) | Fasi dettaglio multi-sezione (libreria unica di fasi) |
| [piani/PIANO-DASHBOARD-STATI-88.md](piani/PIANO-DASHBOARD-STATI-88.md) | Dashboard PM e stati della commessa (segnalazione #88) |
| [piani/HANDOFF-PREVENTIVO-INLINE.md](piani/HANDOFF-PREVENTIVO-INLINE.md) | Preventivo editabile inline in «Preventivo vs Consuntivo» |

**DDP / acquisti / magazzino**
| Documento | Cosa contiene |
|-----------|---------------|
| [piani/PIANO-GESTORE-DDP-V41.md](piani/PIANO-GESTORE-DDP-V41.md) | Port del prototipo `Gestione_DDP_New_V41.html` (5 schede) |
| [piani/PIANO-142-GREZZI-PICKER.md](piani/PIANO-142-GREZZI-PICKER.md) | **#142**: lavorati 101 col grezzo 201 nei picker DDP (derivazione visibile, coppia officina+commerciale, scelta fornitore) |
| [piani/PIANO-ACQUISTI-CODICE-ATEC.md](piani/PIANO-ACQUISTI-CODICE-ATEC.md) | Codici ATEC ↔ Danea + Inbox Acquisti |
| [piani/PIANO-MIGRAZIONE-DANEA-ATEC.md](piani/PIANO-MIGRAZIONE-DANEA-ATEC.md) | Ripartenza archivio Danea «Atec» + trasferimento catalogo |

**SAL / scadenze / milestone**
| Documento | Cosa contiene |
|-----------|---------------|
| [piani/SAL-SPEC.md](piani/SAL-SPEC.md) | Specifica del modulo SAL |
| [piani/SAL-PAGE-SPEC.md](piani/SAL-PAGE-SPEC.md) | Pagina PM «SAL» globale + prospetto |
| [piani/SAL-V10-PLAN.md](piani/SAL-V10-PLAN.md) | Estensione SAL / fatturazione a parità col prototipo v10 |
| [piani/SCADENZE-SPEC.md](piani/SCADENZE-SPEC.md) | Pagina PM «Scadenze» (cruscotto unico) |
| [piani/MILESTONE-GANTT-SPEC.md](piani/MILESTONE-GANTT-SPEC.md) | Gantt delle Milestone (Fase 3) |
| [piani/PIANO-SEGNALAZIONI-BILANCIO.md](piani/PIANO-SEGNALAZIONI-BILANCIO.md) | Segnalazioni sul Bilancio commessa |

**HR / permessi / tecnico**
| Documento | Cosa contiene |
|-----------|---------------|
| [piani/PIANO-HR-PRESENZE.md](piani/PIANO-HR-PRESENZE.md) | **Modulo HR**: timbrature, ferie e permessi dentro ATEC PM |
| [piani/PIANO-HR-PORT-ORIGINALE.md](piani/PIANO-HR-PORT-ORIGINALE.md) | Le opzioni del programma «Timbrature» che mancavano ad ATEC PM |
| [piani/PIANO-PERMESSI-REBUILD.md](piani/PIANO-PERMESSI-REBUILD.md) | **Rebuild della gestione permessi** (piano in vigore, passi 1-7) |
| [piani/FASE-E-SOSTITUZIONE-LIVELLI.md](piani/FASE-E-SOSTITUZIONE-LIVELLI.md) | La scala di livello sparisce per sostituzione |
| [piani/PIANO-MIGLIORIE-TECNICHE.md](piani/PIANO-MIGLIORIE-TECNICHE.md) | **Debito tecnico**: blocchi A-F (migrazioni, rete di sicurezza, prestazioni, sicurezza) |
| [piani/MIGLIORIE-GESTIONE-BUG.md](piani/MIGLIORIE-GESTIONE-BUG.md) | Modulo Segnalazioni — piano operativo (L1-L9) |
| [piani/PIANO-SYNC-RISORSE.md](piani/PIANO-SYNC-RISORSE.md) | **Sincronizzazione in tempo reale Risorse PM ⇄ ATEC Risorse (VPS)**: confronto dei due programmi, dati reali, motore, regole di merge, fasi, decisioni |

## 📦 Handoff datati (storia delle sessioni)

| Documento | Sessione |
|-----------|----------|
| [handoff/HANDOFF-CHAT-20260825.md](handoff/HANDOFF-CHAT-20260825.md) | 25/08/2026 |

## 🗄️ Archivio — si legge, non si aggiorna

| Documento | Perché è qui |
|-----------|--------------|
| [archivio/PIANO-PERMESSI.md](archivio/PIANO-PERMESSI.md) | Superato da `piani/PIANO-PERMESSI-REBUILD.md` (citato ancora in molti commenti del codice) |
| [archivio/PERMESSI-MAPPA-ENDPOINT.gen.md](archivio/PERMESSI-MAPPA-ENDPOINT.gen.md) | **Generato** dallo script: non si modifica a mano |
| [archivio/CENSIMENTO-N1-E3.md](archivio/CENSIMENTO-N1-E3.md) | Censimento query N+1 del blocco E3 |

---

## 📍 Documenti che stanno FUORI da `docs/` (di proposito)

| Percorso | Perché resta lì |
|----------|-----------------|
| `CLAUDE.md` · `AGENTS.md` | Indice master, caricati in automatico a inizio sessione. **Tienili allineati** |
| `atec-pm-web/README.md` | Convenzione npm: avvio rapido e struttura cartelle della SPA |
| `tutorial-video/README.md` | Procedura dei video tutorial, sta accanto ai materiali di montaggio |
| `.claude/skills/**/SKILL.md` · `.agents/**` | File di configurazione degli agenti: devono stare nel loro posto per funzionare |
| `backups/**` · `deploy/out/**` · `graphify-out/**` | Copie, output di build e materiale generato |
