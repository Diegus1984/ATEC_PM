import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
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
import { eliminaHrRettifica, inviaHrRettifica } from "@/lib/api/hr"
import type { HrGiornata } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"

function oraDa(iso: string): string {
  const d = new Date(iso)
  return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`
}

/**
 * Dettaglio di una giornata: le timbrature grezze come sono arrivate (Ecos + rettifiche)
 * e, per chi ha la scrittura, la rettifica. La timbratura originale resta SEMPRE — la
 * rettifica è una riga in più con autore e motivo, e solo le rettifiche si possono togliere.
 */
export function GiornataDialog({
  open,
  onOpenChange,
  giornata,
  employeeId,
  canWrite,
  onChanged,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  giornata: HrGiornata | null
  employeeId: number
  canWrite: boolean
  onChanged: () => void
}) {
  const confirm = useConfirm()
  const [ora, setOra] = React.useState("")
  const [verso, setVerso] = React.useState<"IN" | "OUT">("IN")
  const [motivo, setMotivo] = React.useState("")

  React.useEffect(() => {
    if (!open) return
    setOra("")
    setVerso("IN")
    setMotivo("")
  }, [open])

  const rettifica = useMutation({
    mutationFn: inviaHrRettifica,
    onSuccess: () => {
      notifySuccess("Rettifica registrata")
      onChanged()
      onOpenChange(false)
    },
    onError: (e) => notifyError((e as Error).message),
  })

  const elimina = useMutation({
    mutationFn: eliminaHrRettifica,
    onSuccess: () => {
      notifySuccess("Rettifica eliminata")
      onChanged()
      onOpenChange(false)
    },
    onError: (e) => notifyError((e as Error).message),
  })

  if (!giornata) return null
  const giorno = giornata.giorno.slice(0, 10)

  function inviaRettifica() {
    if (!ora || !motivo.trim() || !giornata) return
    rettifica.mutate({
      employeeId,
      orario: `${giorno}T${ora}:00`,
      verso,
      motivo: motivo.trim(),
    })
  }

  async function eliminaRiga(id: number) {
    const ok = await confirm({
      title: "Eliminare la rettifica?",
      description:
        "La giornata verrà ricalcolata senza questa timbratura. Il grezzo del rilevatore non si tocca.",
      confirmLabel: "Elimina",
      destructive: true,
    })
    if (ok) elimina.mutate(id)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Giornata del {formatDateShort(giornata.giorno)}</DialogTitle>
          <DialogDescription>
            {giornata.nota || "Nessuna timbratura registrata."}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-1">
          <p className="text-sm font-medium">Timbrature grezze</p>
          {giornata.timbrature.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nessuna timbratura.</p>
          ) : (
            <ul className="space-y-1">
              {giornata.timbrature.map((t) => (
                <li
                  key={t.id}
                  className="flex items-center gap-2 rounded-md border px-2 py-1 text-sm"
                >
                  <span className="tabular-nums font-medium">{oraDa(t.orario)}</span>
                  <span>{t.verso === "IN" ? "Entrata" : "Uscita"}</span>
                  <Badge variant={t.origine === "RETTIFICA" ? "default" : "outline"}>
                    {t.origine}
                  </Badge>
                  {t.motivo && (
                    <span
                      className="min-w-0 flex-1 truncate text-xs text-muted-foreground"
                      title={`${t.motivo}${t.creataDa ? ` — ${t.creataDa}` : ""}`}
                    >
                      {t.motivo}
                      {t.creataDa ? ` — ${t.creataDa}` : ""}
                    </span>
                  )}
                  {canWrite && t.origine === "RETTIFICA" && (
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      className="ml-auto"
                      disabled={elimina.isPending}
                      onClick={() => void eliminaRiga(t.id)}
                    >
                      <Trash2 className="size-4" />
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        {canWrite && (
          <div className="space-y-2 rounded-md border p-3">
            <p className="text-sm font-medium">Aggiungi rettifica</p>
            <div className="flex items-end gap-2">
              <div className="space-y-1">
                <Label htmlFor="rettifica-ora">Ora</Label>
                <Input
                  id="rettifica-ora"
                  type="time"
                  value={ora}
                  onChange={(e) => setOra(e.target.value)}
                  className="w-28"
                />
              </div>
              <div className="space-y-1">
                <Label>Verso</Label>
                <Select value={verso} onValueChange={(v) => setVerso(v as "IN" | "OUT")}>
                  <SelectTrigger className="w-32">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="IN">Entrata</SelectItem>
                    <SelectItem value="OUT">Uscita</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="space-y-1">
              <Label htmlFor="rettifica-motivo">Motivo (obbligatorio)</Label>
              <Textarea
                id="rettifica-motivo"
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
                placeholder="Es. uscita non timbrata, giustificata dal responsabile"
                rows={2}
              />
              {/* Il motivo resta scritto nel cartellino e lo legge chiunque lo gestisca:
                  la causale sanitaria non deve finirci (piano §8). */}
              <p className="text-xs text-muted-foreground">
                Scrivi il motivo organizzativo. Mai causali sanitarie o dati di salute.
              </p>
            </div>
            <div className="flex justify-end">
              <Button
                size="sm"
                disabled={!ora || !motivo.trim() || rettifica.isPending}
                onClick={inviaRettifica}
              >
                Registra rettifica
              </Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
