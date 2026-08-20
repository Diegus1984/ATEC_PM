/**
 * Chiave permesso di ogni rotta che NON ha una voce di menu propria (dettagli,
 * sotto-pagine, alias). Le rotte del menu prendono la chiave da `navigation.ts`.
 *
 * Regola: una sotto-pagina eredita la chiave della sua pagina madre — le pagine del
 * Gestore DDP seguono «Gestore DDP», Ferie segue «Risorse» — così l'admin ha un solo
 * interruttore per famiglia e non deve ricordarsi le sotto-voci. Se in futuro una
 * sotto-pagina deve avere un livello suo, si registra una chiave dedicata e la si mette
 * qui (registrandola anche in `auth_features`, altrimenti resta libera per tutti).
 */
export const ROUTE_FEATURE_KEYS: Record<string, string> = {
  "risorse/ferie": "nav.risorse",
  "codex/ricodifica": "nav.codex",
  "commesse/:projectId": "nav.commesse",
  "commesse/:projectId/:section": "nav.commesse",
  "mom/:id": "nav.mom",
  "gestore-ddp/feedback/magazzino": "nav.gestore_ddp",
  "gestore-ddp/controllo": "nav.gestore_ddp",
  "gestore-ddp/controllo/:view": "nav.gestore_ddp",
  "gestore-ddp/:projectId": "nav.gestore_ddp",
  "preventivi/:id": "nav.preventivi",
  // Scheda permessi di una persona e catalogo delle funzioni: sotto-pagine di «Permessi».
  "permessi/persona/:employeeId": "nav.permessi",
  "permessi/catalogo": "nav.permessi",
}
