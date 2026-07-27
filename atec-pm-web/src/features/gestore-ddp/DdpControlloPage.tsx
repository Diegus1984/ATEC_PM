import { useQuery } from "@tanstack/react-query"
import { BarChart3, LayoutGrid } from "lucide-react"
import { useNavigate, useParams, useSearchParams } from "react-router-dom"

import {
  PmSidebar,
  type PmContainer,
  type PmQuickView,
} from "@/components/shared/pm-sidebar"
import {
  fetchDdpControlSummary,
  fetchDdpDeliveriesByDay,
  fetchDdpSummary,
} from "@/lib/api/ddp-manager"
import { useProjectHub } from "@/lib/signalr/use-project-hub"

import { CONTROL_REPORTS, controlReportDef } from "./ddp-control-defs"
import { DdpConsegneView } from "./DdpConsegneView"
import { DdpControlReportView } from "./DdpControlReportView"
import { GestoreDdpPage } from "./GestoreDdpPage"

// Pannello della sezione Gestore DDP: sidebar PM condivisa (stesso formato del Pannello
// Lavorazioni) con il Gestore in testa alle viste rapide, l'Analisi Consegne e i 6 report
// di controllo come elenco; il contenuto selezionato è renderizzato a destra.
// La vista vive nell'URL (/gestore-ddp per il Gestore, /gestore-ddp/controllo/:view per
// il resto) così i deep-link e il back del browser funzionano.

export function DdpControlloPage({ defaultView = "rit" }: { defaultView?: string }) {
  const navigate = useNavigate()
  const { view } = useParams<{ view: string }>()
  const [searchParams] = useSearchParams()
  const requested = view ?? defaultView
  const current =
    requested === "gestore" || requested === "consegne" || controlReportDef(requested)
      ? requested
      : "rit"
  const ddpType = searchParams.get("type") === "OFFICINA" ? "OFFICINA" : "COMMERCIAL"

  // staleTime 0: gli aggregati del pannello dipendono dalle distinte modificate in
  // ALTRE pagine, e l'hub esclude la connessione che ha fatto la modifica — senza
  // questo, tornando qui entro i 5 min della cache globale si vedrebbero dati vecchi.
  const summaryQuery = useQuery({
    queryKey: ["ddp-summary"],
    queryFn: fetchDdpSummary,
    staleTime: 0,
  })
  const controlQuery = useQuery({
    queryKey: ["ddp-control-summary"],
    queryFn: fetchDdpControlSummary,
    staleTime: 0,
  })
  const deliveriesQuery = useQuery({
    queryKey: ["ddp-deliveries-by-day"],
    queryFn: fetchDdpDeliveriesByDay,
    staleTime: 0,
  })

  useProjectHub("all", () => {
    void summaryQuery.refetch()
    void controlQuery.refetch()
    void deliveriesQuery.refetch()
  })

  const byReport = new Map(
    (controlQuery.data ?? []).map((entry) => [entry.report, entry])
  )

  const quickViews: PmQuickView[] = [
    {
      key: "gestore",
      selected: current === "gestore",
      onClick: () => navigate("/gestore-ddp"),
      icon: <LayoutGrid />,
      label: "Gestore DDP",
      count: summaryQuery.data?.length ?? 0,
    },
    {
      key: "consegne",
      selected: current === "consegne",
      onClick: () => navigate("/gestore-ddp/controllo/consegne"),
      icon: <BarChart3 />,
      label: "Analisi Consegne",
      count: deliveriesQuery.data?.length ?? 0,
    },
  ]

  function selectReport(report: string, type: "COMMERCIAL" | "OFFICINA") {
    navigate(`/gestore-ddp/controllo/${report}?type=${type}`)
  }

  const containers: PmContainer[] = CONTROL_REPORTS.map((def) => {
    const counts = byReport.get(def.key)
    const total = (counts?.commercialCount ?? 0) + (counts?.officinaCount ?? 0)
    return {
      key: def.key,
      selected: current === def.key,
      onClick: () => selectReport(def.key, "COMMERCIAL"),
      label: `${def.badge} — ${def.title}`,
      count: total,
      dots:
        total > 0
          ? [
              def.key === "rit"
                ? { dotClass: "bg-rose-500", label: "In ritardo" }
                : { dotClass: "bg-amber-500", label: "Da gestire" },
            ]
          : [],
      children: [
        {
          key: `${def.key}-commercial`,
          selected: current === def.key && ddpType === "COMMERCIAL",
          onClick: () => selectReport(def.key, "COMMERCIAL"),
          label: "Commerciali",
          count: counts?.commercialCount ?? 0,
          dotClass: "bg-sky-500",
        },
        {
          key: `${def.key}-officina`,
          selected: current === def.key && ddpType === "OFFICINA",
          onClick: () => selectReport(def.key, "OFFICINA"),
          label: "Officine",
          count: counts?.officinaCount ?? 0,
          dotClass: "bg-amber-500",
        },
      ],
    }
  })

  const reportDef = controlReportDef(current)

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight text-foreground">
          Gestore DDP
        </h1>
        <p className="text-sm text-muted-foreground">
          Distinte per commessa, report di controllo e analisi delle consegne su tutte
          le commesse.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar
          storageKey="ddp-controllo"
          quickViews={quickViews}
          containers={containers}
          containersLabel="Report di Controllo Commesse"
          emptyLabel="Nessun report disponibile"
        />

        <main className="min-w-0 flex-1 overflow-y-auto p-4">
          {current === "gestore" ? (
            <GestoreDdpPage />
          ) : current === "consegne" ? (
            <DdpConsegneView />
          ) : reportDef ? (
            <DdpControlReportView def={reportDef} ddpType={ddpType} />
          ) : null}
        </main>
      </div>
    </div>
  )
}
