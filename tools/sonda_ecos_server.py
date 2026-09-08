#!/usr/bin/env python3
"""Sonda EcosAgile eseguita SUL SERVER di produzione, con le credenziali che il server ha gia'.

Gemella di sonda_ecos.py, ma senza chiedere la password a nessuno: carica via scp un piccolo
script PowerShell in C:\\ATEC_PM\\Updates\\, lo lancia via ssh e lo cancella. Lo script legge
sul server appsettings.Secrets.json (connection string cifrata DPAPI) -> res_settings
(utente, ClientID, password Ecos cifrata DPAPI) -> TokenGet -> le chiamate chieste. Password,
connection string e token restano nella memoria della sessione remota: non arrivano qui, non
finiscono a log, non stanno sulla riga di comando (che su Windows e' troppo corta per
-EncodedCommand: e' il motivo del file).

Modi:
  --calibra   chiamata a corpo VUOTO: dice se l'API esiste (-99 = no), se e' negata (-2, col
              ServiceID da chiedere a SoftAgile) o se siamo autorizzati (errore di validazione).
              E' l'unico modo ammesso di nominare una Post*: niente viene creato.
  (default)   lettura con filtro, poche righe: CODE, RECORDCOUNT, nomi dei campi; con --valori
              anche la prima riga senza i campi con nominativi.

Esempi:
  python tools/sonda_ecos_server.py PeopleStampGetAll --righe 1
  python tools/sonda_ecos_server.py PeopleAbsenceRequestGetAll --filtro "UpdateDate=>='2026-09-01 00:00:00'" --valori
  python tools/sonda_ecos_server.py Timesheet2GetAll PeopleAbsenceRequestPost --calibra

Serve la chiave SSH ~/.ssh/atec_vps (la stessa di segnalazioni.py). Le chiamate all'API di
produzione di Ecos si fanno su richiesta di Diego: sempre un filtro, poche righe.
"""
import argparse
import io
import os
import re
import subprocess
import sys
import tempfile

SERVER = "atec@192.168.2.150"
SSH_KEY = os.path.expanduser(r"~\.ssh\atec_vps")
REMOTO = "C:/ATEC_PM/Updates/sonda_ecos_tmp.ps1"
REMOTO_WIN = r"C:\ATEC_PM\Updates\sonda_ecos_tmp.ps1"
SENSIBILI = ("NameComplete", "NameFirst", "NameLast", "Name", "ShortName", "ApproveNameComplete",
             "Email", "EMail", "EmailAddress", "Picture", "PictureFS", "StampPicture")

