import * as React from "react"

import type { ChatChange, ChatTyping } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Sottoscrive l'hub commesse (`/hubs/project`): `ChatChanged` e `ChatTyping`.
 */
export function useProjectChatHub(
  projectId: number | null,
  onChange: (change: ChatChange) => void,
  onTyping?: (typing: ChatTyping) => void
): { sendTyping: (chatId: number) => void } {
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  const typingRef = React.useRef(onTyping)
  React.useEffect(() => {
    typingRef.current = onTyping
  }, [onTyping])

  const connectionRef = React.useRef<ReturnType<typeof createHubConnection> | null>(
    null
  )

  React.useEffect(() => {
    if (projectId == null || projectId <= 0) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")
    connectionRef.current = connection

    connection.on("ChatChanged", (change: ChatChange) => {
      if (change.projectId !== projectId) return
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 300)
    })

    connection.on("ChatTyping", (typing: ChatTyping) => {
      if (typing.projectId !== projectId) return
      typingRef.current?.(typing)
    })

    const rejoin = async () => {
      try {
        await connection.invoke("JoinProject", projectId)
      } catch {
        /* best-effort */
      }
    }

    connection.onreconnected(() => {
      void rejoin()
    })

    void (async () => {
      const ok = await startHub(connection)
      if (!ok || disposed) return
      await rejoin()
    })()

    return () => {
      disposed = true
      connectionRef.current = null
      if (debounce) clearTimeout(debounce)
      void stopHub(connection)
    }
  }, [projectId])

  const sendTyping = React.useCallback(
    (chatId: number) => {
      const connection = connectionRef.current
      if (!connection || projectId == null || chatId <= 0) return
      void connection.invoke("ChatTyping", projectId, chatId).catch(() => undefined)
    },
    [projectId]
  )

  return { sendTyping }
}
