/** Testo → HTML sicuro per i template di stampa/export (unico punto di escape). */
export function escapeHtml(value: string | null | undefined): string {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
}

export interface PrintOptions {
  title: string
  subtitle?: string
  meta?: { label: string; value: string | number }[]
  contentHtml: string
  orientation?: "portrait" | "landscape"
  paperSize?: "A4" | "A3"
  customStyles?: string
}

export function printHtml({
  title,
  subtitle,
  meta = [],
  contentHtml,
  orientation = "landscape",
  paperSize = "A4",
  customStyles = "",
}: PrintOptions): void {
  const logoUri = `${window.location.origin}/atec-logo.png`
  const dateStr = new Date().toLocaleString("it-IT", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  })

  const metaHtml = meta.length > 0
    ? `<div class="print-meta">${meta
        .map(
          (m) =>
            `<div><b>${escapeHtml(m.label)}</b>${escapeHtml(String(m.value))}</div>`
        )
        .join("")}</div>`
    : ""

  // margin: 0 su @page: in Chromium le intestazioni/piè di pagina del browser
  // (titolo + URL) occupano i margini; senza margine non vengono disegnate.
  // Il padding del body ripristina lo spazio utile del foglio.
  const html = `<!DOCTYPE html>
<html lang="it">
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
      padding: 14mm;
      font-size: 11px;
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

    /* Piè di pagina */
    .print-foot { margin-top: 14px; font-size: 9px; color: #9AA7B4; border-top: 1px solid #E4ECF5; padding-top: 8px }

    ${customStyles}
  </style>
</head>
<body>
  <div class="print-head">
    <img class="print-logo" src="${logoUri}" alt="Automation Technology" onerror="this.style.display='none'">
    <div class="print-title">${escapeHtml(title)}</div>
    ${subtitle ? `<div class="print-subtitle">${escapeHtml(subtitle)}</div>` : ""}
  </div>
  ${metaHtml}
  <div class="print-content">
    ${contentHtml}
  </div>
  <div class="print-foot">
    Documento generato il ${dateStr} · Automation Technology S.r.l.
  </div>
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
  // nelle intestazioni di stampa (se i margini non bastano a nasconderle).
  const blob = new Blob([html], { type: "text/html;charset=utf-8" })
  const url = URL.createObjectURL(blob)
  const win = window.open(url, "_blank")
  if (!win) {
    URL.revokeObjectURL(url)
    return
  }
  const revoke = () => URL.revokeObjectURL(url)
  win.addEventListener("afterprint", revoke)
  // Fallback se afterprint non arriva (chiusura anticipata della finestra).
  setTimeout(revoke, 60_000)
}
