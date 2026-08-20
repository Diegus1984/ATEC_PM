import { cn } from "@/lib/utils"

/**
 * Riferimento commessa impilato su righe distinte — codice / cliente / titolo —
 * invece della riga unica "CODICE — Titolo" che obbligava a colonne larghe e
 * finiva comunque troncata a metà parola.
 *
 * Senza `code` (gruppi generici della Check list, commessa di sistema INTERNA)
 * mostra il solo `fallback` su una riga.
 */
export function ProjectStackedLabel({
  code,
  customer,
  title,
  fallback,
  className,
}: {
  code?: string | null
  customer?: string | null
  title?: string | null
  /** Testo unico da usare quando non c'è un codice commessa. */
  fallback?: string
  /** Classi del contenitore (larghezza massima, chip, ecc.). */
  className?: string
}) {
  const codeText = (code ?? "").trim()
  const customerText = (customer ?? "").trim()
  const titleText = (title ?? "").trim()

  if (!codeText) {
    return (
      <div
        className={cn("min-w-0 truncate text-xs leading-tight", className)}
        title={fallback}
      >
        {fallback || "—"}
      </div>
    )
  }

  // Tooltip con tutto il contenuto: le singole righe restano troncate.
  const full = [codeText, customerText, titleText].filter(Boolean).join(" · ")
  return (
    <div
      className={cn("flex min-w-0 flex-col gap-0.5 text-xs leading-tight", className)}
      title={full}
    >
      <span className="truncate font-mono font-semibold text-foreground">
        {codeText}
      </span>
      {customerText ? (
        <span className="truncate font-normal text-muted-foreground">
          {customerText}
        </span>
      ) : null}
      {titleText ? (
        <span className="truncate font-normal italic text-muted-foreground/80">
          {titleText}
        </span>
      ) : null}
    </div>
  )
}
