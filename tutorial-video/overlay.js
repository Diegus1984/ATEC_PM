// Sovrimpressioni del tutorial: sottotitolo, riquadri di evidenziazione, cursore
// finto (Playwright non registra il puntatore vero) e cartello di titolo.
// Viene iniettata nella pagina con page.evaluate.
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
      font-family: Inter, system-ui, sans-serif; text-align: center; padding: 0 6%;
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

module.exports = { OVERLAY }
