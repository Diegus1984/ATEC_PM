import sys
import os
import subprocess
import argparse

SSH_KEY = os.path.expanduser(r"~\.ssh\atec_vps")
SERVER = "atec@192.168.2.150"
DB_USER = "atecpm"
DB_PASS = "pa5dfskE7jmVExotmNhTNhxq"
DB_NAME = "atec_pm"
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ATTS_DIR = os.path.join(REPO_ROOT, "_bug_atts")

def run_remote_mysql(query):
    # Usiamo powershell encoded command per evitare qualsiasi problema di quoting tra Windows e OpenSSH
    ps_cmd = f"""
$mysql = 'C:\\Program Files\\MySQL\\MySQL Server 8.4\\bin\\mysql.exe'
& $mysql -u {DB_USER} -p{DB_PASS} --default-character-set=utf8mb4 -e "{query}"
"""
    import base64
    enc = base64.b64encode(ps_cmd.encode("utf-16le")).decode("ascii")
    cmd = ["ssh", "-i", SSH_KEY, "-o", "StrictHostKeyChecking=no", "-o", "LogLevel=ERROR", SERVER, "powershell", "-NoProfile", "-EncodedCommand", enc]
    proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace")
    output = proc.stdout
    # Rimuovi eventuali warning mysql
    clean_lines = [l for l in output.splitlines() if "Using a password on the command line interface can be insecure" not in l and not l.startswith("#< CLIXML")]
    return "\n".join(clean_lines)

def download_attachment(stored_name, local_name):
    os.makedirs(ATTS_DIR, exist_ok=True)
    local_path = os.path.join(ATTS_DIR, local_name)
    remote_path = f"C:/ATEC_PM/Uploads/bugs/{stored_name}"
    cmd = ["scp", "-i", SSH_KEY, "-o", "StrictHostKeyChecking=no", "-o", "LogLevel=ERROR", f"{SERVER}:{remote_path}", local_path]
    res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if os.path.exists(local_path):
        return local_path
    return None

def main():
    parser = argparse.ArgumentParser(description="Consulta e scarica segnalazioni ATEC PM dal server di produzione.")
    parser.add_argument("id", nargs="?", type=int, help="ID della segnalazione da leggere")
    parser.add_argument("--ultime", "-n", type=int, default=15, help="Numero di ultime segnalazioni da mostrare")
    parser.add_argument("--aperte", "-a", action="store_true", help="Mostra solo le segnalazioni OPEN / IN_PROGRESS")
    args = parser.parse_args()

    if args.id:
        # Dettaglio segnalazione
        sql = f"SELECT b.id, b.kind, b.title, b.description, b.area, b.severity, b.status, b.admin_note, b.context, b.fixed_in_build, DATE_FORMAT(b.created_at, '%Y-%m-%d %H:%i') AS created_at, DATE_FORMAT(b.resolved_at, '%Y-%m-%d %H:%i') AS resolved_at, CONCAT(IFNULL(e.first_name,''), ' ', IFNULL(e.last_name,'')) AS autore FROM {DB_NAME}.bug_reports b LEFT JOIN {DB_NAME}.employees e ON b.created_by = e.id WHERE b.id = {args.id} \\G"
        res = run_remote_mysql(sql)
        if not res.strip() or "Empty set" in res:
            print(f"Segnalazione #{args.id} non trovata.")
            return

        print(f"\n========================================================")
        print(f" DETTAGLIO SEGNALAZIONE #{args.id}")
        print(f"========================================================\n")
        print(res.strip())

        # Allegati
        sql_att = f"SELECT id, file_name, stored_name, is_reply, size_bytes FROM {DB_NAME}.bug_report_attachments WHERE bug_id = {args.id};"
        res_att = run_remote_mysql(sql_att)
        att_lines = [l for l in res_att.strip().splitlines() if l and not l.startswith("id\t")]
        if att_lines:
            print(f"\nAllegati trovati ({len(att_lines)}):")
            for line in att_lines:
                parts = line.split("\t")
                if len(parts) >= 3:
                    att_id, file_name, stored_name = parts[0], parts[1], parts[2]
                    local_filename = f"bug{args.id}_{file_name}"
                    local_path = download_attachment(stored_name, local_filename)
                    if local_path:
                        print(f"  - {file_name} -> SCARICATO IN: {local_path}")
                    else:
                        print(f"  - {file_name} (Stored: {stored_name}) -> Errore download")
        else:
            print("\nNessun allegato.")
        print()
    else:
        # Elenco
        where = "WHERE b.status IN ('OPEN', 'IN_PROGRESS')" if args.aperte else ""
        limit = "" if args.aperte else f"LIMIT {args.ultime}"
        sql = f"SELECT b.id, b.kind, b.status, b.severity, b.title, CONCAT(IFNULL(e.first_name,''), ' ', IFNULL(e.last_name,'')) AS autore, DATE_FORMAT(b.created_at, '%Y-%m-%d %H:%i') AS data FROM {DB_NAME}.bug_reports b LEFT JOIN {DB_NAME}.employees e ON b.created_by = e.id {where} ORDER BY b.id DESC {limit};"
        res = run_remote_mysql(sql)
        print("\n=== SEGNALAZIONI ATEC PM ===\n")
        print(res.strip())
        print()

if __name__ == "__main__":
    main()
