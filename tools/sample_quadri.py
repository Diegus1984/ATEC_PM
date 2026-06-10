import pymysql

c = pymysql.connect(host="localhost", user="root", password="Atec2005",
                    database="atec_pm", charset="utf8mb4")
cur = c.cursor()

cur.execute("""
    SELECT r.id, r.modello, q.id, q.system_key, q.controllore, q.payload, q.area_lavoro,
           (SELECT COUNT(*) FROM gamma_distinta d WHERE d.quadro_id=q.id) AS n
    FROM gamma_robot r
    JOIN gamma_quadro q ON q.robot_id = r.id
    WHERE r.modello IN ('IRB 6640', 'IRB 7600', 'IRB 4600 Type C', 'IRB 6620', 'IRB 1200')
    ORDER BY r.modello, q.payload DESC
    LIMIT 30
""")
for r in cur.fetchall():
    print(r)

c.close()
