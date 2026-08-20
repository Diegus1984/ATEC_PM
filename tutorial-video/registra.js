// Registra il video del tutorial pilotando Chrome con playwright-core.
// Ogni passo dura esattamente quanto il suo audio (durate.json), così il montaggio
// finale è un semplice "video + traccia voce" senza sincronizzazioni a mano.
const fs = require("fs")
const path = require("path")
const { chromium } = require("playwright-core")

const BASE = __dirname
const CHROME = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"
// TUT_ORIGIN: http://localhost:5173 (sviluppo) oppure http://192.168.2.150:5150 (produzione)
const ORIGIN = process.env.TUT_ORIGIN || "http://localhost:5173"
const URL = ORIGIN + "/config-sezioni"
// Token JWT: variabile TUT_TOKEN, oppure un file token.txt in questa cartella.
const TOKEN = (process.env.TUT_TOKEN || fs.readFileSync(path.join(BASE, "token.txt"), "utf8")).trim()
// Con TUT_READONLY=1 ogni richiesta che non sia GET viene abortita: contro la produzione
// è la garanzia che nessun clic possa scrivere, nemmeno per errore.
const READONLY = process.env.TUT_READONLY === "1"
const W = 1600
const H = 900

const copione = JSON.parse(fs.readFileSync(path.join(BASE, "copione.json"), "utf8"))
const durate = JSON.parse(fs.readFileSync(path.join(BASE, "durate.json"), "utf8"))
const secondi = Object.fromEntries(durate.map((d) => [d.id, d.seconds]))

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// ── overlay: sottotitolo, riquadri di evidenziazione, cursore finto ──────────
const OVERLAY = () => {
  const css = document.createElement("style")
  css.textContent = `
    #tut-sub {
      position: fixed; left: 50%; bottom: 28px; transform: translateX(-50%);
      max-width: 78%; padding: 12px 26px; z-index: 2147483000;
      background: rgba(15,23,42,.92); color: #fff; border-radius: 10px;
      font: 600 21px/1.35 Inter, system-ui, sans-serif; text-align: center;
      box-shadow: 0 8px 30px rgba(0,0,0,.35); opacity: 0; transition: opacity .35s;
      pointer-events: none; letter-spacing: .2px;
    }
    #tut-sub.on { opacity: 1; }
    .tut-hl {
      position: fixed; z-index: 2147482000; pointer-events: none;
      border: 3px solid #f59e0b; border-radius: 8px;
      box-shadow: 0 0 0 9999px rgba(15,23,42,.42), 0 0 18px rgba(245,158,11,.9);
      transition: all .45s cubic-bezier(.4,0,.2,1); opacity: 0;
    }
    .tut-hl.on { opacity: 1; }
    #tut-cur {
      position: fixed; z-index: 2147483600; width: 22px; height: 22px;
      margin: -3px 0 0 -3px; pointer-events: none; opacity: 0;
      transition: opacity .3s;
      background: no-repeat center/contain url('data:image/svg+xml;utf8,\
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M5 2l14 9-6.5 1.2L15 19l-3 1.2-2.6-6.6L5 17z" fill="%23fff" stroke="%23111" stroke-width="1.4" stroke-linejoin="round"/></svg>');
      filter: drop-shadow(0 2px 3px rgba(0,0,0,.5));
    }
    #tut-cur.on { opacity: 1; }
    #tut-cur.click::after {
      content: ""; position: absolute; left: -14px; top: -14px;
      width: 44px; height: 44px; border-radius: 50%;
      border: 3px solid #f59e0b; animation: tut-ping .5s ease-out;
    }
    @keyframes tut-ping { from { transform: scale(.3); opacity: 1 } to { transform: scale(1); opacity: 0 } }
    #tut-title {
      position: fixed; inset: 0; z-index: 2147483500; display: flex;
      flex-direction: column; align-items: center; justify-content: center;
      background: rgba(15,23,42,.93); color: #fff; opacity: 0;
      transition: opacity .6s; pointer-events: none;
      font-family: Inter, system-ui, sans-serif;
    }
    #tut-title.on { opacity: 1; }
    #tut-title b { font-size: 62px; letter-spacing: -1px; }
    #tut-title span { font-size: 26px; opacity: .8; margin-top: 14px; font-weight: 500; }
  `
  document.head.appendChild(css)

  const sub = document.createElement("div")
  sub.id = "tut-sub"
  document.body.appendChild(sub)

  const cur = document.createElement("div")
  cur.id = "tut-cur"
  document.body.appendChild(cur)

  const title = document.createElement("div")
  title.id = "tut-title"
  title.innerHTML = "<b></b><span></span>"
  document.body.appendChild(title)

  window.__sub = (text) => {
    if (!text) { sub.classList.remove("on"); return }
    sub.textContent = text
    sub.classList.add("on")
  }
  window.__title = (main, small) => {
    if (!main) { title.classList.remove("on"); return }
    title.querySelector("b").textContent = main
    title.querySelector("span").textContent = small || ""
    title.classList.add("on")
  }
  window.__cursor = (x, y, clicking) => {
    cur.classList.add("on")
    cur.style.left = x + "px"
    cur.style.top = y + "px"
    if (clicking) {
      cur.classList.remove("click")
      void cur.offsetWidth
      cur.classList.add("click")
    }
  }
  window.__cursorOff = () => cur.classList.remove("on")

  window.__hl = (boxes) => {
    document.querySelectorAll(".tut-hl").forEach((n) => n.remove())
    ;(boxes || []).forEach((b) => {
      const d = document.createElement("div")
      d.className = "tut-hl"
      d.style.left = b.x - 6 + "px"
      d.style.top = b.y - 6 + "px"
      d.style.width = b.width + 12 + "px"
      d.style.height = b.height + 12 + "px"
      if (b.solo === false) d.style.boxShadow = "0 0 18px rgba(245,158,11,.9)"
      document.body.appendChild(d)
      requestAnimationFrame(() => d.classList.add("on"))
    })
  }
  window.__hlOff = () => {
    document.querySelectorAll(".tut-hl").forEach((n) => {
      n.classList.remove("on")
      setTimeout(() => n.remove(), 450)
    })
  }
}

