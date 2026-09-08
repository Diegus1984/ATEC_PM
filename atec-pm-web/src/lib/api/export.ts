// Export Excel VERO (.xlsx) di una tabella: il client dice cosa (colonne tipizzate + righe
// che l'utente vede), il server compone il foglio con EPPlus (`POST /api/export/xlsx`):
// numeri come numeri, date come date, percentuali col formato percentuale, filtro su ogni
// colonna, intestazione bloccata.
//
// Nato con la #150: il CSV del Prospetto SAL scriveva `27122.172` e Excel italiano lo leggeva
// come ventisette milioni (il punto è il separatore delle migliaia). REGOLA: un export
// tabellare con numeri dentro passa di qui, non da un CSV né da una tabella HTML
// rinominata `.xls`.

import { apiPostBlob } from "@/lib/api/client"
import { downloadFile } from "@/lib/download"

/**
 * Tipo di colonna: decide formato di cella e allineamento nel foglio.
 * - `percento`: il valore è in punti percentuali (18,37 = 18,37%), lo converte il server;
 * - `euro`: arrotondato a 2 decimali nel valore (è quello che va in fattura);
 * - `data`: stringa ISO `aaaa-mm-gg` (o `aaaa-mm-ggThh:mm:ss`), come arriva dal server.
 */
export type ExcelColumnType = "testo" | "intero" | "numero" | "euro" | "percento" | "data"

export interface ExcelColumn {
  etichetta: string
  tipo: ExcelColumnType
  /** Decimali mostrati per `numero` e `percento` (default 2). */
  decimali?: number
}

/** Valori così come stanno nei DTO: la cella vuota è `null`. */
export type ExcelCell = string | number | boolean | null

export interface ExcelTable {
  /** Nome del foglio (il server toglie i caratteri vietati e tronca a 31). */
  foglio?: string
  colonne: ExcelColumn[]
  /** Una riga = una cella per colonna, nello stesso ordine di `colonne`. */
  righe: ExcelCell[][]
}

export const XLSX_MIME =
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"

/** Chiede il foglio al server e lo scarica col nome dato (`.xlsx`). */
export async function downloadXlsx(fileName: string, table: ExcelTable): Promise<void> {
  const blob = await apiPostBlob("/api/export/xlsx", { ...table, nomeFile: fileName })
  downloadFile(fileName, blob, XLSX_MIME)
}
