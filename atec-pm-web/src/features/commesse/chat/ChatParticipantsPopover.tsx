import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, Users, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { notifyError } from "@/lib/toast"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
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
}: {
  chatId: number
  projectId: number
  participantCount: number
}) {
  const queryClient = useQueryClient()
  const [open, setOpen] = React.useState(false)
  const [addId, setAddId] = React.useState<string>("")

  const participantsQuery = useQuery({
    queryKey: ["project-chat-participants", chatId],
    queryFn: () => fetchChatParticipants(chatId),
    enabled: open && chatId > 0,
  })

  const employeesQuery = useQuery({
    queryKey: ["mom-employees"],
    queryFn: fetchMoMEmployees,
    enabled: open,
  })

  const addMutation = useMutation({
    mutationFn: (employeeId: number) => addChatParticipant(chatId, employeeId),
    onSuccess: async () => {
      setAddId("")
      await queryClient.invalidateQueries({
        queryKey: ["project-chat-participants", chatId],
      })
      await queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
    },
    onError: (err: Error) => notifyError(err),
  })

  const removeMutation = useMutation({
    mutationFn: (employeeId: number) => removeChatParticipant(chatId, employeeId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["project-chat-participants", chatId],
      })
      await queryClient.invalidateQueries({ queryKey: ["project-chats", projectId] })
    },
    onError: (err: Error) => notifyError(err),
  })

  const participants = participantsQuery.data ?? []
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
          {participantCount} partecipanti
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-72 p-3">
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
          <Select value={addId} onValueChange={setAddId}>
            <SelectTrigger className="h-8 flex-1 text-xs">
              <SelectValue placeholder="Aggiungi…" />
            </SelectTrigger>
            <SelectContent>
              {availableToAdd.map((e) => (
                <SelectItem key={e.id} value={String(e.id)}>
                  {e.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
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
