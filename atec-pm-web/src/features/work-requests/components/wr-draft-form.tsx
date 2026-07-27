// ── Form di creazione rapida bozze (staging) ───────────────────────────────

import * as React from "react"
import { Plus } from "lucide-react"

import { DateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import type { ProjectListItem, WorkRequestSaveRequest } from "@/lib/api/types"
import {
  isSystemProjectCode,
  SYSTEM_PROJECT_INTERNA,
} from "@/lib/system-projects"
import { notifyError } from "@/lib/toast"

import { newWorkRequestPayload } from "../wr-shared"

export function WrDraftForm({
  projects,
  internaProjectId,
  onCreate,
}: {
  projects: ProjectListItem[]
  internaProjectId: number | null
  onCreate: (payload: WorkRequestSaveRequest) => void
}) {
  const [projectId, setProjectId] = React.useState<string>("")
  const [description, setDescription] = React.useState("")
  const [requestDate, setRequestDate] = React.useState<string | null>(
    () => new Date().toISOString().split("T")[0]
  )

  // Default: commessa sistema INTERNA (lavorazioni generiche) quando disponibile.
  React.useEffect(() => {
    if (projectId) return
    if (internaProjectId != null) setProjectId(String(internaProjectId))
  }, [projectId, internaProjectId])

  function handleAdd() {
    const resolvedProjectId = Number(projectId) || internaProjectId || 0
    if (!resolvedProjectId) {
      notifyError(
        `Seleziona una commessa (o assicurati che esista la voce ${SYSTEM_PROJECT_INTERNA})`
      )
      return
    }
    if (!description.trim()) return

    // Le lavorazioni sulla commessa INTERNA nascono già interne, fornitore ATEC.
    const isInterna =
      resolvedProjectId === internaProjectId ||
      isSystemProjectCode(projects.find((p) => p.id === resolvedProjectId)?.code)

    onCreate(
      newWorkRequestPayload({
        projectId: resolvedProjectId,
        requestDate: requestDate ?? "",
        description: description.trim(),
        type: isInterna ? "Internal" : "",
        isStaging: true,
        poSupplier: isInterna ? "ATEC" : "",
      })
    )

    setDescription("")
  }

  return (
    <div className="mb-4 grid grid-cols-1 items-end gap-4 rounded-lg border bg-muted/30 p-4 md:grid-cols-4">
      <div className="grid gap-2">
        <Label>Commessa / Progetto</Label>
        <Select value={projectId} onValueChange={setProjectId}>
          <SelectTrigger className="w-full">
            <SelectValue placeholder={`${SYSTEM_PROJECT_INTERNA} (generica)`} />
          </SelectTrigger>
          <SelectContent>
            {projects.map((p) => (
              <SelectItem key={p.id} value={String(p.id)}>
                {isSystemProjectCode(p.code)
                  ? "INTERNA — Generica"
                  : `${p.code} - ${p.title}`}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="grid gap-2 md:col-span-2">
        <Label>Descrizione Lavorazione</Label>
        <Input
          placeholder="Inserisci descrizione della lavorazione..."
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
      </div>
      <div className="flex gap-2 items-end">
        <div className="grid gap-2 flex-1">
          <Label>Data Richiesta</Label>
          <DateField value={requestDate} onChange={setRequestDate} />
        </div>
        <Button size="icon" className="shrink-0" onClick={handleAdd}>
          <Plus className="size-4" />
        </Button>
      </div>
    </div>
  )
}
