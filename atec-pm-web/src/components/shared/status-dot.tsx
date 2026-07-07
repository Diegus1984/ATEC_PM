import * as React from "react"

import { cn } from "@/lib/utils"

/** Indicatore di stato: pallino colorato + etichetta. Standard ATEC PM per gli stati. */
export function StatusDot({
  color,
  className,
  children,
}: {
  /** Classe Tailwind del colore pallino, es. "bg-emerald-500". */
  color: string
  className?: string
  children: React.ReactNode
}) {
  return (
    <span className={cn("flex items-center gap-2 whitespace-nowrap", className)}>
      <span className={cn("size-2 rounded-full", color)} />
      {children}
    </span>
  )
}

/** Stato attivo/disattivo: verde "Attivo" / grigio "Disattivo". */
export function ActiveStatus({ active }: { active: boolean }) {
  return (
    <StatusDot color={active ? "bg-emerald-500" : "bg-zinc-400"}>
      {active ? "Attivo" : "Disattivo"}
    </StatusDot>
  )
}
