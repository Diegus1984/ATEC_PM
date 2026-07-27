// ── Scritture inline sulla distinta officina ───────────────────────────────
// Ogni campo editabile in griglia è un PUT completo della riga con una patch:
// stessa chiamata, stessa gestione del 409 (concorrenza). Qui c'è una sola
// implementazione, istanziata una volta per campo così ciascuna colonna
// conserva il proprio `isPending` (si disabilita solo la cella in scrittura).

import * as React from "react"
import { useMutation } from "@tanstack/react-query"

import { ApiError } from "@/lib/api/client"
import {
  deleteOfficinaItem,
  updateOfficinaItem,
} from "@/lib/api/project-ddp-officina"
import type {
  OfficinaItem,
  OfficinaItemSaveRequest,
  SupplierListItem,
} from "@/lib/api/types"
import { notifyError } from "@/lib/toast"

import { toForm } from "./officina-shared"
import { toDateOnly } from "@/lib/date-iso"

type Patch = Partial<OfficinaItemSaveRequest>

/** Una mutation "un campo della riga": PUT completo + patch, con gestione 409. */
function useFieldMutation(projectId: number, invalidate: () => Promise<void> | void) {
  return useMutation({
    mutationFn: ({ item, patch }: { item: OfficinaItem; patch: Patch }) =>
      updateOfficinaItem(projectId, item.id, { ...toForm(item), ...patch }),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        void invalidate()
        return
      }
      notifyError(err)
    },
  })
}

export interface OfficinaRowMutations {
  pending: {
    status: boolean
    quantity: boolean
    produced: boolean
    destination: boolean
    destinationSpec: boolean
    daneaRef: boolean
    dateNeeded: boolean
    orderDate: boolean
    supplier: boolean
    treatment: boolean
    notes: boolean
    remove: boolean
  }
  changeStatus: (item: OfficinaItem, statusKey: string) => void
  applyQuantityPatch: (
    item: OfficinaItem,
    patch: { quantity?: number; itemStatus?: string }
  ) => void
  changeProduced: (item: OfficinaItem, quantityProduced: number) => void
  changeDestination: (item: OfficinaItem, destination: string) => void
  commitDestinationSpec: (item: OfficinaItem, destinationSpec: string) => void
  commitDaneaRef: (item: OfficinaItem, daneaRef: string) => void
  changeDateNeeded: (item: OfficinaItem, dateNeeded: string | null) => void
  changeOrderDate: (item: OfficinaItem, orderDate: string | null) => void
  changeSupplier: (item: OfficinaItem, supplier: SupplierListItem | null) => void
  changeTreatment: (item: OfficinaItem, treatment: string) => void
  commitNotes: (item: OfficinaItem, notes: string) => void
  /** Cancellazione DEFINITIVA (solo ADMIN/PM): la conferma la chiede il chiamante. */
  removeRow: (item: OfficinaItem) => void
}

