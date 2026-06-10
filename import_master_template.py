"""
Importa la struttura cartelle e file da C:\\Users\\diego\\Desktop\\MASTER_TEMPLATE
nelle tabelle project_template_folders / project_template_files.
Copia i file fisici in {BasePath}/TEMPLATES/ con naming {folder_id}_{filename}.
Idempotente: prima ripulisce le tabelle (e i file in TEMPLATES generati da precedenti import).
"""
import os
import shutil
import sys
from pathlib import Path
import pymysql

SRC_ROOT = Path(r"C:\Users\diego\Desktop\MASTER_TEMPLATE")

DB_CFG = dict(
    host="localhost", port=3306,
    user="root", password="Atec2005",
    database="atec_pm",
    charset="utf8mb4"
)

def main():
    if not SRC_ROOT.exists():
        print(f"ERRORE: {SRC_ROOT} non trovata", file=sys.stderr)
        sys.exit(1)

    conn = pymysql.connect(**DB_CFG)
    cur = conn.cursor()

    # 1) Leggi BasePath e calcola TEMPLATES dir
    cur.execute("SELECT config_value FROM app_config WHERE config_key = 'BasePath'")
    row = cur.fetchone()
    base_path = Path(row[0] if row else r"C:\ATEC_Commesse")
    templates_dir = base_path / "TEMPLATES"
    templates_dir.mkdir(parents=True, exist_ok=True)
    print(f"BasePath:        {base_path}")
    print(f"TEMPLATES dir:   {templates_dir}")
    print(f"Sorgente:        {SRC_ROOT}")
    print()

    # 2) Reset tabelle e cleanup file precedenti
    print("[1/4] Pulizia stato precedente...")
    cur.execute("SELECT disk_path FROM project_template_files")
    old_files = [r[0] for r in cur.fetchall()]
    for p in old_files:
        try:
            if os.path.exists(p):
                os.remove(p)
        except Exception as e:
            print(f"  WARN: non riesco a eliminare {p}: {e}")

    cur.execute("DELETE FROM project_template_files")
    cur.execute("DELETE FROM project_template_folders")
    cur.execute("ALTER TABLE project_template_files AUTO_INCREMENT = 1")
    cur.execute("ALTER TABLE project_template_folders AUTO_INCREMENT = 1")
    conn.commit()
    print(f"  righe rimosse: {len(old_files)} file fisici, tabelle troncate")

    # 3) Walk ricorsivo: cartelle prima (DFS) → poi file
    print("[2/4] Import cartelle...")
    # map: disk_path (str) → db folder id
    path_to_id: dict[str, int] = {}

    def insert_folder(parent_db_id, name, sort_order):
        cur.execute(
            "INSERT INTO project_template_folders (parent_id, name, sort_order) VALUES (%s, %s, %s)",
            (parent_db_id, name, sort_order)
        )
        return cur.lastrowid

    # Raccogli tutte le cartelle in ordine (BFS) per garantire che parent esista prima dei figli
    folder_count = 0
    # Walk con sorted(name) per stabilità
    for current_dir, subdirs, _ in os.walk(SRC_ROOT):
        subdirs.sort()  # stabilità ordinamento
        cur_path = Path(current_dir)
        if cur_path == SRC_ROOT:
            # Le root del MASTER_TEMPLATE diventano root anche nel DB (parent_id=NULL)
            continue
        parent_path = cur_path.parent
        if parent_path == SRC_ROOT:
            parent_db_id = None
        else:
            parent_db_id = path_to_id[str(parent_path)]
        # sort_order: indice in parent
        siblings = sorted([d for d in os.listdir(parent_path) if (parent_path / d).is_dir()])
        sort_order = siblings.index(cur_path.name)
        new_id = insert_folder(parent_db_id, cur_path.name, sort_order)
        path_to_id[str(cur_path)] = new_id
        folder_count += 1

    conn.commit()
    print(f"  cartelle inserite: {folder_count}")

    # 4) Import file
    print("[3/4] Import file...")
    file_count = 0
    total_bytes = 0
    for current_dir, _, files in os.walk(SRC_ROOT):
        cur_path = Path(current_dir)
        if cur_path == SRC_ROOT:
            continue  # MASTER_TEMPLATE root non contiene file direttamente
        folder_db_id = path_to_id[str(cur_path)]
        for fname in sorted(files):
            src_file = cur_path / fname
            try:
                # Inserisco prima per avere l'id, poi rinomino su disco con id
                cur.execute(
                    "INSERT INTO project_template_files (folder_id, file_name, disk_path, file_size) "
                    "VALUES (%s, %s, %s, %s)",
                    (folder_db_id, fname, "", src_file.stat().st_size)
                )
                file_id = cur.lastrowid
                disk_filename = f"{folder_db_id}_{fname}"
                disk_path = templates_dir / disk_filename
                # Gestisci collisioni (nello stesso folder due file dovrebbero essere unici, ma per sicurezza)
                counter = 1
                while disk_path.exists():
                    stem = Path(fname).stem
                    ext = Path(fname).suffix
                    disk_filename = f"{folder_db_id}_{stem}_{counter}{ext}"
                    disk_path = templates_dir / disk_filename
                    counter += 1
                shutil.copy2(src_file, disk_path)
                cur.execute(
                    "UPDATE project_template_files SET disk_path = %s WHERE id = %s",
                    (str(disk_path), file_id)
                )
                file_count += 1
                total_bytes += src_file.stat().st_size
            except Exception as e:
                print(f"  ERR su {src_file}: {e}", file=sys.stderr)

    conn.commit()
    print(f"  file copiati: {file_count} ({total_bytes/1024:.1f} KB totali)")

    # 5) Riepilogo
    print("[4/4] Verifica finale...")
    cur.execute("SELECT COUNT(*) FROM project_template_folders")
    n_folders = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM project_template_files")
    n_files = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM project_template_folders WHERE parent_id IS NULL")
    n_roots = cur.fetchone()[0]

    print(f"  Folders in DB:  {n_folders} ({n_roots} root)")
    print(f"  Files in DB:    {n_files}")
    print(f"  File su disco:  {sum(1 for _ in templates_dir.iterdir())}")
    print()
    print("Import completato.")

    cur.close()
    conn.close()


if __name__ == "__main__":
    main()
