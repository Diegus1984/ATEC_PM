using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v82 — LE QUATTRO SEZIONI DI COMMESSA CHE NON AVEVANO UN PERMESSO (segnalazione #63).
//
// Chat, DDP Commerciali, DDP Officina e Documenti erano le uniche sezioni della commessa
// senza `featureKey`: per la regola «feature non registrata = accesso libero» (valida sia
// in FeatureAccessService sia in lib/auth/permissions.ts) erano visibili a chiunque, e i
// loro endpoint avevano il solo [Authorize]. In pratica qualunque utente autenticato
// leggeva e scriveva le DDP di TUTTE le commesse e caricava file in qualunque commessa.
//
// 🔑 REGISTRARE NON È RESTRINGERE, ed è voluto: nascono a **livello 0**, cioè esattamente
// ciò che valeva prima (tutti). Cambia solo che da oggi esistono nella pagina «Permessi»
// e sono configurabili. La stretta vera arriva con i profili (Fase 2 del piano), dove le
// liste bianche decidono chi legge e chi scrive.
//
// ⚠️ Per i ruoli a lista bianca (access_mode='GRANTS') una feature nuova, anche a livello
// 0, è NEGATA finché non entra nella loro lista: è l'opposto del fallback dei livelli.
// Oggi non ci sono ruoli GRANTS attivi, ma il reparto Contabilità ha lo stesso
// comportamento via GetContabilitaGrants — la sua lista resta di 4 funzioni e non
// comprendeva comunque Chat/DDP/Documenti, quindi per lui non cambia nulla.
public sealed class M082_ChiaviCommessaLivello0 : IMigrazione
{
    public int Versione => 82;

    public string Descrizione => "project.chat/ddp_commerciale/ddp_officina/documenti a livello 0 + pulizia auth_role_features orfane";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
            ('project.chat',           'Commessa — Chat',           'project', 0, 'HIDDEN'),
            ('project.ddp_commerciale','Commessa — DDP Commerciali','project', 0, 'HIDDEN'),
            ('project.ddp_officina',   'Commessa — DDP Officina',   'project', 0, 'HIDDEN'),
            ('project.documenti',      'Commessa — Documenti',      'project', 0, 'HIDDEN')");

        // Bonifica: il ruolo AMM è stato tolto da auth_levels con la v66, ma le sue
        // concessioni erano rimaste qui a fare da zavorra (4 righe che non descrivono più
        // nessun ruolo esistente). La Contabilità è gestita per REPARTO dal 04/08/2026.
        int orfane = c.Execute(@"DELETE FROM auth_role_features
            WHERE role_name NOT IN (SELECT role_name FROM auth_levels)");

        log.LogInformation(
            "[Migration v82] Le 4 sezioni di commessa senza permesso sono registrate a livello 0 (nessuno perde accesso). Concessioni orfane rimosse: {Orfane}.",
            orfane);
    }
}
