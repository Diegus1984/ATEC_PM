using ATEC.PM.Server.Services;
using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Le credenziali Ecos scritte dall'applicazione, come nel dialogo «Configurazione
/// Credenziali» del programma «Timbrature».
///
/// <para>Le proprietà da difendere: quelle messe dalla pagina <b>vincono</b> su quelle nel
/// file del server (altrimenti cambiarle dalla pagina non avrebbe effetto), la password è
/// <b>write-only</b> (non torna indietro e salvando a vuoto non si cancella), e senza
/// database si ripiega sull'appsettings invece di dichiarare il modulo non configurato.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class CredenzialiEcosTests
{
    private readonly SchemaCondiviso _schema;

    public CredenzialiEcosTests(SchemaCondiviso schema)
    {
        _schema = schema;
        using MySqlConnection c = _schema.Apri();
        c.Execute("DELETE FROM res_settings WHERE `key` LIKE 'ecos.%'");
    }

    private static IConfiguration ConfigConCredenziali() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ecos:BaseUrl"] = "https://vecchio.esempio/api?ApiName=",
            ["Ecos:UserId"] = "utente-da-file",
            ["Ecos:Password"] = "password-da-file",
            ["Ecos:ClientId"] = "client-da-file",
        }).Build();

    private EcosClient Client(IConfiguration config) =>
        new(config, NullLogger<EcosClient>.Instance,
            new ResourcesDbService(_schema.Servizio()));

    [FactRichiedeMySql]
    public void Senza_niente_nel_database_valgono_quelle_del_file()
    {
        HrEcosSettingsDto lette = Client(ConfigConCredenziali()).LeggiCredenziali();

        Assert.Equal("utente-da-file", lette.UserId);
        Assert.Equal("client-da-file", lette.ClientId);
        Assert.True(lette.HasPassword);
        Assert.True(lette.Configured);
        Assert.Equal("APPSETTINGS", lette.Source);
    }

    [FactRichiedeMySql]
    public void Quelle_scritte_dalla_pagina_vincono_su_quelle_del_file()
    {
        EcosClient client = Client(ConfigConCredenziali());

        client.SalvaCredenziali(new HrEcosSettingsDto
        {
            BaseUrl = "https://ha.ecosagile.com/dd/api.pm?ApiName=",
            UserId = "utente-nuovo",
            ClientId = "client-nuovo",
            Password = "password-nuova",
        });

        HrEcosSettingsDto lette = client.LeggiCredenziali();
        Assert.Equal("utente-nuovo", lette.UserId);
        Assert.Equal("client-nuovo", lette.ClientId);
        Assert.Equal("DATABASE", lette.Source);
        Assert.True(lette.Configured);

        // La password non esce mai: esce solo il fatto che ci sia.
        Assert.Null(lette.Password);
        Assert.True(lette.HasPassword);
    }

    [FactRichiedeMySql]
    public void La_password_e_write_only_salvare_a_vuoto_non_la_cancella()
    {
        EcosClient client = Client(new ConfigurationBuilder().Build());

        client.SalvaCredenziali(new HrEcosSettingsDto
        {
            UserId = "utente", ClientId = "client", Password = "segreta",
        });

        // Secondo salvataggio senza toccare la password: cambia solo l'utente.
        client.SalvaCredenziali(new HrEcosSettingsDto
        {
            UserId = "utente-corretto", ClientId = "client", Password = null,
        });

        HrEcosSettingsDto lette = client.LeggiCredenziali();
        Assert.Equal("utente-corretto", lette.UserId);
        Assert.True(lette.HasPassword);
        Assert.True(lette.Configured);
    }

    [FactRichiedeMySql]
    public void La_password_sul_database_e_cifrata()
    {
        Client(new ConfigurationBuilder().Build()).SalvaCredenziali(new HrEcosSettingsDto
        {
            UserId = "utente", ClientId = "client", Password = "segretissima",
        });

        using MySqlConnection c = _schema.Apri();
        string? salvata = c.ExecuteScalar<string>(
            "SELECT `value` FROM res_settings WHERE `key` = 'ecos.password'");

        Assert.False(string.IsNullOrEmpty(salvata));
        Assert.DoesNotContain("segretissima", salvata);
    }

    [FactRichiedeMySql]
    public void Senza_credenziali_da_nessuna_parte_il_modulo_resta_a_riposo()
    {
        EcosClient client = Client(new ConfigurationBuilder().Build());

        Assert.False(client.Configured);
        Assert.False(client.LeggiCredenziali().HasPassword);

        // Il messaggio dice DOVE si mettono: chi lo legge non deve indovinare.
        EcosApiException errore = Assert.ThrowsAsync<EcosApiException>(() => client.TokenAsync()).Result;
        Assert.Contains("Credenziali Ecos non configurate", errore.Message);
    }
}
