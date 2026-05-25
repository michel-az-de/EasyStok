using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Configuracoes;

namespace EasyStock.Web.Services;

public class ConfiguracoesService(ApiClient api, SessionService session)
{
    private string BuildContextQuery()
    {
        var empresaId = session.GetEmpresaId();
        var lojaId = session.GetLojaId();
        return string.IsNullOrWhiteSpace(empresaId) || string.IsNullOrWhiteSpace(lojaId)
            ? string.Empty
            : $"?empresaId={Uri.EscapeDataString(empresaId)}&lojaId={Uri.EscapeDataString(lojaId)}";
    }

    public Task<ApiResult<Configuracoes>> ObterAsync() =>
        api.GetAsync<Configuracoes>($"configuracoes{BuildContextQuery()}");

    // Payload alinhado ao AtualizarConfiguracaoLojaCommand da API.
    public Task<ApiResult<object>> SalvarAsync(ConfiguracoesViewModel vm) =>
        api.PatchAsync<object>("configuracoes", new
        {
            empresaId = Guid.TryParse(session.GetEmpresaId(), out var empresaId) ? empresaId : Guid.Empty,
            lojaId = Guid.TryParse(session.GetLojaId(), out var lojaId) ? lojaId : Guid.Empty,
            diasAlertaValidade = vm.DiasAlertaValidade,
            diasAlertaParado = vm.DiasAlertaParado,
            quantidadeMinimaPadrao = vm.QtyMinPadrao,
            quantidadeCriticaPadrao = vm.QtyCritPadrao,
            notificarEstoqueCritico = vm.NotifEstoqueCritico,
            notificarValidade = vm.NotifValidade,
            notificarParado = vm.NotifParado,
            notificarReposicao = vm.NotifReposicao,
            fifoAtivo = vm.Fifo
        });
}
