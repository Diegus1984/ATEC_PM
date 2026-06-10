"""Recalc manuale dei totali per un preventivo — replica RecalcTotals del server."""
import sys, pymysql
sys.stdout.reconfigure(encoding="utf-8")

QUOTE_ID = 36  # PRV-2026-0003

c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

# Aggregati items (solo padri attivi)
cur.execute("""
    SELECT COALESCE(SUM(line_total),0),
           COALESCE(SUM(quantity * cost_price),0),
           COALESCE(SUM(line_total * vat_pct / 100),0)
    FROM quote_items
    WHERE quote_id = %s AND COALESCE(parent_item_id,0) = 0 AND COALESCE(is_active,1) = 1
""", (QUOTE_ID,))
subtotal, cost_total, vat_total_raw = cur.fetchone()

# Sconti dal record quote
cur.execute("SELECT COALESCE(discount_pct,0), COALESCE(discount_abs,0) FROM quotes WHERE id=%s", (QUOTE_ID,))
disc_pct, disc_abs = cur.fetchone()

after_disc = subtotal * (1 - disc_pct / 100) - disc_abs
total = after_disc
vat_total = vat_total_raw * (1 - disc_pct / 100)
total_with_vat = total + vat_total
profit = total - cost_total

print(f"Preventivo id={QUOTE_ID}")
print(f"  Subtotal:      {subtotal}")
print(f"  CostTotal:     {cost_total}")
print(f"  Total:         {total}")
print(f"  TotalWithVat:  {total_with_vat}")
print(f"  Profit:        {profit}")

cur.execute("""
    UPDATE quotes SET subtotal=%s, cost_total=%s, vat_total=%s,
                      total=%s, total_with_vat=%s, profit=%s
    WHERE id=%s
""", (subtotal, cost_total, vat_total, total, total_with_vat, profit, QUOTE_ID))
c.commit()
print("UPDATE OK")
c.close()
