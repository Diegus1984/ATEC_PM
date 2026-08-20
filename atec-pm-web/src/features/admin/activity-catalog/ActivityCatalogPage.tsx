import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

import { ActivityCatalogEditor } from "./ActivityCatalogEditor"

// Anagrafica attività: catalogo globale delle voci-attività standard, precaricate alla creazione
// di una commessa. Fedele al modale "Anagrafica attività" del prototipo: aggiungi, rinomina inline,
// riordina con drag-and-drop, disattiva/elimina, «Ripristina standard». Le voci hanno id stabile.
// La griglia sta in `ActivityCatalogEditor`, condivisa con il dialogo richiamato dal form commessa.
export function ActivityCatalogPage() {
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>Anagrafica attività</CardTitle>
          <CardDescription>
            Catalogo delle voci-attività standard precaricate alla creazione di una
            commessa
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ActivityCatalogEditor />
        </CardContent>
      </Card>
    </div>
  )
}
