import { formatDateShort } from "@/lib/date-iso"
import { cn } from "@/lib/utils"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

/**
 * Il numero di protocollo della mutua da scrivere, con la scorciatoia per i giorni successivi
 * dello stesso certificato: se la persona ne ha già uno nei giorni prima, quello è la prima
 * scelta e bastano due clic (Diego, 10/09/2026). «Nuovo protocollo» apre il campo vuoto.
 *
 * <p>Il numero resta facoltativo: la malattia si segna anche senza, e il certificato arriva
 * quando arriva.</p>
 */
export function ProtocolloScelta({
  ultimo,
  ultimoGiorno,
  valore,
  onValore,
}: {
  /** L'ultimo protocollo della persona nei giorni prima; «» se non ce n'è. */
  ultimo: string
  /** Il giorno a cui apparteneva, per dire da dove viene. */
  ultimoGiorno: string | null
  valore: string
  onValore: (v: string) => void
}) {
  const stessoDiPrima = ultimo !== "" && valore === ultimo

  if (ultimo === "") {
    return (
      <div className="space-y-1">
        <Label htmlFor="protocollo">Protocollo della mutua</Label>
        <Input
          id="protocollo"
          value={valore}
          onChange={(e) => onValore(e.target.value)}
          placeholder="es. 453206692"
          className="font-mono tabular-nums"
          maxLength={40}
        />
        <p className="text-xs text-muted-foreground">
          Si può lasciare vuoto e scriverlo quando arriva il certificato.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-2">
      <Label>Protocollo della mutua</Label>

      <button
        type="button"
        onClick={() => onValore(ultimo)}
        aria-pressed={stessoDiPrima}
        className={cn(
          "flex w-full items-start gap-2 rounded-md border p-2 text-left text-sm transition-colors",
          stessoDiPrima ? "border-primary bg-accent" : "hover:bg-accent/60"
        )}
      >
        <span
          className={cn(
            "mt-0.5 size-3.5 flex-none rounded-full border",
            stessoDiPrima ? "border-[5px] border-primary" : "border-input"
          )}
        />
        <span>
          <span className="font-mono font-medium tabular-nums">{ultimo}</span>
          <span className="block text-xs text-muted-foreground">
            {ultimoGiorno
              ? `lo stesso del ${formatDateShort(ultimoGiorno.slice(0, 10))}`
              : "l'ultimo inserito"}
          </span>
        </span>
      </button>

      <button
        type="button"
        onClick={() => onValore(valore === ultimo ? "" : valore)}
        aria-pressed={!stessoDiPrima}
        className={cn(
          "flex w-full items-start gap-2 rounded-md border p-2 text-left text-sm transition-colors",
          !stessoDiPrima ? "border-primary bg-accent" : "hover:bg-accent/60"
        )}
      >
        <span
          className={cn(
            "mt-0.5 size-3.5 flex-none rounded-full border",
            !stessoDiPrima ? "border-[5px] border-primary" : "border-input"
          )}
        />
        <span>
          Nuovo protocollo
          <span className="block text-xs text-muted-foreground">
            un certificato diverso, o nessun numero
          </span>
        </span>
      </button>

      {!stessoDiPrima && (
        <Input
          value={valore}
          onChange={(e) => onValore(e.target.value)}
          placeholder="es. 453206692"
          className="font-mono tabular-nums"
          maxLength={40}
          autoFocus
        />
      )}
    </div>
  )
}
