import * as React from "react"
import { useMutation } from "@tanstack/react-query"

import { DateField } from "@/components/shared/date-field"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
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
import {
  createMoM,
  fetchMoMDetail,
  fetchMoMProjects,
  updateMoM,
} from "@/lib/api/mom"
import type { MoMProjectLookup, MoMSaveRequest } from "@/lib/api/types"

interface VerbaleForm {
  tipo: string
  projectId: number | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
}

function emptyForm(): VerbaleForm {
  return {
    tipo: "RIUNIONE",
    projectId: null,
    title: "",
    meetingDate: new Date().toISOString().slice(0, 10),
    inDashboard: true,
  }
}

function formToRequest(form: VerbaleForm): MoMSaveRequest {
  return {
    tipo: form.tipo,
    projectId: form.tipo === "COMMESSA" ? form.projectId : null,
    title: form.title.trim(),
    meetingDate: form.meetingDate,
    inDashboard: form.inDashboard,
  }
}

export function MoMVerbaleDialog({
  open,
  verbaleId,
  onClose,
  onSaved,
  defaultProjectCode,
  defaultTipo,
}: {
  open: boolean
  verbaleId: number | "new" | null
  onClose: () => void
  onSaved: (id?: number) => Promise<void>
  defaultProjectCode?: string | null
  defaultTipo?: string | null
}) {
  const isNew = verbaleId === "new"
  const editId = typeof verbaleId === "number" ? verbaleId : null

  const [form, setForm] = React.useState<VerbaleForm>(emptyForm)
  const [projects, setProjects] = React.useState<MoMProjectLookup[]>([])
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setError(null)

    let cancelled = false

    void fetchMoMProjects()
      .then((projs) => {
        if (cancelled) return
        setProjects(projs)

        if (isNew) {
          const base = emptyForm()
          if (defaultProjectCode) {
            const match = projs.find((p) => p.code === defaultProjectCode)
            if (match) {
              base.tipo = "COMMESSA"
              base.projectId = match.id
            }
          } else if (defaultTipo) {
            base.tipo = defaultTipo
          }
          setForm(base)
        }
      })
      .catch(() => {
        if (!cancelled) setProjects([])
      })

    if (!isNew && editId != null) {
      void fetchMoMDetail(editId)
        .then((detail) => {
          if (cancelled) return
          setForm({
            tipo: detail.tipo,
            projectId: detail.projectId,
            title: detail.title,
            meetingDate: detail.meetingDate ? detail.meetingDate.slice(0, 10) : null,
            inDashboard: detail.inDashboard,
          })
        })
        .catch((err: Error) => {
          if (!cancelled) setError(err.message)
        })
    }

    return () => {
      cancelled = true
    }
  }, [open, isNew, editId, defaultProjectCode, defaultTipo])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!form.title.trim()) throw new Error("Il titolo è obbligatorio.")
      if (form.tipo === "COMMESSA" && form.projectId == null) {
        throw new Error("Seleziona una commessa.")
      }
      const request = formToRequest(form)
      if (isNew) return createMoM(request)
      if (editId == null) throw new Error("Verbale non valido.")
      await updateMoM(editId, request)
      return editId
    },
    onSuccess: async (id) => {
      await onSaved(id)
    },
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{isNew ? "Nuovo verbale" : "Modifica verbale"}</DialogTitle>
          <DialogDescription>
            {isNew
              ? "Crea un nuovo verbale di riunione o commessa."
              : "Aggiorna i dati di intestazione del verbale."}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Tipo</Label>
            <Select
              value={form.tipo}
              onValueChange={(value) =>
                setForm({
                  ...form,
                  tipo: value,
                  projectId: value === "COMMESSA" ? form.projectId : null,
                })
              }
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="RIUNIONE">Riunione</SelectItem>
                <SelectItem value="COMMESSA">Commessa</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="grid gap-2">
            <Label>Commessa</Label>
            <LookupCombobox
              options={projects.map((project) => ({
                id: project.id,
                name: project.display,
              }))}
              value={form.projectId ?? null}
              onValueChange={(id) => setForm({ ...form, projectId: id })}
              disabled={form.tipo !== "COMMESSA"}
              placeholder="Seleziona commessa"
              searchPlaceholder="Cerca commessa…"
              emptyText="Nessuna commessa trovata"
            />
          </div>

          <div className="grid gap-2">
            <Label>Titolo *</Label>
            <Input
              value={form.title}
              onChange={(event) => setForm({ ...form, title: event.target.value })}
              placeholder="Titolo del verbale"
            />
          </div>

          <div className="grid gap-2">
            <Label>Data riunione</Label>
            <DateField
              value={form.meetingDate}
              onChange={(value) => setForm({ ...form, meetingDate: value })}
              size="sm"
            />
          </div>

          <Label className="flex items-center gap-2 text-sm font-normal">
            <Checkbox
              checked={form.inDashboard}
              onCheckedChange={(checked) =>
                setForm({ ...form, inDashboard: checked === true })
              }
            />
            dashboard commesse
          </Label>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saveMutation.isPending}>
            Annulla
          </Button>
          <Button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}>
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
