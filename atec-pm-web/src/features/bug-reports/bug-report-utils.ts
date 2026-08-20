import type { BugKind, BugReport, BugSeverity, BugStatus } from "@/lib/api/types"

/** Etichette e colori delle segnalazioni: unico punto di verità per pagina e dialog. */

export const KIND_META: Record<BugKind, { label: string; className: string }> = {
  BUG: { label: "Bug", className: "border-red-200 bg-red-50 text-red-700" },
  IMPROVEMENT: {
    label: "Miglioria",
    className: "border-sky-200 bg-sky-50 text-sky-700",
  },
}

export const SEVERITY_META: Record<
  BugSeverity,
  { label: string; className: string }
> = {
  LOW: { label: "Bassa", className: "border-zinc-200 bg-zinc-50 text-zinc-600" },
  MEDIUM: {
    label: "Media",
    className: "border-amber-200 bg-amber-50 text-amber-700",
  },
  HIGH: {
    label: "Alta",
    className: "border-red-300 bg-red-100 text-red-800 font-semibold",
  },
}

export const STATUS_META: Record<
  BugStatus,
  { label: string; className: string }
> = {
  OPEN: { label: "Aperta", className: "border-amber-300 bg-amber-50 text-amber-800" },
  IN_PROGRESS: {
    label: "In lavorazione",
    className: "border-blue-300 bg-blue-50 text-blue-800",
  },
  RESOLVED: {
    label: "Risolta",
    className: "border-teal-300 bg-teal-50 text-teal-800",
  },
  REJECTED: {
    label: "Non accolta",
    className: "border-zinc-300 bg-zinc-100 text-zinc-600",
  },
}

export const STATUS_ORDER: BugStatus[] = [
  "OPEN",
  "IN_PROGRESS",
  "RESOLVED",
  "REJECTED",
]

/** Dimensione file leggibile (gli allegati sono screenshot: KB/MB bastano). */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/**
 * Blocco Markdown `BUG-NNN` da incollare in `BUGS.md` o nel prompt di correzione.
 *
 * Sta qui e non nelle due schermate che lo offrono (menu della riga e dialogo): erano due
 * copie della stessa funzione, e una copia si aggiorna sempre da sola — con l'altra che
 * continua a produrre il vecchio formato senza che nessuno se ne accorga.
 */
export function buildBugMarkdown(bug: BugReport): string {
  const statusSign =
    bug.status === "RESOLVED"
      ? "x"
      : bug.status === "IN_PROGRESS"
        ? "~"
        : bug.status === "REJECTED"
          ? "-"
          : " "

  const atts = (bug.attachments ?? []).map(
    (a) => `  - ${a.isReply ? "[Risposta] " : ""}${a.fileName} (${formatSize(a.sizeBytes)})`
  )

  return [
    `### BUG-${String(bug.id).padStart(3, "0")} — ${bug.title}`,
    `- **Stato:** [${statusSign}] ${STATUS_META[bug.status].label}`,
    `- **Data:** ${bug.createdAt.slice(0, 10)}`,
    `- **Autore:** ${bug.createdByName}`,
    `- **Modulo:** ${bug.area || "Non specificato"}`,
    `- **Gravità:** ${bug.severity}`,
    bug.fixedInBuild ? `- **Build fix:** ${bug.fixedInBuild}` : null,
    `- **Contesto:**\n\`\`\`\n${bug.context || "Nessun contesto registrato"}\n\`\`\``,
    `- **Descrizione:**\n${bug.description || "—"}`,
    `- **Risposta:**\n${bug.adminNote || "—"}`,
    atts.length > 0 ? `- **Allegati:**\n${atts.join("\n")}` : null,
  ]
    .filter(Boolean)
    .join("\n")
}
