// Versione del client web: serve a capire, mentre l'app è aperta, che sul server è
// stata caricata una build più nuova (deploy con `aggiorna-server.bat`).
//
// Come funziona: `npm run build` conia un id (data/ora) e lo mette in DUE posti —
// il `<meta name="app-build">` di `index.html` (arriva insieme alla pagina, quindi è
// la build che il browser sta davvero eseguendo) e `version.json` accanto agli asset
// (sempre riletto dal server, mai dalla cache). Se i due valori divergono, la scheda
// aperta sta girando su codice vecchio.
//
// L'id NON è compilato dentro il bundle: lo cambierebbe a ogni build, e con lui
// l'hash nel nome del file, facendo ripartire 2,5 MB a ogni deploy per niente.

/** Id della build che il browser sta effettivamente eseguendo. */
export const APP_BUILD: string = readBuildId()

function readBuildId(): string {
  // Il tag lo inietta vite solo in `npm run build`: in sviluppo non c'è e vale "dev",
  // esattamente come prima (il banner resta zitto perché anche version.json manca).
  const meta = document.querySelector('meta[name="app-build"]')
  const valore = meta?.getAttribute("content")
  return valore && valore.length > 0 ? valore : "dev"
}

/**
 * Legge l'id di build presente ORA sul server.
 * Torna `null` se non è leggibile (server in riavvio durante l'aggiornamento, rete
 * giù, dev server senza `version.json`): in quel caso non si conclude nulla e si
 * riprova al giro successivo.
 */
export async function fetchServerBuild(): Promise<string | null> {
  try {
    const res = await fetch(
      `${import.meta.env.BASE_URL}version.json?t=${Date.now()}`,
      { cache: "no-store" }
    )
    if (!res.ok) {
      return null
    }
    const data: unknown = await res.json()
    const build = (data as { build?: unknown } | null)?.build
    return typeof build === "string" && build.length > 0 ? build : null
  } catch {
    return null
  }
}

/**
 * Id di build del SERVER (data di installazione dei binari, «20260828-1126»).
 *
 * Non e' lo stesso di `fetchServerBuild()`, che legge `version.json` e riguarda il CLIENT:
 * un aggiornamento di solo C# lascia il client invariato, e senza questo dato
 * dall'applicazione non si vedrebbe che il server e' cambiato.
 *
 * Torna `null` se il server non risponde o non lo dichiara (versione piu' vecchia): in quel
 * caso la riga in basso mostra solo la parte «Web», come prima.
 */
export async function fetchServerVersion(): Promise<string | null> {
  try {
    const res = await fetch("/api/health", { cache: "no-store" })
    if (!res.ok) return null
    const data: unknown = await res.json()
    const build = (data as { build?: unknown } | null)?.build
    return typeof build === "string" && build.length > 0 ? build : null
  } catch {
    return null
  }
}
