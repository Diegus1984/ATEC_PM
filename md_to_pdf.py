"""
Converte SEZIONI_COSTO_GUIDA.md in PDF leggibile.
Parser markdown semplice ma efficace: heading, paragrafi, liste,
tabelle, code block, bold, italic, inline-code, link.
"""
import re
from pathlib import Path
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import cm, mm
from reportlab.lib.colors import HexColor, white, black
from reportlab.lib.enums import TA_LEFT
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    Preformatted, PageBreak, KeepTogether
)

SRC = Path(__file__).parent / "SEZIONI_COSTO_GUIDA.md"
OUT = Path(__file__).parent / "SEZIONI_COSTO_GUIDA.pdf"

# ── Stili ────────────────────────────────────────────────────────
styles = getSampleStyleSheet()
NAVY = HexColor("#1A1D26")
BLUE = HexColor("#2563EB")
GRAY = HexColor("#6B7280")
LIGHT_GRAY = HexColor("#F3F4F6")
BORDER = HexColor("#E4E7EC")
CODE_BG = HexColor("#F8FAFC")

h1 = ParagraphStyle("h1", parent=styles["Heading1"], fontSize=18, spaceBefore=18, spaceAfter=10,
                   textColor=NAVY, fontName="Helvetica-Bold", leading=22)
h2 = ParagraphStyle("h2", parent=styles["Heading2"], fontSize=14, spaceBefore=14, spaceAfter=6,
                   textColor=BLUE, fontName="Helvetica-Bold", leading=18)
h3 = ParagraphStyle("h3", parent=styles["Heading3"], fontSize=12, spaceBefore=10, spaceAfter=4,
                   textColor=NAVY, fontName="Helvetica-Bold", leading=15)
body = ParagraphStyle("body", parent=styles["BodyText"], fontSize=10, leading=14,
                     textColor=black, fontName="Helvetica", spaceAfter=4, alignment=TA_LEFT)
bullet = ParagraphStyle("bullet", parent=body, leftIndent=14, bulletIndent=4, spaceAfter=2)
quote = ParagraphStyle("quote", parent=body, leftIndent=12, textColor=GRAY,
                      fontName="Helvetica-Oblique", borderColor=BORDER,
                      borderPadding=6, backColor=LIGHT_GRAY, spaceBefore=4, spaceAfter=8)
code_style = ParagraphStyle("code", parent=body, fontName="Courier", fontSize=8.5,
                           textColor=NAVY, leading=11, backColor=CODE_BG,
                           borderColor=BORDER, borderPadding=6, leftIndent=4)
small = ParagraphStyle("small", parent=body, fontSize=8.5, textColor=GRAY)

# ── Inline markdown → reportlab markup ───────────────────────────
def inline(text: str) -> str:
    # 1. Estrai code inline e link in placeholder per proteggerli dai regex bold/italic
    placeholders = {}
    def stash(prefix, html):
        key = f"\x00{prefix}{len(placeholders)}\x00"
        placeholders[key] = html
        return key

    # inline code `...`
    def repl_code(m):
        content = m.group(1).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        return stash("C", f'<font face="Courier" color="#2563EB">{content}</font>')
    text = re.sub(r"`([^`\n]+)`", repl_code, text)

    # link [testo](url)
    def repl_link(m):
        content = m.group(1).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        return stash("L", f'<font color="#2563EB"><u>{content}</u></font>')
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", repl_link, text)

    # 2. Escape XML sul testo "nudo" rimasto (placeholder includono \x00 quindi safe)
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

    # 3. Bold **...** e italic *...*
    text = re.sub(r"\*\*([^*\n]+)\*\*", r"<b>\1</b>", text)
    text = re.sub(r"(?<!\*)\*([^*\n]+)\*(?!\*)", r"<i>\1</i>", text)

    # 4. Reinserisci placeholder
    for key, html in placeholders.items():
        text = text.replace(key, html)
    return text

