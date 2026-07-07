import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronLeft, ChevronRight, Pencil, Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { TimesheetEntryDialog } from "@/features/timesheet/TimesheetEntryDialog"
import {
  entryTypeChipClass,
  entryTypeLabel,
} from "@/features/timesheet/entry-types"
import {
  addDays,
  deleteTimesheetEntry,
  fetchRange,
  fetchRegistrableEmployees,
  mondayOfWeek,
  parseIsoDate,
  toIsoDate,
} from "@/lib/api/timesheet"
import { getSession } from "@/lib/auth/session"
import type { TimesheetEntryDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"

type CalView = "month" | "week"

const MAX_CHIPS_PER_DAY = 3
const DAY_NAMES = ["lun", "mar", "mer", "gio", "ven", "sab", "dom"]

/** Ore formattate stile WPF `0.#` (cultura it-IT → virgola decimale). */
function fmtHours(hours: number): string {
  return hours.toLocaleString("it-IT", { maximumFractionDigits: 1 })
}

function capitalize(text: string): string {
  return text.length > 0 ? text[0].toUpperCase() + text.slice(1) : text
}

/** Intervallo di date (ISO) coperto dalla vista corrente. */
function getRange(view: CalView, anchor: Date): { start: string; end: string } {
  if (view === "week") {
    const monday = parseIsoDate(mondayOfWeek(anchor))
    const start = toIsoDate(monday)
    return { start, end: addDays(start, 6) }
  }
  const first = new Date(anchor.getFullYear(), anchor.getMonth(), 1)
  const lead = (first.getDay() + 6) % 7
  const gridStart = new Date(first.getFullYear(), first.getMonth(), 1 - lead)
  const last = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 0)
  const lastDow = (last.getDay() + 6) % 7
  const gridEnd = new Date(
    last.getFullYear(),
    last.getMonth(),
    last.getDate() + (6 - lastDow)
  )
  return { start: toIsoDate(gridStart), end: toIsoDate(gridEnd) }
}

function buildDays(start: string, end: string): Date[] {
  const days: Date[] = []
  const cursor = parseIsoDate(start)
  const endDate = parseIsoDate(end)
  while (cursor <= endDate) {
    days.push(new Date(cursor))
    cursor.setDate(cursor.getDate() + 1)
  }
  return days
}

function formatLabel(view: CalView, anchor: Date): string {
  if (view === "week") {
    const range = getRange("week", anchor)
    const start = parseIsoDate(range.start)
    const end = parseIsoDate(range.end)
    const startText = start.toLocaleDateString("it-IT", {
      day: "numeric",
      month: "short",
    })
    const endText = end.toLocaleDateString("it-IT", {
      day: "numeric",
      month: "short",
      year: "numeric",
    })
    return `${startText} – ${endText}`
  }
  return capitalize(
    anchor.toLocaleDateString("it-IT", { month: "long", year: "numeric" })
  )
}

function HoursBadge({ hours }: { hours: number }) {
  return (
    <span className="rounded bg-primary/10 px-1.5 py-px text-[10px] font-semibold tabular-nums text-primary">
      {fmtHours(hours)}h
    </span>
  )
}

function EntryChip({
  entry,
  onEdit,
  onDelete,
}: {
  entry: TimesheetEntryDto
  onEdit: (entry: TimesheetEntryDto) => void
  onDelete: (entry: TimesheetEntryDto) => void
}) {
  const tooltip =
    `${entry.phaseDisplay}\n${fmtHours(entry.hours)} ore · ${entryTypeLabel(
      entry.entryType
    )}` + (entry.notes ? `\n${entry.notes}` : "")

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>
        <button
          type="button"
          title={tooltip}
          onClick={(event) => {
            event.stopPropagation()
            onEdit(entry)
          }}
          onDoubleClick={(event) => event.stopPropagation()}
          className={cn(
            "w-full truncate rounded px-1.5 py-0.5 text-left text-[11px] leading-tight",
            entryTypeChipClass(entry.entryType)
          )}
        >
          {fmtHours(entry.hours)}h&nbsp;&nbsp;{entry.phaseDisplay}
        </button>
      </ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onClick={() => onEdit(entry)}>
          <Pencil />
          Modifica
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem variant="destructive" onClick={() => onDelete(entry)}>
          <Trash2 />
          Elimina
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  )
}

