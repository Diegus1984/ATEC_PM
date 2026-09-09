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
  /**
   * La timbratura che manca (l'uscita): HR ne scrive l'ora nel dettaglio della giornata, senza
   * motivo, e «Scrivi su Ecos» la inserisce (09/09/2026 sera). Lo decide il server
   * (`HrDayDto.EcosMissingToInsert`). Finché manca, la giornata non è allineata.
   */
  mancanteDaInserire: boolean
  /** true = c'è qualcosa da scrivere su Ecos: orari, rettifiche o la pausa dedotta. */
  daScrivere: boolean
  /** true = c'è qualcosa che riguarda Ecos e niente è da inviare, escluso o incerto. */
  allineato: boolean
}

export function riassuntoInvioEcos(
  giornata: Pick<HrDay, "punches"> & { ecosBreakToInsert?: boolean; ecosMissingToInsert?: string | null }
): RiassuntoInvioEcos {
  const diEcos = giornata.punches.filter(
    (t) => (t.source === "ECOS" && Boolean(t.ecosStampId)) || t.ecosInsert
  )
  const pausaDaInserire = giornata.ecosBreakToInsert === true
  const mancanteDaInserire = Boolean(giornata.ecosMissingToInsert)
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
    mancanteDaInserire,
    daScrivere: daInviare.length > 0 || pausaDaInserire,
    allineato:
      diEcos.length > 0 &&
      daInviare.length === 0 &&
      nonInviabili === 0 &&
      incerte === 0 &&
      !pausaDaInserire &&
      !mancanteDaInserire,
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

// ── Gli orari decisi a mano (09/09/2026 sera) ─────────────────────────────────
//
// Diego: «devo poter modificare a mano gli orari». Nel dettaglio della giornata ogni
// timbratura che riguarda Ecos ha un campo ora, proposto con l'arrotondato del motore: su
// Ecos (e qui, che ne è lo specchio) va quello che HR scrive. Qui la parte pura: cosa si
// propone e cosa parte davvero.

/** Una timbratura su cui HR decide l'orario da scrivere su Ecos. */
export interface OrarioDaScrivere {
  punchId: number
  direction: string
  tipo: "ecos" | "rettifica"
  /** L'orario che Ecos ha adesso («HH:mm»); null per una rettifica non ancora su Ecos. */
  suEcos: string | null
  /** La proposta del motore: l'orario arrotondato («HH:mm»). */
  proposto: string
  /** false = l'arrotondamento cambierebbe giorno: si guarda a mano. */
  inviabile: boolean
  /** Rettifica mandata senza risposta certa: non riparte da sola. */
  incerta: boolean
}

/** La pausa dedotta dal motore, proposta con i suoi orari. */
export interface PausaDaScrivere {
  uscita: string
  rientro: string
}

function hhmm(iso: string | null | undefined): string {
  return iso ? iso.slice(11, 16) : ""
}

/** La timbratura che manca alla giornata (l'uscita): HR ne scrive l'orario, senza motivo. */
export interface MancanteDaScrivere {
  direction: string
}

export function orariDaScrivere(
  giornata: Pick<HrDay, "punches" | "clockOut1" | "clockIn2"> & {
    ecosBreakToInsert?: boolean
    ecosMissingToInsert?: string | null
  }
): { righe: OrarioDaScrivere[]; pausa: PausaDaScrivere | null; mancante: MancanteDaScrivere | null } {
  const righe: OrarioDaScrivere[] = []
  for (const t of giornata.punches) {
    const diEcos = t.source === "ECOS" && Boolean(t.ecosStampId)
    if (!diEcos && !t.ecosInsert) continue
    righe.push({
      punchId: t.id,
      direction: t.direction,
      tipo: diEcos ? "ecos" : "rettifica",
      suEcos: diEcos ? hhmm(t.ecosPunchedAt ?? t.punchedAt) : null,
      proposto: hhmm(t.roundedAt ?? t.punchedAt),
      inviabile: t.canSendToEcos || t.ecosUncertain,
      incerta: t.ecosUncertain,
    })
  }
  const pausa: PausaDaScrivere | null =
    giornata.ecosBreakToInsert === true
      ? { uscita: giornata.clockOut1.replace("*", ""), rientro: giornata.clockIn2.replace("*", "") }
      : null
  const mancante: MancanteDaScrivere | null = giornata.ecosMissingToInsert
    ? { direction: giornata.ecosMissingToInsert }
    : null
  return { righe, pausa, mancante }
}

/**
 * Cosa parte davvero con gli orari scelti: le timbrature il cui orario scelto è diverso da
 * quello che Ecos ha (le rettifiche sempre, non sono ancora là), più le due strisciate della
 * pausa. Le incerte e le non inviabili restano fuori.
 */
export function scelteDaScrivere(
  righe: OrarioDaScrivere[],
  valori: Record<number, string>,
  pausa: PausaDaScrivere | null,
  mancante: MancanteDaScrivere | null = null,
  mancanteOra = ""
): { timbrature: OrarioDaScrivere[]; conPausa: boolean; conMancante: boolean; totale: number } {
  const timbrature = righe.filter((r) => {
    if (!r.inviabile || r.incerta) return false
    const scelto = valori[r.punchId] ?? r.proposto
    return r.suEcos == null || scelto !== r.suEcos
  })
  const conPausa = pausa != null
  // La timbratura che manca parte solo quando HR ha scritto un orario.
  const conMancante = mancante != null && orarioValido(mancanteOra)
  return {
    timbrature,
    conPausa,
    conMancante,
    totale: timbrature.length + (conPausa ? 2 : 0) + (conMancante ? 1 : 0),
  }
}

/** «08:00» sì, «8.00» o vuoto no: il server rifiuterebbe tutto prima di toccare Ecos. */
export function orarioValido(valore: string | undefined): boolean {
  return /^\d{2}:\d{2}$/.test(valore ?? "")
}
