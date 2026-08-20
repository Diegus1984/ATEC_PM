// ── Anagrafica tariffe (`tariff_options`) ──────────────────────────────────
//
// L'API esisteva già completa dal giorno uno (GET/POST/DELETE, anti-duplicati, blocco
// della cancellazione se il valore è in uso) ma NESSUN file del web la chiamava: i valori
// a DB (25/50/80 €, 0,90 €/km, …) non erano raggiungibili da nessuna schermata.
// Qui c'è la UI mancante, più il PUT per correggere un importo.
//
// Nota importante che la pagina dice a video: modificare un importo NON riscrive la
// storia. Il valore viene copiato nella riga quando lo si sceglie, quindi qui si sposta
// solo quello che verrà proposto d'ora in avanti.

import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { MoneyInput } from "@/components/shared/money-input"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import {
  addTariffOption,
  deleteTariffOption,
  fetchTariffOptions,
  updateTariffOption,
} from "@/lib/api/tariffs"
import { TARIFF_TYPES, type TariffOptionDto } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { euro, parseDecimal } from "@/lib/format"
import { notifyError, notifySuccess } from "@/lib/toast"

const TARIFFS_QUERY_KEY = ["tariff-options"]

/**
 * Elenco tariffe, opzionalmente ristretto ad alcuni tipi (il calcolatore Officine mostra
 * solo le tariffe orarie, la Configurazione sezioni le mostra tutte).
 */
export function TariffOptionsPanel({
  types,
}: {
  types?: readonly string[]
}) {
  const queryClient = useQueryClient()
  // `canWriteFeature` e non `canAccessFeature`: questo pannello MODIFICA le tariffe, quindi
  // con la funzione concessa in sola lettura i comandi devono sparire, non restare cliccabili
  // e farsi respingere dall'API a cose fatte.
  const canEdit = canWriteFeature("nav.config_sezioni")

  const query = useQuery({
    queryKey: TARIFFS_QUERY_KEY,
    queryFn: () => fetchTariffOptions(),
  })

  const shown = React.useMemo(
    () => TARIFF_TYPES.filter((t) => !types || types.includes(t.key)),
    [types]
  )

  const byType = React.useMemo(() => {
    const map = new Map<string, TariffOptionDto[]>()
    for (const option of query.data ?? []) {
      const list = map.get(option.tariffType) ?? []
      list.push(option)
      map.set(option.tariffType, list)
    }
    for (const list of map.values()) list.sort((a, b) => a.value - b.value)
    return map
  }, [query.data])

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: TARIFFS_QUERY_KEY })
  }

  if (query.isLoading) {
    return <p className="text-sm text-muted-foreground">Caricamento…</p>
  }
  if (query.isError) {
    return (
      <p className="text-sm text-destructive">
        {(query.error as Error).message || "Errore nel caricamento delle tariffe."}
      </p>
    )
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {shown.map((type) => (
          <TariffTypeBlock
            key={type.key}
            typeKey={type.key}
            label={type.label}
            hint={type.hint}
            options={byType.get(type.key) ?? []}
            canEdit={canEdit}
            onChanged={invalidate}
          />
        ))}
      </div>
      <p className="text-xs text-muted-foreground">
        Correggere un importo cambia solo quello che verrà <em>proposto</em> d'ora in
        avanti: le righe già compilate conservano il valore con cui sono state scritte.
        Un valore già usato non si può eliminare.
      </p>
    </div>
  )
}

