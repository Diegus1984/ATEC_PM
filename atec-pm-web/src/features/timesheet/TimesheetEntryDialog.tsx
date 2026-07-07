import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
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
import { ENTRY_TYPES } from "@/features/timesheet/entry-types"
import {
  deleteTimesheetEntry,
  fetchDayTotal,
  fetchPhasesForEmployee,
  fetchProjectIdForPhase,
  fetchProjectsForEmployee,
  fetchRegistrableEmployees,
  parseIsoDate,
  saveTimesheetEntry,
  toIsoDate,
} from "@/lib/api/timesheet"
import { getSession } from "@/lib/auth/session"
import type { TimesheetEntryDto, TimesheetSaveRequest } from "@/lib/api/types"
import { cn } from "@/lib/utils"

const MAX_HOURS_PER_DAY = 24

function fmtHours(hours: number): string {
  return hours.toLocaleString("it-IT", { maximumFractionDigits: 1 })
}

export function TimesheetEntryDialog({
  open,
  onOpenChange,
  employeeId,
  presetDate,
  entry,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  employeeId: number
  presetDate: string
  entry: TimesheetEntryDto | null
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const isEdit = entry !== null
  const todayIso = toIsoDate(new Date())

  const [empId, setEmpId] = React.useState(employeeId)
  const [workDate, setWorkDate] = React.useState(presetDate)
  const [projectId, setProjectId] = React.useState("")
  const [phaseId, setPhaseId] = React.useState("")
  const [hours, setHours] = React.useState("8")
  const [entryType, setEntryType] = React.useState("REGULAR")
  const [notes, setNotes] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)

  // Reset all'apertura — popola dai valori esistenti o dai preset.
  React.useEffect(() => {
    if (!open) {
      return
    }
    setEmpId(entry?.employeeId ?? employeeId)
    setWorkDate(entry ? entry.workDate.slice(0, 10) : presetDate)
    setHours(entry ? String(entry.hours) : "8")
    setEntryType(entry?.entryType ?? "REGULAR")
    setNotes(entry?.notes ?? "")
    setPhaseId(entry ? String(entry.projectPhaseId) : "")
    setProjectId("")
    setError(null)
  }, [open, entry, presetDate, employeeId])

  const employeesQuery = useQuery({
    queryKey: ["timesheet-employees"],
    queryFn: fetchRegistrableEmployees,
  })

  // In modifica: risolvi la commessa della fase per preselezionare la Select.
  const phaseProjectQuery = useQuery({
    queryKey: ["timesheet-phase-project", entry?.projectPhaseId],
    queryFn: () => fetchProjectIdForPhase(entry!.projectPhaseId),
    enabled: open && isEdit,
  })

  React.useEffect(() => {
    if (open && isEdit && phaseProjectQuery.data) {
      setProjectId(String(phaseProjectQuery.data))
    }
  }, [open, isEdit, phaseProjectQuery.data])

  const projectsQuery = useQuery({
    queryKey: ["timesheet-projects", empId],
    queryFn: () => fetchProjectsForEmployee(empId),
    enabled: open && empId > 0,
  })

  const phasesQuery = useQuery({
    queryKey: ["timesheet-phases", empId, projectId],
    queryFn: () => fetchPhasesForEmployee(empId, Number(projectId)),
    enabled: open && empId > 0 && projectId !== "",
  })

  const dayTotalQuery = useQuery({
    queryKey: ["timesheet-day-total", empId, workDate, entry?.id ?? 0],
    queryFn: () => fetchDayTotal(empId, workDate, entry?.id ?? 0),
    enabled: open && empId > 0 && workDate !== "",
  })

  const otherHours = dayTotalQuery.data ?? 0
  const available = Math.max(0, MAX_HOURS_PER_DAY - otherHours)

  const invalidateLists = React.useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ["timesheet-range"] })
  }, [queryClient])

  const saveMutation = useMutation({
    mutationFn: (payload: TimesheetSaveRequest) => saveTimesheetEntry(payload),
    onSuccess: async () => {
      await invalidateLists()
      onOpenChange(false)
    },
    onError: (err: Error) => setError(err.message),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteTimesheetEntry(id),
    onSuccess: async () => {
      await invalidateLists()
      onOpenChange(false)
    },
    onError: (err: Error) => setError(err.message),
  })

  function handleEmployeeChange(value: string) {
    setEmpId(Number(value))
    setProjectId("")
    setPhaseId("")
  }

  function handleProjectChange(value: string) {
    setProjectId(value)
    setPhaseId("")
  }

  function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)

    const parsedHours = Number(hours.replace(",", "."))
    const parsedPhaseId = Number(phaseId)

    if (!parsedPhaseId) {
      setError("Seleziona una fase.")
      return
    }
    if (!workDate) {
      setError("Seleziona una data.")
      return
    }
    if (workDate > todayIso) {
      setError("Non è possibile registrare o spostare ore in date future.")
      return
    }
    if (!parsedHours || parsedHours <= 0) {
      setError("Inserisci un numero di ore valido (maggiore di zero).")
      return
    }
    if (parsedHours > MAX_HOURS_PER_DAY) {
      setError("Ogni singola registrazione non può superare 24 ore.")
      return
    }
    if (otherHours + parsedHours > MAX_HOURS_PER_DAY) {
      setError(
        otherHours > 0
          ? `Superato il limite di 24h per il giorno: altre registrazioni ${fmtHours(
              otherHours
            )}h, puoi inserire al massimo ${fmtHours(available)}h.`
          : "Il totale ore per il giorno non può superare 24."
      )
      return
    }

    saveMutation.mutate({
      id: entry?.id ?? 0,
      employeeId: empId,
      projectPhaseId: parsedPhaseId,
      workDate,
      hours: parsedHours,
      entryType,
      notes,
    })
  }

  function handleDelete() {
    if (!entry) {
      return
    }
    void confirm({
      title: "Elimina registrazione",
      description: `Eliminare la timbratura di ${fmtHours(
        entry.hours
      )}h del ${parseIsoDate(entry.workDate).toLocaleDateString("it-IT")}?`,
      confirmLabel: "Elimina",
    }).then((ok) => {
      if (ok) {
        deleteMutation.mutate(entry.id)
      }
    })
  }

  const session = getSession()
  const showEmployeeSelect =
    (employeesQuery.data?.length ?? 0) > 1 &&
    (session?.user.userRole === "ADMIN" ||
      session?.user.userRole === "PM" ||
      session?.user.userRole === "RESP_REPARTO")

  const busy = saveMutation.isPending || deleteMutation.isPending

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{isEdit ? "Modifica ore" : "Registra ore"}</DialogTitle>
          <DialogDescription>
            {isEdit
              ? "Correggi una registrazione di oggi o di un giorno precedente. Il totale del giorno non può superare 24 ore."
              : "Nuova registrazione per oggi o un giorno passato. Il totale del giorno non può superare 24 ore."}
          </DialogDescription>
        </DialogHeader>

        <form className="flex flex-col gap-4" onSubmit={handleSubmit}>
          {showEmployeeSelect ? (
            <div className="grid gap-2">
              <Label>Registra per</Label>
              <Select value={String(empId)} onValueChange={handleEmployeeChange}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(employeesQuery.data ?? []).map((employee) => (
                    <SelectItem key={employee.id} value={String(employee.id)}>
                      {employee.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : null}

          <div className="grid gap-2">
            <Label>Data</Label>
            <DateField
              value={workDate || null}
              onChange={(value) => setWorkDate(value ?? "")}
              disableAfter={new Date()}
              clearable={false}
              placeholder="Seleziona data"
            />
          </div>

          <div className="grid gap-2">
            <Label>Commessa</Label>
            <Select value={projectId} onValueChange={handleProjectChange}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Seleziona commessa" />
              </SelectTrigger>
              <SelectContent>
                {(projectsQuery.data ?? []).map((project) => (
                  <SelectItem
                    key={project.projectId}
                    value={String(project.projectId)}
                  >
                    {project.display}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="grid gap-2">
            <Label>Fase</Label>
            <Select
              value={phaseId}
              onValueChange={setPhaseId}
              disabled={projectId === ""}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Seleziona fase" />
              </SelectTrigger>
              <SelectContent>
                {(phasesQuery.data ?? []).map((phase) => (
                  <SelectItem key={phase.phaseId} value={String(phase.phaseId)}>
                    {phase.display}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label htmlFor="hours">Ore</Label>
              <Input
                id="hours"
                inputMode="decimal"
                value={hours}
                onChange={(event) => setHours(event.target.value)}
                required
              />
            </div>
            <div className="grid gap-2">
              <Label>Tipo</Label>
              <Select value={entryType} onValueChange={setEntryType}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {ENTRY_TYPES.map((type) => (
                    <SelectItem key={type.value} value={type.value}>
                      {type.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="grid gap-2">
            <Label htmlFor="notes">Note</Label>
            <Textarea
              id="notes"
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              rows={2}
            />
          </div>

          <p
            className={cn(
              "text-xs",
              available <= 0 ? "text-destructive" : "text-muted-foreground"
            )}
          >
            Massimo 24h per giorno · altre registrazioni: {fmtHours(otherHours)}h
            · disponibili per questa voce: {fmtHours(available)}h
          </p>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}

          <DialogFooter>
            {isEdit ? (
              <Button
                type="button"
                variant="destructive"
                className="mr-auto"
                onClick={handleDelete}
                disabled={busy}
              >
                <Trash2 />
                Elimina
              </Button>
            ) : null}
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Annulla
            </Button>
            <Button type="submit" disabled={busy}>
              {saveMutation.isPending
                ? "Salvataggio…"
                : isEdit
                  ? "Salva modifiche"
                  : "Salva"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
