import path from "node:path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig, loadEnv, type Plugin } from "vite"

/**
 * Identificativo della build (data/ora locale, es. `20260806-1732`): cambia a ogni
 * `npm run build`. Finisce in DUE posti — un `<meta name="app-build">` dentro
 * `index.html` e `dist/version.json` accanto agli asset: il client confronta i due
 * valori e, se differiscono, sa che sul server è stata caricata una versione più
 * nuova di quella che ha in memoria.
 *
 * NON va messo dentro il bundle JS (prima ci finiva, come `__APP_BUILD__`): cambiando
 * il contenuto cambia l'hash nel nome del file, e ogni build sfornava 2,5 MB di
 * bundle "nuovo" anche senza toccare una riga di codice. L'aggiornamento del server
 * spedisce solo i file cambiati, quindi quei 2,5 MB partivano a ogni deploy per
 * niente. In `index.html` (0,5 kB, comunque riscritto a ogni build) non costa nulla.
 */
function makeBuildId(): string {
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, "0")
  return (
    `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}` +
    `-${pad(now.getHours())}${pad(now.getMinutes())}`
  )
}

/**
 * Scrive l'id di build nei due posti che il client confronta:
 * - `<meta name="app-build">` in `index.html` = la build che il browser sta eseguendo;
 * - `version.json` accanto agli asset = la build che c'è ORA sul server.
 */
function appVersionPlugin(buildId: string): Plugin {
  return {
    name: "atec-app-version",
    apply: "build",
    generateBundle() {
      this.emitFile({
        type: "asset",
        fileName: "version.json",
        source: JSON.stringify({ build: buildId }),
      })
    },
    transformIndexHtml() {
      return [
        {
          tag: "meta",
          attrs: { name: "app-build", content: buildId },
          injectTo: "head",
        },
      ]
    },
  }
}

export default defineConfig(({ mode, command }) => {
  const env = loadEnv(mode, process.cwd(), "")
  const apiTarget = env.VITE_DEV_API_PROXY ?? "http://localhost:5150"
  const buildId = command === "build" ? makeBuildId() : "dev"

  return {
    plugins: [react(), tailwindcss(), appVersionPlugin(buildId)],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./src"),
      },
    },
    server: {
      port: 5173,
      proxy: {
        "/api": {
          target: apiTarget,
          changeOrigin: true,
        },
        "/hubs": {
          target: apiTarget,
          changeOrigin: true,
          ws: true,
        },
        "/uploads": {
          target: apiTarget,
          changeOrigin: true,
        },
      },
    },
    build: {
      outDir: "dist",
      emptyOutDir: true,
    },
  }
})
