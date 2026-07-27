// ── Scritture sulle lavorazioni: crea, aggiorna, patch di campo, elimina ───

import * as React from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"

import {
  createWorkRequest,
  deleteWorkRequest,
  patchWorkRequestField,
  updateWorkRequest,
} from "@/lib/api/workRequests"
import type { WorkRequestSaveRequest } from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"

export function useWorkRequestMutations() {
  const queryClient = useQueryClient()

  // Le tre viste (commessa, priorità, tutte) hanno chiavi diverse: si invalidano
  // tutte, altrimenti una modifica fatta da una vista resta stantia nelle altre.
  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["work-requests"] })
    void queryClient.invalidateQueries({ queryKey: ["all-work-requests"] })
    void queryClient.invalidateQueries({ queryKey: ["priority-work-requests"] })
  }, [queryClient])

  const create = useMutation({
    mutationFn: createWorkRequest,
    onSuccess: () => {
      notifySuccess("Lavorazione inserita con successo")
      invalidate()
    },
    onError: (err) => notifyError(err),
  })

  const update = useMutation({
    mutationFn: ({ id, request }: { id: number; request: WorkRequestSaveRequest }) =>
      updateWorkRequest(id, request),
    onSuccess: () => {
      notifySuccess("Aggiornato con successo")
      invalidate()
    },
    onError: (err) => {
      // In caso di CONFLITTO (riga modificata da un altro utente) ricarica i dati freschi
      notifyError(err)
      invalidate()
    },
  })

  const patch = useMutation({
    mutationFn: ({
      id,
      field,
      value,
    }: {
      id: number
      field: string
      value: unknown
    }) => patchWorkRequestField(id, field, value),
    onSuccess: () => invalidate(),
    onError: (err) => notifyError(err),
  })

  const remove = useMutation({
    mutationFn: deleteWorkRequest,
    onSuccess: () => {
      notifySuccess("Lavorazione eliminata")
      invalidate()
    },
    onError: (err) => notifyError(err),
  })

  const patchField = React.useCallback(
    (id: number, field: string, value: unknown) => patch.mutate({ id, field, value }),
    [patch]
  )

  return { invalidate, create, update, patch, remove, patchField }
}
