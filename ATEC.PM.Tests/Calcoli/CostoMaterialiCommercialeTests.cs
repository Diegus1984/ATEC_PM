using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Segnalazione #119 — quanto costa il materiale commerciale di una commessa che ha dentro un
/// gruppo Codex importato.
///
/// <para>L'intestazione del gruppo in <c>bom_items</c> è una riga vera ma è solo un'etichetta:
/// i soldi stanno sui componenti. 🪤 È il contrario della griglia, che arrotola il costo sul
/// padre e mostra i figli a zero — chi arriva qui con in mente la griglia legge gli assert
/// rovesciati.</para>
///
/// <para>Si rompe in silenzio: nessun errore, solo un materiale più alto del vero. Fino al
/// 28/08/2026 la card «Costi» del dettaglio commessa ricopiava la somma <b>senza</b> la dedup
/// dei padri e diceva un numero diverso dal Bilancio sulla stessa commessa; ora chiama
/// <see cref="ProjectEconomics.GetCommercialMaterialCost"/>, che è la funzione eseguita qui.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class CostoMaterialiCommercialeTests
{
    private readonly SchemaCondiviso _schema;

    public CostoMaterialiCommercialeTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    /// <summary>
    /// Il costo del gruppo lo fanno i componenti. Se l'intestazione si sommasse, i suoi
    /// 500,00 € sarebbero gli stessi soldi contati una seconda volta.
    /// </summary>
    [FactRichiedeMySql]
    public void L_intestazione_del_gruppo_non_si_somma_ai_suoi_componenti()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);

        int gruppo = Riga(c, commessa, "511250826.001", quantita: 1, costo: 500m);
        Riga(c, commessa, "201250826.001", quantita: 2, costo: 30m, padre: gruppo);
        Riga(c, commessa, "211250826.002", quantita: 1, costo: 40m, padre: gruppo);

        Assert.Equal(100m, ProjectEconomics.GetCommercialMaterialCost(c, commessa, Esclusi(c)));
    }

    /// <summary>
    /// La controprova: senza gruppi importati la dedup non deve togliere niente. È la maggior
    /// parte delle commesse, ed è quello che si romperebbe scrivendo il filtro al contrario.
    /// </summary>
    [FactRichiedeMySql]
    public void Senza_gruppi_importati_si_contano_tutte_le_righe()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);

        Riga(c, commessa, "201250826.003", quantita: 3, costo: 10m);
        Riga(c, commessa, "301250826.004", quantita: 5, costo: 2m);

        Assert.Equal(40m, ProjectEconomics.GetCommercialMaterialCost(c, commessa, Esclusi(c)));
    }

    // ═══════════════════════════════════════════════════════════════
    // SEMINA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Gli stati «esclusi da totale» (A9) come li legge il Bilancio; mai un array vuoto,
    /// che in Dapper vorrebbe dire «nessuna riga».</summary>
    private static string[] Esclusi(MySqlConnection c)
    {
        string[] stati = ComposizioneDdp.StatiEsclusi(c).ToArray();
        return stati.Length > 0 ? stati : [""];
    }

    private static int Commessa(MySqlConnection c)
    {
        int cliente = c.ExecuteScalar<int?>("SELECT id FROM customers ORDER BY id LIMIT 1")
            ?? Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int pm = c.ExecuteScalar<int?>("SELECT id FROM employees ORDER BY id DESC LIMIT 1")
            ?? Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        return Inserisci(c,
            @"INSERT INTO projects (code, title, customer_id, pm_id, status)
              VALUES ('C20260828.119', 'Commessa di prova', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = pm });
    }

    /// <summary>Riga di DDP Commerciale; con <paramref name="padre"/> è un componente del gruppo.</summary>
    private static int Riga(
        MySqlConnection c, int commessa, string partNumber, decimal quantita, decimal costo,
        int? padre = null, string stato = "DA") =>
        Inserisci(c, @"
            INSERT INTO bom_items
                (project_id, part_number, description, quantity, unit_cost, item_status,
                 parent_bom_item_id)
            VALUES (@P, @Codice, 'Componente', @Q, @C, @S, @Padre)",
            new { P = commessa, Codice = partNumber, Q = quantita, C = costo, S = stato, Padre = padre });

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