async function main() {
  // headless: in finestra reale Chrome non rispetta il viewport richiesto e il video
  // esce con una fascia bianca attorno al contenuto.
  const browser = await chromium.launch({ executablePath: CHROME, headless: true })
  const context = await browser.newContext({
    viewport: { width: W, height: H },
    recordVideo: { dir: path.join(BASE, "video"), size: { width: W, height: H } },
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

  const page = await context.newPage()

  // Lucchetto: contro la produzione lascia passare solo le letture. Qualsiasi
  // POST/PUT/PATCH/DELETE viene abortita prima di partire e registrata a video log.
  const bloccate = []
  if (READONLY) {
    await page.route("**/*", (route) => {
      const req = route.request()
      if (req.method() === "GET" || req.method() === "HEAD") return route.continue()
      bloccate.push(req.method() + " " + req.url())
      return route.abort()
    })
    console.log("SOLA LETTURA attiva su", ORIGIN)
  }

  // Timeout corto: un selettore che non trova nulla deve fallire subito, altrimenti si
  // mangia 30 secondi dentro un passo e la voce resta indietro per tutto il resto.
  context.setDefaultTimeout(3000)

  const tPage = Date.now()
  await page.goto(URL, { waitUntil: "networkidle" })
  await page.waitForSelector("text=Configurazione sezioni", { timeout: 30000 }).catch(() => {})
  await sleep(1500)
  await page.evaluate(OVERLAY)
  await sleep(300)

  const marks = []
  const t0 = Date.now()

  // helpers ------------------------------------------------------------------
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

  /** Porta l'elemento in vista PRIMA di evidenziarlo: senza questo il riquadro
   *  finisce fuori schermo (il pannello delle fasi orfane spinge giù l'albero). */
  async function show(locs, andMove = true) {
    const list = [].concat(locs)
    await list[0].scrollIntoViewIfNeeded().catch(() => {})
    await sleep(650)
    await highlight(list)
    if (andMove) await moveToEl(list[0])
  }

  async function step(id, fn) {
    const s = copione.find((c) => c.id === id)
    const budget = (secondi[id] || 5) * 1000
    const start = Date.now()
    marks.push({ id, atMs: start - t0 })
    await page.evaluate((t) => window.__sub(t), s.sub)
    if (fn) await fn()
    const left = budget - (Date.now() - start)
    if (left > 0) await sleep(left)
    await clearHl().catch(() => {})
  }

  // localizzatori ------------------------------------------------------------
  const contatore = page.locator("p", { hasText: /gruppi —/ }).first()
  const pannelloReparti = page.locator("div.rounded-lg.border.p-3").first()
  const barraGestione = page.locator("button", { hasText: "GESTIONE" }).first()
  const cardAlbero = page.locator("div.grid.gap-4").first()

  // ── PASSI ────────────────────────────────────────────────────────────────
  await step("01-intro", async () => {
    await page.evaluate(() => window.__title("Configurazione sezioni", "ATEC PM — tutorial"))
    await sleep(5200)
    await page.evaluate(() => window.__title(null))
    await sleep(600)
  })

  await step("02-perche", async () => {
    const sezione = page.locator("div.rounded-md.border.bg-card").first()
    await sezione.scrollIntoViewIfNeeded().catch(() => {})
    await sleep(900)
    await sleep(6000)
    const fasi = page.locator("p", { hasText: /^Fasi template/ }).first()
    await show([fasi.locator("xpath=..").first()])
    await sleep(4000)
    const badge = page.locator("span", { hasText: /^IN SEDE$/ }).first()
    await show([badge])
  })

  await step("03-mappa", async () => {
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: "smooth" }))
    await sleep(800)
    await show([cardAlbero], false)
    await sleep(6500)
    await show([pannelloReparti])
  })

  await step("04-contatore", async () => {
    await show([contatore])
  })

  await step("05-apri-gruppo", async () => {
    await clickEl(barraGestione)          // chiude
    await sleep(1400)
    await clickEl(barraGestione)          // riapre
  })

  await step("06-sezione", async () => {
    const sezione = page.locator("div.rounded-md.border.bg-card").first()
    await show([sezione])
  })

  await step("07-tipo", async () => {
    const inSede = page.locator("span", { hasText: /^IN SEDE$/ }).first()
    await show([inSede])
    await sleep(8000)
    const daCli = page.locator("span", { hasText: /^DA CLIENTE$/ }).first()
    await show([daCli])
  })

  await step("08-reparti", async () => {
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: "smooth" }))
    await sleep(800)
    await show([pannelloReparti])
  })

  await step("09-drag", async () => {
    const rep = page.locator("div.cursor-grab").first()
    const sez = page.locator("div.rounded-md.border.bg-card").first()
    const rb = await rep.boundingBox()
    const sb = await sez.boundingBox()
    if (rb && sb) {
      await moveTo(rb.x + rb.width / 2, rb.y + rb.height / 2)
      await page.mouse.down()
      await sleep(300)
      await moveTo(sb.x + sb.width / 2, sb.y + 24, 28)
      await sleep(900)
      // rilascio FUORI da qualsiasi zona valida: nessun dato viene modificato
      await moveTo(sb.x + sb.width / 2, 12, 14)
      await page.mouse.up()
    }
    await sleep(400)
    await highlight([sez])
  })

  await step("10-fasi", async () => {
    const fasi = page.locator("p", { hasText: /^Fasi template/ }).first()
    await show([fasi.locator("xpath=..").first()])
  })

  await step("11-nuova-sezione", async () => {
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: "smooth" }))
    await sleep(600)
    const menu = barraGestione.locator("xpath=following-sibling::*[1]").first()
    await clickEl(menu)
    await sleep(500)
    const voce = page.locator("[role=menuitem]", { hasText: "Nuova sezione" }).first()
    await clickEl(voce)
    await sleep(600)
  })

  await step("12-default", async () => {
    const dc = page.locator("label", { hasText: "Default commessa" }).first()
    const dp = page.locator("label", { hasText: "Default preventivo" }).first()
    await highlight([dc, dp])
    await moveToEl(dc)
  })

  await step("13-orfane", async () => {
    const annulla = page.locator("button", { hasText: /^Annulla$/ }).first()
    await clickEl(annulla)
    await sleep(600)
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: "smooth" }))
    await sleep(800)
    const orfane = page.locator("p", { hasText: /^Fasi senza sezione/ }).first()
    await show([orfane.locator("xpath=..").first()])
  })

  await step("14-tariffe", async () => {
    const tariffe = page.locator("text=Anagrafica tariffe").first()
    await tariffe.scrollIntoViewIfNeeded().catch(() => {})
    await sleep(900)
    const card = tariffe.locator("xpath=ancestor::div[contains(@class,'rounded-xl') or contains(@class,'rounded-lg')][1]").first()
    await highlight([card]).catch(() => {})
    await sleep(9000)
    await page.evaluate(() => window.scrollBy({ top: 260, behavior: "smooth" }))
  })

  await step("15-fine", async () => {
    await page.evaluate(() => { window.__hlOff(); window.__cursorOff(); window.scrollTo({ top: 0, behavior: "smooth" }) })
    await sleep(1800)
    await page.evaluate(() => window.__title("Configurazione sezioni", "anagrafica condivisa — le modifiche le vedono tutti"))
  })

  await page.evaluate(() => window.__sub(null))
  await sleep(800)

  const offsetMs = t0 - tPage
  fs.writeFileSync(path.join(BASE, "marks.json"), JSON.stringify({ offsetMs, marks }, null, 2))

  await context.close()
  await browser.close()

  const files = fs.readdirSync(path.join(BASE, "video")).filter((f) => f.endsWith(".webm"))
  console.log("VIDEO:", files.join(", "))
  console.log("OFFSET_MS:", offsetMs)
  if (READONLY) {
    console.log("SCRITTURE BLOCCATE:", bloccate.length)
    bloccate.forEach((b) => console.log("   " + b))
  }
}

main().catch((e) => { console.error(e); process.exit(1) })
