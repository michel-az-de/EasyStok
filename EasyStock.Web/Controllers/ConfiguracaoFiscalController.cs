using EasyStock.Web.Models.ViewModels.ConfiguracaoFiscal;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ConfiguracaoFiscalController(
    ConfiguracaoFiscalService svc,
    SessionService session) : BaseController(session)
{
    // ADR-0032 fatia 8: Config Fiscal virou aba de Configuracoes. A rota antiga
    // redireciona (302) p/ nao quebrar links/bookmarks. Os POSTs abaixo continuam
    // existindo e redirecionam de volta para a aba fiscal.
    [HttpGet("/configuracao-fiscal")]
    public IActionResult Index() => Redirect("/configuracoes?tab=fiscal");

    [HttpPost("/configuracao-fiscal/dados-emitente")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarDadosEmitente(
        string regimeTributario,
        string? inscricaoEstadual,
        string? inscricaoMunicipal,
        string? logradouro,
        string? numero,
        string? complemento,
        string? bairro,
        string? cidade,
        string? uf,
        string? cep)
    {
        var endereco = new EnderecoFiscalDto
        {
            Logradouro = logradouro,
            Numero = numero,
            Complemento = complemento,
            Bairro = bairro,
            Cidade = cidade,
            Uf = uf,
            Cep = cep,
        };

        var result = await svc.AtualizarDadosEmitenteAsync(
            regimeTributario, inscricaoEstadual, inscricaoMunicipal, endereco);

        if (HasErrorVerbose(result, "Dados do emitente"))
            return Redirect("/configuracoes?tab=fiscal");

        Toast("success", "Dados do emitente atualizados.");
        return Redirect("/configuracoes?tab=fiscal");
    }

    [HttpPost("/configuracao-fiscal/provedor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarProvedor(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
        {
            Toast("error", "Selecione um provedor.");
            return Redirect("/configuracoes?tab=fiscal");
        }

        var result = await svc.EscolherProvedorAsync(provedor);
        if (HasErrorVerbose(result, "Provedor"))
            return Redirect("/configuracoes?tab=fiscal");

        Toast("success", $"Provedor '{provedor}' selecionado.");
        return Redirect("/configuracoes?tab=fiscal");
    }

    [HttpPost("/configuracao-fiscal/csc")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarCsc(string cscId, string cscToken)
    {
        if (string.IsNullOrWhiteSpace(cscId) || string.IsNullOrWhiteSpace(cscToken))
        {
            Toast("error", "CSC ID e CSC Token sao obrigatorios.");
            return Redirect("/configuracoes?tab=fiscal");
        }

        var result = await svc.ConfigurarCscAsync(cscId, cscToken);
        if (HasErrorVerbose(result, "CSC"))
            return Redirect("/configuracoes?tab=fiscal");

        Toast("success", "CSC configurado com sucesso.");
        return Redirect("/configuracoes?tab=fiscal");
    }

    [HttpPost("/configuracao-fiscal/serie-ambiente")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarSerieAmbiente(string? ambiente, short? serieNfce)
    {
        var result = await svc.AlterarSerieAmbienteAsync(ambiente, serieNfce);
        if (HasErrorVerbose(result, "Serie/Ambiente"))
            return Redirect("/configuracoes?tab=fiscal");

        Toast("success", "Serie e ambiente atualizados.");
        return Redirect("/configuracoes?tab=fiscal");
    }

    [HttpPost("/configuracao-fiscal/habilitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Habilitar()
    {
        var result = await svc.HabilitarAsync();
        if (HasErrorVerbose(result, "Habilitar emissao"))
            return Redirect("/configuracoes?tab=fiscal");

        Toast("success", "Emissao fiscal habilitada.");
        return Redirect("/configuracoes?tab=fiscal");
    }

    [HttpPost("/configuracao-fiscal/desabilitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Desabilitar()
    {
        var result = await svc.DesabilitarAsync();
        if (HasErrorVerbose(result, "Desabilitar emissao"))
            return Redirect("/configuracoes?tab=fiscal");

        Toast("success", "Emissao fiscal desabilitada.");
        return Redirect("/configuracoes?tab=fiscal");
    }
}
