import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { useConfirm } from "@/components/shared/confirm"
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
  addCodexReference,
  deleteCodexReference,
  fetchCodexReferences,
  updateCodexDescription,
} from "@/lib/api/codex"
import type { CodexListItem } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { notifySuccess } from "@/lib/toast"

import { CodexRefSearch, type RefItem } from "./CodexRefSearch"

/**
 * Modifica di un articolo Codex: la descrizione (le altre colonne provengono dalla
 * sincronizzazione e non sono editabili — vedi CodexController.Update) e, per i
 * particolari a disegno 1xx, il grezzo commerciale da cui derivano (#135).
 *
 * La derivazione non si decide più solo alla nascita del codice (CodexGeneratePanel):
 * qui si vede sempre e si cambia in qualsiasi momento.
 */
export function CodexEditDialog({
  item,
  onClose,
  onSaved,
}: {
  item: CodexListItem | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const canManage = canWriteFeature("action.manage_codex")

  const [descr, setDescr] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)
  // Derivazione mostrata/scelta, e id della RIGA di codex_item_references: quello serve
  // solo alla DELETE, perché addCodexReference è un upsert e si arrangia da sé.
  const [ref201, setRef201] = React.useState<RefItem | null>(null)
  const [refRowId, setRefRowId] = React.useState<number | null>(null)
  const [refTouched, setRefTouched] = React.useState(false)

  // Solo i particolari a disegno (famiglia 1xx) si ricavano da un grezzo commerciale.
  const is101 = item !== null && item.codice.startsWith("1")

  React.useEffect(() => {
    if (item) {
      setDescr(item.descr)
      setError(null)
      setRefTouched(false)
      // La lista porta già la derivazione (col punto), quindi il campo nasce pieno senza attese.
      // 🪤 `id` qui è finto: la lista espone il codice del 201, non il suo id Codex. Lo riempie
      // la rilettura qui sotto, e nel frattempo non può fare danni perché senza un tocco
      // dell'utente non parte né upsert né DELETE.
      setRefRowId(item.refCommercialeId)
      setRef201(
        item.refCommercialeCodice
          ? {
              id: 0,
              codice: item.refCommercialeCodice,
              descr: item.refCommercialeDescr,
            }
          : null
      )
    }
  }, [item])

  /**
   * Rilettura all'apertura della scheda: UNA chiamata, non una per riga (nella griglia la
   * derivazione arriva già dalla lista). Serve al 🪤 del pezzo — un 101 ha UN SOLO grezzo e
   * l'upsert sostituisce in silenzio: se un collega l'ha cambiata dopo che l'elenco è stato
   * caricato, chi apre deve vedere il valore VERO prima di rimpiazzarlo.
   */
  const refQuery = useQuery({
    queryKey: ["codex-references", item?.id ?? 0],
    queryFn: () => fetchCodexReferences(item?.id ?? 0),
    enabled: item !== null && is101,
  })

  React.useEffect(() => {
    const rows = refQuery.data
    // Chi ha già toccato il campo comanda: la rilettura non gli cancella la scelta.
    if (!rows || refTouched) return
    const row = rows.find((r) => r.refType === "201") ?? null
    setRefRowId(row?.id ?? null)
    setRef201(
      row
        ? { id: row.refCodexId, codice: row.refCodice, descr: row.refDescr }
        : null
    )
  }, [refQuery.data, refTouched])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!item) return
      const trimmed = descr.trim()
      if (!trimmed) {
        throw new Error("La descrizione non può essere vuota.")
      }
      await updateCodexDescription(item.id, trimmed)
      // La derivazione si scrive SOLO se l'utente l'ha toccata: riscriverla identica a ogni
      // salvataggio di descrizione sposterebbe l'autore della modifica senza che nessuno
      // abbia deciso niente.
      if (canManage && is101 && refTouched) {
        if (ref201) {
          await addCodexReference({
            sourceCodexId: item.id,
            refCodexId: ref201.id,
            refType: "201",
          })
        } else if (refRowId != null) {
          await deleteCodexReference(refRowId)
        }
      }
    },
    onSuccess: async () => {
      const refSaved = canManage && is101 && refTouched
      if (refSaved) {
        await queryClient.invalidateQueries({
          queryKey: ["codex-references", item?.id ?? 0],
        })
      }
      // La colonna «Rif. Commerciale» vive nella lista (queryKey ["codex", …]): la si
      // invalida qui e non solo in `onSaved`, perché il dialogo lo apre anche la pagina
      // Composizione, che di quella lista non sa niente.
      await queryClient.invalidateQueries({ queryKey: ["codex"] })
      notifySuccess(
        refSaved
          ? "Articolo e riferimento commerciale aggiornati."
          : "Articolo aggiornato."
      )
      await onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  /**
   * Togliere la derivazione è distruttivo e non si torna indietro da soli: si chiede prima di
   * salvare, non al click sulla X, perché finché non si salva non è stato cancellato niente.
   */
  async function handleSave() {
    if (canManage && is101 && refTouched && !ref201 && refRowId != null) {
      const ok = await confirm({
        title: "Togli riferimento commerciale",
        description: `Il particolare ${item?.codice} resterà senza grezzo di partenza: nella DDP Commerciale non verrà più proposto l'acquisto del 201.`,
        confirmLabel: "Togli",
      })
      if (!ok) return
    }
    saveMutation.mutate()
  }

  return (
    <Dialog open={item !== null} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Modifica articolo</DialogTitle>
          <DialogDescription>
            {is101
              ? "Aggiorna la descrizione e il grezzo commerciale di derivazione."
              : "Aggiorna la descrizione dell'articolo Codex."}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Codice</Label>
            <p className="text-sm font-semibold text-primary tabular-nums">
              {item?.codice}
            </p>
          </div>
          <div className="grid gap-2">
            <Label>Descrizione</Label>
            <Input
              value={descr}
              autoFocus
              onChange={(event) => setDescr(event.target.value)}
            />
          </div>
          {is101 ? (
            <CodexRefSearch
              prefix="2"
              label="Rif. Commerciale (201) — il grezzo da cui si ricava"
              placeholder="Digita per cercare un 2xx…"
              value={ref201}
              disabled={!canManage}
              onSelect={(next) => {
                setRefTouched(true)
                setRef201(next)
              }}
            />
          ) : null}
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => void handleSave()}
            disabled={!descr.trim() || saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
