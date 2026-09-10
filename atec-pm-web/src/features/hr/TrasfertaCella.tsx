import { useMutation } from "@tanstack/react-query"

import { setHrTravelDay } from "@/lib/api/hr"
import { notifyError, notifySuccess } from "@/lib/toast"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"

/** Valore fittizio della voce «nessuna»: Radix Select non accetta value="". */
const NESSUNA = "__nessuna__"

/** «20», «37,5»: gli euro come li scrive il foglio del consulente. */
function euro(valore: number): string {
  return valore.toLocaleString("it-IT", { maximumFractionDigits: 2 })
}

/**
 * Una casella della riga «TRASFERTA - €» del calendario mensile: una combo con le tariffe
 * dell'indennità di trasferta, che HR sceglie giorno per giorno (Diego, 10/09/2026, dal
 * foglio «PRESENZE 08 AGOSTO 26.xlsx»). Si salva subito, senza conferma; il totale della
 * persona lo rifà il server.
 *
 * <p>Dove non si può mettere — giorni non lavorati, o sola lettura — resta il testo, così la
 * riga si legge lo stesso senza far sembrare cliccabile quello che non lo è.</p>
 */
export function TrasfertaCella({
  employeeId,
  date,
  importo,
  modificabile,
  tariffe,
  onSalvata,
}: {
  employeeId: number
  /** Giorno in ISO («2026-08-03»). */
  date: string
  /** L'importo già scritto, come lo manda il server («20», «» se non c'è). */
  importo: string
  modificabile: boolean
  /** Le tariffe proposte, dall'anagrafica: nome («Italia») e importo. */
  tariffe: { label: string; value: number }[]
  onSalvata: () => void
}) {
  const salva = useMutation({
    mutationFn: (amount: number | null) => setHrTravelDay({ employeeId, date, amount }),
    onSuccess: (messaggio) => {
      notifySuccess(messaggio || "Salvata.")
      onSalvata()
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Non riuscita."),
  })

  if (!modificabile) {
    return <div className="flex h-6 w-full items-center justify-center">{importo}</div>
  }

  // L'importo di oggi può non essere fra le tariffe (tariffa cambiata dopo): si aggiunge in
  // coda, altrimenti la combo mostrerebbe vuoto su un valore che c'è.
  const attuale = importo ? Number(importo.replace(",", ".")) : null
  const voci = [...tariffe]
  if (attuale != null && !voci.some((v) => v.value === attuale))
    voci.push({ label: "", value: attuale })

  return (
    <Select
      value={attuale == null ? NESSUNA : String(attuale)}
      onValueChange={(v) => salva.mutate(v === NESSUNA ? null : Number(v))}
      disabled={salva.isPending}
    >
      <SelectTrigger
        className="h-6 w-full justify-center gap-0.5 border-0 bg-transparent px-0 font-mono text-[10px] shadow-none focus:ring-0 [&>svg]:size-2.5 [&>svg]:opacity-40"
        aria-label={`Indennità di trasferta del ${date}`}
        title="Scegli l'indennità di trasferta della giornata"
      >
        <SelectValue placeholder="" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={NESSUNA}>(nessuna)</SelectItem>
        {voci
          .sort((a, b) => a.value - b.value)
          .map((v) => (
            <SelectItem key={v.value} value={String(v.value)}>
              {v.label ? `${v.label} — ${euro(v.value)} €` : `${euro(v.value)} €`}
            </SelectItem>
          ))}
      </SelectContent>
    </Select>
  )
}
