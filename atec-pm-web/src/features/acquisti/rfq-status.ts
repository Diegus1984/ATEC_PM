// Etichette italiane degli stati RDO: il server parla DRAFT/SENT/CLOSED/CANCELLED,
// l'utente deve leggere Bozza/Inviata/Aggiudicata/Annullata (stessa parola ovunque).
export const RFQ_STATUS_LABELS: Record<string, string> = {
  DRAFT: "Bozza",
  SENT: "Inviata ai fornitori",
  CLOSED: "Aggiudicata",
  CANCELLED: "Annullata",
}

export function rfqStatusLabel(status: string | null | undefined): string {
  if (!status) return "—"
  return RFQ_STATUS_LABELS[status] ?? status
}
