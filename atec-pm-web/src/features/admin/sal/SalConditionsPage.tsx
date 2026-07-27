import { ArrowLeft } from "lucide-react"
import { useLocation, useNavigate } from "react-router-dom"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
import {
  fetchSalConditions,
  createSalCondition,
  updateSalCondition,
  toggleActiveSalCondition,
  deleteSalCondition,
  reorderSalConditions,
  resetSalConditions,
  fetchSapCausali,
  createSapCausale,
  updateSapCausale,
  toggleSapCausale,
  deleteSapCausale,
  reorderSapCausali,
  resetSapCausali,
  fetchPaymentStates,
  createPaymentState,
  updatePaymentState,
  togglePaymentState,
  deletePaymentState,
  reorderPaymentStates,
  resetPaymentStates,
} from "@/lib/api/sal"
import type { SalCondition } from "@/lib/api/types"

import { SalOptionListPanel } from "./SalOptionListPanel"

/** Tab della pagina anagrafiche (valori usati anche dal deep-link `state.tab`). */
type SalAdminTab = "condizioni" | "sap" | "pagamenti"

const TAB_VALUES: SalAdminTab[] = ["condizioni", "sap", "pagamenti"]

/**
 * Stati pagamento «di sistema» (v10): `Pagata` e `Parzialmente Pagata` guidano
 * colori e regole di lock delle righe SAL → non rinominabili né eliminabili
 * (confronto case-insensitive; il server le blocca comunque).
 */
function isSystemPaymentState(row: SalCondition): boolean {
  const label = row.label.trim().toLowerCase()
  return label === "pagata" || label === "parzialmente pagata"
}

/**
 * Pagina «Anagrafiche SAL» (rotta storica `/admin/sal-conditions`, D7: nessuna
 * nuova rotta/feature key): tre cataloghi globali del modulo SAL in altrettanti
 * tab — Condizioni pagamento · Causali SAP · Stati Pagamento.
 */
