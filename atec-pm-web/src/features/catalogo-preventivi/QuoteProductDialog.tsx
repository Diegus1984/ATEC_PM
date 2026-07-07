import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { Plus, X } from "lucide-react"

import { RichTextEditor, type RichTextEditorHandle } from "@/components/shared/RichTextEditor"
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  createProduct,
  fetchProduct,
  updateProduct,
} from "@/lib/api/quote-catalog"
import type { QuoteProductSaveDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"

interface VariantRow {
  id: number
  code: string
  name: string
  /** Stringhe per gli input numerici (parse al salvataggio). */
  costPrice: string
  markupValue: string
}

function parseDecimal(value: string): number {
  const n = Number((value ?? "").replace(",", "."))
  return Number.isFinite(n) ? n : 0
}

function fmt2(value: number): string {
  return value.toLocaleString("it-IT", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

function UnitField({
  unit,
  align = "right",
  className,
  ...props
}: React.ComponentProps<typeof Input> & {
  unit: string
  align?: "left" | "center" | "right"
}) {
  return (
    <div className="relative">
      <Input
        {...props}
        className={cn(
          "h-8",
          align === "right" && "pr-7 text-right",
          align === "center" && "px-7 text-center",
          align === "left" && "pr-7",
          className
        )}
      />
      <span
        className={cn(
          "pointer-events-none absolute top-1/2 -translate-y-1/2 text-xs text-muted-foreground",
          align === "center" ? "right-2" : "right-2"
        )}
      >
        {unit}
      </span>
    </div>
  )
}

// Riga variante nuova: markup default 1 (come VariantRow del WPF, non 1.300).
function emptyVariant(): VariantRow {
  return { id: 0, code: "", name: "", costPrice: "0", markupValue: "1" }
}

/**
 * Crea/modifica prodotto di catalogo. Fedele a QuoteProductDialog del WPF:
 * Nome*, Codice, Tipologia (Prodotto/Contenuto), editor descrizione (TinyMCE),
 * griglia varianti (Codice/Nome/Costo/K/Prezzo cliente), «Auto-include».
 * Le immagini vivono dentro la descrizione (description_rtf), non in imagePath.
 */
export function QuoteProductDialog({
  open,
  categoryId,
  productId,
  onClose,
  onSaved,
}: {
  open: boolean
  categoryId: number
  productId: number | null
  onClose: () => void
  onSaved: () => void
}) {
  const editId = productId
  const [name, setName] = React.useState("")
  const [code, setCode] = React.useState("")
  const [itemType, setItemType] = React.useState("product")
  const [autoInclude, setAutoInclude] = React.useState(false)
  const [variants, setVariants] = React.useState<VariantRow[]>([emptyVariant()])
  const [description, setDescription] = React.useState("")
  const [loaded, setLoaded] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)

  const editorRef = React.useRef<RichTextEditorHandle | null>(null)

  React.useEffect(() => {
    if (!open) {
      setLoaded(false)
      return
    }
    setError(null)

    if (editId == null) {
      setName("")
      setCode("")
      setItemType("product")
      setAutoInclude(false)
      setVariants([emptyVariant()])
      setDescription("")
      setLoaded(true)
      return
    }

    let cancelled = false
    setLoaded(false)
    void fetchProduct(editId)
      .then((product) => {
        if (cancelled) return
        setName(product.name)
        setCode(product.code)
        setItemType(product.itemType === "content" ? "content" : "product")
        setAutoInclude(product.autoInclude)
        setDescription(product.descriptionRtf ?? "")
        const rows = product.variants.map<VariantRow>((v) => ({
          id: v.id,
          code: v.code,
          name: v.name,
          costPrice: String(v.costPrice ?? 0),
          markupValue: String(v.markupValue ?? 1),
        }))
        setVariants(rows.length > 0 ? rows : [emptyVariant()])
        setLoaded(true)
      })
      .catch((err: Error) => {
        if (!cancelled) {
          setError(err.message)
          setLoaded(true)
        }
      })
    return () => {
      cancelled = true
    }
  }, [open, editId])

  function setVariant(index: number, patch: Partial<VariantRow>) {
    setVariants((prev) =>
      prev.map((row, i) => (i === index ? { ...row, ...patch } : row))
    )
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!name.trim()) {
        throw new Error("Il nome è obbligatorio.")
      }
      const descriptionRtf = editorRef.current?.getContent() ?? description
      const dto: QuoteProductSaveDto = {
        categoryId,
        itemType,
        code: code.trim(),
        name: name.trim(),
        descriptionRtf,
        imagePath: "",
        attachmentPath: "",
        autoInclude,
        sortOrder: 0,
        isActive: true,
        variants: variants.map((v, index) => ({
          id: v.id,
          code: v.code.trim(),
          name: v.name.trim(),
          costPrice: parseDecimal(v.costPrice),
          markupValue: parseDecimal(v.markupValue),
          sortOrder: index,
        })),
      }
      if (editId != null) {
        await updateProduct(editId, dto)
      } else {
        await createProduct(dto)
      }
    },
    onSuccess: () => onSaved(),
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[92vh] flex-col gap-4 overflow-hidden sm:max-w-5xl">
        <DialogHeader>
          <DialogTitle>
            {editId != null ? "Modifica prodotto" : "Nuovo prodotto"}
          </DialogTitle>
        </DialogHeader>

        <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto pr-1">
          {/* Header: Nome / Codice / Tipologia */}
          <div className="grid grid-cols-1 gap-4 md:grid-cols-[1fr_160px_160px]">
            <div className="grid gap-2">
              <Label>Nome prodotto *</Label>
              <Input
                value={name}
                autoFocus
                onChange={(event) => setName(event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Codice</Label>
              <Input
                value={code}
                onChange={(event) => setCode(event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Tipologia</Label>
              <Select value={itemType} onValueChange={setItemType}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="product">Prodotto</SelectItem>
                  <SelectItem value="content">Contenuto</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          {/* Descrizione (editor) */}
          <div className="grid gap-2">
            <Label>Descrizione</Label>
            {loaded ? (
              <RichTextEditor
                ref={editorRef}
                initialValue={description}
                height={340}
              />
            ) : (
              <div className="flex h-[340px] items-center justify-center rounded-md border text-sm text-muted-foreground">
                Caricamento…
              </div>
            )}
          </div>

          {/* Varianti */}
          <div className="grid gap-2">
            <div className="flex items-center gap-3">
              <Label className="text-sm font-bold">
                VARIANTI DEL PRODOTTO / SERVICE A LISTINO
              </Label>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setVariants((prev) => [...prev, emptyVariant()])}
              >
                <Plus className="size-4" />
                Aggiungi riga
              </Button>
            </div>
            <p className="text-xs text-muted-foreground">
              Prezzo cliente = costo × K (es. 1.000 € × 1,3 = 1.300 €)
            </p>
            <div className="rounded-md border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-28">Codice</TableHead>
                    <TableHead>Nome</TableHead>
                    <TableHead className="w-32 text-right">Costo az. (€)</TableHead>
                    <TableHead className="w-24 text-center">K (×)</TableHead>
                    <TableHead className="w-32 text-right">Prezzo cl. (€)</TableHead>
                    <TableHead className="w-12" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {variants.map((row, index) => {
                    const sell = parseDecimal(row.costPrice) * parseDecimal(row.markupValue)
                    return (
                      <TableRow key={index}>
                        <TableCell>
                          <Input
                            value={row.code}
                            className="h-8"
                            onChange={(event) =>
                              setVariant(index, { code: event.target.value })
                            }
                          />
                        </TableCell>
                        <TableCell>
                          <Input
                            value={row.name}
                            className="h-8"
                            onChange={(event) =>
                              setVariant(index, { name: event.target.value })
                            }
                          />
                        </TableCell>
                        <TableCell>
                          <UnitField
                            unit="€"
                            value={row.costPrice}
                            inputMode="decimal"
                            onChange={(event) =>
                              setVariant(index, { costPrice: event.target.value })
                            }
                          />
                        </TableCell>
                        <TableCell>
                          <UnitField
                            unit="×"
                            align="center"
                            value={row.markupValue}
                            inputMode="decimal"
                            onChange={(event) =>
                              setVariant(index, { markupValue: event.target.value })
                            }
                          />
                        </TableCell>
                        <TableCell className="text-right font-semibold tabular-nums">
                          {fmt2(sell)} €
                        </TableCell>
                        <TableCell>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon-sm"
                            className="text-destructive hover:bg-destructive/10"
                            title="Rimuovi variante"
                            onClick={() =>
                              setVariants((prev) =>
                                prev.filter((_, i) => i !== index)
                              )
                            }
                          >
                            <X className="size-3.5" />
                          </Button>
                        </TableCell>
                      </TableRow>
                    )
                  })}
                  {variants.length === 0 ? (
                    <TableRow>
                      <TableCell
                        colSpan={6}
                        className="text-center text-sm text-muted-foreground"
                      >
                        Nessuna variante. Aggiungi una riga.
                      </TableCell>
                    </TableRow>
                  ) : null}
                </TableBody>
              </Table>
            </div>
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter className="flex items-center sm:justify-between">
          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={autoInclude}
              onCheckedChange={(value) => setAutoInclude(value === true)}
            />
            Aggiungi automaticamente ai nuovi preventivi
          </label>
          <div className="flex gap-2">
            <Button variant="outline" onClick={onClose}>
              Annulla
            </Button>
            <Button
              onClick={() => saveMutation.mutate()}
              disabled={!name.trim() || saveMutation.isPending}
            >
              {saveMutation.isPending ? "Salvataggio…" : "Salva"}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
