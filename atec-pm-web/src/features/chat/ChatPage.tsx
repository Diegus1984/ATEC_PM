import { useSearchParams } from "react-router-dom"

import { ChatWorkspace } from "@/features/chat/ChatWorkspace"

/**
 * Pagina «Chat» del menu principale (#78): tutte le conversazioni di cui faccio parte,
 * divise per commessa, altra attività e senza commessa.
 *
 * L'altezza è fissata come nelle altre pagine a due colonne: la lista e la conversazione
 * scorrono dentro, la pagina no.
 */
export function ChatPage() {
  const [params] = useSearchParams()
  const chatParam = Number(params.get("chat") ?? 0)

  return (
    <div className="h-[calc(100vh-11rem)] min-h-0 overflow-hidden rounded-lg border bg-card">
      <ChatWorkspace
        scope={{ kind: "all" }}
        initialChatId={chatParam > 0 ? chatParam : null}
      />
    </div>
  )
}