export function SalConditionsPage() {
  const location = useLocation()
  const navigate = useNavigate()
  const fromProject = location.state?.fromProject
  const projectId = location.state?.projectId
  const backUrl = fromProject && projectId ? `/commesse/${projectId}/sal` : null

  // Deep-link: il foglio SAL apre direttamente il tab richiesto via state.tab
  const requestedTab = location.state?.tab as SalAdminTab | undefined
  const initialTab: SalAdminTab =
    requestedTab && TAB_VALUES.includes(requestedTab) ? requestedTab : "condizioni"

  return (
    <div className="space-y-4">
      {backUrl && (
        <Button variant="outline" size="sm" onClick={() => navigate(backUrl)} className="gap-1.5">
          <ArrowLeft className="size-4" />
          Commessa
        </Button>
      )}
      <Card>
        <CardHeader>
          <CardTitle>Anagrafiche SAL</CardTitle>
          <CardDescription>
            Cataloghi globali del modulo SAL: condizioni di pagamento, causali Conto SAP e stati
            pagamento utilizzabili negli step SAL delle commesse
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Tabs defaultValue={initialTab}>
            <TabsList>
              <TabsTrigger value="condizioni">Condizioni pagamento</TabsTrigger>
              <TabsTrigger value="sap">Causali SAP</TabsTrigger>
              <TabsTrigger value="pagamenti">Stati Pagamento</TabsTrigger>
            </TabsList>

            <TabsContent value="condizioni" className="pt-2">
              <SalOptionListPanel
                queryKey="sal-conditions"
                description="Condizioni di pagamento selezionabili nella colonna Condizioni degli step SAL"
                columnLabel="Condizione di pagamento"
                newItemPlaceholder="Nuova condizione di pagamento (es. 45 gg. dffm.)..."
                emptyText="Nessuna condizione presente. Aggiungine una dal campo qui sotto."
                resetConfirm={{
                  title: "Ripristina condizioni standard",
                  description:
                    "Le condizioni personalizzate verranno sostituite con l'elenco standard (A Vista, 30 gg. dffm., ecc.). Le commesse già create non verranno modificate.",
                }}
                deleteConfirm={{
                  title: "Elimina condizione",
                  description: (label) => `Eliminare la condizione "${label}"?`,
                }}
                api={{
                  list: () => fetchSalConditions(false),
                  create: (label) => createSalCondition({ label }),
                  rename: (row, label) => updateSalCondition(row.id, { label }),
                  toggleActive: (id, active) => toggleActiveSalCondition(id, active),
                  remove: (id) => deleteSalCondition(id),
                  reorder: (ids) => reorderSalConditions(ids),
                  reset: () => resetSalConditions(),
                }}
              />
            </TabsContent>

            <TabsContent value="sap" className="pt-2">
              <SalOptionListPanel
                queryKey="sal-sap-causali"
                description="Causali Conto SAP selezionabili nella colonna Conto SAP degli step SAL"
                columnLabel="Causale Conto SAP"
                newItemPlaceholder="Nuova causale Conto SAP (es. Acconto)..."
                emptyText="Nessuna causale presente. Aggiungine una dal campo qui sotto."
                resetConfirm={{
                  title: "Ripristina causali standard",
                  description:
                    "Le causali personalizzate verranno sostituite con l'elenco standard (Acconto, Ricavo). Le righe SAL già compilate non verranno modificate.",
                }}
                deleteConfirm={{
                  title: "Elimina causale",
                  description: (label) => `Eliminare la causale "${label}"?`,
                }}
                api={{
                  list: () => fetchSapCausali(),
                  create: (label) => createSapCausale(label),
                  rename: (row, label) => updateSapCausale(row.id, label),
                  toggleActive: (id, active) => toggleSapCausale(id, active),
                  remove: (id) => deleteSapCausale(id),
                  reorder: (ids) => reorderSapCausali(ids),
                  reset: () => resetSapCausali(),
                }}
              />
            </TabsContent>

            <TabsContent value="pagamenti" className="pt-2">
              <SalOptionListPanel
                queryKey="sal-payment-states"
                description="Stati pagamento/incasso selezionabili nella colonna Pagamento degli step SAL. «Pagata» e «Parzialmente Pagata» sono voci di sistema (colori comunque personalizzabili); il colore, se impostato, tinge la riga del foglio SAL."
                columnLabel="Stato pagamento"
                newItemPlaceholder="Nuovo stato pagamento (es. Anticipo)..."
                emptyText="Nessuno stato presente. Aggiungine uno dal campo qui sotto."
                resetConfirm={{
                  title: "Ripristina stati standard",
                  description:
                    "Gli stati personalizzati verranno sostituiti con l'elenco standard (Pagata, Parzialmente Pagata). Le righe SAL già compilate non verranno modificate.",
                }}
                deleteConfirm={{
                  title: "Elimina stato pagamento",
                  description: (label) => `Eliminare lo stato "${label}"?`,
                }}
                api={{
                  list: () => fetchPaymentStates(),
                  create: (label) => createPaymentState({ label }),
                  // PUT full-replace: nel rename i colori correnti viaggiano
                  // invariati (altrimenti verrebbero azzerati dal server)
                  rename: (row, label) =>
                    updatePaymentState(row.id, {
                      label,
                      colorBg: row.colorBg,
                      colorFg: row.colorFg,
                    }),
                  toggleActive: (id, active) => togglePaymentState(id, active),
                  remove: (id) => deletePaymentState(id),
                  reorder: (ids) => reorderPaymentStates(ids),
                  reset: () => resetPaymentStates(),
                  // Editor colori: label VUOTA = il server aggiorna solo i colori
                  // senza toccare l'etichetta → non si sovrascrive un eventuale
                  // rename concorrente con la label stantia della cache locale
                  saveColors: (row, colorBg, colorFg) =>
                    updatePaymentState(row.id, { label: "", colorBg, colorFg }),
                }}
                isSystemEntry={isSystemPaymentState}
                withColors
              />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>
    </div>
  )
}
