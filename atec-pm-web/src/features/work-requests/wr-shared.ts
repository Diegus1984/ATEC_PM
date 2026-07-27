// ── Lavorazioni: viste, colonne e costruzione payload (nessun React) ───────

import type { WorkRequest, WorkRequestSaveRequest } from "@/lib/api/types"

export type WorkRequestViewMode =
  | "drafts"
  | "priorities"
  | "consegne"
  | "trattamenti"
  | "project"

export type WrVisibleColumns = Record<string, boolean>

export const WR_COLUMN_LABELS: { id: string; label: string }[] = [
  { id: "project", label: "Commessa" },
  { id: "requestDate", label: "Data Richiesta" },
  { id: "description", label: "Descrizione" },
  { id: "type", label: "Tipo" },
  { id: "rfqs", label: "RDO (Offerte)" },
  { id: "oda", label: "Dati ODA (Ordine)" },
  { id: "priority", label: "Priorità" },
  { id: "availabilityDate", label: "Disponibilità" },
  { id: "notes", label: "Note" },
  { id: "status", label: "Stato" },
  { id: "treatment", label: "Trattamento" },
  { id: "treatmentDate", label: "Data Trattamento" },
  { id: "treatmentNotes", label: "Note Trattamento" },
]

export const WR_VIEW_HEADERS: Record<
  WorkRequestViewMode,
  { title: string; description: string }
> = {
  drafts: {
    title: "Bozze Lavorazioni (Staging)",
    description:
      "Richieste preliminari in attesa di assegnazione commessa e conferma definitiva.",
  },
  priorities: {
    title: "Tabella Priorità",
    description: "Lavorazioni attive ordinate per priorità e data richiesta.",
  },
  consegne: {
    title: "Tracciamento Consegne",
    description: "Monitoraggio delle date di disponibilità e conferma arrivi.",
  },
  trattamenti: {
    title: "Rapporto Trattamenti",
    description:
      "Lavorazioni meccaniche che richiedono trattamenti termici o superficiali speciali.",
  },
  project: {
    title: "Dettaglio Lavorazioni Commessa",
    description:
      "Griglia di dettaglio per commessa. Modifica i valori direttamente nelle celle.",
  },
}

/** Colonne accese di default: ogni vista ne mostra solo quelle che le servono. */
export function defaultVisibleColumnsFor(
  viewMode: WorkRequestViewMode
): WrVisibleColumns {
  const defaults: WrVisibleColumns = {
    project: viewMode !== "project",
    requestDate: true,
    description: true,
    type: true,
    rfqs: true,
    oda: true,
    priority: true,
    availabilityDate: true,
    notes: true,
    status: true,
    treatment: true,
    treatmentDate: viewMode === "trattamenti",
    treatmentNotes: viewMode === "trattamenti",
  }

  if (viewMode === "drafts") {
    return {
      ...defaults,
      type: false,
      rfqs: false,
      oda: false,
      priority: false,
      availabilityDate: false,
      notes: false,
      status: false,
      treatment: false,
    }
  }

  if (viewMode === "trattamenti") {
    return {
      ...defaults,
      type: false,
      rfqs: false,
      oda: false,
      priority: false,
      availabilityDate: false,
      status: false,
      treatment: false,
    }
  }

  return defaults
}

/** Le bozze (staging) compaiono solo nella vista dedicata. */
export function filterRowsByView(
  rows: WorkRequest[],
  viewMode: WorkRequestViewMode
): WorkRequest[] {
  switch (viewMode) {
    case "drafts":
      return rows.filter((r) => r.isStaging)
    case "consegne":
      return rows.filter((r) => !r.isDelivered && !r.isStaging)
    case "trattamenti":
      return rows.filter(
        (r) => r.hasTreatment && !r.isTreatmentConfirmed && !r.isStaging
      )
    default:
      return rows.filter((r) => !r.isStaging)
  }
}

/** Nuova lavorazione: campi obbligatori dal chiamante, il resto ai valori neutri. */
export function newWorkRequestPayload(
  overrides: Partial<WorkRequestSaveRequest> & {
    projectId: number
    description: string
    requestDate: string
  }
): WorkRequestSaveRequest {
  return {
    type: "",
    priority: 2,
    availabilityDate: "",
    notes: "",
    isStaging: false,
    isUltraCritical: false,
    isDelivered: false,
    rfqs: [],
    poSupplier: "",
    poNumber: "",
    poDate: "",
    hasTreatment: false,
    treatmentDate: "",
    treatmentNotes: "",
    isTreatmentConfirmed: false,
    ...overrides,
  }
}

/** Riga esistente → payload di salvataggio completo, con una patch applicata. */
export function toSaveRequest(
  req: WorkRequest,
  patch: Partial<WorkRequestSaveRequest> = {}
): WorkRequestSaveRequest {
  return {
    projectId: req.projectId,
    requestDate: req.requestDate,
    description: req.description,
    type: req.type,
    priority: req.priority,
    availabilityDate: req.availabilityDate,
    notes: req.notes,
    isUltraCritical: req.isUltraCritical,
    isDelivered: req.isDelivered,
    isStaging: req.isStaging,
    rfqs: req.rfqs,
    poSupplier: req.poSupplier,
    poNumber: req.poNumber,
    poDate: req.poDate,
    hasTreatment: req.hasTreatment,
    treatmentDate: req.treatmentDate,
    treatmentNotes: req.treatmentNotes,
    isTreatmentConfirmed: req.isTreatmentConfirmed,
    rowVersion: req.rowVersion,
    ...patch,
  }
}
