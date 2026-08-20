import { ChevronDown } from "lucide-react"

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Badge } from "@/components/ui/badge"
import { canWriteFeature } from "@/lib/auth/permissions"
import { cn } from "@/lib/utils"

import {
  DASHBOARD_STATUS_META,
  projectStatusMeta,
} from "./project-status"

/**
 * La colonna «Stato» cliccabile della Dashboard (#88): il badge di stato che, per chi ha il
 * permesso «Opera su commesse sospese o chiuse», si apre in una tendina con i quattro stati
 * (Bozza · Attiva · Stand-by · Chiusa — mai «Annullata», che è l'eliminazione).
 *
 * Per chi non ha il permesso resta il badge fermo di sempre: mostrare una tendina che poi
 * risponde «non puoi» è peggio che non mostrarla.
 */
export function ProjectStatusCell({
  status,
  disabled,
  onChangeStatus,
}: {
  status: string
  disabled?: boolean
  onChangeStatus: (next: string) => void
}) {
  const meta = projectStatusMeta(status)
  const puoCambiare = canWriteFeature("action.project_locked_write")

  const badge = (
    <Badge
      variant="outline"
      className={cn(
        "gap-1",
        meta.className,
        meta.borderClassName,
        puoCambiare && "cursor-pointer select-none"
      )}
    >
      <meta.icon className="size-3.5" />
      {meta.label}
      {puoCambiare ? <ChevronDown className="size-3 opacity-60" /> : null}
    </Badge>
  )

  if (!puoCambiare) return badge

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        asChild
        disabled={disabled}
        onClick={(e) => e.stopPropagation()}
      >
        {badge}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" onClick={(e) => e.stopPropagation()}>
        {DASHBOARD_STATUS_META.map((s) => (
          <DropdownMenuItem
            key={s.value}
            disabled={s.value === status}
            onSelect={() => onChangeStatus(s.value)}
          >
            <s.icon className={cn("size-4", s.className)} />
            {s.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
