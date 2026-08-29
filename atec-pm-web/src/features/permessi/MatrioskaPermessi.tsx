import * as React from "react"
import { ChevronDown, RotateCcw } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { CATALOGO_PERMESSI, type VoceCatalogoGen } from "@/config/catalogo.gen"
import { NAV_GROUPS, flattenNavItems } from "@/config/navigation"
import { COMMESSA_SECTIONS } from "@/features/commesse/commessa-sections"
import type { FunzionePermessoDto, StatoCombo } from "@/lib/api/types"
import { etichettaStato } from "./stato-permesso"

/**
 * La scheda matrioska (PIANO-PERMESSI-REBUILD.md §5, passo 5): l'editor RENDE l'albero del
 * catalogo unico con uno switch sul `kind` — una voce aggiunta a `catalogo-permessi.json`
 * compare qui da sola, senza toccare questa pagina (§12.2). Le regole che incarna:
 *
 * - **spegnere non è cancellare** (§3.7): ogni gesto scrive una riga (anche `NO`) marcata
 *   «a mano», che «Applica template» rispetta;
 * - **i micro sono chiavi figlie** (§12.1): «Vede prezzi» scrive su `<chiave>.prices`,
 *   una chiave come le altre;
 * - **menu ≠ albero commessa** (§3.2): le sezioni dell'albero stanno ANNIDATE sotto
 *   Commesse, con le loro chiavi `project.*`;
 * - il renderer di DEFAULT (kind sconosciuto → riga con combo) tiene la pagina viva
 *   quando il catalogo cresce.
 */

export interface MatrioskaProps {
  funzioni: FunzionePermessoDto[]
  pending: boolean
  onImposta: (featureKey: string, stato: StatoCombo) => void
  /** Accende/spegne un'intera sezione: un solo gesto, tante chiavi. */
  onImpostaTante: (featureKeys: string[], stato: StatoCombo) => void
  /** «Torna al template»: toglie la decisione a mano su una chiave. */
  onRiallinea: (featureKey: string) => void
}

/** Stato effettivo per chiave, dal server (jolly già espanso). Chiave assente = NO. */
function useStati(funzioni: FunzionePermessoDto[]) {
  return React.useMemo(() => {
    const mappa = new Map<string, FunzionePermessoDto>()
    for (const f of funzioni) mappa.set(f.featureKey, f)
    return mappa
  }, [funzioni])
}

function statoDi(mappa: Map<string, FunzionePermessoDto>, chiave: string): StatoCombo {
  return mappa.get(chiave)?.stato ?? "NO"
}

/** Le voci COMANDABILI di un ramo (chiave propria, non ritirate): decidono la pill di sezione. */
function vociComandabili(voce: VoceCatalogoGen): VoceCatalogoGen[] {
  return (voce.figli ?? []).filter((f) => f.chiave != null && !f.ritirata)
}

// ─────────────────────────────────────────────────────────────────────────────
// Editor (colonna destra)
// ─────────────────────────────────────────────────────────────────────────────

export function MatrioskaEditor(props: MatrioskaProps) {
  const stati = useStati(props.funzioni)
  return (
    <div className="space-y-3">
      {CATALOGO_PERMESSI.map((sezione) => (
        <SezioneMenu key={sezione.label} sezione={sezione} stati={stati} {...props} />
      ))}
    </div>
  )
}

/** Un gruppo del menu laterale: toggle padre, pill spenta/parziale/tutta, voci dentro. */
function SezioneMenu({
  sezione,
  stati,
  ...props
}: MatrioskaProps & { sezione: VoceCatalogoGen; stati: Map<string, FunzionePermessoDto> }) {
  const [aperta, setAperta] = React.useState(false)
  const voci = vociComandabili(sezione)
  const accese = voci.filter((v) => statoDi(stati, v.chiave!) !== "NO").length

  const pill =
    accese === 0 ? "spenta" : accese === voci.length ? "tutta" : `parziale ${accese}/${voci.length}`

  function toggleSezione(accendi: boolean) {
    const daCambiare = voci
      .filter((v) => (statoDi(stati, v.chiave!) !== "NO") !== accendi)
      .map((v) => v.chiave!)
    if (daCambiare.length > 0) props.onImpostaTante(daCambiare, accendi ? "FULL" : "NO")
  }

  return (
    <div className="rounded-lg border">
      <div className="flex items-center gap-3 px-3 py-2">
        <Switch
          checked={accese > 0}
          disabled={props.pending || voci.length === 0}
          onCheckedChange={toggleSezione}
          aria-label={`Accendi o spegni tutta la sezione ${sezione.label}`}
        />
        <button
          type="button"
          className="flex flex-1 items-center justify-between gap-2 text-left"
          onClick={() => setAperta((v) => !v)}
        >
          <span className="text-sm font-medium">{sezione.label}</span>
          <span className="flex items-center gap-2">
            <Badge variant={accese === 0 ? "outline" : "secondary"}>{pill}</Badge>
            <ChevronDown
              className={`size-4 shrink-0 transition-transform duration-(--accordion-duration) ease-(--accordion-ease) ${
                aperta ? "rotate-180" : ""
              }`}
            />
          </span>
        </button>
      </div>
      <Collapsible open={aperta}>
        <div className="space-y-1 border-t px-3 py-2">
          {(sezione.figli ?? []).map((voce, i) => (
            <RigaCatalogo key={voce.chiave ?? `${voce.label}-${i}`} voce={voce} stati={stati} {...props} />
          ))}
        </div>
      </Collapsible>
    </div>
  )
}

