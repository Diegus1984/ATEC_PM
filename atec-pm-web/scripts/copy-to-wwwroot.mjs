import fs from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const distDir = path.resolve(__dirname, "../dist")
const targetDir = path.resolve(__dirname, "../../ATEC.PM.Server/wwwroot")

if (!fs.existsSync(distDir)) {
  console.error("Build mancante: esegui prima `npm run build` in atec-pm-web/")
  process.exit(1)
}

fs.rmSync(targetDir, { recursive: true, force: true })
fs.cpSync(distDir, targetDir, { recursive: true })

console.log(`Copiato ${distDir} -> ${targetDir}`)
