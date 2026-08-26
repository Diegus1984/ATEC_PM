using System.Data;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Fornitore per nome: serve dove un testo libero deve diventare una FK all'anagrafica.
///
/// <para>Il caso è quello della #119: nella DDP Officina il fornitore è testo
/// (<c>supplier_name</c>, copiato dal Codex), nella DDP Commerciale è
/// <c>supplier_id</c> → <c>suppliers</c>. Quando un componente passa da una griglia
/// all'altra il nome va agganciato, e va agganciato allo stesso modo sia dalla migrazione
/// storica (M105) sia dall'import di tutti i giorni: da qui la regola in un posto solo.</para>
///
/// <para>Prima il match esatto, poi quello normalizzato (senza maiuscole, punti e spazi):
/// «SMC Italia S.p.A» del Codex e «SMC Italia S.p.A.» dell'anagrafica sono lo stesso
/// fornitore. Se non si trova si torna <c>null</c> — un campo da compilare a mano è meglio
/// di un aggancio indovinato, che nessuno andrebbe più a ricontrollare.</para>
/// </summary>
public static class FornitoreLookup
{
    public static int? TrovaPerNome(IDbConnection c, string? nome, IDbTransaction? tx = null)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        return c.ExecuteScalar<int?>(@"
            SELECT id FROM suppliers
            WHERE company_name = @Nome
               OR REPLACE(REPLACE(LOWER(TRIM(company_name)), '.', ''), ' ', '')
                = REPLACE(REPLACE(LOWER(TRIM(@Nome)), '.', ''), ' ', '')
            ORDER BY (company_name = @Nome) DESC, id
            LIMIT 1", new { Nome = nome }, tx);
    }
}
