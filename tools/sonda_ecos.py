#!/usr/bin/env python3
"""Sonda di SOLA LETTURA verso le API EcosAgile («eTime»).

Serve a scoprire, senza scrivere codice C#, come risponde davvero un'API sul nostro tenant:
se esiste, se l'utente ha il diritto, quali campi torna, che forma hanno i valori.
Manuale: docs/tools/TOOLS.md e docs/guide/ECOS-API-MANUALE.md (§12).

Regole di sicurezza incorporate:
  - credenziali SOLO da variabili d'ambiente (ECOS_USERID, ECOS_PASSWORD, ECOS_CLIENTID,
    ECOS_BASEURL opzionale); non si salvano, non si stampano, il token non finisce a video;
  - si chiamano solo API il cui nome contiene "Get"; le "Post" sono ammesse SOLO con
    --calibra, cioè a corpo vuoto: si legge la forma dell'errore (-99 = non esiste,
    -2 = negata, validazione = esiste e siamo autorizzati) e non si crea nulla;
  - i nominativi (NameComplete, NameFirst, ...) sono nascosti salvo --mostra-nomi;
  - poche righe per pagina e sempre un filtro: il limitatore di Ecos blocca chi esagera.

Esempi:
  python tools/sonda_ecos.py PeopleStampGetAll --righe 1
  python tools/sonda_ecos.py PeopleAbsenceRequestGetAll --filtro "UpdateDate=>='2026-08-01 00:00:00'" --righe 3
  python tools/sonda_ecos.py Timesheet2GetAll PeopleExpressLightGetAll --calibra
  python tools/sonda_ecos.py PeopleStampGetAll --filtro "YearMonth=='202608'" --conta
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE_PREDEFINITA = "https://ha.ecosagile.com/dd/api.pm?ApiName="
SENSIBILI = {
    "NameComplete", "NameFirst", "NameLast", "Name", "ShortName", "ApproveNameComplete",
    "Email", "EMail", "EmailAddress", "Picture", "PictureFS", "StampPicture",
}
MAX_PAGINE = 200


def credenziali():
    userid = os.environ.get("ECOS_USERID", "").strip()
    password = os.environ.get("ECOS_PASSWORD", "")
    clientid = os.environ.get("ECOS_CLIENTID", "").strip()
    base = os.environ.get("ECOS_BASEURL", "").strip() or BASE_PREDEFINITA
    if not (userid and password and clientid):
        raise SystemExit(
            "Mancano le credenziali: impostare ECOS_USERID, ECOS_PASSWORD, ECOS_CLIENTID "
            "(PowerShell: $env:ECOS_USERID=\"...\").")
    if not base.endswith("ApiName="):
        base = base.rstrip("?&") + ("&" if "?" in base else "?") + "ApiName="
    return base, userid, password, clientid


def chiama(url, campi):
    dati = urllib.parse.urlencode(campi).encode("utf-8")
    req = urllib.request.Request(url, data=dati, method="POST")
    req.add_header("Content-Type", "application/x-www-form-urlencoded")
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.status, r.read().decode("utf-8", "replace")


def token(base, userid, password, clientid):
    _, body = chiama(base + "TokenGet", {"Userid": userid, "Password": password, "ClientID": clientid})
    d = json.loads(body)
    err = d["ECOSAGILE_TABLE_DATA"].get("ECOSAGILE_ERROR_MESSAGE", {})
    if err.get("CODE") != "OK":
        raise SystemExit(f"TokenGet fallita: ERROR_CODE={err.get('ERROR_CODE')} {err.get('MESSAGE')}")
    return d["ECOSAGILE_TABLE_DATA"]["ECOSAGILE_DATA"]["ECOSAGILE_DATA_ROW"]["AuthToken"]


def filtri_da_argomenti(voci):
    """'Campo=<op><valore>' -> dizionario. Il primo '=' separa il campo, il resto è il valore."""
    out = {}
    for v in voci or []:
        if "=" not in v:
            raise SystemExit(f"Filtro non valido: {v!r} (forma attesa: Campo=<operatore><valore>)")
        campo, valore = v.split("=", 1)
        out[campo.strip()] = valore
    return out


def normalizza_righe(tabella):
    dati = tabella.get("ECOSAGILE_DATA") or {}
    if not isinstance(dati, dict):
        return []
    righe = dati.get("ECOSAGILE_DATA_ROW") or []
    if isinstance(righe, dict):
        righe = [righe]
    return righe


def una_pagina(base, tok, api, filtro, righe, pagina, campi):
    url = (f"{base}{api}&PageNumber={pagina}&RowsPerPage={righe}&DF=1"
           f"&AppCode=ATEC_PM_sonda&AuthToken={urllib.parse.quote(tok, safe='')}")
    if campi:
        url += "&ResultFields=" + urllib.parse.quote(campi, safe=",")
    try:
        status, body = chiama(url, filtro)
    except urllib.error.HTTPError as ex:
        return None, f"HTTP {ex.code}: {ex.read()[:200]!r}"
    except Exception as ex:  # rete, timeout
        return None, f"HTTP KO: {ex}"
    try:
        return json.loads(body), None
    except json.JSONDecodeError:
        return None, f"risposta non JSON (HTTP {status}): {body[:200]!r}"


def sonda(base, tok, api, filtro, righe, pagina, campi, mostra_nomi, grezzo, conta):
    print(f"\n### {api}")
    if not filtro and not conta:
        print("  (nessun filtro: chiedo poche righe per non pesare sul limitatore di Ecos)")

    d, errore = una_pagina(base, tok, api, filtro, righe, pagina, campi)
    if errore:
        print("  " + errore)
        return
    t = d.get("ECOSAGILE_TABLE_DATA", {})
    err = t.get("ECOSAGILE_ERROR_MESSAGE", {})
    print(f"  CODE={err.get('CODE')} ERROR_CODE={err.get('ERROR_CODE')} MESSAGE={str(err.get('MESSAGE'))[:200]}")
    if err.get("CODE") != "OK":
        return
    print(f"  RECORDCOUNT={err.get('RECORDCOUNT')} LASTPAGE={err.get('LASTPAGE')} "
          f"REACHEDMAXRECORD={err.get('REACHEDMAXRECORD')} MAXRECORD={err.get('MAXRECORD')}")

    righe_dati = normalizza_righe(t)
    if conta:
        totale = len(righe_dati)
        p = pagina
        while str(err.get("LASTPAGE", "")).lower() != "true" and righe_dati and p < pagina + MAX_PAGINE:
            p += 1
            d, errore = una_pagina(base, tok, api, filtro, righe, p, campi)
            if errore:
                print(f"  pagina {p}: {errore}")
                break
            t = d.get("ECOSAGILE_TABLE_DATA", {})
            err = t.get("ECOSAGILE_ERROR_MESSAGE", {})
            if err.get("CODE") != "OK":
                print(f"  pagina {p}: ERROR_CODE={err.get('ERROR_CODE')} {err.get('MESSAGE')}")
                break
            righe_dati = normalizza_righe(t)
            totale += len(righe_dati)
        print(f"  TOTALE righe: {totale} (pagine lette: {p - pagina + 1})")
        return

    print(f"  righe in pagina: {len(righe_dati)}")
    if not righe_dati:
        return
    print(f"  CAMPI ({len(righe_dati[0])}): {', '.join(righe_dati[0].keys())}")
    for i, riga in enumerate(righe_dati[: (len(righe_dati) if grezzo else 1)], 1):
        esempio = riga if mostra_nomi else {k: v for k, v in riga.items() if k not in SENSIBILI}
        etichetta = "ESEMPIO" if not mostra_nomi else "RIGA"
        print(f"  {etichetta} {i}:")
        print("   " + json.dumps(esempio, ensure_ascii=False))


def calibra(base, tok, api):
    """Chiamata a corpo VUOTO: si legge solo la forma dell'errore. Non crea nulla."""
    print(f"\n### {api} (calibrazione a vuoto)")
    d, errore = una_pagina(base, tok, api, {}, 1, 1, None)
    if errore:
        print("  " + errore)
        return
    err = d.get("ECOSAGILE_TABLE_DATA", {}).get("ECOSAGILE_ERROR_MESSAGE", {})
    codice = str(err.get("ERROR_CODE"))
    msg = str(err.get("MESSAGE"))
    if err.get("CODE") == "OK":
        verdetto = "ESISTE, autorizzata (ha risposto OK a vuoto: è una GET)"
    elif "Wrong API name" in msg or codice == "-99" and "name" in msg.lower():
        verdetto = "NON ESISTE"
    elif codice == "-2":
        verdetto = "ESISTE ma NEGATA (manca il diritto: vedi ServiceID nel messaggio)"
    else:
        verdetto = "ESISTE e siamo autorizzati (errore di validazione, niente creato)"
    print(f"  CODE={err.get('CODE')} ERROR_CODE={codice} MESSAGE={msg[:220]}")
    print(f"  → {verdetto}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("api", nargs="+", help="uno o più ApiName (es. PeopleStampGetAll)")
    p.add_argument("--filtro", action="append", metavar="Campo=<op><valore>",
                   help="filtro nel body, ripetibile (es. \"UpdateDate=>='2026-08-01 00:00:00'\")")
    p.add_argument("--righe", type=int, default=3, help="righe per pagina (default 3)")
    p.add_argument("--pagina", type=int, default=1, help="numero di pagina (default 1)")
    p.add_argument("--campi", metavar="A,B,C", help="ResultFields (release Ecos >= 6.10)")
    p.add_argument("--mostra-nomi", action="store_true", help="non nascondere i nominativi")
    p.add_argument("--grezzo", action="store_true", help="stampa tutte le righe della pagina, non solo la prima")
    p.add_argument("--conta", action="store_true", help="scorre tutte le pagine e conta le righe (nessun dato a video)")
    p.add_argument("--calibra", action="store_true",
                   help="chiamata a corpo vuoto per capire se l'API esiste e se abbiamo il diritto (ammessa anche sulle Post)")
    a = p.parse_args()

    for nome in a.api:
        # Le API di scrittura si riconoscono dal verbo nel nome (Post/Put/Set/Delete); le
        # letture non hanno sempre «Get» dentro (es. PeopleAbsenceRequestRefineWorkAll).
        if not a.calibra and re.search(r"Post|Put|Set|Delete", nome):
            raise SystemExit(
                f"{nome}: la sonda chiama solo API di lettura. Per le Post usa --calibra "
                "(corpo vuoto, niente scritto).")

    base, userid, password, clientid = credenziali()
    tok = token(base, userid, password, clientid)
    print("token ottenuto (non lo stampo)")

    filtro = filtri_da_argomenti(a.filtro)
    for nome in a.api:
        if a.calibra:
            calibra(base, tok, nome)
        else:
            sonda(base, tok, nome, filtro, a.righe, a.pagina, a.campi, a.mostra_nomi, a.grezzo, a.conta)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(130)
