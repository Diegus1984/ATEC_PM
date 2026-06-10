"""Aggiorna v_timesheet_with_section per supportare fasi locali
(project_phases con phase_template_id NULL + name/category/cost_section_template_id snapshot)."""
import sys, pymysql
sys.stdout.reconfigure(encoding="utf-8")

c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

cur.execute("DROP VIEW IF EXISTS v_timesheet_with_section")
cur.execute("""
    CREATE VIEW v_timesheet_with_section AS
    SELECT
        te.id          AS entry_id,
        te.employee_id AS employee_id,
        te.project_phase_id AS project_phase_id,
        te.work_date   AS work_date,
        te.hours       AS hours,
        te.entry_type  AS entry_type,
        pp.project_id  AS project_id,
        COALESCE(NULLIF(pp.custom_name,''), pp.name, pt.name) AS phase_name,
        COALESCE(pp.cost_section_template_id, pt.cost_section_template_id) AS cost_section_template_id,
        CONCAT(emp.first_name,' ',emp.last_name) AS employee_name,
        COALESCE(d.hourly_cost, 0) AS hourly_cost
    FROM timesheet_entries te
    JOIN project_phases pp ON pp.id = te.project_phase_id
    LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
    JOIN employees emp ON emp.id = te.employee_id
    LEFT JOIN (
        SELECT employee_id, MIN(department_id) AS department_id
        FROM employee_departments
        GROUP BY employee_id
    ) ed ON ed.employee_id = emp.id
    LEFT JOIN departments d ON d.id = ed.department_id
""")
c.commit()

# verifica
cur.execute("SHOW CREATE VIEW v_timesheet_with_section")
r = cur.fetchone()
print("View ricreata. Estratto FROM:")
sql = r[1]
print("  " + sql[sql.find("from"):sql.find("from")+200])
c.close()
print("OK")
