import path from "node:path"
import { defineConfig } from "vitest/config"

// Test della LOGICA PURA del client (formattazioni, date, calcoli, regole): girano in Node,
// senza browser né server, in pochi secondi. La UI resta fuori: si prova a runtime.
// `npm test` = `vitest run`; li lancia anche il deploy prima di `npm run build`.
export default defineConfig({
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
    // Stesso fuso e stessa lingua del server e degli utenti: le date «gg/mm/aa» e i
    // numeri «1.234,50» vengono da toLocaleString it-IT.
    env: { TZ: "Europe/Rome" },
  },
})
