import type { HrDay } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { cn } from "@/lib/utils"

export type ToneStato = "ok" | "bad" | "warn" | "info" | "dim"

export interface StatoLetto {
  /** Cosa dire a chi legge, in parole: «Manca l'uscita», «Ferie», «Tutto regolare». */
  label: string
  tone: ToneStato
  /** Giornata di riposo (sabato, domenica, festivo) senza timbrature: riga spenta. */
  riposo: boolean
  /** Assenza da Ecos (ferie, permesso, malattia, infortunio). */
  assenza: boolean
  assenzaParziale: boolean
}

const ASSENZE: Record<string, string> = {
  VACATION: "Ferie",
  PERMIT: "Permesso",
  SICKNESS: "Malattia",
  INJURY: "Infortunio",
  OTHER: "Assenza",
}

/**
 * Traduce la nota del motore in una frase per chi legge il cartellino. La regola di
 * cosa è anomalia resta del server (`hasAnomaly` = nota che comincia con ⚠): qui si
 * scelgono solo le parole. La nota originale resta disponibile nella colonna «Nota».
 */
export function statoGiornata(g: HrDay): StatoLetto {
  const nota = (g.note ?? "").trim()
  const segnalata = g.lastReminderAt
    ? ` · segnalata il ${formatDateShort(g.lastReminderAt.slice(0, 10))}`
    : ""

  // Assenze da Ecos: la nota è il codice, con le ore fra parentesi se parziale.
  const assenza = /^(VACATION|PERMIT|SICKNESS|INJURY|OTHER)(?: \(([\d.,]+)h\))?$/.exec(nota)
  if (assenza) {
    const tipo = ASSENZE[assenza[1]] ?? "Assenza"
    const ore = assenza[2]
    return {
      label: ore ? `${tipo} ${ore.replace(".", ",")}h` : tipo,
      tone: "info",
      riposo: false,
      assenza: true,
      assenzaParziale: Boolean(ore),
    }
  }

  if (g.hasAnomaly) {
    return {
      label: fraseAnomalia(nota) + segnalata,
      tone: "bad",
      riposo: false,
      assenza: false,
      assenzaParziale: false,
    }
  }

  if (!g.hasData) {
    const d = new Date(g.workDate)
    const weekend = d.getDay() === 0 || d.getDay() === 6
    if (g.isHoliday || weekend) {
      return {
        label: g.isHoliday && !weekend ? "Festivo" : g.isHoliday ? "Festivo" : "Riposo",
        tone: "dim",
        riposo: true,
        assenza: false,
        assenzaParziale: false,
      }
    }
    if (nota.includes("Notte: le ore di stanotte contano sul giorno prima")) {
      return { label: "Fine del turno di notte", tone: "dim", riposo: false, assenza: false, assenzaParziale: false }
    }
    const oggi = new Date().toISOString().slice(0, 10)
    if (g.workDate.slice(0, 10) > oggi) {
      return { label: "", tone: "dim", riposo: false, assenza: false, assenzaParziale: false }
    }
    return {
      label: g.canRemind ? "Nessuna timbratura" + segnalata : "Nessuna timbratura",
      tone: g.canRemind ? "warn" : "dim",
      riposo: false,
      assenza: false,
      assenzaParziale: false,
    }
  }

  if (nota === "FORFAIT") {
    return { label: "Orario a forfait", tone: "dim", riposo: false, assenza: false, assenzaParziale: false }
  }
  if (nota.startsWith("Giornata in corso")) {
    return { label: "Giornata in corso", tone: "info", riposo: false, assenza: false, assenzaParziale: false }
  }
  if (nota.startsWith("AUTO_P: Uscita mancante")) {
    const ora = /Stimata (\d{1,2}:\d{2})/.exec(nota)?.[1]
    return {
      label: (ora ? `Uscita non timbrata, stimata alle ${ora}` : "Uscita non timbrata, stimata") + segnalata,
      tone: "warn",
      riposo: false,
      assenza: false,
      assenzaParziale: false,
    }
  }

  const straordinario = !/^0h 0+m$/.test(g.overtime) && g.overtime !== "" && g.overtime !== "---"
  return {
    label: (straordinario ? "Regolare, con straordinario" : "Tutto regolare") + segnalata,
    tone: "ok",
    riposo: false,
    assenza: false,
    assenzaParziale: false,
  }
}

function fraseAnomalia(nota: string): string {
  const testo = nota.split(" · ")[0]
  if (testo.includes("Solo entrata")) return "Manca l'uscita"
  if (testo.includes("manca una timbratura della notte")) return "Manca una timbratura della notte"
  if (testo.includes("due turni nella stessa giornata")) return "Due turni nello stesso giorno"
  if (testo.includes("Verificare timbrature")) return "Timbrature da verificare"
  return testo.replace(/^⚠\s*/, "").replace(/^(INCOMPLETO|ERR):\s*/i, "") || "Da verificare"
}

const TONI: Record<ToneStato, string> = {
  ok: "bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
  bad: "bg-destructive/10 text-destructive",
  warn: "bg-amber-500/10 text-amber-700 dark:text-amber-400",
  info: "bg-sky-500/10 text-sky-700 dark:text-sky-400",
  dim: "bg-muted text-muted-foreground",
}

/** La pillola «Com'è la giornata»: un pallino del colore e la frase. */
export function StatoGiornata({ stato, className }: { stato: StatoLetto; className?: string }) {
  if (!stato.label) return <span className="text-muted-foreground">—</span>
  return (
    <span
      className={cn(
        "inline-flex h-6 items-center gap-1.5 rounded-full px-2.5 text-xs font-semibold",
        TONI[stato.tone],
        className
      )}
    >
      {stato.tone !== "dim" && <span className="size-1.5 rounded-full bg-current" />}
      {stato.label}
    </span>
  )
}
