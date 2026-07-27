import { DateField } from "@/components/shared/date-field"

/**
 * Date picker inline di cella: il DateField standard dell'app (icona calendario,
 * giorno della settimana impilato sopra la data, «X» per rimuovere) in versione
 * trasparente che eredita sfondo e colore della riga tinta, come gli altri campi
 * inline (pattern DdpInlineTextCell); bianco solo al passaggio del mouse.
 * Usato per «Data Prev.».
 */
export function DdpInlineDateCell({
  value,
  disabled,
  onChange,
}: {
  value: string | null
  disabled?: boolean
  /** `null` = data rimossa. */
  onChange: (value: string | null) => void
}) {
  return (
    <span
      className="block min-w-[8.5rem]"
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
    >
      <DateField
        value={value}
        onChange={onChange}
        disabled={disabled}
        size="sm"
        placeholder="—"
        stackedWeekday
        // text-current + [&_svg]:text-current: testo e icona calendario ereditano
        // il colore della riga tinta (l'icona di suo è text-foreground cablato).
        className="w-full min-w-0 border-transparent bg-transparent text-sm text-current shadow-none hover:bg-white hover:text-foreground dark:hover:bg-zinc-950 [&_svg]:text-current"
      />
    </span>
  )
}
