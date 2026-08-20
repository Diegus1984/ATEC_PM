import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { fetchMoMEmployees } from "@/lib/api/mom"
import { createChat } from "@/lib/api/project-chat"
import { fetchProjectsLookup } from "@/lib/api/projects"
import {
  ALTRE_ATTIVITA_SECTION_LABEL,
  COMMESSE_SECTION_LABEL,
  partitionCommesseAltreAttivita,
} from "@/lib/project-code"
import { notifyError } from "@/lib/toast"

/** Tutte le commesse aperte: la tendina del contesto le vuole in una volta sola. */
const PROJECTS_PAGE_SIZE = 500

/**
 * «Nuova chat» — prima del titolo si sceglie il contesto (#78): una commessa, un'altra
 * attività, oppure «— Nessuna —» per una conversazione che non è legata a niente.
 *
 * La tendina è una `LookupCombobox` e non un `Select`: sono centinaia di voci, si cercano
 * digitando (regola delle tendine lunghe del progetto).
 */
export function NewChatDialog({
  open,
  defaultProjectId,
  lockProject,
  onClose,
  onCreated,
}: {
  open: boolean
  /** Commessa proposta all'apertura (dentro il tab di commessa è quella corrente). */
  defaultProjectId: number | null
  /** Nel tab di una commessa il contesto è quello e non si cambia. */
  lockProject?: boolean
  onClose: () => void
  onCreated: (chatId: number) => void
}) {
  const [projectId, setProjectId] = React.useState<number | null>(defaultProjectId)
  const [title, setTitle] = React.useState("")
  const [participants, setParticipants] = React.useState<number[]>([])
  const [filter, setFilter] = React.useState("")

  const employeesQuery = useQuery({
    queryKey: ["mom-employees"],
    queryFn: fetchMoMEmployees,
    enabled: open,
  })

  const projectsQuery = useQuery({
    queryKey: ["chat-context-projects", defaultProjectId],
    queryFn: () =>
      fetchProjectsLookup({
        page: 1,
        pageSize: PROJECTS_PAGE_SIZE,
        // La commessa di partenza deve esserci anche se è chiusa, o il campo si aprirebbe vuoto.
        includeId: defaultProjectId ?? undefined,
      }),
    enabled: open && !lockProject,
  })

  React.useEffect(() => {
    if (open) {
      setProjectId(defaultProjectId)
      setTitle("")
      setParticipants([])
      setFilter("")
    }
  }, [open, defaultProjectId])

  const createMutation = useMutation({
    mutationFn: () =>
      createChat({ projectId, title: title.trim(), participantIds: participants }),
    onSuccess: (id) => onCreated(id),
    onError: (err: Error) => notifyError(err),
  })

  const projectOptions = React.useMemo(() => {
    const items = projectsQuery.data?.items ?? []
    const { commesse, altreAttivita } = partitionCommesseAltreAttivita(
      items,
      (p) => p.code,
      (p) => p.code
    )
    return [
      ...commesse.map((p) => ({
        id: p.id,
        name: p.code,
        hint: p.title,
        group: COMMESSE_SECTION_LABEL,
      })),
      ...altreAttivita.map((p) => ({
        id: p.id,
        name: p.code,
        hint: p.title,
        group: ALTRE_ATTIVITA_SECTION_LABEL,
      })),
    ]
  }, [projectsQuery.data])

  const employees = employeesQuery.data ?? []
  const q = filter.trim().toLowerCase()
  const visible = q
    ? employees.filter((e) => e.name.toLowerCase().includes(q))
    : employees
  const canCreate = title.trim().length > 0 || participants.length > 0

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Nuova chat</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          {lockProject ? null : (
            <div className="grid gap-1.5">
              <Label>Commessa o attività</Label>
              <LookupCombobox
                options={projectOptions}
                value={projectId}
                onValueChange={setProjectId}
                noneLabel="— Nessuna —"
                placeholder="— Nessuna —"
                searchPlaceholder="Cerca commessa o attività…"
                emptyText="Nessuna commessa"
                loading={projectsQuery.isLoading}
              />
            </div>
          )}
          <div className="grid gap-1.5">
            <Label>Titolo (opzionale se scegli i partecipanti)</Label>
            <Input
              value={title}
              autoFocus
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Vuoto = nomi dei partecipanti (es. Rossi / Bianchi)"
            />
          </div>
          <div className="grid gap-1.5">
            <Label>Partecipanti</Label>
            <Input
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              placeholder="Cerca persona…"
            />
            <div className="grid max-h-48 grid-cols-2 gap-1.5 overflow-y-auto rounded-md border p-2">
              {visible.map((e) => (
                <label key={e.id} className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={participants.includes(e.id)}
                    onCheckedChange={(v) =>
                      setParticipants((prev) =>
                        v ? [...prev, e.id] : prev.filter((x) => x !== e.id)
                      )
                    }
                  />
                  <span className="truncate">{e.name}</span>
                </label>
              ))}
            </div>
            <p className="text-[11px] text-muted-foreground">
              Se ne dimentichi qualcuno lo aggiungi dopo, scrivendo «@» nel messaggio.
            </p>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => createMutation.mutate()}
            disabled={!canCreate || createMutation.isPending}
          >
            Crea
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
