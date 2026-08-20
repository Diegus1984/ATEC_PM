// Registra il tutorial del Bilancio commessa sulla commessa dimostrativa.
// Stessa impalcatura del primo tutorial: ogni passo dura quanto il suo audio.
const fs = require("fs")
const path = require("path")
const { chromium } = require("playwright-core")
const { OVERLAY } = require("./overlay")

const BASE = __dirname
const CHROME = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"
const ORIGIN = process.env.TUT_ORIGIN || "http://localhost:5173"
const PROJ = process.env.TUT_PROJECT || "29"
const URL = `${ORIGIN}/commesse/${PROJ}/budget_vs_actual`
// Token JWT: variabile TUT_TOKEN, oppure un file token.txt in questa cartella.
const TOKEN = (process.env.TUT_TOKEN || fs.readFileSync(path.join(BASE, "token.txt"), "utf8")).trim()
const W = 1600
const H = 900

const copione = JSON.parse(fs.readFileSync(path.join(BASE, "copione-bilancio.json"), "utf8"))
const durate = JSON.parse(fs.readFileSync(path.join(BASE, "durate-copione-bilancio.json"), "utf8"))
const secondi = Object.fromEntries(durate.map((d) => [d.id, d.seconds]))
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function main() {
  const browser = await chromium.launch({ executablePath: CHROME, headless: true })
  const context = await browser.newContext({
    viewport: { width: W, height: H },
    recordVideo: { dir: path.join(BASE, "video-bilancio"), size: { width: W, height: H } },
    storageState: {
      cookies: [],
      origins: [{
        origin: ORIGIN,
        localStorage: [
          { name: "atec_pm_token", value: TOKEN },
          { name: "atec_pm_user", value: JSON.stringify({ employeeId: 1, fullName: "Admin", userRole: "ADMIN", mustChangePassword: false }) },
        ],
      }],
    },
  })

  // Timeout corto: un selettore che non trova nulla deve fallire subito. Con i 30
  // secondi di default un solo selettore sbagliato allunga il suo passo di mezzo
  // minuto e da lì in poi la voce resta indietro rispetto al video.
  context.setDefaultTimeout(3000)

  const page = await context.newPage()
  const tPage = Date.now()
  await page.goto(URL, { waitUntil: "networkidle" })
  await page.waitForSelector("text=Impegno Risorse", { timeout: 30000 }).catch(() => {})
  await sleep(1800)
  await page.evaluate(OVERLAY)
  await sleep(300)

  const marks = []
  const t0 = Date.now()

  async function moveTo(x, y, steps = 22) {
    const p = await page.evaluate(() => ({ x: window.__lastX || 800, y: window.__lastY || 450 }))
    for (let i = 1; i <= steps; i++) {
      const nx = p.x + ((x - p.x) * i) / steps
      const ny = p.y + ((y - p.y) * i) / steps
      await page.evaluate(([a, b]) => { window.__cursor(a, b); window.__lastX = a; window.__lastY = b }, [nx, ny])
      await page.mouse.move(nx, ny)
      await sleep(16)
    }
  }
  async function moveToEl(loc) {
    const b = await loc.boundingBox()
    if (!b) return null
    await moveTo(b.x + b.width / 2, b.y + Math.min(b.height / 2, 18))
    return b
  }
  async function clickEl(loc) {
    const b = await moveToEl(loc)
    if (!b) return
    await page.evaluate(([a, c]) => window.__cursor(a, c, true), [b.x + b.width / 2, b.y + Math.min(b.height / 2, 18)])
    await sleep(220)
    await loc.click({ position: { x: Math.min(b.width / 2, 40), y: Math.min(b.height / 2, 18) } }).catch(() => {})
    await sleep(300)
  }
  async function highlight(locs) {
    const boxes = []
    for (const l of [].concat(locs)) {
      const b = await l.boundingBox().catch(() => null)
      if (b) boxes.push(b)
    }
    await page.evaluate((b) => window.__hl(b), boxes)
  }
  async function clearHl() { await page.evaluate(() => window.__hlOff()) }
  async function show(locs, andMove = true) {
    const list = [].concat(locs)
    await list[0].scrollIntoViewIfNeeded().catch(() => {})
    await sleep(650)
    await highlight(list)
    if (andMove) await moveToEl(list[0])
  }
  const sfori = []
  async function step(id, fn) {
    const s = copione.find((c) => c.id === id)
    const budget = (secondi[id] || 5) * 1000
    const start = Date.now()
    marks.push({ id, atMs: start - t0 })
    await page.evaluate((t) => window.__sub(t), s.sub)
    if (fn) await fn()
    const left = budget - (Date.now() - start)
    if (left > 0) await sleep(left)
    else sfori.push(`${id}: +${Math.round(-left / 1000)}s oltre il parlato`)
    await clearHl().catch(() => {})
  }

  // fascia blu con un dato titolo
  const fascia = (titolo) =>
    page.locator("div").filter({ hasText: new RegExp("^" + titolo + "$") }).locator("xpath=ancestor::div[contains(@class,'rounded-lg')][1]").first()
  const titoloFascia = (t) => page.getByText(t, { exact: true }).first()
  /**
   * Rettangolo del riquadro KPI che contiene una certa etichetta. Fatto dentro la
   * pagina con closest(): risalire con xpath agli antenati data-slot non trovava
   * la Card, e l'attesa del locator andava in timeout.
   */
  async function kpiBox(label, scroll = true) {
    if (scroll) {
      await page.evaluate((lbl) => {
        const el = [...document.querySelectorAll("*")].find(
          (n) => n.children.length === 0 && n.textContent.trim() === lbl
        )
        const card = el && (el.closest('[data-slot="card"]') || el.parentElement)
        if (card) card.scrollIntoView({ block: "center", behavior: "smooth" })
      }, label)
      await sleep(900)
    }
    return page.evaluate((lbl) => {
      const el = [...document.querySelectorAll("*")].find(
        (n) => n.children.length === 0 && n.textContent.trim() === lbl
      )
      if (!el) return null
      const card = el.closest('[data-slot="card"]') || el.parentElement
      const r = card.getBoundingClientRect()
      return { x: r.x, y: r.y, width: r.width, height: r.height }
    }, label)
  }
  async function showBox(boxes) {
    const list = [].concat(boxes).filter(Boolean)
    if (!list.length) return
    await page.evaluate((b) => window.__hl(b), list)
  }
  async function moveToBox(box) {
    if (!box) return
    await moveTo(box.x + Math.min(box.width / 2, 90), box.y + Math.min(box.height / 2, 30))
  }

  // ── PASSI ──────────────────────────────────────────────────────────────
  await step("b01-intro", async () => {
    await page.evaluate(() => window.__title("Bilancio commessa", "ATEC PM — tutorial"))
    await sleep(6000)
    await page.evaluate(() => window.__title(null))
    await sleep(600)
  })

  await step("b02-dove", async () => {
    const tab = page.getByText("Preventivo vs Consuntivo", { exact: true }).first()
    await highlight([tab]).catch(() => {})
    await sleep(7000)
    const voceMenu = page.getByRole("link", { name: "Bilancio" }).first()
    await highlight([voceMenu]).catch(() => {})
    await moveToEl(voceMenu).catch(() => {})
  })

  await step("b03-mappa", async () => {
    for (const t of ["Impegno Risorse", "Ordine Commessa", "Conto Economico", "Riepilogo Costi", "Scheda Prezzi"]) {
      const el = titoloFascia(t)
      const b = await el.boundingBox().catch(() => null)
      if (!b) continue
      await el.scrollIntoViewIfNeeded().catch(() => {})
      await sleep(250)
      await highlight([el])
      await sleep(4200)
    }
  })

  await step("b04-tre-colonne", async () => {
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: "smooth" }))
    await sleep(800)
    const riga = page.locator("span").filter({ hasText: /^Prev:/ }).first()
    await show([riga.locator("xpath=..").first()])
  })

  await step("b05-delta", async () => {
    const delta = page.locator("span").filter({ hasText: /^Δ / }).first()
    await show([delta])
  })

  await step("b06-gruppi", async () => {
    const gruppo = page.locator("button").filter({ hasText: /SITO PILOTA|GESTIONE/ }).first()
    await clickEl(gruppo).catch(() => {})
    await sleep(1200)
    await clickEl(gruppo).catch(() => {})
    await sleep(800)
  })

  await step("b07-ordine", async () => {
    const t = titoloFascia("Ordine Commessa")
    await t.scrollIntoViewIfNeeded().catch(() => {})
    await sleep(700)
    const tabella = page.locator("table").first()
    await show([tabella]).catch(() => {})
  })

  await step("b08-delta-ordine", async () => {
    const vend = page.getByText(/TOTALE COSTI DI VENDITA|Totale Costi di Vendita/).first()
    await show([vend.locator("xpath=ancestor::tr[1]").first()]).catch(async () => {
      await show([vend]).catch(() => {})
    })
    await sleep(13000)
    const margine = page.getByText(/MARGINE DI SICUREZZA|Margine di Sicurezza/).first()
    await show([margine.locator("xpath=ancestor::tr[1]").first()]).catch(async () => {
      await show([margine]).catch(() => {})
    })
  })

  await step("b09-conto-economico", async () => {
    const t = titoloFascia("Conto Economico")
    await t.scrollIntoViewIfNeeded().catch(() => {})
    await sleep(700)
    await highlight([t])
  })

  await step("b10-costi", async () => {
    const a = await kpiBox("Totale Costi")
    const b = await kpiBox("Consuntivo Costi", false)
    await showBox([a, b])
    await moveToBox(a)
  })

  await step("b11-redditivita", async () => {
    const a = await kpiBox("Redditività Teorica Commessa")
    await showBox([a])
    await moveToBox(a)
    await sleep(10000)
    const b = await kpiBox("Redditività Effettiva Commessa", false)
    await showBox([b])
    await moveToBox(b)
  })

  await step("b12-formule", async () => {
    // hover fermo sul riquadro: il tooltip con la formula compare da solo
    await clearHl()
    const card = await kpiBox("Redditività Effettiva Commessa")
    await moveToBox(card)
    await sleep(9000)
    const altra = await kpiBox("Totale Costi")
    await moveToBox(altra)
    await sleep(6000)
  })

  await step("b13-riepilogo", async () => {
    const t = titoloFascia("Riepilogo Costi")
    await t.scrollIntoViewIfNeeded().catch(() => {})
    await sleep(700)
    await highlight([t])
    await sleep(14000)
    const trasferta = page.getByText(/Spese Trasferta/).first()
    await show([trasferta]).catch(() => {})
  })

  await step("b14-scheda-prezzi", async () => {
    const t = titoloFascia("Scheda Prezzi")
    await t.scrollIntoViewIfNeeded().catch(() => {})
    await sleep(800)
    const catena = page.getByText(/Totale Costi di Vendita/).first()
    await show([catena]).catch(() => {})
  })

  await step("b15-cross", async () => {
    await page.evaluate(() => window.__hlOff())
    await page.goto(ORIGIN + "/bilancio", { waitUntil: "networkidle" })
    await sleep(2000)
    await page.evaluate(OVERLAY)
    await page.evaluate((t) => window.__sub(t), "Tutte le commesse insieme")
    await sleep(1200)
    const filtro = page.getByText(/Sotto soglia/).first()
    await show([filtro]).catch(() => {})
    await sleep(6000)
    await clickEl(filtro).catch(() => {})
    await sleep(2000)
  })

  await step("b16-fine", async () => {
    await page.evaluate(() => { window.__hlOff(); window.__cursorOff() })
    await sleep(1500)
    await page.evaluate(() => window.__title("Bilancio commessa", "le ore registrate male fanno un bilancio che mente"))
  })

  await page.evaluate(() => window.__sub(null))
  await sleep(800)

  const offsetMs = t0 - tPage
  fs.writeFileSync(path.join(BASE, "marks-bilancio.json"), JSON.stringify({ offsetMs, marks }, null, 2))

  await context.close()
  await browser.close()
  const files = fs.readdirSync(path.join(BASE, "video-bilancio")).filter((f) => f.endsWith(".webm"))
  console.log("VIDEO:", files.join(", "))
  console.log("OFFSET_MS:", offsetMs)
  if (sfori.length) {
    console.log("PASSI CHE HANNO SFORATO (la voce resterebbe indietro):")
    sfori.forEach((s) => console.log("   " + s))
  } else {
    console.log("Nessun passo ha sforato il proprio parlato.")
  }
}

main().catch((e) => { console.error(e); process.exit(1) })
