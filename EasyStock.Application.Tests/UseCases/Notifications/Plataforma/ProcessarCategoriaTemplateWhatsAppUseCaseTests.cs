using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications.Plataforma;
using EasyStock.Domain.Entities.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Notifications.Plataforma;

/// <summary>
/// N6: a Meta não deixa sobrescrever o webhook de template; <c>template_category_update</c> chega ao callback do app e
/// o template recategorizado para marketing passa a ser bloqueado na plataforma.
/// </summary>
public class ProcessarCategoriaTemplateWhatsAppUseCaseTests
{
    private readonly ITemplateMetaEstadoRepository _estados = Substitute.For<ITemplateMetaEstadoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ProcessarCategoriaTemplateWhatsAppUseCase Sut() =>
        new(_estados, _uow, NullLogger<ProcessarCategoriaTemplateWhatsAppUseCase>.Instance);

    private static string Corpo(string valueJson) =>
        $$"""{"object":"whatsapp_business_account","entry":[{"id":"WABA","changes":[{"field":"template_category_update","value":{{valueJson}}}]}]}""";

    [Fact]
    public async Task AvisoComCorrectCategoryMarketingBloqueia()
    {
        var ok = await Sut().ExecuteAsync(Corpo(
            """{"message_template_id":1,"message_template_name":"prazo_estourado","message_template_language":"pt_BR","correct_category":"MARKETING"}"""));

        ok.Should().BeTrue();
        await _estados.Received(1).GravarCategoriaAsync("prazo_estourado", "pt_BR", "MARKETING", Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task NovaCategoriaMarketingBloqueia()
    {
        await Sut().ExecuteAsync(Corpo(
            """{"message_template_id":1,"message_template_name":"prazo_estourado","message_template_language":"pt_BR","previous_category":"UTILITY","new_category":"MARKETING"}"""));

        await _estados.Received(1).GravarCategoriaAsync("prazo_estourado", "pt_BR", "MARKETING", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UtilityNaoBloqueia()
    {
        await Sut().ExecuteAsync(Corpo(
            """{"message_template_id":1,"message_template_name":"prazo_estourado","message_template_language":"pt_BR","previous_category":"MARKETING","new_category":"UTILITY"}"""));

        // A categoria consumada é gravada como está: desfaz um bloqueio antigo, nunca cria um.
        await _estados.DidNotReceive().GravarCategoriaAsync(
            Arg.Any<string>(), Arg.Any<string>(), "MARKETING", Arg.Any<CancellationToken>());
        await _estados.Received(1).GravarCategoriaAsync("prazo_estourado", "pt_BR", "UTILITY", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdiomaComHifenCasaComSublinhado()
    {
        // O webhook manda pt-BR; o envio usa pt_BR. O repositório normaliza; o caso entrega o idioma como veio.
        TemplateMetaEstado.NormalizarIdioma("pt-BR").Should().Be(TemplateMetaEstado.NormalizarIdioma("pt_BR"));
        TemplateMetaEstado.Criar("Prazo_Estourado", "pt-BR", "marketing").Should().Match<TemplateMetaEstado>(
            e => e.Nome == "prazo_estourado" && e.Idioma == "pt_br" && e.EhMarketing);

        await Sut().ExecuteAsync(Corpo(
            """{"message_template_name":"Prazo_Estourado","message_template_language":"pt-BR","new_category":"MARKETING"}"""));

        await _estados.Received(1).GravarCategoriaAsync("Prazo_Estourado", "pt-BR", "MARKETING", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"entry":[]}""")]
    [InlineData("""{"entry":[{"changes":[{"field":"template_category_update"}]}]}""")]
    [InlineData("""{"entry":[{"changes":[{"field":"template_category_update","value":{}}]}]}""")]
    [InlineData("nao e json")]
    public async Task PayloadSemCamposNaoQuebra(string corpo)
    {
        var ok = await Sut().ExecuteAsync(corpo);

        ok.Should().BeTrue();
        await _estados.DidNotReceiveWithAnyArgs().GravarCategoriaAsync(default!, default!, default!, default);
    }
}