function DayCell({
  date,
  inMonth,
  showDayNumber,
  isToday,
  isSelected,
  isFuture,
  entries,
  onSelect,
  onAdd,
  onEdit,
  onDelete,
}: {
  date: Date
  inMonth: boolean
  showDayNumber: boolean
  isToday: boolean
  isSelected: boolean
  isFuture: boolean
  entries: TimesheetEntryDto[]
  onSelect: (date: Date) => void
  onAdd: (date: Date) => void
  onEdit: (entry: TimesheetEntryDto) => void
  onDelete: (entry: TimesheetEntryDto) => void
}) {
  const dayTotal = entries.reduce((sum, entry) => sum + entry.hours, 0)
  const visible = entries.slice(0, MAX_CHIPS_PER_DAY)
  const extra = entries.length - visible.length

  const bgClass = isFuture
    ? "bg-muted/50"
    : isToday
      ? "bg-primary/5"
      : inMonth
        ? "bg-card"
        : "bg-muted/30"

  const numClass = isFuture
    ? "text-muted-foreground/50"
    : isToday || isSelected
      ? "font-semibold text-primary"
      : inMonth
        ? "text-foreground"
        : "text-muted-foreground"

  return (
    <div
      onClick={() => onSelect(date)}
      onDoubleClick={() => !isFuture && onAdd(date)}
      className={cn(
        "flex flex-col gap-1 border-r border-b p-1.5 [&:nth-child(7n)]:border-r-0",
        showDayNumber ? "min-h-24" : "min-h-[26rem]",
        bgClass,
        isSelected && "ring-2 ring-primary ring-inset",
        isFuture ? "cursor-default" : "cursor-pointer"
      )}
    >
      {showDayNumber ? (
        <div className="flex items-center justify-between">
          <span className={cn("text-xs tabular-nums", numClass)}>
            {date.getDate()}
          </span>
          {dayTotal > 0 ? <HoursBadge hours={dayTotal} /> : null}
        </div>
      ) : dayTotal > 0 ? (
        <div className="flex justify-end">
          <HoursBadge hours={dayTotal} />
        </div>
      ) : null}

      <div className="flex flex-col gap-1 overflow-y-auto">
        {visible.map((entry) => (
          <EntryChip
            key={entry.id}
            entry={entry}
            onEdit={onEdit}
            onDelete={onDelete}
          />
        ))}
        {extra > 0 ? (
          <span className="text-[10px] text-muted-foreground italic">
            +{extra} altre
          </span>
        ) : null}
      </div>
    </div>
  )
}

