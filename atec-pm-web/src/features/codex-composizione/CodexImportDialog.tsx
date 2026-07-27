import * as React from "react"
import { AlertCircle, CheckCircle2, FileSpreadsheet, Loader2 } from "lucide-react"

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
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { fetchCodex } from "@/lib/api/codex"
import { commitImport, fetchImportPreview, fetchCompositionChildren } from "@/lib/api/codex-compositions"
import type { CodexImportPreviewResult, CodexListItem } from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"

import { parseStepBom, sameCode } from "./step-bom"

interface CodexImportDialogProps {
  /** File STEP scelto dall'utente: il dialog è aperto quando è non-null. */
  file: File | null
  /** Ref al connectionId SignalR: self-exclusion, l'autore non riceve la propria notifica. */
  connRef?: React.RefObject<string | null>
  /** Import riuscito: `parent` è il composito (radice del file) su cui è stata caricata la distinta. */
  onSuccess: (parent: CodexListItem) => void
  onCancel: () => void
}

/**
 * Importa la distinta di un composito dal file STEP dell'assieme esportato dal CAD.
 * Il composito di destinazione è la radice del file stesso: niente selezione manuale.
 * Flusso: parse STEP → risoluzione dell'assieme nel Codex → anteprima validata dal
 * server (articoli verificati su Codex + catalogo) → conferma esplicita.
 */
