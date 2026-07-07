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
          {description ??
            "Modulo in migrazione dal client WPF. L'API esiste già: prossimo sprint UI web."}
        </CardDescription>
      </CardHeader>
      <CardContent className="text-sm text-muted-foreground">
        Nel frattempo puoi usare il client Windows (ATEC.PM.Client) per questo modulo.
      </CardContent>
    </Card>
  )
}
