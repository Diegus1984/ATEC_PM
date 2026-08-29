# ARCHIVIO — piano permessi del 13/08/2026 (mattina)

> **Non è più il piano attivo.** Il piano è `PIANO-PERMESSI.md`.
>
> Questo file resta come diario della Fase 1 (migrazione v82, buchi chiusi,
> trappole). Non usarlo per decidere il modello.

---

## Fase 1 — le 4 funzioni mancanti · FATTA il 13/08/2026 (v82)

*Provata a runtime e a video, in locale — manca il deploy.*

Migrazione **v82**: `project.chat`, `project.ddp_commerciale`,
`project.ddp_officina`, `project.documenti` in `auth_features` a **livello 0**
(registrare ≠ restringere) + le 4 voci in `commessa-sections.ts`. Bonifica
delle 4 concessioni orfane del ruolo `AMM` in `auth_role_features` (0 residue).

### Due punti sbagliati nel piano originale (non ripetere)

1. **`DdpCommercialController` e `DdpOfficinaController` NON servono le sezioni
   di commessa.** Hanno una sola action ciascuno (`GET /inbox`) e i chiamanti
   sono **Inbox Acquisti** e **Inbox Officina**. Gate con
   `nav.acquisti_inbox` / `nav.officina_inbox`, non `project.*`.
2. **Il CRUD di DDP e Documenti sta in `ProjectsController`.** Gate
   **per-action, mai di classe**: un gate di classe travolge `GET /api/projects`,
   unica porta della Contabilità verso il SAL.

### Estensioni al motore nate qui

- **Più chiavi = OR.** `[RequireFeature("project.ddp_commerciale", "nav.gestore_ddp", "nav.acquisti_inbox")]`.
  La Sintesi DDP del Gestore DDP legge le righe dall’endpoint di commessa.
- **`AccessOnly = true`.** `POST /api/chat/{id}/mark-read` non deve prendere 403
  in sola lettura.

### Difetti corretti nello stesso giro

- Cartella **Chat** non è più raggiungibile da Documenti (`IsPathAllowed` sul
  percorso relativo, non sul nome al livello elencato).
- `GET /api/ddp-aggregations`: lettura libera / scrittura riservata (prima 403
  per TECH/RESP → totali A9 vuoti in silenzio).
- Anti-traversal: `IsUnderBasePath` con separatore, case-insensitive.

### Fase 1-bis — `server_path` · FATTA il 13/08/2026

1. `[RequireFeature("action.edit_project")]` sulla `PUT /api/projects/{id}`.
2. Percorso sotto `BasePath`. Invariato passa sempre. La radice è rifiutata.
3. La `POST` non scrive `server_path` dal body.

### Trappole

- `DELETE /api/projects/{id}/hard` cancella la cartella di `server_path`.
  Prima di eliminare una commessa di prova, azzerare `server_path`.
- La shell mangia i backslash negli heredoc: i percorsi di test si scrivono
  su file, non con `cat`.
- IDOR Chat (8 action su 12 senza partecipazione): **non chiuso**, da decidere.
- Fallback permissivo attuale: feature non registrata = accesso a tutti.
  Il piano nuovo **inverte** questa regola.

### Cosa la Fase 1 non chiude

Scritture DDP da `PurchaseRfqController` / `CatalogMappingController` /
`WorkRequestDdpSync` / destinazioni e trattamenti. `DdpItemEventsController`
senza chiave. Notifiche a chi non ha la sezione. Pannello «Seleziona una
sezione» che cita Documenti anche a chi non ce l’ha.