export function CodexImportDialog({
  file,
  connRef,
  onSuccess,
  onCancel,
}: CodexImportDialogProps) {
  const confirm = useConfirm()

  const [parent, setParent] = React.useState<CodexListItem | null>(null)
  const [sourceInfo, setSourceInfo] = React.useState("")
  const [previewItems, setPreviewItems] = React.useState<CodexImportPreviewResult[] | null>(null)
  const [replaceExisting, setReplaceExisting] = React.useState(true)
  const [hasExisting, setHasExisting] = React.useState(false)
  const [isSaving, setIsSaving] = React.useState(false)

  // onCancel cambia a ogni render della pagina: si tiene l'ultima versione in un ref
  // per non far ripartire l'analisi del file quando cambia solo la callback.
  const onCancelRef = React.useRef(onCancel)
  React.useEffect(() => {
    onCancelRef.current = onCancel
  }, [onCancel])

  // All'arrivo del file: parse STEP → risoluzione del padre a DB → anteprima validata.
  // Su qualunque errore: toast e chiusura (il file scelto non è utilizzabile).
  React.useEffect(() => {
    if (!file) return
    let cancelled = false

    setParent(null)
    setSourceInfo("")
    setPreviewItems(null)
    setReplaceExisting(true)
    setHasExisting(false)
    setIsSaving(false)

    void (async () => {
      try {
        const text = await file.text()
        const bom = parseStepBom(text)
        if (!bom) {
          throw new Error("Il file non contiene una struttura assieme STEP riconoscibile.")
        }
        if (!bom.rootCode) {
          throw new Error(
            `L'assieme del file ("${bom.rootName}") non ha un codice nel nome: impossibile individuare il composito.`
          )
        }
        if (!["5", "6", "7"].includes(bom.rootCode.charAt(0))) {
          throw new Error(
            `L'assieme del file (${bom.rootCode}) non è un composito 5xx/6xx/7xx.`
          )
        }

        // Risolve il codice radice nell'articolo Codex (match esatto, punti ignorati).
        const found = await fetchCodex({
          filters: { codice: bom.rootCode.replace(/\./g, "") },
          pageSize: 10,
        })
        const parentItem =
          found.items.find((item) => sameCode(item.codice, bom.rootCode ?? "")) ?? null
        if (!parentItem) {
          throw new Error(
            `L'assieme ${bom.rootCode} non esiste nel Codex: crearlo prima di importarne la distinta.`
          )
        }

        const existingChildren = await fetchCompositionChildren(parentItem.id)
        const hasExistingComps = existingChildren.length > 0

        // Componenti CAD senza codice (es. attrezzature non a distinta): righe non
        // valide in anteprima, verranno saltate all'importazione.
        const coded = bom.items.filter((item) => item.code != null)
        const noCode: CodexImportPreviewResult[] = bom.items
          .filter((item) => item.code == null)
          .map((item) => ({
            code: item.name,
            quantity: item.quantity,
            descr: null,
            id: null,
            source: null,
            isValid: false,
            error: "Componente CAD senza codice",
          }))

        const preview =
          coded.length > 0
            ? await fetchImportPreview(
                parentItem.id,
                coded.map((item) => ({ code: item.code as string, quantity: item.quantity }))
              )
            : []
        if (cancelled) return

        setParent(parentItem)
        setHasExisting(hasExistingComps)
        setSourceInfo(`${file.name} — ${bom.items.length} componenti diretti`)
        setPreviewItems([...preview, ...noCode])
      } catch (err) {
        if (!cancelled) {
          notifyError(err)
          onCancelRef.current()
        }
      }
    })()

    return () => {
      cancelled = true
    }
  }, [file])

  async function handleConfirm() {
    if (!parent || !previewItems) return
    const validItems = previewItems.filter(item => item.isValid)
    // Guardia difensiva: il pulsante è già disabilitato quando validCount === 0.
    if (validItems.length === 0) return

    // La sostituzione elimina la distinta attuale → conferma obbligatoria.
    if (replaceExisting && hasExisting) {
      const ok = await confirm({
        title: "Sostituisci distinta esistente",
        description: `La distinta attuale di ${parent.codice} verrà eliminata e sostituita dai ${validItems.length} articoli importati. Continuare?`,
        confirmLabel: "Sostituisci",
      })
      if (!ok) return
    }

    setIsSaving(true)
    try {
      await commitImport(
        {
          parentId: parent.id,
          items: validItems.map(item => ({ code: item.code, quantity: item.quantity })),
          replaceExisting,
        },
        connRef?.current
      )
      notifySuccess(`${validItems.length} articoli importati in ${parent.codice}`)
      onSuccess(parent)
    } catch (err) {
      notifyError(err)
    } finally {
      setIsSaving(false)
    }
  }

  const validCount = previewItems ? previewItems.filter(i => i.isValid).length : 0
  const invalidCount = previewItems ? previewItems.filter(i => !i.isValid).length : 0

  return (
    <Dialog open={file != null} onOpenChange={(next) => !next && onCancel()}>
      <DialogContent className="max-w-3xl max-h-[90vh] flex flex-col p-6">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <FileSpreadsheet className="size-5 text-[#2F6098]" />
            {parent ? `Importa distinta — ${parent.codice}` : "Importa distinta da STEP"}
          </DialogTitle>
          <DialogDescription>
            {parent
              ? `${parent.descr || "Composito"} — verifica l'anteprima e conferma l'importazione.`
              : "Lettura del file e verifica degli articoli sul Codex…"}
          </DialogDescription>
        </DialogHeader>

        <div className="flex-1 min-h-0 space-y-4 overflow-y-auto pr-1 py-1">
          {previewItems === null ? (
            <div className="flex items-center justify-center gap-2 py-12 text-sm text-muted-foreground">
              <Loader2 className="size-4 animate-spin" />
              Analisi del file in corso…
            </div>
          ) : (
            <div className="space-y-4">
              {sourceInfo ? (
                <p className="text-xs text-muted-foreground">{sourceInfo}</p>
              ) : null}
              <div className="flex flex-wrap items-center justify-between gap-2 rounded-md bg-muted/40 p-3 text-sm">
                <div className="flex items-center gap-4">
                  <div className="flex items-center gap-1 text-[#059669] font-medium">
                    <CheckCircle2 className="size-4" />
                    <span>{validCount} validi</span>
                  </div>
                  {invalidCount > 0 && (
                    <div className="flex items-center gap-1 text-destructive font-medium">
                      <AlertCircle className="size-4" />
                      <span>{invalidCount} errori</span>
                    </div>
                  )}
                </div>
                <div className="flex items-center gap-2">
                  <Checkbox
                    id="replace-checkbox"
                    checked={replaceExisting}
                    onCheckedChange={(checked) => setReplaceExisting(checked === true)}
                  />
                  <Label htmlFor="replace-checkbox" className="cursor-pointer select-none">
                    Sostituisci distinta esistente
                  </Label>
                </div>
              </div>

              <div className="rounded-md border max-h-[300px] overflow-y-auto">
                <table className="w-full text-xs">
                  <thead className="sticky top-0 bg-muted border-b text-muted-foreground uppercase tracking-wider font-semibold">
                    <tr>
                      <th className="px-3 py-2 text-left w-32">Codice</th>
                      <th className="px-3 py-2 text-center w-16">Q.tà</th>
                      <th className="px-3 py-2 text-left">Descrizione / Stato</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y">
                    {previewItems.map((item, idx) => (
                      <tr key={idx} className={item.isValid ? "hover:bg-muted/30" : "bg-destructive/5 hover:bg-destructive/10"}>
                        <td className="px-3 py-2 font-mono font-medium">{item.code}</td>
                        <td className="px-3 py-2 text-center font-semibold tabular-nums">{item.quantity}</td>
                        <td className="px-3 py-2">
                          {item.isValid ? (
                            <span className="text-foreground">{item.descr}</span>
                          ) : (
                            <span className="text-destructive font-medium flex items-center gap-1">
                              <AlertCircle className="size-3 shrink-0" />
                              {item.error}
                            </span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {invalidCount > 0 ? (
                <p className="text-xs text-muted-foreground">
                  Gli articoli con errore non verranno importati.
                </p>
              ) : null}
            </div>
          )}
        </div>

        <DialogFooter className="border-t pt-4">
          <Button variant="outline" onClick={onCancel} disabled={isSaving}>
            Annulla
          </Button>
          <Button
            onClick={handleConfirm}
            disabled={isSaving || validCount === 0 || parent == null}
          >
            {isSaving && <Loader2 className="size-4 animate-spin mr-2" />}
            Importa {validCount} elementi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
