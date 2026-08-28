import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { fetchRealEmployees } from "@/lib/api/employees"
import { createHrAbsence } from "@/lib/api/hr"
import { notifyError, notifySuccess } from "@/lib/toast"

interface NuovaRichiestaDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  canManage: boolean
}

export function NuovaRichiestaDialog({
  open,
  onOpenChange,
  canManage,
}: NuovaRichiestaDialogProps) {
  const queryClient = useQueryClient()

  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  const [absenceType, setAbsenceType] = React.useState<string>("VACATION")
  const [isFullDay, setIsFullDay] = React.useState(true)
  const [dateFrom, setDateFrom] = React.useState(() => {
    const d = new Date()
    return d.toISOString().slice(0, 10)
  })
  const [dateTo, setDateTo] = React.useState(() => {
    const d = new Date()
    return d.toISOString().slice(0, 10)
  })
  const [hours, setHours] = React.useState<string>("4")
  const [notes, setNotes] = React.useState("")

  const dipendentiQuery = useQuery({
    queryKey: ["employees-real"],
    queryFn: fetchRealEmployees,
    enabled: canManage && open,
  })

  const opzioniDipendenti: LookupComboboxOption<number>[] = React.useMemo(
    () => (dipendentiQuery.data ?? []).map((d) => ({ id: d.id, name: d.name })),
    [dipendentiQuery.data]
  )

  const creaMutation = useMutation({
    mutationFn: () =>
      createHrAbsence({
        employeeId: canManage && employeeId ? employeeId : null,
        absenceType,
        isFullDay,
        dateFrom,
        dateTo: isFullDay ? dateTo : dateFrom,
        hours: isFullDay ? null : Number(hours.replace(",", ".")),
        notes: notes.trim() || null,
      }),
    onSuccess: () => {
      notifySuccess("Richiesta inserita con successo")
      void queryClient.invalidateQueries({ queryKey: ["hr-absences"] })
      void queryClient.invalidateQueries({ queryKey: ["hr-timesheet"] })
      void queryClient.invalidateQueries({ queryKey: ["hr-calendar"] })
      onOpenChange(false)
      // Reset form
      setNotes("")
    },
    onError: (e) => notifyError((e as Error).message),
  })

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!dateFrom) {
      notifyError("Inserisci la data di inizio")
      return
    }
    if (isFullDay && !dateTo) {
      notifyError("Inserisci la data di fine")
      return
    }
    if (isFullDay && dateFrom > dateTo) {
      notifyError("La data di inizio non può superare la data di fine")
      return
    }
    if (!isFullDay) {
      const numHours = Number(hours.replace(",", "."))
      if (isNaN(numHours) || numHours <= 0 || numHours > 24) {
        notifyError("Inserisci un numero di ore valido (> 0)")
        return
      }
    }
    creaMutation.mutate()
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <form onSubmit={handleSubmit} className="space-y-4">
          <DialogHeader>
            <DialogTitle>Nuova richiesta assenza / ferie</DialogTitle>
            <DialogDescription>
              Invia una richiesta di ferie, permesso o assenza al responsabile.
            </DialogDescription>
          </DialogHeader>

          {canManage && (
            <div className="space-y-1.5">
              <Label>Dipendente (opzionale se per sé stessi)</Label>
              <LookupCombobox<number>
                options={opzioniDipendenti}
                value={employeeId}
                onValueChange={setEmployeeId}
                placeholder="Per me stesso"
                noneLabel="— per me stesso —"
                loading={dipendentiQuery.isLoading}
                className="w-full"
              />
            </div>
          )}

          <div className="space-y-1.5">
            <Label>Tipologia assenza</Label>
            <Select value={absenceType} onValueChange={setAbsenceType}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="VACATION">Ferie (FE)</SelectItem>
                <SelectItem value="PERMIT">Permesso / ROL (PE)</SelectItem>
                <SelectItem value="SICKNESS">Malattia (MA)</SelectItem>
                <SelectItem value="INJURY">Infortunio (IN)</SelectItem>
                <SelectItem value="OTHER">Altra assenza</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center space-x-2 pt-1">
            <Checkbox
              id="full-day-check"
              checked={isFullDay}
              onCheckedChange={(checked) => setIsFullDay(checked === true)}
            />
            <Label
              htmlFor="full-day-check"
              className="text-sm font-normal cursor-pointer"
            >
              Giornata intera (uno o più giorni)
            </Label>
          </div>

          {isFullDay ? (
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>Dal giorno</Label>
                <Input
                  type="date"
                  value={dateFrom}
                  onChange={(e) => setDateFrom(e.target.value)}
                  required
                />
              </div>
              <div className="space-y-1.5">
                <Label>Al giorno (incluso)</Label>
                <Input
                  type="date"
                  value={dateTo}
                  min={dateFrom}
                  onChange={(e) => setDateTo(e.target.value)}
                  required
                />
              </div>
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>Giorno</Label>
                <Input
                  type="date"
                  value={dateFrom}
                  onChange={(e) => {
                    setDateFrom(e.target.value)
                    setDateTo(e.target.value)
                  }}
                  required
                />
              </div>
              <div className="space-y-1.5">
                <Label>Numero di ore</Label>
                <Input
                  type="number"
                  step="0.5"
                  min="0.5"
                  max="24"
                  value={hours}
                  onChange={(e) => setHours(e.target.value)}
                  placeholder="Es. 4"
                  required
                />
              </div>
            </div>
          )}

          <div className="space-y-1.5">
            <Label>Note / Motivo (opzionale)</Label>
            <Textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Eventuali dettagli o giustificativi..."
              rows={2}
            />
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Annulla
            </Button>
            <Button type="submit" disabled={creaMutation.isPending}>
              {creaMutation.isPending ? "Invio in corso…" : "Invia richiesta"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
