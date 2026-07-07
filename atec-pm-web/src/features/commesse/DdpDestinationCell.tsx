import * as React from "react"

import { Input } from "@/components/ui/input"
import type { DdpDestinationItem } from "@/lib/api/types"

import { DdpDestinationCombo } from "./DdpDestinationCombo"

/**
 * Cella «Destinazione»: testo + menu «⋮» — stesso stile della cella Stato (etichetta
 * sullo sfondo colorato della riga + trigger che apre il menu). La specifica libera vive
 * in una colonna separata, affiancata (DdpDestinationSpecCell).
 */
export function DdpDestinationCell({
  destination,
  destinations,
  disabled,
  onDestinationChange,
}: {
  destination: string
  destinations: DdpDestinationItem[]
  disabled?: boolean
  onDestinationChange: (destination: string) => void
}) {
  return (
    <div
      className="flex min-w-[150px] items-center gap-1"
      onClick={(event) => event.stopPropagation()}
    >
      <span className="min-w-0 flex-1 truncate font-semibold whitespace-nowrap">
        {destination || "—"}
      </span>
      <DdpDestinationCombo
        destination={destination}
        destinations={destinations}
        disabled={disabled}
        onSelect={onDestinationChange}
      />
    </div>
  )
}

/**
 * Cella «Specifica destinazione»: input libero (es. R1, QE1), commit su blur/Invio.
 * Disabilitata finché la riga non ha una descrizione destinazione. Lo sfondo è
 * trasparente (prende il colore della riga come le altre celle) e diventa bianco solo
 * mentre si scrive (focus), poi torna del colore della riga.
 */
export function DdpDestinationSpecCell({
  destination,
  destinationSpec,
  disabled,
  onSpecCommit,
}: {
  destination: string
  destinationSpec: string
  disabled?: boolean
  onSpecCommit: (destinationSpec: string) => void
}) {
  const [draftSpec, setDraftSpec] = React.useState(destinationSpec)

  React.useEffect(() => {
    setDraftSpec(destinationSpec)
  }, [destinationSpec])

  const hasDestination = Boolean(destination.trim())

  if (!hasDestination) {
    return <span className="text-muted-foreground">—</span>
  }

  return (
    <Input
      value={draftSpec}
      disabled={disabled}
      placeholder="Es. R1, QE1…"
      className="h-8 min-w-[120px] border-transparent bg-transparent text-xs shadow-none placeholder:text-current placeholder:opacity-60 focus:border-input focus:bg-white focus:text-foreground focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
      onClick={(event) => event.stopPropagation()}
      onChange={(event) => setDraftSpec(event.target.value)}
      onBlur={() => {
        if (draftSpec !== destinationSpec) {
          onSpecCommit(draftSpec)
        }
      }}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.currentTarget.blur()
        }
      }}
    />
  )
}
