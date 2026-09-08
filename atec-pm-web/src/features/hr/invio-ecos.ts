import type { HrDay, HrPunch } from "@/lib/api/types"

/**
 * Cosa dice il pulsante «Invia a Ecos» di una giornata (08/09/2026).
 *
 * Le decisioni per timbratura (arrotondamento, inviabile, da inviare) le prende il SERVER
 * (`HrPunchDto.RoundedAt / CanSendToEcos / ToSendToEcos`): qui si contano e si riassumono,
 * così il dialogo e un eventuale filtro di pagina leggono la stessa cosa.
 */
export interface RiassuntoInvioEcos {
  /** Timbrature di Ecos della giornata (le rettifiche non c'entrano: su Ecos non esistono). */
  diEcos: number
  /** Quelle con un orario su Ecos diverso da quello arrotondato: sono queste che partono. */
  daInviare: HrPunch[]
  /** Di Ecos ma non inviabili: l'arrotondamento cambierebbe giorno. */
  nonInviabili: number
  /** L'ultimo invio riuscito fra le timbrature della giornata (ISO), o null. */
  ultimoInvio: string | null
  /** true = c'è almeno una timbratura di Ecos e nessuna è da inviare né esclusa. */
  allineato: boolean
}

export function riassuntoInvioEcos(giornata: Pick<HrDay, "punches">): RiassuntoInvioEcos {
  const diEcos = giornata.punches.filter((t) => t.source === "ECOS" && Boolean(t.ecosStampId))
  const daInviare = diEcos.filter((t) => t.toSendToEcos)
  const nonInviabili = diEcos.filter((t) => !t.canSendToEcos).length
  const invii = diEcos
    .map((t) => t.ecosSentAt)
    .filter((x): x is string => typeof x === "string" && x.length > 0)
    .sort()
  return {
    diEcos: diEcos.length,
    daInviare,
    nonInviabili,
    ultimoInvio: invii.length > 0 ? invii[invii.length - 1] : null,
    allineato: diEcos.length > 0 && daInviare.length === 0 && nonInviabili === 0,
  }
}

/** «Entrata» / «Uscita» dal verso di Ecos. */
export function versoTimbratura(direction: string): string {
  return direction === "IN" ? "Entrata" : "Uscita"
}