/**
 * Una voce del catalogo, POLIMORFICA sul kind: voce di menu e sezione-commessa hanno
 * checkbox + micro; le azioni una combo; un kind futuro cade nel renderer di default.
 */
function RigaCatalogo({
  voce,
  stati,
  annidata,
  ...props
}: MatrioskaProps & {
  voce: VoceCatalogoGen
  stati: Map<string, FunzionePermessoDto>
  annidata?: boolean
}) {
  if (voce.ritirata) return null

  // Sezione-commessa senza chiave (Prev vs Consuntivo): informativa, non comandabile.
  if (voce.chiave == null) {
    return (
      <div className="py-1.5 pl-6 text-sm text-muted-foreground">
        {voce.label} <span className="text-xs">— senza chiave propria (governata altrove)</span>
      </div>
    )
  }

  const figli = (voce.figli ?? []).filter((f) => !f.ritirata)
  const sezioniCommessa = figli.filter((f) => f.kind === "sezione-commessa")
  const azioni = figli.filter((f) => f.kind !== "sezione-commessa")

  return (
    <div className={annidata ? "pl-4" : undefined}>
      {voce.kind === "voce" || voce.kind === "sezione-commessa" ? (
        <RigaConMicro voce={voce} stati={stati} {...props} />
      ) : (
        // azioni, ambiti e i kind di domani: etichetta + combo a tre stati.
        <RigaAzione voce={voce} stati={stati} {...props} />
      )}

      {sezioniCommessa.length > 0 ? (
        <div className="mb-1 ml-6 mt-1 space-y-1 rounded-md border-l pl-3">
          <div className="pt-1 text-xs font-medium text-muted-foreground">
            Sezioni nell'albero della commessa
          </div>
          {sezioniCommessa.map((f, i) => (
            <RigaCatalogo key={f.chiave ?? `${f.label}-${i}`} voce={f} stati={stati} {...props} />
          ))}
        </div>
      ) : null}

      {azioni.length > 0 ? (
        <div className="mb-1 ml-6 mt-1 space-y-1 border-l pl-3">
          {azioni.map((f, i) => (
            <RigaCatalogo key={f.chiave ?? `${f.label}-${i}`} voce={f} stati={stati} {...props} />
          ))}
        </div>
      ) : null}
    </div>
  )
}

/** Voce con checkbox visibile + micro «Sola lettura» e (se dichiarato) «Vede prezzi». */
function RigaConMicro({
  voce,
  stati,
  ...props
}: MatrioskaProps & { voce: VoceCatalogoGen; stati: Map<string, FunzionePermessoDto> }) {
  const chiave = voce.chiave!
  const funzione = stati.get(chiave)
  const stato = funzione?.stato ?? "NO"
  const accesa = stato !== "NO"
  const aMano = funzione?.origin === "MANO"
  const haPrezzi = (voce.micros ?? []).includes("prices")
  const chiaveMicro = `${chiave}.prices`
  const microStato = statoDi(stati, chiaveMicro)
  const microAMano = stati.get(chiaveMicro)?.origin === "MANO"

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 py-1">
      <label className="flex min-w-[220px] flex-1 cursor-pointer items-center gap-2">
        <Checkbox
          checked={accesa}
          disabled={props.pending}
          onCheckedChange={(v) => props.onImposta(chiave, v ? "FULL" : "NO")}
        />
        <span className="text-sm">{voce.label}</span>
        {voce.chiaveCondivisa ? (
          <Badge variant="outline" className="text-xs" title="Stessa chiave della voce di menu: qui e lì si comanda la stessa cosa.">
            condivisa
          </Badge>
        ) : null}
        {aMano ? <Badge variant="secondary">a mano</Badge> : null}
      </label>

      {accesa ? (
        <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Switch
            checked={stato === "READ"}
            disabled={props.pending}
            onCheckedChange={(v) => props.onImposta(chiave, v ? "READ" : "FULL")}
            aria-label={`Sola lettura su ${voce.label}`}
          />
          sola lettura
        </span>
      ) : null}

      {haPrezzi && accesa ? (
        <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Switch
            checked={microStato !== "NO"}
            disabled={props.pending}
            onCheckedChange={(v) => props.onImposta(chiaveMicro, v ? "FULL" : "NO")}
            aria-label={`Vede prezzi su ${voce.label}`}
          />
          vede prezzi
          {microAMano ? <Badge variant="secondary">a mano</Badge> : null}
        </span>
      ) : null}

      {aMano || microAMano ? (
        <Button
          variant="ghost"
          size="sm"
          className="h-7 px-2 text-xs"
          disabled={props.pending}
          title="Torna al template: toglie le decisioni a mano su questa voce"
          onClick={() => {
            props.onRiallinea(chiave)
            if (microAMano) props.onRiallinea(chiaveMicro)
          }}
        >
          <RotateCcw className="size-3.5" />
          template
        </Button>
      ) : null}
    </div>
  )
}

