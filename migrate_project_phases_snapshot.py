"""Migrazione: aggiunge snapshot fields a project_phases + backfill da phase_templates."""
import sys, pymysql
sys.stdout.reconfigure(encoding="utf-8")

c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

def col_exists(table, col):
    cur.execute(
        "SELECT COUNT(*) FROM information_schema.COLUMNS "
        "WHERE TABLE_SCHEMA='atec_pm' AND TABLE_NAME=%s AND COLUMN_NAME=%s",
        (table, col))
    return cur.fetchone()[0] > 0

# 1) Aggiungi colonne se non esistono
alters = [
    ("name",                       "ADD COLUMN name VARCHAR(100) NULL AFTER phase_template_id"),
    ("category",                   "ADD COLUMN category VARCHAR(50) NULL AFTER name"),
    ("cost_section_template_id",   "ADD COLUMN cost_section_template_id INT NULL AFTER category"),
    ("is_local",                   "ADD COLUMN is_local TINYINT(1) NOT NULL DEFAULT 0 AFTER cost_section_template_id"),
]
for col, sql_part in alters:
    if not col_exists("project_phases", col):
        cur.execute(f"ALTER TABLE project_phases {sql_part}")
        print(f"  + colonna {col} aggiunta")
    else:
        print(f"  = colonna {col} già presente")

# 2) Rendi phase_template_id NULLABLE (per fasi locali pure)
cur.execute("""SELECT IS_NULLABLE FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA='atec_pm' AND TABLE_NAME='project_phases' AND COLUMN_NAME='phase_template_id'""")
if cur.fetchone()[0] == "NO":
    cur.execute("ALTER TABLE project_phases MODIFY COLUMN phase_template_id INT NULL")
    print("  phase_template_id ora NULLABLE")
else:
    print("  phase_template_id già NULLABLE")

# 3) Indice
cur.execute("""SELECT COUNT(*) FROM information_schema.STATISTICS
               WHERE TABLE_SCHEMA='atec_pm' AND TABLE_NAME='project_phases' AND INDEX_NAME='idx_pp_section'""")
if cur.fetchone()[0] == 0:
    cur.execute("ALTER TABLE project_phases ADD INDEX idx_pp_section (cost_section_template_id)")
    print("  + indice idx_pp_section")

c.commit()

# 4) Backfill snapshot per le righe esistenti
cur.execute("""
    UPDATE project_phases pp
    JOIN phase_templates pt ON pt.id = pp.phase_template_id
    SET pp.name = COALESCE(pp.name, pt.name),
        pp.category = COALESCE(pp.category, pt.category),
        pp.cost_section_template_id = COALESCE(pp.cost_section_template_id, pt.cost_section_template_id)
    WHERE pp.name IS NULL OR pp.cost_section_template_id IS NULL
""")
print(f"  backfill: {cur.rowcount} righe aggiornate con snapshot")

c.commit()

# 5) Verifica
cur.execute("SELECT COUNT(*) FROM project_phases WHERE name IS NULL")
print(f"  righe ancora senza name: {cur.fetchone()[0]}")
cur.execute("SELECT COUNT(*) FROM project_phases WHERE is_local=1")
print(f"  righe is_local=1: {cur.fetchone()[0]}")

print("OK")
c.close()
