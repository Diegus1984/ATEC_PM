import { ChatWorkspace } from "@/features/chat/ChatWorkspace"

/**
 * Tab «Chat» del dettaglio commessa: le chat di QUESTA commessa soltanto.
 *
 * Il corpo è lo stesso della pagina «Chat» del menu principale (`ChatWorkspace`), che dopo la
 * #78 sa lavorare sia sull'elenco di una commessa sia su quello globale: due copie della stessa
 * conversazione avrebbero significato correggere ogni difetto due volte.
 */
export function ProjectChat({
  projectId,
  projectCode,
  projectTitle,
  initialChatId,
}: {
  projectId: number
  projectCode?: string
  projectTitle?: string
  initialChatId?: number | null
}) {
  const label = [projectCode, projectTitle].filter(Boolean).join(" · ")

  return (
    <ChatWorkspace
      scope={{ kind: "project", projectId, label: label || undefined }}
      initialChatId={initialChatId}
    />
  )
}