export function TimesheetPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const session = getSession()
  const defaultEmployeeId = session?.user.employeeId ?? 0

  const todayIso = toIsoDate(new Date())

  const [view, setView] = React.useState<CalView>("month")
  const [anchor, setAnchor] = React.useState<Date>(() => new Date())
  const [selectedDay, setSelectedDay] = React.useState<string>(todayIso)
  const [employeeId, setEmployeeId] = React.useState(defaultEmployeeId)
  const [notice, setNotice] = React.useState<string | null>(null)

  const [dialogOpen, setDialogOpen] = React.useState(false)
  const [editingEntry, setEditingEntry] =
    React.useState<TimesheetEntryDto | null>(null)
  const [presetDate, setPresetDate] = React.useState<string>(todayIso)

  const range = React.useMemo(() => getRange(view, anchor), [view, anchor])
  const days = React.useMemo(
    () => buildDays(range.start, range.end),
    [range.start, range.end]
  )

  const employeesQuery = useQuery({
    queryKey: ["timesheet-employees"],
    queryFn: fetchRegistrableEmployees,
  })

  const query = useQuery({
    queryKey: ["timesheet-range", employeeId, range.start, range.end],
    queryFn: () => fetchRange(employeeId, range.start, range.end),
    enabled: employeeId > 0,
  })

  const deleteMutation = useMutation({
    mutationFn: deleteTimesheetEntry,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["timesheet-range"] })
    },
  })

  const entries = React.useMemo(() => query.data ?? [], [query.data])
  const totalHours = entries.reduce((sum, entry) => sum + entry.hours, 0)

  const entriesByDay = React.useMemo(() => {
    const map = new Map<string, TimesheetEntryDto[]>()
    for (const entry of entries) {
      const key = entry.workDate.slice(0, 10)
      const list = map.get(key)
      if (list) {
        list.push(entry)
      } else {
        map.set(key, [entry])
      }
    }
    return map
  }, [entries])

  function navigate(direction: -1 | 1) {
    setNotice(null)
    setAnchor((current) =>
      view === "week"
        ? new Date(
            current.getFullYear(),
            current.getMonth(),
            current.getDate() + direction * 7
          )
        : new Date(current.getFullYear(), current.getMonth() + direction, 1)
    )
  }

  function goToday() {
    setNotice(null)
    setAnchor(new Date())
    setSelectedDay(todayIso)
  }

  function openAdd(dayIso: string) {
    if (dayIso > todayIso) {
      setNotice("Non è possibile registrare ore in date future.")
      return
    }
    setNotice(null)
    setEditingEntry(null)
    setPresetDate(dayIso)
    setDialogOpen(true)
  }

  function openEdit(entry: TimesheetEntryDto) {
    setNotice(null)
    setEditingEntry(entry)
    setPresetDate(entry.workDate.slice(0, 10))
    setDialogOpen(true)
  }

  function requestDelete(entry: TimesheetEntryDto) {
    void confirm({
      title: "Elimina registrazione",
      description: `Eliminare la registrazione di ${fmtHours(
        entry.hours
      )} h del ${parseIsoDate(entry.workDate).toLocaleDateString(
        "it-IT"
      )} su "${entry.phaseDisplay}"?`,
      confirmLabel: "Elimina",
    }).then((ok) => {
      if (ok) {
        deleteMutation.mutate(entry.id)
      }
    })
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Timesheet</CardTitle>
              <CardDescription>
                Calendario versamento ore — mese e settimana
              </CardDescription>
            </div>
            <Button
              onClick={() => openAdd(selectedDay)}
              disabled={employeeId <= 0}
            >
              <Plus />
              Aggiungi
            </Button>
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          {/* Toolbar navigazione + vista */}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" onClick={goToday}>
                Oggi
              </Button>
              <Button
                variant="outline"
                size="icon"
                onClick={() => navigate(-1)}
                aria-label="Precedente"
              >
                <ChevronLeft />
              </Button>
              <Button
                variant="outline"
                size="icon"
                onClick={() => navigate(1)}
                aria-label="Successivo"
              >
                <ChevronRight />
              </Button>
              <span className="ml-1 text-base font-semibold">
                {formatLabel(view, anchor)}
              </span>
            </div>

            <div className="flex items-center gap-3">
              {(employeesQuery.data?.length ?? 0) > 1 ? (
                <div className="flex items-center gap-2">
                  <span className="text-sm text-muted-foreground">
                    Dipendente
                  </span>
                  <Select
                    value={String(employeeId)}
                    onValueChange={(value) => setEmployeeId(Number(value))}
                  >
                    <SelectTrigger size="sm" className="w-52">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {(employeesQuery.data ?? []).map((employee) => (
                        <SelectItem
                          key={employee.id}
                          value={String(employee.id)}
                        >
                          {employee.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              ) : null}

              <ToggleGroup
                type="single"
                variant="outline"
                size="sm"
                spacing={0}
                value={view}
                onValueChange={(value) => {
                  if (value) {
                    setNotice(null)
                    setView(value as CalView)
                  }
                }}
              >
                <ToggleGroupItem value="month">Mese</ToggleGroupItem>
                <ToggleGroupItem value="week">Settimana</ToggleGroupItem>
              </ToggleGroup>
            </div>
          </div>

          {/* Griglia calendario */}
          <div className="overflow-hidden rounded-lg border">
            <div className="grid grid-cols-7">
              {view === "week"
                ? days.map((date) => {
                    const iso = toIsoDate(date)
                    const isToday = iso === todayIso
                    return (
                      <div
                        key={iso}
                        className={cn(
                          "border-r border-b px-2 py-1.5 text-xs font-semibold [&:nth-child(7n)]:border-r-0",
                          isToday
                            ? "bg-primary/5 text-primary"
                            : "bg-muted/50 text-muted-foreground"
                        )}
                      >
                        {DAY_NAMES[(date.getDay() + 6) % 7]} {date.getDate()}
                      </div>
                    )
                  })
                : DAY_NAMES.map((name) => (
                    <div
                      key={name}
                      className="border-r border-b bg-muted/50 px-2 py-1.5 text-xs font-semibold text-muted-foreground [&:nth-child(7n)]:border-r-0"
                    >
                      {name}
                    </div>
                  ))}
            </div>

            <div className="grid grid-cols-7">
              {days.map((date) => {
                const iso = toIsoDate(date)
                return (
                  <DayCell
                    key={iso}
                    date={date}
                    inMonth={date.getMonth() === anchor.getMonth()}
                    showDayNumber={view === "month"}
                    isToday={iso === todayIso}
                    isSelected={iso === selectedDay}
                    isFuture={iso > todayIso}
                    entries={entriesByDay.get(iso) ?? []}
                    onSelect={(picked) => {
                      setNotice(null)
                      setSelectedDay(toIsoDate(picked))
                    }}
                    onAdd={(picked) => openAdd(toIsoDate(picked))}
                    onEdit={openEdit}
                    onDelete={requestDelete}
                  />
                )
              })}
            </div>
          </div>

          {/* Stato / messaggi */}
          {query.isLoading ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : query.isError ? (
            <p className="text-sm text-destructive">
              {(query.error as Error).message}
            </p>
          ) : (
            <p className="text-sm text-muted-foreground">
              {entries.length} registrazioni ·{" "}
              <span className="font-medium text-foreground">
                {fmtHours(totalHours)} ore
              </span>{" "}
              nel periodo
            </p>
          )}
          {notice ? <p className="text-sm text-destructive">{notice}</p> : null}
        </CardContent>
      </Card>

      <TimesheetEntryDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        employeeId={employeeId}
        presetDate={presetDate}
        entry={editingEntry}
      />
    </div>
  )
}