export function useOfficinaRowMutations(
  projectId: number,
  invalidate: () => Promise<void> | void
): OfficinaRowMutations {
  const status = useFieldMutation(projectId, invalidate)
  const quantity = useFieldMutation(projectId, invalidate)
  const produced = useFieldMutation(projectId, invalidate)
  const destination = useFieldMutation(projectId, invalidate)
  const destinationSpec = useFieldMutation(projectId, invalidate)
  const daneaRef = useFieldMutation(projectId, invalidate)
  const dateNeeded = useFieldMutation(projectId, invalidate)
  const orderDate = useFieldMutation(projectId, invalidate)
  const supplier = useFieldMutation(projectId, invalidate)
  const treatment = useFieldMutation(projectId, invalidate)
  const notes = useFieldMutation(projectId, invalidate)

  // Rimuove la riga e la sua bozza di lavorazione in staging; diversa dall'annullo,
  // che è solo un cambio stato.
  const remove = useMutation({
    mutationFn: (item: OfficinaItem) => deleteOfficinaItem(projectId, item.id),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const changeStatus = React.useCallback(
    (item: OfficinaItem, statusKey: string) => {
      if (statusKey === item.itemStatus || status.isPending) return
      status.mutate({ item, patch: { itemStatus: statusKey } })
    },
    [status]
  )

  const applyQuantityPatch = React.useCallback(
    (item: OfficinaItem, patch: { quantity?: number; itemStatus?: string }) => {
      quantity.mutate({ item, patch })
    },
    [quantity]
  )

  const changeProduced = React.useCallback(
    (item: OfficinaItem, quantityProduced: number) => {
      produced.mutate({ item, patch: { quantityProduced } })
    },
    [produced]
  )

  const changeDestination = React.useCallback(
    (item: OfficinaItem, next: string) => {
      // Cambiando destinazione la specifica si azzera, tranne quando si passa da
      // una destinazione valorizzata a un'altra (lì la specifica resta).
      const nextSpec = !next.trim()
        ? ""
        : !item.destination?.trim()
          ? ""
          : (item.destinationSpec ?? "")
      if (
        next === (item.destination ?? "") &&
        nextSpec === (item.destinationSpec ?? "")
      ) {
        return
      }
      if (destination.isPending) return
      destination.mutate({
        item,
        patch: { destination: next, destinationSpec: nextSpec },
      })
    },
    [destination]
  )

  const commitDestinationSpec = React.useCallback(
    (item: OfficinaItem, next: string) => {
      if (
        !item.destination?.trim() ||
        next === (item.destinationSpec ?? "") ||
        destinationSpec.isPending
      ) {
        return
      }
      destinationSpec.mutate({ item, patch: { destinationSpec: next } })
    },
    [destinationSpec]
  )

  const commitDaneaRef = React.useCallback(
    (item: OfficinaItem, value: string) => {
      const next = value.trim()
      if (next === (item.daneaRef ?? "") || daneaRef.isPending) return
      daneaRef.mutate({ item, patch: { daneaRef: next } })
    },
    [daneaRef]
  )

  const changeDateNeeded = React.useCallback(
    (item: OfficinaItem, value: string | null) => {
      if (value === toDateOnly(item.dateNeeded) || dateNeeded.isPending) return
      dateNeeded.mutate({ item, patch: { dateNeeded: value } })
    },
    [dateNeeded]
  )

  const changeOrderDate = React.useCallback(
    (item: OfficinaItem, value: string | null) => {
      if (value === toDateOnly(item.orderDate) || orderDate.isPending) return
      orderDate.mutate({ item, patch: { orderDate: value } })
    },
    [orderDate]
  )

  const changeSupplier = React.useCallback(
    (item: OfficinaItem, next: SupplierListItem | null) => {
      if ((next?.id ?? null) === (item.supplierId ?? null) || supplier.isPending) {
        return
      }
      supplier.mutate({
        item,
        patch: {
          supplierId: next?.id ?? null,
          supplierName: next?.companyName ?? "",
        },
      })
    },
    [supplier]
  )

  const changeTreatment = React.useCallback(
    (item: OfficinaItem, next: string) => {
      if (next === (item.treatment ?? "") || treatment.isPending) return
      treatment.mutate({ item, patch: { treatment: next } })
    },
    [treatment]
  )

  const commitNotes = React.useCallback(
    (item: OfficinaItem, value: string) => {
      const next = value.trim()
      if (next === (item.notes ?? "") || notes.isPending) return
      notes.mutate({ item, patch: { notes: next } })
    },
    [notes]
  )

  const removeRow = React.useCallback(
    (item: OfficinaItem) => remove.mutate(item),
    [remove]
  )

  // Oggetto memoizzato: le colonne lo tengono in dipendenza, ricrearlo a ogni
  // render rimonterebbe le celle (e ucciderebbe i popover aperti).
  return React.useMemo(
    () => ({
      pending: {
        status: status.isPending,
        quantity: quantity.isPending,
        produced: produced.isPending,
        destination: destination.isPending,
        destinationSpec: destinationSpec.isPending,
        daneaRef: daneaRef.isPending,
        dateNeeded: dateNeeded.isPending,
        orderDate: orderDate.isPending,
        supplier: supplier.isPending,
        treatment: treatment.isPending,
        notes: notes.isPending,
        remove: remove.isPending,
      },
      changeStatus,
      applyQuantityPatch,
      changeProduced,
      changeDestination,
      commitDestinationSpec,
      commitDaneaRef,
      changeDateNeeded,
      changeOrderDate,
      changeSupplier,
      changeTreatment,
      commitNotes,
      removeRow,
    }),
    [
      status.isPending,
      quantity.isPending,
      produced.isPending,
      destination.isPending,
      destinationSpec.isPending,
      daneaRef.isPending,
      dateNeeded.isPending,
      orderDate.isPending,
      supplier.isPending,
      treatment.isPending,
      notes.isPending,
      remove.isPending,
      changeStatus,
      applyQuantityPatch,
      changeProduced,
      changeDestination,
      commitDestinationSpec,
      commitDaneaRef,
      changeDateNeeded,
      changeOrderDate,
      changeSupplier,
      changeTreatment,
      commitNotes,
      removeRow,
    ]
  )
}
