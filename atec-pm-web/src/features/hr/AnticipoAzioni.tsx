import { useMutation } from "@tanstack/react-query"
import { Check, X } from "lucide-react"

import { setHrEarlyEntry } from "@/lib/api/hr"
import type { HrDay } from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"
import { Button } from "@/components/ui/button"

import { durataBreve } from "./ore"

/**
 * L'entrata prima delle 8 si decide dalla riga, con gli stessi «Approva» e «Rifiuta» delle
 * richieste di ferie e permessi (Diego, 10/09/2026: «l'approvazione dell'orario prima delle 8
 * vorrei apparisse anche così»). Finché nessuno decide la giornata conta dalle 8: approvare
 * rimette l'orario timbrato e rifà subito il conto della giornata.
 *
 * <p>A decisione presa restano un segno e il perché, così si vede che qualcuno ci ha già
 * guardato — e ci si può tornare sopra dal dettaglio della giornata.</p>
 */
export function AnticipoAzioni({
  employeeId,
  date,
  giornata,
  onChanged,
}: {
  employeeId: number
  /** Giorno in ISO («2026-09-09»). */
  date: string
  giornata: HrDay
  onChanged: () => void
}) {
  const minuti = giornata.earlyEntryMinutes ?? 0
  const deciso = giornata.earlyEntryAuthorized ?? null

  const anticipo = useMutation({
    mutationFn: (authorized: boolean) => setHrEarlyEntry({ employeeId, workDate: date, authorized }),
    onSuccess: (messaggio) => {
      notifySuccess(messaggio || "Deciso.")
      onChanged()
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Non riuscito."),
  })

  if (minuti <= 0) return null

  const quanto = durataBreve(minuti)

  if (deciso === null) {
    return (
      <span className="space-x-1 whitespace-nowrap">
        <Button
          size="sm"
          variant="outline"
          className="h-7 text-xs text-emerald-600 hover:bg-emerald-50 hover:text-emerald-700 dark:hover:bg-emerald-950"
          onClick={() => anticipo.mutate(true)}
          disabled={anticipo.isPending}
          title={`Ha timbrato ${quanto} prima delle 8: approvando vale l'orario timbrato, e va subito su Ecos`}
        >
          <Check className="mr-1 size-3" />
          Approva
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="h-7 text-xs text-rose-600 hover:bg-rose-50 hover:text-rose-700 dark:hover:bg-rose-950"
          onClick={() => anticipo.mutate(false)}
          disabled={anticipo.isPending}
          title={`Ha timbrato ${quanto} prima delle 8: rifiutando la giornata parte dalle 8, anche su Ecos`}
        >
          <X className="mr-1 size-3" />
          Rifiuta
        </Button>
      </span>
    )
  }

  return (
    <span
      className="inline-flex items-center gap-1 whitespace-nowrap text-xs text-muted-foreground"
      title={
        deciso
          ? `Anticipo di ${quanto} approvato: su Ecos e qui vale l'orario timbrato`
          : `Anticipo di ${quanto} rifiutato: su Ecos e qui la giornata parte dalle 8`
      }
    >
      {deciso ? (
        <Check className="size-3.5 text-emerald-600 dark:text-emerald-400" />
      ) : (
        <X className="size-3.5" />
      )}
      {quanto}
    </span>
  )
}