PROLOGO = r'''
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Security
function Decifra($b64) {
  $b = [Convert]::FromBase64String($b64)
  try { return [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($b, $null, 'LocalMachine')) }
  catch { return [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($b, $null, 'CurrentUser')) }
}
$secretsPath = 'C:\ATEC_PM\Server\appsettings.Secrets.json'
if (-not (Test-Path $secretsPath)) { "MANCA $secretsPath"; exit 1 }
$sec = [IO.File]::ReadAllText($secretsPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
try { $cs = Decifra $sec.'ConnectionStrings:Default' } catch { "connection string non decifrabile da questa sessione"; exit 2 }
$db = @{}
foreach ($kv in ($cs -split ';')) { $i = $kv.IndexOf('='); if ($i -gt 0) { $db[$kv.Substring(0,$i).Trim().ToLower()] = $kv.Substring($i+1).Trim() } }
$cs = $null
$dbUser = $db['uid']; if (-not $dbUser) { $dbUser = $db['user id'] }; if (-not $dbUser) { $dbUser = $db['user'] }; if (-not $dbUser) { $dbUser = $db['username'] }
$dbPass = $db['pwd']; if (-not $dbPass) { $dbPass = $db['password'] }
$dbHost = $db['server']; if (-not $dbHost) { $dbHost = $db['host'] }; if (-not $dbHost) { $dbHost = 'localhost' }
$mysql = 'C:\Program Files\MySQL\MySQL Server 8.4\bin\mysql.exe'
$env:MYSQL_PWD = $dbPass
$righe = cmd /c "`"$mysql`" -h $dbHost -u $dbUser --default-character-set=utf8mb4 -N -B -e `"SELECT * FROM atec_pm.res_settings`" 2>nul"
$env:MYSQL_PWD = $null; $dbPass = $null; $db = $null
$cfg = @{}
foreach ($r in $righe) { $p = "$r" -split "`t", 2; if ($p.Count -eq 2) { $cfg[$p[0]] = $p[1] } }
foreach ($k in 'ecos.baseurl','ecos.userid','ecos.clientid','ecos.password') { if (-not $cfg[$k]) { "MANCA $k (righe lette: $(@($righe).Count))"; exit 1 } }
try { $pw = Decifra $cfg['ecos.password'] } catch { "password Ecos non decifrabile da questa sessione"; exit 2 }
$base = $cfg['ecos.baseurl']
"utente: " + $cfg['ecos.userid'] + "  clientid: " + $cfg['ecos.clientid'] + "  base: " + $base
function Chiama($uri, $body) {
  $resp = Invoke-WebRequest -Method Post -Uri $uri -Body $body -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing -TimeoutSec 60
  $c = $resp.Content; if ($null -eq $c) { $c = '' }; return "$c"
}
try { $tj = Chiama ($base + 'TokenGet') @{ Userid = $cfg['ecos.userid']; Password = $pw; ClientID = $cfg['ecos.clientid'] } }
catch { "TokenGet HTTP KO: " + $_.Exception.Message; exit 3 }
$pw = $null
$t = $tj | ConvertFrom-Json
$te = $t.ECOSAGILE_TABLE_DATA.ECOSAGILE_ERROR_MESSAGE
if ($te.CODE -ne 'OK') { "TokenGet: CODE=$($te.CODE) ERROR_CODE=$($te.ERROR_CODE) $($te.MESSAGE)"; exit 3 }
$tok = $t.ECOSAGILE_TABLE_DATA.ECOSAGILE_DATA.ECOSAGILE_DATA_ROW.AuthToken
"token ottenuto (non stampato)"
$sensibili = @(SENSIBILI)
function Prova($api, $body, $righe, $valori) {
  $uri = $base + $api + '&DF=1&PageNumber=1&RowsPerPage=' + $righe + '&AppCode=ATEC_PM_sonda&AuthToken=' + [Uri]::EscapeDataString($tok)
  "### $api"
  try { $c = Chiama $uri $body } catch { "  HTTP KO: " + $_.Exception.Message; return }
  if ($c.Length -eq 0) { "  risposta VUOTA (nessun JSON)"; return }
  try { $j = $c | ConvertFrom-Json } catch { "  NON JSON: " + $c.Substring(0, [Math]::Min(150, $c.Length)); return }
  $e = $j.ECOSAGILE_TABLE_DATA.ECOSAGILE_ERROR_MESSAGE
  $msg = ("" + $e.MESSAGE) -replace "[`r`n]+", ' '
  if ($msg.Length -gt 240) { $msg = $msg.Substring(0, 240) }
  "  CODE=$($e.CODE) ERROR_CODE=$($e.ERROR_CODE) RECORDCOUNT=$($e.RECORDCOUNT) LASTPAGE=$($e.LASTPAGE) MESSAGE=$msg"
  $d = $j.ECOSAGILE_TABLE_DATA.ECOSAGILE_DATA
  if (-not ($d -is [PSCustomObject]) -or -not $d.ECOSAGILE_DATA_ROW) { return }
  $rows = @($d.ECOSAGILE_DATA_ROW)
  $nomi = $rows[0].PSObject.Properties.Name
  "  righe in pagina: $($rows.Count)"
  "  CAMPI ($($nomi.Count)): " + ($nomi -join ', ')
  if ($valori) {
    $riga = @{}
    foreach ($n in $nomi) { if ($sensibili -notcontains $n) { $riga[$n] = $rows[0].$n } }
    "  ESEMPIO (senza nominativi): " + ($riga | ConvertTo-Json -Compress)
  }
}
'''


