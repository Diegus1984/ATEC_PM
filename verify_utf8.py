import sys
sys.stdout.reconfigure(encoding="utf-8")
import pymysql
c = pymysql.connect(host="localhost", port=3306, user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()
cur.execute("SELECT id, name FROM project_template_folders WHERE name LIKE %s", ("%Qualit%",))
for r in cur.fetchall():
    print(f"ID={r[0]}  name='{r[1]}'  len={len(r[1])}  bytes={r[1].encode('utf-8')!r}")
c.close()
