import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * Collapsible — contenitore con apertura/chiusura animata e morbida.
 *
 * L'animazione (durata + curva) NON è cablata qui: arriva dalle CSS var globali
 * `--accordion-duration` / `--accordion-ease` definite in `src/index.css`.
 * Modificandole lì cambia il comportamento di TUTTI gli accordion dell'app —
 * questo è il "parametro globale" unico.
 *
 * Il contenuto appare in sincrono con l'apertura (così la transizione 0fr→1fr
 * ha qualcosa da misurare) e resta montato per tutta la durata della chiusura,
 * poi viene smontato. Durante la chiusura mostra l'ULTIMO contenuto visto da
 * aperto, anche se nel frattempo il chiamante azzera i dati (es. una bozza
 * legata alla riga aperta). Da chiuso è `inert`/`aria-hidden`, quindi non
 * focalizzabile col tab né letto dagli screen reader.
 *
 * Uso tipico (l'header con il pulsante lo gestisce il chiamante):
 *
 *   <Collapsible open={open}>
 *     <div className="border-t p-3">…contenuto…</div>
 *   </Collapsible>
 */
function Collapsible({
  open,
  className,
  children,
  ...props
}: React.ComponentProps<"div"> & { open: boolean }) {
  // Resta true finché l'animazione di chiusura non è terminata, così il
  // contenuto non sparisce di colpo ma collassa in modo fluido.
  const [visible, setVisible] = React.useState(open)
  // Ultimo contenuto mostrato da aperto: serve durante la chiusura, quando il
  // chiamante può aver già azzerato `children`.
  const lastContent = React.useRef(children)
  if (open) lastContent.current = children

  React.useEffect(() => {
    if (open) setVisible(true)
  }, [open])

  function handleTransitionEnd(event: React.TransitionEvent<HTMLDivElement>) {
    if (
      event.target === event.currentTarget &&
      event.propertyName === "grid-template-rows" &&
      !open
    ) {
      setVisible(false)
    }
  }

  const content = open ? children : visible ? lastContent.current : null

  return (
    <div
      data-slot="collapsible"
      data-state={open ? "open" : "closed"}
      aria-hidden={!open}
      inert={!open}
      onTransitionEnd={handleTransitionEnd}
      className={cn("accordion-content", className)}
      {...props}
    >
      {/* unico figlio diretto: vedi regola `.accordion-content > *` in index.css */}
      <div>{content}</div>
    </div>
  )
}

export { Collapsible }
