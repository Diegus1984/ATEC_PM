import { Badge } from "@/components/ui/badge"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type { ModuleStatus } from "@/config/navigation"

interface ModulePlaceholderProps {
  title: string
  description?: string
  status?: ModuleStatus
}

function statusBadge(status: ModuleStatus) {
  switch (status) {
    case "live":
      return <Badge>Disponibile</Badge>
    case "partial":
      return <Badge variant="secondary">Parziale</Badge>
    default:
      return <Badge variant="outline">In migrazione</Badge>
  }
}

export function ModulePlaceholder({
  title,
  description,
  status = "planned",
}: ModulePlaceholderProps) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-2">
          <CardTitle>{title}</CardTitle>
          {statusBadge(status)}
        </div>
        <CardDescription>
          {description ?? "Modulo non ancora disponibile in questa versione."}
        </CardDescription>
      </CardHeader>
      <CardContent className="text-sm text-muted-foreground">
        La voce di menu esiste già per fissare la struttura: la pagina arriva con lo
        sviluppo del modulo.
      </CardContent>
    </Card>
  )
}
