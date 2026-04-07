using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Configuracoes;

namespace EasyStock.Web.Services;

public class ConfiguracoesService(ApiClient api)
{
    public Task<ApiResult<Configuracoes>> ObterAsync() =>
        api.GetAsync<Configuracoes>("configuracoes");

    public Task<ApiResult<object>> SalvarAsync(ConfiguracoesViewModel vm) =>
        api.PatchAsync<object>("configuracoes", new
        {
            diasAlertaValidade = vm.DiasAlertaValidade,
            diasAlertaParado = vm.DiasAlertaParado,
            qtyMinPadrao = vm.QtyMinPadrao,
            notifEstoqueCritico = vm.NotifEstoqueCritico,
            notifValidade = vm.NotifValidade,
            notifParado = vm.NotifParado,
            notifReposicao = vm.NotifReposicao,
            fifo = vm.Fifo
        });
}
