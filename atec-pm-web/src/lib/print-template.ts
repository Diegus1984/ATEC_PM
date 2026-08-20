import { notifyError } from "@/lib/toast"

/** Testo → HTML sicuro per i template di stampa/export (unico punto di escape). */
export function escapeHtml(value: string | null | undefined): string {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
}

export interface PrintOptions {
  /** Titolo in testo semplice: viene sempre "escapato". */
  title: string
  /** Variante HTML del titolo, inserita SENZA escape (es. il codice commessa in
   *  `<span class="code">`, che il foglio di stile della testata sa già colorare).
   *  Usare solo con markup costruito qui dentro, mai con testo che arriva dal DB. */
  titleHtml?: string
  subtitle?: string
  meta?: { label: string; value: string | number }[]
  contentHtml: string
  orientation?: "portrait" | "landscape"
  paperSize?: "A4" | "A3"
  customStyles?: string
}

/**
 * Margini content-box (mm). Non vanno su `@page`: con `margin: 0` Chromium/Edge
 * nascondono data, titolo e URL del browser; i mm utili si ripetono a ogni pagina
 * tramite thead/tfoot della cornice `.print-page-frame`.
 */
const PRINT_MARGIN = { top: 12, right: 14, bottom: 14, left: 14 } as const

export function printHtml({
  title,
  titleHtml,
  subtitle,
  meta = [],
  contentHtml,
  orientation = "landscape",
  paperSize = "A4",
  customStyles = "",
}: PrintOptions): boolean {
  const logoUri = `${window.location.origin}/atec-logo.png`
  const dateStr = new Date().toLocaleString("it-IT", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  })

  const metaHtml =
    meta.length > 0
      ? `<div class="print-meta">${meta
          .map(
            (m) =>
              `<div><b>${escapeHtml(m.label)}</b>${escapeHtml(String(m.value))}</div>`
          )
          .join("")}</div>`
      : ""

  // `moznomarginboxes`: Firefox non stampa le proprie diciture di margine.
  // Chromium/Edge: `@page { margin: 0 }` toglie lo spazio alle intestazioni/piè
  // nativi (data, blob URL, n. pagina) — unica via lato pagina, non c’è API JS.
  const html = `<!DOCTYPE html>
<html lang="it" moznomarginboxes>
<head>
  <meta charset="UTF-8">
  <title></title>
  <style>
    @page { size: ${paperSize.toLowerCase()} ${orientation}; margin: 0 }
    * { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact }
    html { -webkit-print-color-adjust: exact; print-color-adjust: exact }
    body {
      font-family: 'Hanken Grotesk', Segoe UI, Arial, sans-serif;
      color: #27384A;
      margin: 0;
      padding: 0;
      font-size: 11px;
    }

    /* Cornice: ripete i margini su ogni pagina (il padding del body vale solo in pagina 1). */
    table.print-page-frame {
      width: 100%;
      border-collapse: collapse;
      table-layout: fixed;
    }
    table.print-page-frame > thead { display: table-header-group }
    table.print-page-frame > tfoot { display: table-footer-group }
    table.print-page-frame > tbody { display: table-row-group }
    table.print-page-frame > thead > tr > td,
    table.print-page-frame > tfoot > tr > td {
      border: none;
      padding: 0;
      font-size: 0;
      line-height: 0;
    }
    .print-margin-top { height: ${PRINT_MARGIN.top}mm }
    .print-margin-bottom { height: ${PRINT_MARGIN.bottom}mm }
    table.print-page-frame > tbody > tr > td.print-page-body {
      border: none;
      padding: 0 ${PRINT_MARGIN.right}mm 0 ${PRINT_MARGIN.left}mm;
      vertical-align: top;
    }

    /* Intestazione standard ATEC con Logo Automation */
    .print-head { border-bottom: 2px solid #2F6098; padding-bottom: 10px; margin-bottom: 14px }
    .print-logo { height: 34px; width: auto; display: block; margin-bottom: 8px }
    .print-title { margin: 2px 0 4px; font-size: 22px; color: #243340; font-weight: 800; letter-spacing: -.01em; line-height: 1.2 }
    .print-title .code { font-family: 'JetBrains Mono', monospace; font-weight: 700; color: #2F6098 }
    .print-subtitle { font-size: 11px; text-transform: uppercase; letter-spacing: .08em; color: #788896; font-weight: 700 }
    .print-sub { font-size: 12px; color: #5A6B7A; margin-top: 4px }

    /* Metadati */
    .print-meta { display: flex; gap: 26px; flex-wrap: wrap; margin: 10px 0 16px; font-size: 11px }
    .print-meta b { display: block; font-size: 9px; text-transform: uppercase; letter-spacing: .05em; color: #788896; font-weight: 700; margin-bottom: 2px }

    /* Piè di pagina del documento (ATEC), non quello del browser */
    .print-foot { margin-top: 14px; font-size: 9px; color: #9AA7B4; border-top: 1px solid #E4ECF5; padding-top: 8px }

    ${customStyles}

    /* Dopo i customStyles (spesso selettori nudi table/td/tr): la cornice non deve
       ereditare bordi, padding o page-break-inside:avoid che spezzerebbero il flusso. */
    table.print-page-frame {
      width: 100% !important;
      border: none !important;
      border-collapse: collapse !important;
      table-layout: fixed !important;
      background: transparent !important;
    }
    table.print-page-frame > thead { display: table-header-group !important }
    table.print-page-frame > tfoot { display: table-footer-group !important }
    table.print-page-frame > tbody { display: table-row-group !important }
    table.print-page-frame > tbody > tr { page-break-inside: auto !important }
    table.print-page-frame > thead > tr > td,
    table.print-page-frame > tfoot > tr > td {
      border: none !important;
      background: transparent !important;
      padding: 0 !important;
      font-size: 0 !important;
      line-height: 0 !important;
    }
    table.print-page-frame > tbody > tr > td.print-page-body {
      border: none !important;
      background: transparent !important;
      padding: 0 ${PRINT_MARGIN.right}mm 0 ${PRINT_MARGIN.left}mm !important;
      vertical-align: top !important;
    }
    .print-margin-top { height: ${PRINT_MARGIN.top}mm !important }
    .print-margin-bottom { height: ${PRINT_MARGIN.bottom}mm !important }
  </style>
</head>
<body>
  <table class="print-page-frame">
    <thead>
      <tr><td><div class="print-margin-top"></div></td></tr>
    </thead>
    <tfoot>
      <tr><td><div class="print-margin-bottom"></div></td></tr>
    </tfoot>
    <tbody>
      <tr>
        <td class="print-page-body">
          <div class="print-head">
            <img class="print-logo" src="${logoUri}" alt="Automation Technology" onerror="this.style.display='none'">
            <div class="print-title">${titleHtml ?? escapeHtml(title)}</div>
            ${subtitle ? `<div class="print-subtitle">${escapeHtml(subtitle)}</div>` : ""}
          </div>
          ${metaHtml}
          <div class="print-content">
            ${contentHtml}
          </div>
          <div class="print-foot">
            Documento generato il ${dateStr} · Automation Technology S.r.l.
          </div>
        </td>
      </tr>
    </tbody>
  </table>
  <script>
    window.onload = function() {
      setTimeout(function() {
        window.print();
      }, 300);
    }
  </script>
</body>
</html>`

  // Blob URL: evita about:blank che in Chrome riporta l'URL della pagina app
  // se l'utente riattiva a mano «Intestazioni e piè di pagina» con margini custom.
  const blob = new Blob([html], { type: "text/html;charset=utf-8" })
  const url = URL.createObjectURL(blob)
  const win = window.open(url, "_blank")
  if (!win) {
    // Popup bloccati: prima si usciva in silenzio, quindi si cliccava «Stampa»
    // e non succedeva niente. Ora il motivo si vede.
    URL.revokeObjectURL(url)
    notifyError(
      null,
      "Stampa bloccata dal browser: consenti i popup per questo sito e riprova."
    )
    return false
  }
  const revoke = () => URL.revokeObjectURL(url)
  win.addEventListener("afterprint", revoke)
  // Fallback se afterprint non arriva (chiusura anticipata della finestra).
  setTimeout(revoke, 60_000)
  return true
}