# ── Parsing del file MD per blocchi ──────────────────────────────
def parse_md(text: str):
    lines = text.split("\n")
    elements = []
    i = 0
    while i < len(lines):
        line = lines[i]

        # Code fence ```
        if line.strip().startswith("```"):
            j = i + 1
            buf = []
            while j < len(lines) and not lines[j].strip().startswith("```"):
                buf.append(lines[j])
                j += 1
            code_text = "\n".join(buf)
            # Preformatted preserves whitespace; usa font monospace piccolo
            elements.append(Preformatted(code_text, code_style))
            elements.append(Spacer(1, 4))
            i = j + 1
            continue

        # Heading
        if line.startswith("# "):
            elements.append(Paragraph(inline(line[2:]), h1))
            i += 1
            continue
        if line.startswith("## "):
            elements.append(Paragraph(inline(line[3:]), h2))
            i += 1
            continue
        if line.startswith("### "):
            elements.append(Paragraph(inline(line[4:]), h3))
            i += 1
            continue

        # Horizontal rule
        if re.match(r"^-{3,}\s*$", line):
            elements.append(Spacer(1, 8))
            i += 1
            continue

        # Blockquote (>)
        if line.startswith("> "):
            buf = []
            while i < len(lines) and lines[i].startswith(">"):
                buf.append(lines[i].lstrip("> ").rstrip())
                i += 1
            elements.append(Paragraph(inline(" ".join(buf)), quote))
            continue

        # Table — header con | e separator |---|
        if "|" in line and i + 1 < len(lines) and re.match(r"^\s*\|?[\s\-:|]+\|?\s*$", lines[i+1]):
            rows = []
            while i < len(lines) and "|" in lines[i] and not re.match(r"^\s*\|?[\s\-:|]+\|?\s*$", lines[i]):
                cells = [c.strip() for c in lines[i].strip().strip("|").split("|")]
                rows.append(cells)
                i += 1
                if i < len(lines) and re.match(r"^\s*\|?[\s\-:|]+\|?\s*$", lines[i]):
                    i += 1  # skip separator
            # Build reportlab Table
            tbl_data = [[Paragraph(inline(c), body) for c in row] for row in rows]
            ncols = max(len(r) for r in rows)
            page_w = A4[0] - 4*cm
            col_w = page_w / ncols
            t = Table(tbl_data, colWidths=[col_w]*ncols, repeatRows=1)
            t.setStyle(TableStyle([
                ("BACKGROUND", (0,0), (-1,0), NAVY),
                ("TEXTCOLOR", (0,0), (-1,0), white),
                ("FONTNAME", (0,0), (-1,0), "Helvetica-Bold"),
                ("FONTSIZE", (0,0), (-1,-1), 9),
                ("GRID", (0,0), (-1,-1), 0.4, BORDER),
                ("VALIGN", (0,0), (-1,-1), "TOP"),
                ("LEFTPADDING", (0,0), (-1,-1), 5),
                ("RIGHTPADDING", (0,0), (-1,-1), 5),
                ("TOPPADDING", (0,0), (-1,-1), 4),
                ("BOTTOMPADDING", (0,0), (-1,-1), 4),
                ("ROWBACKGROUNDS", (0,1), (-1,-1), [white, LIGHT_GRAY]),
            ]))
            elements.append(t)
            elements.append(Spacer(1, 8))
            continue

        # Bullet list
        if re.match(r"^\s*[-*]\s+", line):
            while i < len(lines) and re.match(r"^\s*[-*]\s+", lines[i]):
                content = re.sub(r"^\s*[-*]\s+", "", lines[i])
                elements.append(Paragraph("• " + inline(content), bullet))
                i += 1
            elements.append(Spacer(1, 4))
            continue

        # Numbered list
        if re.match(r"^\s*\d+\.\s+", line):
            while i < len(lines) and re.match(r"^\s*\d+\.\s+", lines[i]):
                content = re.sub(r"^\s*\d+\.\s+", "", lines[i])
                num = re.match(r"^\s*(\d+)\.", lines[i]).group(1)
                elements.append(Paragraph(f"{num}. " + inline(content), bullet))
                i += 1
            elements.append(Spacer(1, 4))
            continue

        # Empty line
        if line.strip() == "":
            i += 1
            continue

        # Paragraph (raccoglie righe consecutive non-vuote)
        buf = [line]
        i += 1
        while i < len(lines) and lines[i].strip() != "" and not re.match(
            r"^(#|>|```|\s*[-*]\s+|\s*\d+\.\s+|---)", lines[i]) and "|" not in lines[i]:
            buf.append(lines[i])
            i += 1
        elements.append(Paragraph(inline(" ".join(buf)), body))

    return elements

# ── Footer con numero pagina ─────────────────────────────────────
def on_page(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(GRAY)
    canvas.drawString(2*cm, 1*cm, "ATEC PM — Sezioni Costo (guida d'uso)")
    canvas.drawRightString(A4[0] - 2*cm, 1*cm, f"Pag. {doc.page}")
    canvas.restoreState()

# ── Main ─────────────────────────────────────────────────────────
def main():
    md_text = SRC.read_text(encoding="utf-8")
    story = parse_md(md_text)

    doc = SimpleDocTemplate(
        str(OUT), pagesize=A4,
        leftMargin=2*cm, rightMargin=2*cm,
        topMargin=2*cm, bottomMargin=2*cm,
        title="Configurazione Sezioni Costo — Guida d'uso",
        author="ATEC PM"
    )
    doc.build(story, onFirstPage=on_page, onLaterPages=on_page)
    print(f"PDF creato: {OUT} ({OUT.stat().st_size // 1024} KB)")

if __name__ == "__main__":
    main()
