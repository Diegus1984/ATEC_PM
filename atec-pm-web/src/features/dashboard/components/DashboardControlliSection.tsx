import * as React from "react"
import { useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import { ArrowUpRight, ClipboardList, Clock, Plane, ReceiptText, type LucideIcon } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { canAccessFeature } from "@/lib/auth/permissions"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { ddpTypeLabel } from "@/features/commesse/ddp-constants"
import { DDP_UPDATED_QUERY_KEY, useDdpUpdatedList } from "@/features/gestore-ddp/useDdpUpdatedList"
import { useSalWarnings } from "@/features/sal/useSalWarnings"
import { useTravelBadge } from "@/features/trasferta/useTravelBadge"
import { useOreCommessaBadge } from "@/features/ore-commessa/useOreCommessaBadge"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { useGlobalSalHub } from "@/lib/signalr/use-sal-hub"
import { cn } from "@/lib/utils"

/** Voce dell'elenco dentro una card: si apre nel punto preciso, non solo sulla pagina. */
interface ControlItem {
  id: string
  /** Riga principale: cosa è (tag DDP, commessa, nome). */
  primary: React.ReactNode
  /** Riga di servizio: chi/quando, importi. */
  secondary?: React.ReactNode
  /** Rotta di destinazione del clic sulla voce. */
  path: string
}

interface ControlCardProps {
  title: string
  icon: LucideIcon
  count: number
  description: string
  path: string
  /**
   * Elenco delle cose da verificare (#114). Quando c'è, è lui a comandare l'avviso:
   * elenco vuoto = card neutra, mai rossa.
   */
  items?: ControlItem[]
}

function ControlCard({ title, icon: Icon, count, description, path, items }: ControlCardProps) {
  const navigate = useNavigate()
  const hasWarning = count > 0

  return (
    <Card
      role="button"
      tabIndex={0}
      onClick={() => navigate(path)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault()
          navigate(path)
        }
      }}
      className={cn(
        "group/card cursor-pointer transition-all hover:shadow-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        hasWarning
          ? "border-red-300 bg-red-50 hover:bg-red-100/70 dark:border-red-900/60 dark:bg-red-950/40 dark:hover:bg-red-900/30"
          : "hover:bg-muted/40"
      )}
    >
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-2 min-w-0">
            <Icon
              className={cn(
                "size-4 shrink-0",
                hasWarning ? "text-red-700 dark:text-red-400" : "text-muted-foreground"
              )}
            />
            <CardTitle
              className={cn(
                "truncate text-sm font-semibold",
                hasWarning ? "text-red-900 dark:text-red-100" : "text-foreground"
              )}
            >
              {title}
            </CardTitle>
          </div>
          <div className="flex items-center gap-1.5 shrink-0">
            {hasWarning ? (
              <Badge
                variant="destructive"
                className="px-1.5 py-0 text-[10px] font-semibold uppercase tracking-wider"
              >
                Avviso
              </Badge>
            ) : null}
            <ArrowUpRight
              className={cn(
                "size-4 opacity-60 transition-transform group-hover/card:translate-x-0.5 group-hover/card:-translate-y-0.5",
                hasWarning ? "text-red-700 dark:text-red-400" : "text-muted-foreground"
              )}
            />
          </div>
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        <div
          className={cn(
            "text-3xl font-bold tabular-nums tracking-tight",
            hasWarning ? "text-red-700 dark:text-red-300" : "text-foreground"
          )}
        >
          {count}
        </div>
        <p
          className={cn(
            "mt-1 text-xs line-clamp-2",
            hasWarning ? "text-red-700/90 dark:text-red-300/90" : "text-muted-foreground"
          )}
        >
          {description}
        </p>

        {items && items.length > 0 ? (
          // Contenitore a BLOCCO con space-y: dentro uno scroller un flex-col schiaccia
          // le voci a zero. Altezza limitata perché la card resti una card.
          <div className="mt-3 max-h-56 space-y-1 overflow-y-auto pr-0.5">
            {items.map((item) => (
              <button
                key={item.id}
                type="button"
                // Il clic sulla voce porta al punto preciso; senza stopPropagation
                // vincerebbe la navigazione generica della card.
                onClick={(e) => {
                  e.stopPropagation()
                  navigate(item.path)
                }}
                className="block w-full rounded-md border border-red-200/70 bg-white/70 px-2 py-1.5 text-left text-xs transition-colors hover:bg-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring dark:border-red-900/50 dark:bg-red-950/30 dark:hover:bg-red-900/30"
              >
                <span className="block truncate font-medium text-red-900 dark:text-red-100">
                  {item.primary}
                </span>
                {item.secondary ? (
                  <span className="block truncate text-[11px] text-red-700/80 dark:text-red-300/80">
                    {item.secondary}
                  </span>
                ) : null}
              </button>
            ))}
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}

/**
 * Sezione «Gestione Controlli» della Dashboard principale (#113, elenchi dalla #114).
 *
 * Mostra sotto le commesse le quattro card di avviso/controllo per il PM:
 * 1. DDP Commesse — elenco delle distinte aggiornate di recente e non ancora aperte
 * 2. SAL / Fatturazione — elenco dei warning di fatturazione e incasso attivi
 * 3. Trasferte (persone con ore di cantiere non verificate)
 * 4. Ore Commessa (persone con ore commessa non verificate)
 *
 * Le card con conteggio a 0 restano visibili in stato neutro (pannello di controllo).
 * Le card con conteggio > 0 si colorano in rosso avviso.
 * Ciascuna card è visibile solo agli utenti che hanno il relativo permesso.
 *
 * Le prime due si svuotano da sole (#114): la DDP esce dall'elenco quando chi guarda la
 * apre nel Gestore, il warning SAL quando viene risolto dalla pagina `/sal`. Elenco vuoto
 * = niente rosso, perché non è rimasto niente da verificare.
 */
export function DashboardControlliSection() {
  const puoDdp = canAccessFeature("nav.gestore_ddp")
  const puoSal = canAccessFeature("nav.sal")
  const puoTrasferta = canAccessFeature("nav.trasferta")
  const puoOre = canAccessFeature("nav.ore_commessa")

  const ddpUpdated = useDdpUpdatedList(7)
  const salWarnings = useSalWarnings()
  const travelCount = useTravelBadge()
  const oreCount = useOreCommessaBadge()

  // Ambiente condiviso: gli elenchi parlano proprio del lavoro degli altri, quindi non
  // possono aspettare il giro di polling. Il collega tocca una DDP o aggiorna un SAL e la
  // card si rifà — best-effort, se l'hub è spento restano i 60 secondi.
  const queryClient = useQueryClient()
  useProjectHub(puoDdp ? "all" : null, () => {
    void queryClient.invalidateQueries({ queryKey: DDP_UPDATED_QUERY_KEY })
  })
  useGlobalSalHub(puoSal, () => {
    void queryClient.invalidateQueries({ queryKey: ["sal-prospetto"] })
  })

  const ddpItems = React.useMemo<ControlItem[]>(
    () =>
      ddpUpdated.map((d) => ({
        id: `${d.projectId}-${d.ddpType}`,
        primary: `DDP ${ddpTypeLabel(d.ddpType)} · ${d.code}${d.title ? ` · ${d.title}` : ""}`,
        secondary: [d.updatedBy, d.updatedAt ? formatDateShort(d.updatedAt) : ""]
          .filter(Boolean)
          .join(" · "),
        // Stessa rotta del Gestore DDP: aprirla è anche ciò che toglie la voce dall'elenco.
        path: `/gestore-ddp/${d.projectId}?type=${d.ddpType}`,
      })),
    [ddpUpdated]
  )

  const salItems = React.useMemo<ControlItem[]>(
    () =>
      salWarnings.map((w, index) => ({
        id: `${w.projectId}-${w.ord}-${w.dataFatt ?? ""}-${index}`,
        primary: `${w.code}${w.cliente ? ` · ${w.cliente}` : ""}${w.step ? ` · ${w.step}` : ""}`,
        secondary: [
          w.alert === "incasso"
            ? "Incasso scaduto"
            : w.alert === "warn"
              ? "Fatturazione scaduta"
              : "Pre-warning fatturazione",
          w.dataFatt ? formatDateShort(w.dataFatt) : "",
          w.importo != null ? euro(w.importo) : "",
        ]
          .filter(Boolean)
          .join(" · "),
        // Ogni voce apre la vista che la contiene, già filtrata sulle sole righe in allarme.
        path: `/sal?view=${w.kind === "incasso" ? "warn-incasso" : "warn-fatturazione"}`,
      })),
    [salWarnings]
  )

  // Se l'utente non ha accesso a nessuna delle quattro funzionalità, la sezione non si mostra.
  if (!puoDdp && !puoSal && !puoTrasferta && !puoOre) {
    return null
  }

  return (
    <section className="flex flex-col gap-3">
      <div>
        <h2 className="text-base font-semibold tracking-tight">Gestione Controlli</h2>
        <p className="text-xs text-muted-foreground">
          Pannello di controllo e avvisi su DDP, SAL fatturazione, trasferte e ore commessa
        </p>
      </div>

      <div className="grid grid-cols-1 items-start gap-4 @xl/main:grid-cols-2 @5xl/main:grid-cols-4">
        {puoDdp && (
          <ControlCard
            title="DDP Commesse"
            icon={ClipboardList}
            count={ddpItems.length}
            description={
              ddpItems.length > 0
                ? ddpItems.length === 1
                  ? "1 DDP aggiornata da verificare"
                  : `${ddpItems.length} DDP aggiornate da verificare`
                : "Nessuna DDP aggiornata negli ultimi 7 giorni"
            }
            path="/gestore-ddp"
            items={ddpItems}
          />
        )}

        {puoSal && (
          <ControlCard
            title="SAL / Fatturazione"
            icon={ReceiptText}
            count={salItems.length}
            description={
              salItems.length > 0
                ? salItems.length === 1
                  ? "1 warning di fatturazione o incasso attivo"
                  : `${salItems.length} warning di fatturazione o incasso attivi`
                : "Nessun warning di fatturazione o incasso SAL"
            }
            // La card intera apre la vista del primo warning in elenco: con soli incassi
            // scaduti, mandare comunque su «Fatturazione» farebbe atterrare su una tabella vuota.
            path={salItems[0]?.path ?? "/sal?view=warn-fatturazione"}
            items={salItems}
          />
        )}

        {puoTrasferta && (
          <ControlCard
            title="Trasferte"
            icon={Plane}
            count={travelCount}
            description={
              travelCount > 0
                ? travelCount === 1
                  ? "1 persona con ore di cantiere da verificare"
                  : `${travelCount} persone con ore di cantiere da verificare`
                : "Nessuna ora di cantiere da verificare"
            }
            path="/trasferta"
          />
        )}

        {puoOre && (
          <ControlCard
            title="Ore Commessa"
            icon={Clock}
            count={oreCount}
            description={
              oreCount > 0
                ? oreCount === 1
                  ? "1 persona con ore commessa da verificare"
                  : `${oreCount} persone con ore commessa da verificare`
                : "Nessuna ora commessa da verificare"
            }
            path="/ore-commessa"
          />
        )}
      </div>
    </section>
  )
}
