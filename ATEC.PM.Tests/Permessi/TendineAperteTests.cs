using System.Reflection;
using ATEC.PM.Server.Controllers;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Lo stesso taglio delle commesse, applicato agli altri quattro elenchi che nascevano per una
/// pagina ed erano finiti a fare da tendina in mezzo gestionale (20/08/2026).
///
/// <para>La malattia era sempre quella: un endpoint aperto «perché serve alle combo», che però
/// spediva a ogni autenticato molto più di quanto la combo mostrasse — costi unitari, partite
/// IVA, costi orari di reparto, email e utenze di login. La cura è sempre quella: <b>l'elenco
/// pieno dietro la chiave della sua pagina, una tendina magra aperta a tutti</b>.</para>
///
/// <para>Questi test non provano che il codice funziona: provano che <b>non è tornato indietro</b>.
/// Il modo naturale di riaprire questi buchi non è un attacco, è una gentilezza — «aggiungo un
/// campo alla tendina, che tanto qui serve».</para>
/// </summary>
public class TendineAperteTests
{
    // ── Fornitori ────────────────────────────────────────────────────────────────

    [Fact]
    public void La_tendina_fornitori_non_porta_l_anagrafica_fiscale()
    {
        Gate.SoloQuestiCampi(
            typeof(SupplierLookupItem),
            new[] { "Id", "CompanyName", "ContactName", "IsActive" },
            "GET /api/suppliers/lookup");

        // Detto esplicito, perché è il punto della faccenda.
        var campi = Gate.CampiDi(typeof(SupplierLookupItem));
        foreach (string vietato in new[] { "VatNumber", "FiscalCode", "Email", "Phone" })
            Assert.DoesNotContain(vietato, campi);
    }

    [Fact]
    public void L_anagrafica_fornitori_sta_dietro_nav_fornitori()
    {
        foreach (string metodo in new[] { "GetAll", "GetById" })
            Assert.Contains("nav.fornitori", Gate.ChiaviDi(typeof(SuppliersController), metodo));

        // Aperta di proposito: il fornitore si sceglie dalla riga di una DDP.
        Assert.Empty(Gate.ChiaviDi(typeof(SuppliersController), "GetLookup"));
    }

    // ── Reparti ──────────────────────────────────────────────────────────────────

    [Fact]
    public void La_tendina_reparti_non_porta_il_costo_orario()
    {
        Gate.SoloQuestiCampi(
            typeof(DepartmentLookupDto),
            new[] { "Id", "Code", "Name", "SortOrder", "IsActive" },
            "GET /api/departments/lookup");

        var campi = Gate.CampiDi(typeof(DepartmentLookupDto));
        Assert.DoesNotContain("HourlyCost", campi);
        Assert.DoesNotContain("DefaultMarkup", campi);
    }

    [Fact]
    public void I_reparti_si_leggono_e_si_scrivono_solo_dalla_configurazione_sezioni()
    {
        // 🪤 Le tre scritture erano APERTE: chiunque fosse autenticato poteva creare,
        // rinominare o cancellare un reparto — e col reparto il suo costo orario.
        foreach (string metodo in new[] { "GetAll", "GetById", "Create", "Update", "UpdateField", "Delete" })
            Assert.Contains("nav.config_sezioni", Gate.ChiaviDi(typeof(DepartmentsController), metodo));

        Assert.Empty(Gate.ChiaviDi(typeof(DepartmentsController), "GetLookup"));
    }

    // ── Dipendenti ───────────────────────────────────────────────────────────────

    [Fact]
    public void L_anagrafica_dipendenti_sta_dietro_nav_utenti()
    {
        // Gli stessi campi di GET /api/users, che quella chiave ce l'ha da sempre: due strade
        // allo stesso dato con permessi diversi sono la definizione del buco.
        foreach (string metodo in new[] { "GetAll", "GetById" })
            Assert.Contains("nav.utenti", Gate.ChiaviDi(typeof(EmployeesController), metodo));
    }

    [Fact]
    public void Le_tendine_dei_nomi_restano_aperte_di_proposito()
    {
        // I nomi dei colleghi servono in mezzo gestionale (assegnare una fase, scegliere un PM):
        // queste quattro ritornano LookupItem, cioè id e nome, e restano aperte.
        foreach (string metodo in new[] { "GetRealEmployees", "GetByDepartment", "GetByPhase", "GetPmList" })
            Assert.Empty(Gate.ChiaviDi(typeof(EmployeesController), metodo));
    }

    [Fact]
    public void Il_tipo_delle_tendine_resta_id_e_nome()
    {
        // LookupItem è condiviso da decine di endpoint aperti: aggiungerci Email o Username
        // «che tanto qui serve» riaprirebbe il buco in tutti insieme, in un colpo solo.
        Gate.SoloQuestiCampi(typeof(LookupItem), new[] { "Id", "Name" },
            "le tendine aperte di mezzo gestionale");
    }

    // ── Catalogo articoli ────────────────────────────────────────────────────────

    [Fact]
    public void I_prezzi_del_catalogo_seguono_il_micro_dei_prezzi()
    {
        // 🪤 Il filtro dei prezzi ricava i micro dalle CHIAVI dell'endpoint: senza chiave è
        // spento, e i campi marcati [DatoSensibile] escono interi lo stesso. Marcare senza
        // mettere il cancello sarebbe un cerotto su una porta aperta — per questo il test
        // pretende tutte e due le cose insieme.
        string[] chiavi = Gate.ChiaviDi(typeof(CatalogController), "GetAll");
        Assert.NotEmpty(chiavi);

        var conPrezzi = PermessiCatalogo.VociPrimarie()
            .Where(v => v.Chiave != null && v.Micros.Contains("prices"))
            .Select(v => v.Chiave!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(chiavi.Any(conPrezzi.Contains),
            "Nessuna delle chiavi di CatalogController.GetAll dichiara il micro «prices»: il filtro " +
            "resterebbe spento e i costi unitari uscirebbero interi a chi nella DDP li ha azzerati.");

        foreach (string campo in new[] { "UnitCost", "ListPrice" })
        {
            PropertyInfo p = typeof(CatalogItemListItem).GetProperty(campo)!;
            Assert.True(p.GetCustomAttributes().Any(a => a.GetType().Name == "DatoSensibileAttribute"),
                $"CatalogItemListItem.{campo} ha perso [DatoSensibile].");
            Assert.True(Nullable.GetUnderlyingType(p.PropertyType) != null,
                $"CatalogItemListItem.{campo} deve essere nullable: azzerare un decimal darebbe " +
                "uno 0,00 € finto, che è peggio di nascondere — è un dato falso a video.");
        }
    }
}
