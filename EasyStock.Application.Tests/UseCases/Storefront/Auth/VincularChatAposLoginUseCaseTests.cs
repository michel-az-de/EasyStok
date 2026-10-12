using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Auth;

public sealed class VincularChatAposLoginUseCaseTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Somente_sessao_verificada_do_mesmo_tenant_vincula_chat(bool mesmoTenant, bool sessaoValida)
    {
        var loja = StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Baba", 0m);
        loja.Ativar();
        var instante = DateTime.UtcNow;
        var conversa = Conversa.Abrir(loja.EmpresaId, "visitante", instante, "Visitante", canal: CanalConversa.ChatSite);
        var token = AcessoChatSite.NovoToken();
        var chat = SessaoChatSite.Abrir(loja.EmpresaId, loja.Id, AcessoChatSite.HashDoToken(token), instante);
        chat.VincularConversa(conversa.Id);
        var session = ClienteSession.Criar(Guid.NewGuid(), mesmoTenant ? loja.EmpresaId : Guid.NewGuid(), TimeProvider.System);
        if (!sessaoValida) session.Revogar("teste");
        var lojas = Substitute.For<IStorefrontRepository>();
        lojas.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(loja);
        var flags = Substitute.For<ITenantFeatureFlagRepository>();
        flags.ListarAtivasAsync(loja.EmpresaId, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);
        var chats = Substitute.For<ISessaoChatSiteRepository>();
        chats.ObterPorTokenHashAsync(loja.EmpresaId, AcessoChatSite.HashDoToken(token), Arg.Any<CancellationToken>()).Returns(chat);
        var sessoes = Substitute.For<IClienteSessionRepository>();
        sessoes.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var conversas = Substitute.For<IConversaRepository>();
        conversas.ListarPorClienteAsync(loja.EmpresaId, session.ClienteId, 50, Arg.Any<CancellationToken>()).Returns(Array.Empty<Conversa>());
        conversas.ObterPorIdAsync(loja.EmpresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var sut = new VincularChatAposLoginUseCase(
            new AcessoChatSite(lojas, flags, Substitute.For<ITenantContextAccessor>(), chats), sessoes,
            new ConversaChatSiteService(conversas, new FakeUnitOfWork()), TimeProvider.System);

        if (!mesmoTenant && sessaoValida)
            await sut.Invoking(x => x.ExecuteAsync("casa-da-baba", session.Id, token)).Should().ThrowAsync<SessaoChatSiteInvalidaException>();
        else
            await sut.ExecuteAsync("casa-da-baba", session.Id, token);

        conversa.ClienteId.Should().Be(mesmoTenant && sessaoValida ? session.ClienteId : null);
    }
}
