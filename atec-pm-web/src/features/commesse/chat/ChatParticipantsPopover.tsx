import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, Users, X } from "lucide-react"

import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import { notifyError } from "@/lib/toast"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { fetchMoMEmployees } from "@/lib/api/mom"
import {
  addChatParticipant,
  fetchChatParticipants,
  removeChatParticipant,
} from "@/lib/api/project-chat"
import { getSession } from "@/lib/auth/session"

export function ChatParticipantsPopover({
  chatId,
  projectId,
  participantCount,
  open: openControlled,
  onOpenChange: onOpenChangeControlled,
}: {
  chatId: number
  /** null per le chat senza commessa: non c'è una lista di commessa da rinfrescare. */
  projectId: number | null
  participantCount: number
  /** Apertura controllata (es. dal menu contestuale della lista). */
  open?: boolean
  onOpenChange?: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const [openUncontrolled, setOpenUncontrolled] = React.useState(false)
  const open = openControlled ?? openUncontrolled
  const setOpen = onOpenChangeControlled ?? setOpenUncontrolled
  const [addId, setAddId] = React.useState<string>("")

  // Caricata anche a pannello chiuso: è la stessa chiave che usa la conversazione, quindi non
  // costa una richiesta in più e il numero sul pulsante è quello vero — non la fotografia
  // presa dall'elenco di sinistra, che arrivava con un giro di ritardo (#78).
  const participantsQuery = useQuery({
    queryKey: ["project-chat-participants", chatId],
    queryFn: () => fetchChatParticipants(chatId),
    enabled: chatId > 0,
  })

  const employeesQuery = useQuery({
    queryKey: ["mom-employees"],
    queryFn: fetchMoMEmployees,
    enabled: open,
  })

  async function refreshLists() {
    await queryClient.invalidateQueries({
      queryKey: ["project-chat-participants", chatId],
    })
    await queryClient.invalidateQueries({ queryKey: ["chat-inbox"] })
    if (projectId != null) {
      await queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
    }
  }

  const addMutation = useMutation({
    mutationFn: (employeeId: number) => addChatParticipant(chatId, employeeId),
    onSuccess: async () => {
      setAddId("")
      await refreshLists()
    },
    onError: (err: Error) => notifyError(err),
  })

  const removeMutation = useMutation({
    mutationFn: (employeeId: number) => removeChatParticipant(chatId, employeeId),
    onSuccess: () => refreshLists(),
    onError: (err: Error) => notifyError(err),
  })

  const participants = participantsQuery.data ?? []
  const shownCount = participantsQuery.data ? participants.length : participantCount
  const currentId = getSession()?.user.employeeId
  const participantIds = new Set(participants.map((p) => p.employeeId))
  const availableToAdd = (employeesQuery.data ?? []).filter(
    (e) => !participantIds.has(e.id)
  )

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="sm" className="h-8 gap-1.5 text-muted-foreground">
          <Users className="size-3.5" />
          {shownCount} partecipanti
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="end"
        className="w-72 p-3"
        // La combo «Aggiungi…» è un popover annidato: senza questa guardia il
        // click (o il focus sulla ricerca) verrebbe letto come "fuori" e
        // chiuderebbe il pannello partecipanti.
        onInteractOutside={(event) => {
          const target = event.target as HTMLElement | null
          if (target?.closest("[data-slot=popover-content]")) {
            event.preventDefault()
          }
        }}
      >
        <p className="mb-2 text-sm font-medium">Partecipanti</p>
        <ul className="max-h-40 space-y-1 overflow-y-auto">
          {participantsQuery.isLoading ? (
            <li className="text-sm text-muted-foreground">Caricamento…</li>
          ) : participants.length === 0 ? (
            <li className="text-sm text-muted-foreground">Nessun partecipante.</li>
          ) : (
            participants.map((p) => (
              <li
                key={p.id}
                className="flex items-center justify-between gap-2 rounded-md px-1 py-0.5 text-sm"
              >
                <span className="truncate">{p.employeeName}</span>
                {p.employeeId !== currentId ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    className="shrink-0 text-muted-foreground hover:text-destructive"
                    aria-label={`Rimuovi ${p.employeeName}`}
                    disabled={removeMutation.isPending}
                    onClick={() => removeMutation.mutate(p.employeeId)}
                  >
                    <X className="size-3.5" />
                  </Button>
                ) : (
                  <span className="text-[10px] text-muted-foreground">tu</span>
                )}
              </li>
            ))
          )}
        </ul>

        <div className="mt-3 flex items-center gap-2 border-t pt-3">
          <LookupCombobox
            options={availableToAdd.map((e) => ({
              id: String(e.id),
              name: e.name,
            }))}
            value={addId || null}
            onValueChange={(id) => setAddId(id ?? "")}
            placeholder="Aggiungi…"
            searchPlaceholder="Cerca dipendente…"
            emptyText="Nessun dipendente disponibile"
            className="h-8 flex-1 text-xs"
          />
          <Button
            type="button"
            size="icon-sm"
            disabled={!addId || addMutation.isPending}
            aria-label="Aggiungi partecipante"
            onClick={() => addMutation.mutate(Number(addId))}
          >
            <Plus />
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
