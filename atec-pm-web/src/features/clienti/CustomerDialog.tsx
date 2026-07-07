import * as React from "react"
import { useMutation } from "@tanstack/react-query"

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
import { Textarea } from "@/components/ui/textarea"
import {
  createCustomer,
  fetchCustomer,
  updateCustomer,
} from "@/lib/api/customers"

const EMPTY = {
  companyName: "",
  contactName: "",
  email: "",
  pec: "",
  phone: "",
  cell: "",
  vatNumber: "",
  fiscalCode: "",
  sdiCode: "",
  address: "",
  paymentTerms: "",
  notes: "",
  isActive: true,
}

type FormState = typeof EMPTY

export function CustomerDialog({
  open,
  customerId,
  onClose,
  onSaved,
}: {
  open: boolean
  customerId: number | "new" | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const isNew = customerId === "new"
  const editId = typeof customerId === "number" ? customerId : null

  const [form, setForm] = React.useState<FormState>(EMPTY)
  const [error, setError] = React.useState<string | null>(null)

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  React.useEffect(() => {
    if (!open) {
      return
    }
    setError(null)

    if (isNew || editId == null) {
      setForm(EMPTY)
      return
    }

    let cancelled = false
    void fetchCustomer(editId)
      .then((data) => {
        if (cancelled) return
        setForm({
          companyName: data.companyName,
          contactName: data.contactName,
          email: data.email,
          pec: data.pec,
          phone: data.phone,
          cell: data.cell,
          vatNumber: data.vatNumber,
          fiscalCode: data.fiscalCode,
          sdiCode: data.sdiCode,
          address: data.address,
          paymentTerms: data.paymentTerms,
          notes: data.notes,
          isActive: data.isActive,
        })
      })
      .catch((err: Error) => {
        if (!cancelled) setError(err.message)
      })
    return () => {
      cancelled = true
    }
  }, [open, isNew, editId])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!form.companyName.trim()) {
        throw new Error("Ragione sociale obbligatoria.")
      }
      const payload = {
        id: editId ?? 0,
        companyName: form.companyName.trim(),
        contactName: form.contactName.trim(),
        email: form.email.trim(),
        pec: form.pec.trim(),
        phone: form.phone.trim(),
        cell: form.cell.trim(),
        address: form.address.trim(),
        vatNumber: form.vatNumber.trim(),
        fiscalCode: form.fiscalCode.trim(),
        paymentTerms: form.paymentTerms.trim(),
        sdiCode: form.sdiCode.trim(),
        notes: form.notes.trim(),
        isActive: form.isActive,
      }
      if (editId != null) {
        await updateCustomer(editId, payload)
      } else {
        await createCustomer(payload)
      }
    },
    onSuccess: async () => {
      await onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{isNew ? "Nuovo cliente" : "Modifica cliente"}</DialogTitle>
          <DialogDescription>
            Anagrafica, contatti e dati fiscali del cliente.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Ragione sociale</Label>
            <Input
              value={form.companyName}
              autoFocus
              onChange={(event) => set("companyName", event.target.value)}
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Referente</Label>
              <Input
                value={form.contactName}
                onChange={(event) => set("contactName", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Indirizzo</Label>
              <Input
                value={form.address}
                onChange={(event) => set("address", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Email</Label>
              <Input
                type="email"
                value={form.email}
                onChange={(event) => set("email", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>PEC</Label>
              <Input
                value={form.pec}
                onChange={(event) => set("pec", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Telefono</Label>
              <Input
                value={form.phone}
                onChange={(event) => set("phone", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Cellulare</Label>
              <Input
                value={form.cell}
                onChange={(event) => set("cell", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-3 gap-4">
            <div className="grid gap-2">
              <Label>P. IVA</Label>
              <Input
                value={form.vatNumber}
                onChange={(event) => set("vatNumber", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Cod. fiscale</Label>
              <Input
                value={form.fiscalCode}
                onChange={(event) => set("fiscalCode", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Cod. SDI</Label>
              <Input
                value={form.sdiCode}
                onChange={(event) => set("sdiCode", event.target.value)}
              />
            </div>
          </div>

          <div className="grid gap-2">
            <Label>Termini di pagamento</Label>
            <Input
              value={form.paymentTerms}
              onChange={(event) => set("paymentTerms", event.target.value)}
            />
          </div>

          <div className="grid gap-2">
            <Label>Note</Label>
            <Textarea
              value={form.notes}
              rows={3}
              onChange={(event) => set("notes", event.target.value)}
            />
          </div>

          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={form.isActive}
              onCheckedChange={(value) => set("isActive", !!value)}
            />
            Cliente attivo
          </label>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!form.companyName.trim() || saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
