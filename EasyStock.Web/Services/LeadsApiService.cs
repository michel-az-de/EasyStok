using EasyStock.Web.Models.ViewModels.Site;

namespace EasyStock.Web.Services;

/// <summary>
/// Wrapper sobre <see cref="ApiClient"/> para chamar o endpoint anonimo
/// POST /api/public/leads. Mantem a Web ignorante do contrato HTTP.
/// </summary>
public sealed class LeadsApiService(ApiClient api)
{
    public Task<Models.Api.ApiResult<LeadResponse>> EnviarFaleConoscoAsync(ContatoViewModel vm) =>
        api.PostAsync<LeadResponse>("public/leads", new
        {
            nome = vm.Nome,
            email = vm.Email,
            origem = "FaleConosco",
            consentimentoLgpd = vm.ConsentimentoLgpd,
            telefone = vm.Telefone,
            empresa = vm.Empresa,
            mensagem = vm.Mensagem,
            tipoNegocio = vm.TipoNegocio,
            receberNewsletter = vm.ReceberNewsletter,
            utmSource = vm.UtmSource,
            utmMedium = vm.UtmMedium,
            utmCampaign = vm.UtmCampaign,
            website = vm.Website
        });

    public Task<Models.Api.ApiResult<LeadResponse>> InscreverNewsletterAsync(NewsletterViewModel vm) =>
        api.PostAsync<LeadResponse>("public/leads", new
        {
            nome = string.IsNullOrWhiteSpace(vm.Nome) ? vm.Email : vm.Nome,
            email = vm.Email,
            origem = "Newsletter",
            consentimentoLgpd = vm.ConsentimentoLgpd,
            receberNewsletter = true,
            website = vm.Website
        });

    // RegistrarTesteGratisAsync sera adicionado em P1 quando AuthController.Registrar
    // capturar lead com origem antes do POST de empresas/registrar.
}

public sealed record LeadResponse(Guid LeadId, string Mensagem);
