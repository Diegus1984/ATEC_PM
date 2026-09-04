import * as React from "react"

import type { ChatChange, ChatTyping } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`), gruppo `project-{id}`: `ChatChanged`
 * (debounced) e `ChatTyping` (subito). Gli eventi di altre commesse vengono scartati
 * prima del debounce. Ritorna `sendTyping` per segnalare agli altri che si sta scrivendo.
 */
export function useProjectChatHub(
  projectId: number | null,
  onChange: (change: ChatChange) => void,
  onTyping?: (typing: ChatTyping) => void
): { sendTyping: (chatId: number) => void } {
  const handlerRef = useLatestRef(onChange)
  const typingRef = useLatestRef(onTyping)
  const connectionRef = useHubSubscription({
    hub: "project",
    enabled: projectId != null && projectId > 0,
    deps: [projectId],
    subscribe: (on) => {
      on("ChatChanged", (change: ChatChange) => handlerRef.current(change), {
        debounceMs: 300,
        when: (change) => change.projectId === projectId,
      })
      on("ChatTyping", (typing: ChatTyping) => typingRef.current?.(typing), {
        debounceMs: 0,
        when: (typing) => typing.projectId === projectId,
      })
    },
    join: (connection) => connection.invoke("JoinProject", projectId),
  })

  const sendTyping = React.useCallback(
    (chatId: number) => {
      const connection = connectionRef.current
      if (!connection || projectId == null || chatId <= 0) return
      void connection.invoke("ChatTyping", projectId, chatId).catch(() => undefined)
    },
    [connectionRef, projectId]
  )

  return { sendTyping }
}
