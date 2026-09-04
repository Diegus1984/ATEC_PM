// ── Riga in coda «Aggiungi step SAL» (crea al blur o con Invio) ────────────

import * as React from "react"
import { Plus } from "lucide-react"

import { Input } from "@/components/ui/input"
import { TableCell, TableRow } from "@/components/ui/table"
import { createSalRow } from "@/lib/api/sal"
import { notifyError } from "@/lib/toast"

import { emptyRowRequest, SAL_COL } from "./sal-sheet-shared"

export function NewSalRowComponent({
  projectId,
  onMutated,
  colSpan,
}: {
  projectId: number
  onMutated: () => void
  colSpan: number
}) {
  const [step, setStep] = React.useState("")
  // Invio + blur consecutivi creerebbero due righe: la guardia lascia passare la prima.
  const committing = React.useRef(false)

  const commit = async () => {
    const val = step.trim()
    if (!val || committing.current) return
    committing.current = true
    try {
      await createSalRow(projectId, emptyRowRequest(val))
      setStep("")
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    } finally {
      committing.current = false
    }
  }

  return (
    <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
      <TableCell className={SAL_COL.num} />
      <TableCell colSpan={colSpan} className="py-2">
        <div className="flex items-center gap-2">
          <Plus className="size-4 text-muted-foreground" />
          <Input
            value={step}
            onChange={(e) => setStep(e.target.value)}
            onBlur={() => void commit()}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault()
                void commit()
              }
            }}
            placeholder="Aggiungi step SAL…"
            className="h-10 max-w-md border-dashed bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
          />
        </div>
      </TableCell>
    </TableRow>
  )
}
