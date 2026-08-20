namespace ATEC.PM.Shared;

/// <summary>
/// Marca una proprietà di DTO che trasporta un dato sensibile (PIANO-PERMESSI-REBUILD.md §12.3).
/// Oggi l'unico tipo è <c>prices</c>: costi, prezzi, importi, margini.
///
/// <para><b>Cosa scatena.</b> Il filtro globale (<c>PrezziSensibiliFilter</c>) — sugli endpoint
/// le cui voci di catalogo dichiarano il micro <c>prices</c> — <b>azzera in uscita</b> le
/// proprietà marcate per chi non ha la chiave <c>&lt;voce&gt;.prices</c> (il JSON le OMETTE:
/// <c>WhenWritingNull</c>; il client le rende «—» via <c>euro()</c>), e <b>respinge in
/// ingresso</b> (403) le scritture che le contengono da chi non ha il micro: un salvataggio
/// senza vedere i prezzi li cancellerebbe (§12.8, falla 1 della revisione).</para>
///
/// <para><b>Regola nullable.</b> La proprietà marcata DEVE essere nullable (<c>decimal?</c>):
/// azzerare un <c>decimal</c> mostrerebbe uno 0,00 € finto — un dato falso, peggio che nascosto.
/// Lo pretende il censimento (<c>CensimentoCatalogoTests</c>).</para>
///
/// <para><b>Proprietà calcolate</b> (es. <c>TotalCost =&gt; Quantity * UnitCost</c>): si marcano
/// per documentazione ma il filtro non le può scrivere (niente setter) — devono diventare
/// <c>null</c> DA SOLE quando la sorgente marcata è <c>null</c>.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DatoSensibileAttribute : Attribute
{
    public string Tipo { get; }

    public DatoSensibileAttribute(string tipo = "prices") => Tipo = tipo;
}
