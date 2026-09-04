import type { ChatChange, ChatMessageAlert } from "@/lib/api/types"
import { useHubSubscription, useLatestRef } from "@/lib/signalr/use-hub-subscription"

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
  const handlerRef = useLatestRef(onChange)
  const messageRef = useLatestRef(options?.onMessage)
  useHubSubscription({
    hub: "project",
    enabled: options?.enabled ?? true,
    deps: [],
    subscribe: (on) => {
      on("ChatChanged", (change: ChatChange) => handlerRef.current(change), {
        debounceMs: 300,
      })
      on("ChatMessageReceived", (alert: ChatMessageAlert) => messageRef.current?.(alert), {
        debounceMs: 0,
      })
    },
    join: (connection) => connection.invoke("JoinChatInbox"),
  })
}
