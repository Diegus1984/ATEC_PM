"""Ripara la commessa 17: due sezioni 'Program Manager' duplicate dopo ConvertToProject.
Strategy: scopri tutte le coppie duplicate (stesso nome, una con template_id NULL e l'altra
con template_id valorizzato). La NULL eredita il template_id, le righe figlie restano.
La sezione duplicata col template viene eliminata se non ha risorse né departments."""
import sys, pymysql
sys.stdout.reconfigure(encoding="utf-8")

PROJECT_ID = 17
c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

cur.execute("""
    SELECT a.id AS null_id, b.id AS tpl_id, b.template_id, a.name
    FROM project_cost_sections a
    JOIN project_cost_sections b
      ON a.project_id = b.project_id
     AND LOWER(a.name) = LOWER(b.name)
     AND a.template_id IS NULL
     AND b.template_id IS NOT NULL
    WHERE a.project_id = %s
""", (PROJECT_ID,))
pairs = cur.fetchall()
print(f"Coppie duplicate trovate: {len(pairs)}")
for null_id, tpl_id, template_id, name in pairs:
    print(f"  - '{name}': null_section={null_id}  tpl_section={tpl_id} (template_id={template_id})")

    # 1) Trasferisci template_id alla sezione null
    cur.execute("UPDATE project_cost_sections SET template_id=%s WHERE id=%s", (template_id, null_id))

    # 2) Trasferisci i department del tpl_section nella null (se mancanti)
    cur.execute("""
        INSERT IGNORE INTO project_cost_section_departments (project_cost_section_id, department_id)
        SELECT %s, department_id FROM project_cost_section_departments WHERE project_cost_section_id=%s
    """, (null_id, tpl_id))

    # 3) Verifica che il duplicato sia "vuoto" (no risorse)
    cur.execute("SELECT COUNT(*) FROM project_cost_resources WHERE section_id=%s", (tpl_id,))
    n_res = cur.fetchone()[0]
    if n_res > 0:
        print(f"    !! tpl_section {tpl_id} ha {n_res} risorse → NON elimino")
        continue

    # 4) Elimina depts del duplicato + il duplicato stesso
    cur.execute("DELETE FROM project_cost_section_departments WHERE project_cost_section_id=%s", (tpl_id,))
    cur.execute("DELETE FROM project_cost_sections WHERE id=%s", (tpl_id,))
    print(f"    duplicato {tpl_id} eliminato")

c.commit()
c.close()
print("OK")
