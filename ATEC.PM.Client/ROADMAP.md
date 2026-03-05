# ATEC PM — Stato Progetto e Roadmap

**Ultimo aggiornamento:** 05 Marzo 2026  
**Repository:** https://github.com/Diegus1984/ATEC_PM.git

---

## Stack Tecnologico

| Componente | Tecnologia |
|---|---|
| Client | WPF .NET 8 (C#) |
| Server | ASP.NET Core Web API .NET 8 |
| Shared | .NET 8 Class Library (Models, DTOs, PermissionEngine) |
| Database | MySQL (XAMPP) + Dapper ORM |
| Auth | JWT Bearer Token |

---

## BLOCCO 1 — Utenti e Permessi ✅ COMPLETATO

### Ruoli
- ADMIN — accesso totale
- PM — vede tutto inclusi dati economici
- RESP_REPARTO — vede il suo reparto
- TECH — vede solo le commesse con fasi del suo reparto

### Reparti
ELE, MEC, PLC, ROB, UTC, ACQ, AMM

### Funzionalità
- Auth JWT (login, token, credenziali)
- `employees` con `user_role`, `username`, `password_hash`
- `employee_departments` — appartenenza reparti (con flag `is_responsible`, `is_primary`)
- `employee_competences` — bivalenze tecniche (visibilità, NON assegnazione)
- `PermissionEngine` centralizzato in Shared
- `UserContext` con `DepartmentCodes`, `ResponsibleDepartmentCodes`, `CompetenceCodes`
- Sidebar con visibilità basata su ruolo
- Sezione "GESTIONE AVANZATA" (solo PM/ADMIN)

### Regole chiave
- Appartenenza reparto (`employee_departments`) → il tecnico CI LAVORA, può essere assegnato
- Competenza (`employee_competences`) → il tecnico CAPISCE, vede fasi/timesheet ma NON viene assegnato

---

## BLOCCO 2 — Dashboard Commessa ✅ COMPLETATO

### Funzionalità
- `ProjectDashboardControl` sostituisce la vecchia pagina "Dettagli"
- Header scuro con codice, titolo, badge stato/priorità, cliente, PM, date
- KPI cards su 2 righe:
  - Riga 1 (tutti): Avanzamento %, Ore, Tecnici attivi, Fasi completate
  - Riga 2 (solo PM/ADMIN): Costo ore, Costo materiali (DDP), Costo totale, Margine
- Barre orizzontali colorate per reparto (budget vs lavorato + costi materiali)
- Tabella tecnici assegnati alle fasi (da `phase_assignments`, non da timesheet)
- Ultime 10 registrazioni timesheet
- Costi DDP escludono stato `CANCELLED`

---

## BLOCCO 3 — Chat Commessa ✅ COMPLETATO

### Struttura
- Chat multiple per commessa (per argomento)
- Tabelle: `project_chats`, `project_chat_participants`, `project_chat_messages`
- PM/ADMIN vedono tutte le chat automaticamente
- TECH/RESP vedono solo le chat dove sono partecipanti

### Funzionalità
- `ProjectChatControl` — lista chat a sinistra, messaggi a destra (stile WhatsApp)
- Bolle colorate (blu = mie, bianche = altri) con avatar/iniziali
- Separatori per data
- @menzioni con popup autocomplete (filtra partecipanti, escludi te stesso)
- @menzioni colorate in bold nel messaggio
- Allegati file con upload base64
- Anteprima inline immagini (jpg/png/bmp/gif) nella bolla
- Link cliccabile per altri tipi file (📎 + nome)
- File salvati in `{commessa}/Chat/{chatId}/`
- Badge messaggi non letti (pallino arancione con conteggio)
- `last_read_message_id` per tracciare lettura
- Mark-read automatico quando apri la chat
- Polling ogni 15 secondi
- Eliminazione messaggio (tasto destro, solo autore o ADMIN)
- Eliminazione chat (solo creatore o ADMIN)
- `NewChatDialog` con selezione partecipanti checkbox

---

## BLOCCO 4 — Notifiche Mail 🔲 DA FARE

### Previsto
- `IHostedService` background job
- SMTP Aruba (`smtp.aruba.it:587` STARTTLS)
- Credenziali cifrate con DPAPI (`ProtectedConfigHelper` già esistente)
- Heartbeat client (ping ogni 30s → `last_seen` su employees)
- Se utente @menzionato non è online → manda mail
- Flag `notified_by_email` per evitare duplicati

---

## BLOCCO 5 — Fasi Commessa e Assegnazione Risorse ✅ COMPLETATO

### Funzionalità
- 40+ fasi template raggruppate per reparto + trasversali
- Auto-inserimento fasi `is_default=true` alla creazione commessa
- `PhasesManagementControl` con ricerca, raggruppamento per reparto, riepilogo ore
- `PhaseRowControl` — UserControl XAML editabile per singola fase
- Budget ore fase = somma automatica ore pianificate tecnici (non editabile manualmente)
- Stato editabile inline (solo PM/ADMIN)
- Auto-avanzamento: `NOT_STARTED` → `IN_PROGRESS` al primo versamento ore timesheet
- Avanzamento % fase e per tecnico
- Assegnazione tecnici filtrata per reparto, no duplicati, ore > 0 obbligatorie
- Modifica ore pianificate tecnico (✏, solo PM/ADMIN)
- Ore lavorate per tecnico (da timesheet) con % (>100% rosso = sforamento)
- `SummaryChanged` evento per aggiornare riepilogo senza ricostruire pagina
- `AddPhasesWindow` per aggiungere fasi non default (checkbox)
- `PhaseTemplatesPage` — gestione avanzata template (is_default, sort_order, CRUD)
- Categoria auto-derivata dal reparto se non specificata

---

## BLOCCO 6 — Report ed Export 🔲 DA FARE

### Previsto
- Export Excel ore per commessa (per reparto, per tecnico, per fase)
- Export PDF riepilogo commessa
- Filtri per periodo, reparto, tecnico

---

## Altre Funzionalità

### Commesse
- CRUD con codice auto-generato (AT + anno + 3 cifre)
- Creazione automatica cartella server
- DDP Commerciali con 12 stati colorati (TO_ORDER, ORDERED, DELIVERED, PARTIAL, TO_BUILD, RFQ, TO_CHECK, CANCELLED, ASSIGNED, SHIPPED, TECH_CHECK, TO_MODULA)

### Gestione Documenti ✅ COMPLETATO
- `DocumentManagerControl` — file manager completo nella commessa
- Navigazione cartelle con breadcrumb cliccabile
- Upload file (bottone + drag & drop, singolo e multiplo)
- Download file (tasto destro → Scarica)
- Crea sottocartelle
- Rinomina file/cartelle
- Elimina file/cartelle (con conferma)
- Sposta file tra cartelle
- Apri in Windows Explorer
- Cartella "Chat" nascosta dal file tree
- `LongPathHelper` per percorsi > 260 caratteri
- `FilesChanged` evento per aggiornare TreeView dopo operazioni
- Gestione duplicati (suffisso _1, _2, ecc.)

### Timesheet
- Timesheet settimanale con navigazione settimane
- Filtro fasi per tecnico (reparto + competenze + trasversali)
- PM/ADMIN vedono tutte le fasi
- Modifica e cancellazione entry
- Auto-avanzamento stato fase al primo versamento ore

### Anagrafica
- Clienti con import Easyfatt
- Fornitori con import Easyfatt
- Catalogo articoli con import Easyfatt
- Dipendenti con reparti, competenze, credenziali

### API Client
- `ApiClient` con GET, POST, PUT, PATCH, DELETE, Upload singolo/multiplo, Download

---

## Deploy (da implementare)

### Piano
- Server fisico in ufficio ATEC (Windows, sempre acceso, UPS)
- MySQL + API ASP.NET Core come Windows Service
- Cartelle commesse su disco locale, condivise in rete LAN
- Dominio Aruba (es. `pm.atec-automation.it`) + certificato SSL
- Port forwarding 443 → IIS reverse proxy → API
- Accesso LAN: DNS interno/hosts → IP locale
- Accesso esterno: DNS Aruba → IP pubblico → port forwarding

### Da fare per il deploy
- `Program.cs` → Windows Service
- Configurazione IIS reverse proxy + HTTPS
- Backup automatico MySQL + cartelle
- Script installazione

---

## Prossimi Passi Consigliati

1. Report ed export Excel/PDF (Blocco 6)
2. Dashboard principale sidebar (riassunto tutte le commesse)
3. Notifiche mail + heartbeat (Blocco 4)
4. Deploy su server produzione