def ps_stringa(s: str) -> str:
    return "'" + s.replace("'", "''") + "'"


def corpo(args) -> str:
    righe = []
    filtro = "@{ " + "; ".join(
        f"{ps_stringa(k)} = {ps_stringa(v)}" for k, v in filtri(args.filtro).items()) + " }" \
        if args.filtro else "@{}"
    for api in args.api:
        if args.calibra:
            righe.append(f"Prova {ps_stringa(api)} '' 1 $false")
        else:
            righe.append(f"Prova {ps_stringa(api)} {filtro} {int(args.righe)} ${'true' if args.valori else 'false'}")
    righe.append("$tok = $null")
    return "\n".join(righe) + "\n"


def filtri(voci):
    out = {}
    for v in voci or []:
        if "=" not in v:
            raise SystemExit(f"Filtro non valido: {v!r} (forma attesa: Campo=<operatore><valore>)")
        campo, valore = v.split("=", 1)
        out[campo.strip()] = valore
    return out


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("api", nargs="+", help="uno o piu' ApiName")
    p.add_argument("--calibra", action="store_true", help="corpo vuoto: esiste? negata? autorizzata? (ammesso sulle Post)")
    p.add_argument("--filtro", action="append", metavar="Campo=<op><valore>", help="filtro nel body, ripetibile")
    p.add_argument("--righe", type=int, default=3, help="righe per pagina (default 3)")
    p.add_argument("--valori", action="store_true", help="stampa la prima riga (senza nominativi)")
    a = p.parse_args()

    for nome in a.api:
        # Le API di scrittura si riconoscono dal verbo nel nome (Post/Put/Set/Delete); le
        # letture non hanno sempre «Get» dentro (es. PeopleAbsenceRequestRefineWorkAll).
        if not a.calibra and re.search(r"Post|Put|Set|Delete", nome):
            raise SystemExit(f"{nome}: senza --calibra si chiamano solo API di lettura.")
    if not a.calibra and not a.filtro:
        print("(nessun filtro: poche righe per non pesare sul limitatore di Ecos)")

    script = PROLOGO.replace("SENSIBILI", ", ".join(ps_stringa(s) for s in SENSIBILI)) + corpo(a)
    fd, locale = tempfile.mkstemp(suffix=".ps1")
    os.close(fd)
    try:
        # BOM UTF-8: senza, PowerShell 5.1 legge il file come ANSI.
        io.open(locale, "w", encoding="utf-8-sig", newline="\r\n").write(script)
        opts = ["-i", SSH_KEY, "-o", "StrictHostKeyChecking=no", "-o", "LogLevel=ERROR"]
        up = subprocess.run(["scp"] + opts + [locale, f"{SERVER}:{REMOTO}"], capture_output=True, timeout=60)
        if up.returncode != 0:
            raise SystemExit("scp fallita: " + up.stderr.decode("utf-8", "replace")[:400])
        try:
            r = subprocess.run(["ssh"] + opts + [SERVER, "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                                "-File", REMOTO_WIN], capture_output=True, timeout=300)
        finally:
            subprocess.run(["ssh"] + opts + [SERVER, "cmd", "/c", "del", REMOTO_WIN], capture_output=True, timeout=60)
    finally:
        os.unlink(locale)

    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    print(r.stdout.decode("utf-8", "replace").rstrip())
    err = r.stderr.decode("utf-8", "replace").strip()
    if err:
        print("STDERR:", err[:1200])
    sys.exit(r.returncode)


if __name__ == "__main__":
    main()
