/**
 * Regola del modulo «Cambia la password SMTP» (Diego, 09/09/2026 sera: «un pulsante che apra
 * un form per cambiare password con doppia conferma»). Pura, senza React: sta qui per il test.
 * Gli spazi in testa o in coda si bloccano apposta: il 09/09 Aruba rifiutava la password
 * salvata (535) e una password incollata con uno spazio di troppo è il primo sospetto.
 */
export function motivoPasswordNonValida(nuova: string, ripeti: string): string | null {
  if (nuova.length === 0) return "Scrivi la nuova password."
  if (nuova !== nuova.trim()) return "La password inizia o finisce con uno spazio: controlla."
  if (nuova !== ripeti) return "Le due password non coincidono."
  return null
}
