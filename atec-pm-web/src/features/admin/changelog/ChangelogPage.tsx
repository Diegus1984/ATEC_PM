import { useQuery } from "@tanstack/react-query"
import { Bug, GitCommitHorizontal } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { fetchChangelog } from "@/lib/api/changelog"

/** «26/08/26 12:02» dalla data ISO della voce; vuota = build ricostruita dalle segnalazioni. */
function dataOra(iso: string): string {
  if (!iso) return ""
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ""
  const dd = String(d.getDate()).padStart(2, "0")
  const mm = String(d.getMonth() + 1).padStart(2, "0")
  const yy = String(d.getFullYear() % 100).padStart(2, "0")
  const hh = String(d.getHours()).padStart(2, "0")
  const mi = String(d.getMinutes()).padStart(2, "0")
  return `${dd}/${mm}/${yy} ${hh}:${mi}`
}

/**
 * Changelog delle versioni pubblicate (dietro `nav.changelog`, di default solo Admin).
 * Le voci nascono DA SOLE al deploy: `aggiorna-server.ps1` raccoglie le prime righe dei
 * commit git dall'ultima versione spedita — nessuno deve compilare elenchi a mano.
 * Le «segnalazioni chiuse» vengono da `bug_reports.fixed_in_build`, quindi compaiono
 * anche per le build precedenti alla nascita di questa pagina.
 */
export function ChangelogPage() {
  const query = useQuery({ queryKey: ["changelog"], queryFn: fetchChangelog })
  const voci = query.data ?? []

  return (
    <Card>
      <CardHeader>
        <CardTitle>Changelog versioni</CardTitle>
        <CardDescription>
          Che cosa è cambiato a ogni versione pubblicata: le modifiche vengono dai
          commit del deploy, le segnalazioni chiuse dal modulo Segnalazioni.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {query.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : query.isError ? (
          <p className="text-sm text-destructive">
            {(query.error as Error).message}
          </p>
        ) : voci.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Nessuna versione registrata: la prima voce nasce col prossimo deploy.
          </p>
        ) : (
          <ol className="space-y-6">
            {voci.map((voce) => (
              <li key={voce.build} className="rounded-md border p-4">
                <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                  <span className="font-mono text-sm font-semibold text-primary">
                    {voce.build || "build sconosciuta"}
                  </span>
                  {dataOra(voce.data) ? (
                    <span className="text-xs text-muted-foreground">
                      {dataOra(voce.data)}
                    </span>
                  ) : null}
                </div>

                {voce.modifiche.length > 0 ? (
                  <ul className="mt-2 space-y-1">
                    {voce.modifiche.map((riga, index) => (
                      <li
                        key={index}
                        className="flex items-start gap-2 text-sm"
                      >
                        <GitCommitHorizontal className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                        <span>{riga}</span>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="mt-2 text-sm text-muted-foreground">
                    Nessun dettaglio registrato per questa build (precedente alla
                    nascita del changelog).
                  </p>
                )}

                {voce.segnalazioni.length > 0 ? (
                  <div className="mt-3 flex flex-wrap items-center gap-1.5">
                    <span className="text-xs font-medium text-muted-foreground">
                      Segnalazioni chiuse:
                    </span>
                    {voce.segnalazioni.map((seg) => (
                      <Badge key={seg.id} variant="outline" className="gap-1">
                        <Bug className="size-3" />
                        #{seg.id}
                        {seg.title ? ` — ${seg.title}` : ""}
                      </Badge>
                    ))}
                  </div>
                ) : null}
              </li>
            ))}
          </ol>
        )}
      </CardContent>
    </Card>
  )
}
