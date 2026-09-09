import * as React from "react"
import { Mail, MailCheck, RotateCw } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import { resyncHrDay } from "@/lib/api/hr"
import type { HrDay } from "@/lib/api/types"
import { formatDateShort, formatDateTimeShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

/**
 * I due comandi della giornata nel dettaglio — l'email al dipendente (📧, voce 1 del port) e
 * la rilettura da Ecos (🔄, voce 2) — in una copia sola: li usano il cartellino di una persona
 * e il «Controllo di ieri» di tutti. Il dialogo del sollecito resta della pagina, che sa a chi
 * lo sta mandando; qui si chiede solo di aprirlo.
 */
export function AzioniGiornata({
  employeeId,
  employeeName,
  giornata,
  onSollecito,
  onChanged,
}: {
  employeeId: number
  employeeName: string
  giornata: HrDay
  /** Apre il dialogo del sollecito per questa giornata. */
  onSollecito: () => void
  /** Dopo una rilettura riuscita: la pagina rilegge le sue viste. */
  onChanged: () => void
}) {
  const confirm = useConfirm()
  const [risincronizzando, setRisincronizzando] = React.useState(false)
  const dataIso = giornata.workDate.slice(0, 10)

  async function risincronizza() {
    const ok = await confirm({
      title: `Rileggere da Ecos il ${formatDateShort(dataIso)}?`,
      description:
        `Si riscaricano da Ecos le timbrature di ${employeeName} per quel ` +
        "giorno e si ricalcola la giornata. Le timbrature cancellate su Ecos spariscono " +
        "anche qui; le rettifiche inserite a mano restano.",
      confirmLabel: "Rileggi",
      destructive: false,
    })
    if (!ok) return

    setRisincronizzando(true)
    try {
      const esito = await resyncHrDay(employeeId, dataIso)
      notifySuccess(esito.message)
      onChanged()
    } catch (e) {
      notifyError(e instanceof Error ? e.message : "Risincronizzazione non riuscita.")
    } finally {
      setRisincronizzando(false)
    }
  }

  return (
    <>
      {giornata.canRemind && (
        <Button
          variant="outline"
          size="sm"
          onClick={onSollecito}
          title={
            giornata.lastReminderAt
              ? `Sollecito già inviato il ${formatDateTimeShort(giornata.lastReminderAt)}`
              : "Manda al dipendente un'email con la giornata da verificare"
          }
        >
          {giornata.lastReminderAt ? (
            <MailCheck className="mr-1 size-3.5" />
          ) : (
            <Mail className="mr-1 size-3.5 text-amber-600 dark:text-amber-500" />
          )}
          {giornata.lastReminderAt ? "Manda di nuovo l'email" : "Manda un'email al dipendente"}
        </Button>
      )}
      <Button
        variant="outline"
        size="sm"
        disabled={risincronizzando}
        onClick={() => void risincronizza()}
        title="Riscarica da Ecos le timbrature di questo giorno e ricalcola"
      >
        <RotateCw className={cn("mr-1 size-3.5", risincronizzando && "animate-spin")} />
        Rileggi da Ecos
      </Button>
    </>
  )
}