function TariffTypeBlock({
  typeKey,
  label,
  hint,
  options,
  canEdit,
  onChanged,
}: {
  typeKey: string
  label: string
  hint: string
  options: TariffOptionDto[]
  canEdit: boolean
  onChanged: () => void
}) {
  const confirm = useConfirm()
  const [draft, setDraft] = React.useState("")
  const [draftName, setDraftName] = React.useState("")

  const addMutation = useMutation({
    mutationFn: (value: number) =>
      addTariffOption({ tariffType: typeKey, label: draftName.trim(), value }),
    onSuccess: () => {
      setDraft("")
      setDraftName("")
      onChanged()
    },
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteTariffOption(id),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  function handleAdd() {
    const value = parseDecimal(draft)
    if (draft.trim() === "" || value <= 0) {
      notifyError(new Error("Inserisci un importo maggiore di zero."))
      return
    }
    addMutation.mutate(value)
  }

  async function handleDelete(option: TariffOptionDto) {
    const ok = await confirm({
      title: "Elimina tariffa",
      description: `Rimuovere ${option.label ? `«${option.label}» ` : ""}${euro(
        option.value
      )} dall'elenco «${label}»?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteMutation.mutate(option.id)
  }

  return (
    <div className="space-y-2 rounded-lg border p-3">
      <div>
        <p className="text-sm font-medium">{label}</p>
        <p className="text-xs text-muted-foreground">{hint}</p>
      </div>

      {options.length === 0 ? (
        <p className="text-xs text-muted-foreground">Nessun valore in elenco.</p>
      ) : (
        <ul className="space-y-1">
          {options.map((option) => (
            <li key={option.id} className="flex items-center gap-1">
              <TariffValueField
                option={option}
                canEdit={canEdit}
                onChanged={onChanged}
              />
              {canEdit && (
                <Tooltip>
                  <TooltipTrigger asChild>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label="Elimina tariffa"
                      disabled={deleteMutation.isPending}
                      onClick={() => void handleDelete(option)}
                    >
                      <Trash2 className="text-destructive" />
                    </Button>
                  </TooltipTrigger>
                  <TooltipContent>Elimina</TooltipContent>
                </Tooltip>
              )}
            </li>
          ))}
        </ul>
      )}

      {canEdit && (
        <div className="flex items-center gap-1">
          <Input
            value={draftName}
            placeholder="Nome"
            disabled={addMutation.isPending}
            className="h-8 w-[42%] bg-background"
            onChange={(event) => setDraftName(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") handleAdd()
            }}
          />
          <MoneyInput
            value={draft}
            placeholder="Nuovo valore"
            disabled={addMutation.isPending}
            className="h-8 bg-background"
            onChange={setDraft}
            onKeyDown={(event) => {
              if (event.key === "Enter") handleAdd()
            }}
          />
          <Button
            variant="outline"
            size="icon-sm"
            aria-label="Aggiungi tariffa"
            disabled={addMutation.isPending}
            onClick={handleAdd}
          >
            <Plus />
          </Button>
        </div>
      )}
    </div>
  )
}

/**
 * Nome e importo modificabili in riga: salvano all'uscita dal campo, solo se sono davvero
 * cambiati. Il nome (#87) serve a distinguere tariffe orarie di lavorazioni diverse —
 * meccanica, carpenteria, stampa 3D — che dal solo importo non si riconoscono.
 */
function TariffValueField({
  option,
  canEdit,
  onChanged,
}: {
  option: TariffOptionDto
  canEdit: boolean
  onChanged: () => void
}) {
  const [text, setText] = React.useState(String(option.value))
  const [name, setName] = React.useState(option.label ?? "")
  const boxRef = React.useRef<HTMLSpanElement>(null)

  // Il server è la verità, ma non mentre il fuoco è dentro il campo: è la stessa guardia
  // che nel blocco 4 ha smesso di cancellare sotto le dita quello che si stava scrivendo.
  React.useEffect(() => {
    if (boxRef.current?.contains(document.activeElement)) return
    setText(String(option.value))
    setName(option.label ?? "")
  }, [option.value, option.label])

  const saveMutation = useMutation({
    mutationFn: ({ value, label }: { value: number; label: string }) =>
      updateTariffOption(option.id, {
        tariffType: option.tariffType,
        label,
        value,
      }),
    onSuccess: () => {
      notifySuccess("Tariffa aggiornata")
      onChanged()
    },
    onError: (err: Error) => {
      setText(String(option.value))
      setName(option.label ?? "")
      notifyError(err)
    },
  })

  if (!canEdit) {
    return (
      <span className="flex flex-1 items-center gap-2">
        {option.label ? (
          <span className="min-w-0 flex-1 truncate text-sm">{option.label}</span>
        ) : null}
        <span className="text-sm tabular-nums">{euro(option.value)}</span>
      </span>
    )
  }

  return (
    <span ref={boxRef} className="flex flex-1 items-center gap-1">
      <Input
        value={name}
        placeholder="Nome"
        disabled={saveMutation.isPending}
        className="h-8 w-[42%] bg-background"
        onChange={(event) => setName(event.target.value)}
        onBlur={() => {
          if (name.trim() === (option.label ?? "")) return
          saveMutation.mutate({ value: option.value, label: name.trim() })
        }}
      />
      <MoneyInput
        value={text}
        disabled={saveMutation.isPending}
        className="h-8 w-full bg-background"
        onChange={setText}
        onCommit={() => {
          const value = parseDecimal(text)
          if (value === option.value) return
          if (text.trim() === "" || value <= 0) {
            setText(String(option.value))
            return
          }
          saveMutation.mutate({ value, label: name.trim() })
        }}
      />
    </span>
  )
}