/** Azione (o kind futuro): etichetta + combo NO / sola lettura / piena. */
function RigaAzione({
  voce,
  stati,
  ...props
}: MatrioskaProps & { voce: VoceCatalogoGen; stati: Map<string, FunzionePermessoDto> }) {
  const chiave = voce.chiave!
  const funzione = stati.get(chiave)
  const stato = funzione?.stato ?? "NO"
  const aMano = funzione?.origin === "MANO"

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 py-1">
      <span className="min-w-[220px] flex-1 text-sm">
        {voce.label}
        {aMano ? (
          <Badge variant="secondary" className="ml-2">
            a mano
          </Badge>
        ) : null}
      </span>
      <Select
        value={stato}
        onValueChange={(v) => props.onImposta(chiave, v as StatoCombo)}
        disabled={props.pending}
      >
        <SelectTrigger size="sm" className="w-[180px]">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {(["NO", "READ", "FULL"] as StatoCombo[]).map((s) => (
            <SelectItem key={s} value={s}>
              {etichettaStato(s)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {aMano ? (
        <Button
          variant="ghost"
          size="sm"
          className="h-7 px-2 text-xs"
          disabled={props.pending}
          title="Torna al template"
          onClick={() => props.onRiallinea(chiave)}
        >
          <RotateCcw className="size-3.5" />
          template
        </Button>
      ) : null}
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Anteprima (colonna sinistra): cosa vedrebbe a video, calcolata dagli stessi stati
// ─────────────────────────────────────────────────────────────────────────────

export function AnteprimaVideo({ funzioni }: { funzioni: FunzionePermessoDto[] }) {
  const stati = useStati(funzioni)
  const puo = (chiave: string) => statoDi(stati, chiave) !== "NO"

  const catalogoPerChiave = React.useMemo(() => {
    const mappa = new Map<string, VoceCatalogoGen>()
    const scendi = (voce: VoceCatalogoGen) => {
      if (voce.chiave != null && !voce.chiaveCondivisa) mappa.set(voce.chiave, voce)
      for (const figlio of voce.figli ?? []) scendi(figlio)
    }
    for (const radice of CATALOGO_PERMESSI) scendi(radice)
    return mappa
  }, [])

  const pillPrezzi = (chiave: string) => {
    if (!(catalogoPerChiave.get(chiave)?.micros ?? []).includes("prices")) return null
    return puo(`${chiave}.prices`) ? null : (
      <Badge variant="outline" className="text-xs">
        senza prezzi
      </Badge>
    )
  }

  return (
    <div className="space-y-4 text-sm">
      <div>
        <div className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Menu laterale
        </div>
        <div className="space-y-2">
          {NAV_GROUPS.map((gruppo) => {
            const visibili = flattenNavItems(gruppo.items).filter((item) =>
              puo(item.featureKey)
            )
            if (visibili.length === 0) return null
            return (
              <div key={gruppo.id}>
                <div className="text-xs text-muted-foreground">{gruppo.label}</div>
                <ul className="ml-3 space-y-0.5">
                  {visibili.map((item) => (
                    <li key={item.id} className="flex items-center gap-2">
                      {item.label}
                      {statoDi(stati, item.featureKey) === "READ" ? (
                        <Badge variant="outline" className="text-xs">
                          sola lettura
                        </Badge>
                      ) : null}
                    </li>
                  ))}
                </ul>
              </div>
            )
          })}
          {NAV_GROUPS.every((g) =>
            flattenNavItems(g.items).every((i) => !puo(i.featureKey))
          ) ? (
            <div className="text-muted-foreground">Nessuna voce di menu visibile.</div>
          ) : null}
        </div>
      </div>

      <div>
        <div className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Albero sotto una commessa
        </div>
        {puo("nav.commesse") ? (
          <ul className="ml-3 space-y-0.5">
            {COMMESSA_SECTIONS.filter(
              (sezione) => !sezione.featureKey || puo(sezione.featureKey)
            ).map((sezione) => (
              <li key={sezione.key} className="flex items-center gap-2">
                {sezione.label}
                {sezione.featureKey && statoDi(stati, sezione.featureKey) === "READ" ? (
                  <Badge variant="outline" className="text-xs">
                    sola lettura
                  </Badge>
                ) : null}
                {sezione.featureKey ? pillPrezzi(sezione.featureKey) : null}
              </li>
            ))}
          </ul>
        ) : (
          <div className="text-muted-foreground">
            Non vede le Commesse: nessun albero da mostrare.
          </div>
        )}
      </div>
    </div>
  )
}
