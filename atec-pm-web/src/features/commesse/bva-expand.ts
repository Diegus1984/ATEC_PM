// ── «Espandi tutto» / «Chiudi tutto» del Bilancio (segnalazione #60) ───────
//
// Non è lo stato delle finestre tenuto in un posto solo: è un ORDINE con un contatore.
// Ogni finestra continua a possedere il suo aperto/chiuso e si riallinea quando il
// contatore cambia — così i due comandi funzionano anche quando sono già tutte aperte o
// già tutte chiuse, e dopo ognuna resta apribile e richiudibile per conto suo.
//
// Sta in un file a sé (e non in `bva-shared.tsx`) perché è contesto + hook, senza
// componenti: mescolarli ai componenti rompe il fast refresh di Vite.

import * as React from "react"

export type BvaExpandCommand = { open: boolean; nonce: number }

export const BvaExpandContext = React.createContext<BvaExpandCommand | null>(
  null
)

/** Aperto/chiuso di una finestra del Bilancio, obbediente ai comandi qui sopra. */
export function useBvaWindow(defaultOpen = true) {
  const command = React.useContext(BvaExpandContext)
  const [open, setOpen] = React.useState(defaultOpen)
  // Chi nasce DOPO un comando non se lo riapplica: parte dal suo default.
  const lastNonce = React.useRef(command?.nonce ?? 0)

  React.useEffect(() => {
    if (!command || command.nonce === lastNonce.current) return
    lastNonce.current = command.nonce
    setOpen(command.open)
  }, [command])

  const toggle = React.useCallback(() => setOpen((v) => !v), [])
  return { open, toggle }
}
