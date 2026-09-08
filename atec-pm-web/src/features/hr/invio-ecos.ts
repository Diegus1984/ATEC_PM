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
  /** true = c'è qualcosa che riguarda Ecos e niente è da inviare, escluso o incerto. */
  allineato: boolean
}

export function riassuntoInvioEcos(giornata: Pick<HrDay, "punches">): RiassuntoInvioEcos {
  const diEcos = giornata.punches.filter(
    (t) => (t.source === "ECOS" && Boolean(t.ecosStampId)) || t.ecosInsert
  )
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
    allineato: diEcos.length > 0 && daInviare.length === 0 && nonInviabili === 0 && incerte === 0,
  }
}

/** «Entrata» / «Uscita» dal verso di Ecos. */
export function versoTimbratura(direction: string): string {
  return direction === "IN" ? "Entrata" : "Uscita"
}
