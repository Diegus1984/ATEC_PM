import { setRawSupplier } from "@/lib/api/project-ddp"
import {
  addOfficinaItem,
  fetchOfficinaItems,
  updateOfficinaItem,
} from "@/lib/api/project-ddp-officina"

import { DDP_STATUS_TO_ORDER } from "./ddp-constants"

/** Punto prima delle ultime 3 cifre (stessa formattazione della pagina Codex). */
function formatCodice(codice: string): string {
  const raw = (codice ?? "").replace(/\./g, "")
  return raw.length > 3 ? `${raw.slice(0, raw.length - 3)}.${raw.slice(-3)}` : raw
}

/** #142 — il grezzo del 101 che si sta inserendo: cosa farne dopo la riga d'officina. */
export interface GrezzoScelto {
  /** Codice del 201 di derivazione (con o senza punti). */
  codice: string
  /** Articolo Danea scelto fra gli abbinamenti del 201 (null = nessuna scelta esplicita). */
  catalogItemId: number | null
  /** Nome del fornitore scelto — solo per il messaggio di esito. */
  fornitoreNome?: string
  /** true = il 201 non ha NESSUN articolo associato: il grezzo nasce «da associare» (bloccato). */
  scoperto?: boolean
}

export interface InserimentoOfficinaParams {
  projectId: number
  /** Codice ATEC del 101 (col punto o senza: il confronto e l'insert normalizzano). */
  codiceAtec: string
  descrizione: string
  /** Costo della LAVORAZIONE: per un 101 con grezzo va lasciato 0 — il materiale sta sulla riga grezzo. */
  unitCost: number
  supplierName: string
  requestedBy: string
  /** Dialogo di conferma del chiamante (useConfirm). */
  confirm: (opts: {
    title: string
    description: string
    confirmLabel: string
    destructive: boolean
  }) => Promise<boolean>
  /** Coda del messaggio («— la trovi nella scheda …»), decisa dal picker chiamante. */
  notaScheda?: string
  /** #142: presente = il 101 ha la derivazione; dopo l'insert si sistema/racconta il grezzo. */
  grezzo?: GrezzoScelto | null
}

/**
 * Inserimento di un 101 in DDP Officina con dedup «già presente → +1» (il flusso del
 * picker officina storico, in un posto solo — lo usano il picker unico e il picker
 * «per codice ATEC»). La riga del grezzo in DDP Commerciale NON si scrive da qui:
 * la genera il motore #135 dentro il POST officina; qui al massimo si applica la
 * SCELTA del fornitore (#142) e si racconta la coppia nel messaggio di esito.
 */
export async function inserisciOfficina(
  p: InserimentoOfficinaParams
): Promise<{ code: string; testo: string } | null> {
  const chiave = (p.codiceAtec ?? "").replace(/\./g, "").trim()
  const codeVis = formatCodice(chiave)

  let existing
  try {
    existing = await fetchOfficinaItems(p.projectId)
  } catch {
    throw new Error(
      "Impossibile leggere la DDP Officina: inserimento annullato per non creare doppioni."
    )
  }
  // Punti normalizzati da ENTRAMBI i lati: le righe nate dai picker stanno senza punto,
  // quelle nate dall'import composizione col punto — sono lo stesso codice.
  const duplicate =
    existing.find(
      (r) =>
        (r.partNumber ?? "").replace(/\./g, "").trim() === chiave &&
        r.itemStatus === DDP_STATUS_TO_ORDER
    ) ?? null

  let testoBase: string
  if (duplicate) {
    const ok = await p.confirm({
      title: "Articolo già presente",
      description: `L'articolo ${codeVis} è già nella DDP Officina in stato Da Ordinare (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
      confirmLabel: "Aggiungi +1",
      destructive: false,
    })
    if (!ok) return null
    // L'update officina riscrive tutti i campi editabili: ricopiati dalla riga esistente.
    await updateOfficinaItem(p.projectId, duplicate.id, {
      id: duplicate.id,
      projectId: p.projectId,
      partNumber: duplicate.partNumber,
      description: duplicate.description,
      quantity: duplicate.quantity + 1,
      quantityProduced: duplicate.quantityProduced ?? 0,
      unitCost: duplicate.unitCost,
      material: duplicate.material,
      treatment: duplicate.treatment,
      supplierName: duplicate.supplierName,
      itemStatus: duplicate.itemStatus,
      requestedBy: duplicate.requestedBy,
      daneaRef: duplicate.daneaRef,
      dateNeeded: duplicate.dateNeeded,
      orderDate: duplicate.orderDate,
      destination: duplicate.destination,
      destinationSpec: duplicate.destinationSpec ?? "",
      notes: duplicate.notes,
      expectedUpdatedAt: null,
    })
    testoBase = "Qtà aggiornata"
  } else {
    await addOfficinaItem(p.projectId, {
      id: 0,
      projectId: p.projectId,
      partNumber: p.codiceAtec,
      description: p.descrizione,
      quantity: 1,
      quantityProduced: 0,
      unitCost: p.unitCost,
      material: "",
      treatment: "",
      supplierName: p.supplierName,
      itemStatus: DDP_STATUS_TO_ORDER,
      requestedBy: p.requestedBy,
      daneaRef: "",
      dateNeeded: null,
      orderDate: null,
      destination: "",
      destinationSpec: "",
      notes: "",
    })
    testoBase = `aggiunto alla DDP Officina${p.notaScheda ?? ""}`
  }

  if (!p.grezzo) return { code: codeVis, testo: testoBase }

  // ── #142: il grezzo in DDP Commerciale l'ha già creato/aggiornato il motore. ──
  const grezzoVis = formatCodice(p.grezzo.codice)
  let codaGrezzo: string
  if (p.grezzo.scoperto) {
    codaGrezzo = ` + grezzo ${grezzoVis} in DDP Commerciale — DA ASSOCIARE a un articolo commerciale: la riga resta bloccata finché non lo associ (Codex → Articoli Danea).`
  } else if (p.grezzo.catalogItemId != null) {
    try {
      await setRawSupplier(p.projectId, p.grezzo.codice, p.grezzo.catalogItemId)
      codaGrezzo = ` + grezzo ${grezzoVis} in DDP Commerciale (fornitore ${p.grezzo.fornitoreNome || "scelto"}).`
    } catch (err) {
      // L'inserimento è riuscito: il fornitore non applicato non deve passare per un
      // fallimento totale — si dice cosa è successo e la riga resta «da definire».
      codaGrezzo = ` + grezzo ${grezzoVis} in DDP Commerciale — fornitore NON applicato: ${(err as Error).message}`
    }
  } else {
    codaGrezzo = ` + grezzo ${grezzoVis} in DDP Commerciale (fornitore da definire).`
  }
  return { code: codeVis, testo: testoBase + codaGrezzo }
}
