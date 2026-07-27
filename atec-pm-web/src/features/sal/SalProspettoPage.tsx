import { SalProspettoView } from "./SalProspettoView"

/**
 * Pagina PM dedicata al Prospetto SAL: tutte le ipotesi di fatturazione aperte e
 * le fatture emesse non incassate di tutte le commesse attive, come voce di
 * navigazione autonoma (oltre alla vista rapida omonima dentro `/sal`). Riusa
 * `SalProspettoView` (ordinamento, colori/badge, controllo periodico, export
 * CSV/Stampa, auto-refresh).
 */
export function SalProspettoPage() {
  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight">Prospetto SAL</h1>
        <p className="text-sm text-muted-foreground">
          Tutte le ipotesi di fatturazione aperte e le fatture emesse non
          incassate, con semaforo scadenze, controllo periodico, ordinamento ed
          export.
        </p>
      </div>
      <SalProspettoView />
    </div>
  )
}
