import * as React from "react"

import { QuoteProductDialog } from "@/features/catalogo-preventivi/QuoteProductDialog"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { fetchProduct } from "@/lib/api/quote-catalog"
import { canWriteFeature } from "@/lib/auth/permissions"
import { notifyError } from "@/lib/toast"

import { ComposizioneTab } from "./ComposizioneTab"
import { MagazzinoTab } from "./MagazzinoTab"
import { PerRobotTab } from "./PerRobotTab"

export function GammaRobotPage() {
  // Era l'ultimo punto del gestionale che decideva sul NOME del ruolo
  // (`userRole === "ADMIN"`): un elenco di nomi lascia fuori qualunque ruolo aggiunto
  // dopo, ed è così che il vecchio DEVELOPER — che stava SOPRA l'ADMIN — si ritrovava
  // trattato da tecnico. Ora è una chiave sulla persona.
  const canEditComposition = canWriteFeature("action.edit_gamma_robot")

  const [productDialog, setProductDialog] = React.useState<{
    open: boolean
    productId: number | null
    categoryId: number
  }>({ open: false, productId: null, categoryId: 0 })

  async function openProduct(productId: number) {
    if (productId <= 0) {
      notifyError("Componente senza anagrafica catalogo collegata.")
      return
    }
    try {
      const product = await fetchProduct(productId)
      setProductDialog({
        open: true,
        productId,
        categoryId: product.categoryId,
      })
    } catch (err) {
      notifyError(err instanceof Error ? err.message : "Errore caricamento prodotto")
    }
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Gamma Robot</CardTitle>
              <CardDescription>
                Distinta schede e componenti per modello robot e quadro
                (listino Gamma Ricambi).
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <Tabs defaultValue="robot">
            <TabsList>
              <TabsTrigger value="robot">Per Robot</TabsTrigger>
              <TabsTrigger value="magazzino">Magazzino</TabsTrigger>
              <TabsTrigger value="composizione">Composizione</TabsTrigger>
            </TabsList>
            <TabsContent value="robot" className="mt-4">
              <PerRobotTab onOpenProduct={(id) => void openProduct(id)} />
            </TabsContent>
            <TabsContent value="magazzino" className="mt-4">
              <MagazzinoTab onOpenProduct={(id) => void openProduct(id)} />
            </TabsContent>
            <TabsContent value="composizione" className="mt-4">
              <ComposizioneTab
                isAdmin={canEditComposition}
                onOpenProduct={(id) => void openProduct(id)}
              />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>

      <QuoteProductDialog
        open={productDialog.open}
        categoryId={productDialog.categoryId}
        productId={productDialog.productId}
        onClose={() =>
          setProductDialog({ open: false, productId: null, categoryId: 0 })
        }
        onSaved={() =>
          setProductDialog({ open: false, productId: null, categoryId: 0 })
        }
      />
    </div>
  )
}
