import * as React from "react"

import type { ChatChange, ChatMessageAlert } from "@/lib/api/types"
import { createHubConnection, startHub, stopHub } from "@/lib/signalr/hubs"

/**
 * Inbox globale: ChatChanged di qualsiasi commessa (gruppo `chat-inbox-all`) e, sul gruppo
 * personale, l'avviso `ChatMessageReceived` di ogni messaggio che mi riguarda (#78).
 *
 * `enabled: false` non apre nemmeno la connessione: serve a chi sta già ascoltando l'hub di
 * una commessa e non vuole una seconda presa aperta per gli stessi eventi.
 */
export function useChatInboxHub(
  onChange: (change: ChatChange) => void,
  options?: {
    enabled?: boolean
    /** Un messaggio nuovo in una chat di cui faccio parte. Niente debounce: è un avviso. */
    onMessage?: (alert: ChatMessageAlert) => void
  }
): void {
  const enabled = options?.enabled ?? true
  const handlerRef = React.useRef(onChange)
  React.useEffect(() => {
    handlerRef.current = onChange
  }, [onChange])

  const messageRef = React.useRef(options?.onMessage)
  React.useEffect(() => {
    messageRef.current = options?.onMessage
  }, [options?.onMessage])

  React.useEffect(() => {
    if (!enabled) return

    let disposed = false
    let debounce: ReturnType<typeof setTimeout> | null = null
    const connection = createHubConnection("project")

    connection.on("ChatChanged", (change: ChatChange) => {
      if (debounce) clearTimeout(debounce)
      debounce = setTimeout(() => handlerRef.current(change), 300)
    })

    connection.on("ChatMessageReceived", (alert: ChatMessageAlert) => {
      messageRef.current?.(alert)
    })

    const rejoin = async () => {
      try {
        await connection.invoke("JoinChatInbox")
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
      if (debounce) clearTimeout(debounce)
      void stopHub(connection)
    }
  }, [enabled])
}
