import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Plus } from "lucide-react"

import { CustomerSearchCombobox } from "@/components/shared/customer-search-combobox"
import { CustomerDialog } from "@/features/clienti/CustomerDialog"
import { Button } from "@/components/ui/button"
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
import { fetchCustomers } from "@/lib/api/customers"
import { createQuote } from "@/lib/api/quotes"

import { quoteTypeLabel } from "./quote-type"

/**
 * Nuovo preventivo. Fedele a NewQuoteDialog del WPF: Cliente* (+ nuovo cliente),
 * Titolo*, Tipo (Service/Impianto). Invia i 3 campi essenziali; il resto va a
 * default lato server (validità 60gg, ecc.).
 */
export function NewQuoteDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (quoteId: number) => void
}) {
  const [customerId, setCustomerId] = React.useState<number | null>(null)
  const [title, setTitle] = React.useState("")
  const [quoteType, setQuoteType] = React.useState("SERVICE")
  const [error, setError] = React.useState<string | null>(null)
  const [customerDialogOpen, setCustomerDialogOpen] = React.useState(false)

  const customersQuery = useQuery({
    queryKey: ["customers"],
    queryFn: fetchCustomers,
    enabled: open,
  })

  React.useEffect(() => {
    if (open) {
      setCustomerId(null)
      setTitle("")
      setQuoteType("SERVICE")
      setError(null)
    }
  }, [open])

  const createMutation = useMutation({
    mutationFn: async () => {
      if (!customerId) throw new Error("Seleziona un cliente.")
      if (!title.trim()) throw new Error("Inserisci un titolo.")
      return createQuote({
        quoteType,
        priceListId: null,
        title: title.trim(),
        customerId,
        contactName1: "",
        contactName2: "",
        contactName3: "",
        deliveryDays: 0,
        validityDays: 60,
        paymentType: "",
        language: "it",
        groupId: null,
        discountPct: 0,
        discountAbs: 0,
        showItemPrices: true,
        showSummary: true,
        showSummaryPrices: true,
        hideQuantities: false,
        notesInternal: "",
        notesQuote: "",
        assignedTo: null,
      })
    },
    onSuccess: (id) => onCreated(id),
    onError: (err: Error) => setError(err.message),
  })

  const customers = [...(customersQuery.data ?? [])].sort((a, b) =>
    a.companyName.localeCompare(b.companyName)
  )

  return (
    <>
      <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Nuovo preventivo</DialogTitle>
          </DialogHeader>

          <div className="space-y-4">
            <div className="grid gap-2">
              <Label>Cliente *</Label>
              <div className="flex gap-2">
                <CustomerSearchCombobox
                  customers={customers}
                  value={customerId}
                  loading={customersQuery.isLoading}
                  onValueChange={setCustomerId}
                  placeholder="Cerca cliente per nome…"
                />
                <Button
                  type="button"
                  variant="outline"
                  size="icon"
                  title="Nuovo cliente"
                  onClick={() => setCustomerDialogOpen(true)}
                >
                  <Plus className="size-4" />
                </Button>
              </div>
            </div>

            <div className="grid gap-2">
              <Label>Titolo *</Label>
              <Input
                value={title}
                autoFocus
                onChange={(event) => setTitle(event.target.value)}
              />
            </div>

            <div className="grid gap-2">
              <Label>Tipo preventivo</Label>
              <Select value={quoteType} onValueChange={setQuoteType}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="SERVICE">{quoteTypeLabel("SERVICE")}</SelectItem>
                  <SelectItem value="IMPIANTO">{quoteTypeLabel("IMPIANTO")}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {error ? <p className="text-sm text-destructive">{error}</p> : null}
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={onClose}>
              Annulla
            </Button>
            <Button
              disabled={customerId == null || !title.trim() || createMutation.isPending}
              onClick={() => createMutation.mutate()}
            >
              {createMutation.isPending ? "Creazione…" : "Crea preventivo"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <CustomerDialog
        open={customerDialogOpen}
        customerId={customerDialogOpen ? "new" : null}
        onClose={() => setCustomerDialogOpen(false)}
        onSaved={async () => {
          setCustomerDialogOpen(false)
          await customersQuery.refetch()
        }}
      />
    </>
  )
}
