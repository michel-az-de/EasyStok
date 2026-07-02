namespace EasyStock.Web.Services;

/// <summary>
/// Feature flags do app Web. Lidas da seção "Features" no appsettings.json,
/// injetadas via IOptions&lt;FeaturesOptions&gt;.
/// </summary>
public sealed class FeaturesOptions
{
    /// <summary>
    /// Habilita o módulo fiscal (NFC-e/NF-e) na UI. Default <c>false</c>: fiscal está
    /// FORA do escopo v1.0 (docs/plan/v1.0/SCOPE.md) enquanto a homologação FocusNFe não
    /// conclui. Com a flag off, o atalho do Dashboard some e a rota /notas-fiscais/*
    /// responde 404. O backend FocusNFe (issue #558) permanece intacto — reativar é só
    /// <c>Features:FiscalHabilitado=true</c> no ambiente do piloto. Issue #770.
    /// </summary>
    public bool FiscalHabilitado { get; set; }
}
