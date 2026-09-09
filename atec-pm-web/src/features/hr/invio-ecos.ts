import type { HrDay, HrPunch } from "@/lib/api/types"

/**
 * Cosa dice il pulsante «Invia a Ecos» di una giornata (08/09/2026).
 *
 * Le decisioni per timbratura (arrotondamento, inviabile, da inviare) le prende il SERVER
 * (`HrPunchDto.RoundedAt / CanSendToEcos / ToSendToEcos`): qui si contano e si riassumono,
 * così il dialogo e un eventuale filtro di pagina leggono la stessa cosa.
 */
export interface RiassuntoInvioEcos {
  /** Timbrature che riguardano Ecos: quelle di Ecos più le rettifiche ancora da inserire. */
  diEcos: number
  /** Quelle che partono: orari da modificare e rettifiche da inserire. */
  daInviare: HrPunch[]
  /** Fra le daInviare, le rettifiche: su Ecos nascono come timbrature nuove. */
  daInserire: HrPunch[]
  /** Non inviabili: l'arrotondamento cambierebbe giorno. */
  nonInviabili: number
  /** Rettifiche inserite senza risposta certa: da verificare su Ecos, non ripartono da sole. */
  incerte: number
  /** L'ultimo invio riuscito fra le timbrature della giornata (ISO), o null. */
  ultimoInvio: string | null
  /**
   * La pausa pranzo dedotta dal motore che su Ecos non c'è: «Allinea Ecos» la inserisce come
   * timbrature vere (09/09/2026). Lo decide il server (`HrDayDto.EcosBreakToInsert`).
   */
  pausaDaInserire: boolean
  /** true = c'è qualcosa da scrivere su Ecos: orari, rettifiche o la pausa dedotta. */
  daScrivere: boolean
  /** true = c'è qualcosa che riguarda Ecos e niente è da inviare, escluso o incerto. */
  allineato: boolean
}

export function riassuntoInvioEcos(
  giornata: Pick<HrDay, "punches"> & { ecosBreakToInsert?: boolean }
): RiassuntoInvioEcos {
  const diEcos = giornata.punches.filter(
    (t) => (t.source === "ECOS" && Boolean(t.ecosStampId)) || t.ecosInsert
  )
  const pausaDaInserire = giornata.ecosBreakToInsert === true
  const daInviare = diEcos.filter((t) => t.toSendToEcos)
  const daInserire = daInviare.filter((t) => t.ecosInsert)
  const incerte = diEcos.filter((t) => t.ecosUncertain).length
  const nonInviabili = diEcos.filter((t) => !t.canSendToEcos && !t.ecosUncertain).length
  const invii = diEcos
    .map((t) => t.ecosSentAt)
    .filter((x): x is string => typeof x === "string" && x.length > 0)
    .sort()
  return {
    diEcos: diEcos.length,
    daInviare,
    daInserire,
    nonInviabili,
    incerte,
    ultimoInvio: invii.length > 0 ? invii[invii.length - 1] : null,
    pausaDaInserire,
    daScrivere: daInviare.length > 0 || pausaDaInserire,
    allineato:
      diEcos.length > 0 && daInviare.length === 0 && nonInviabili === 0 && incerte === 0 && !pausaDaInserire,
  }
}

/** «Entrata» / «Uscita» dal verso di Ecos. */
export function versoTimbratura(direction: string): string {
  return direction === "IN" ? "Entrata" : "Uscita"
}

/**
 * L'ora che Ecos ha ADESSO per la timbratura di una cella, quando è diversa da quella
 * timbrata: dopo «Scrivi su Ecos» la riga piccola della cella mostra QUESTA al posto del
 * timbrato («timbrato 08:00»), così ora calcolata e riga piccola coincidono e la giornata si
 * legge sincronizzata (Diego, 09/09/2026, anteprima «A»); l'orario originale resta nel
 * dettaglio e nel registro. La cella conosce solo l'orario grezzo «07:48» e il verso: si cerca
 * la timbratura della giornata con quell'ora e quel verso e si legge `ecosPunchedAt`.
 * Null = mai inviata, o inviata uguale al timbrato (niente da cambiare).
 */
export function oraSuEcos(
  giornata: Pick<HrDay, "punches">,
  timbrato: string | null | undefined,
  verso: "IN" | "OUT"
): string | null {
  if (!timbrato || timbrato === "--:--") return null
  const entrata = verso === "IN"
  const t = giornata.punches.find(
    (p) => (p.direction === "IN") === entrata && p.punchedAt.slice(11, 16) === timbrato
  )
  const suEcos = t?.ecosPunchedAt?.slice(11, 16)
  return suEcos && suEcos !== timbrato ? suEcos : null
}
