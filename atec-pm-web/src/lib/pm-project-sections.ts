import type { PmContainer, PmSidebarSection } from "@/components/shared/pm-sidebar"
import {
  ALTRE_ATTIVITA_SECTION_LABEL,
  COMMESSE_SECTION_LABEL,
  compareProjectCodes,
  isCommessaCode,
} from "@/lib/project-code"
import { isSystemProjectCode } from "@/lib/system-projects"

/**
 * REGOLA #79 delle barre laterali PM: le commesse **vere** (codice `C` + data)
 * stanno in «Commesse» ordinate crescenti; le attività a codice/testo libero
 * stanno in «Altre Attività», in ordine alfabetico italiano.
 *
 * Chi aggiunge una pagina PM con elenco commesse chiama questa funzione.
 */
export interface PmProjectEntry {
  /** Codice: decide sezione e ordinamento. */
  code: string
  container: PmContainer
}

export function buildPmProjectSections(
  entries: PmProjectEntry[],
  options?: {
    /** Testo della sezione «Commesse» vuota. */
    emptyLabel?: string
    /** Testo della sezione «Altre Attività» vuota. */
    otherEmptyLabel?: string
    /** Se false, omette «Altre Attività». Default: true */
    includeOther?: boolean
    /**
     * Se true (default), le commesse di sistema (INTERNA) restano in testa al
     * gruppo «Altre Attività»; altrimenti solo alfabetico stretto.
     */
    pinSystemFirst?: boolean
  }
): PmSidebarSection[] {
  const includeOther = options?.includeOther ?? true
  const pinSystemFirst = options?.pinSystemFirst ?? true
  const sections: PmSidebarSection[] = [
    {
      key: "projects",
      label: COMMESSE_SECTION_LABEL,
      containers: entries
        .filter((e) => isCommessaCode(e.code))
        .sort((a, b) => compareProjectCodes(a.code, b.code))
        .map((e) => e.container),
      emptyLabel: options?.emptyLabel ?? "Nessuna commessa",
    },
  ]

  if (includeOther) {
    sections.push({
      key: "altre",
      label: ALTRE_ATTIVITA_SECTION_LABEL,
      containers: entries
        .filter((e) => !isCommessaCode(e.code))
        .sort((a, b) => {
          if (pinSystemFirst) {
            const aSys = isSystemProjectCode(a.code) ? 0 : 1
            const bSys = isSystemProjectCode(b.code) ? 0 : 1
            if (aSys !== bSys) return aSys - bSys
          }
          return a.container.label.localeCompare(b.container.label, "it")
        })
        .map((e) => e.container),
      emptyLabel: options?.otherEmptyLabel ?? "Nessuna altra attività",
    })
  }

  return sections
}
