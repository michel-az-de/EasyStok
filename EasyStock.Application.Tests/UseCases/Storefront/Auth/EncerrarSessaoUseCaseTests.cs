using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.TestHelpers;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Domain.Entities.Atendimento;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Auth;

public sealed class EncerrarSessaoUseCaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Logout_revoga_somente_sessao_do_tenant(bool mesmoTenant)
    {
        var empresaId = Guid.NewGuid();
        var storefront = StorefrontEntity.Criar(empresaId, "casa-da-baba", "Casa da Baba", 0m);
        var session = ClienteSession.Criar(Guid.NewGuid(), mesmoTenant ? empresaId : Guid.NewGuid(), TimeProvider.System);
        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(storefront);
        var sessionRepo = Substitute.For<IClienteSessionRepository>();
        sessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var uow = new FakeUnitOfWork();
        var sut = new EncerrarSessaoUseCase(storefrontRepo, sessionRepo, Substitute.For<ISessaoChatSiteRepository>(), uow, TimeProvider.System);

        await sut.ExecuteAsync("casa-da-baba", session.Id);
        await sut.ExecuteAsync("casa-da-baba", session.Id);

        session.Revogada.Should().Be(mesmoTenant);
        uow.CommitCount.Should().Be(mesmoTenant ? 1 : 0);
        await sessionRepo.Received(mesmoTenant ? 1 : 0).UpdateAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_cookie_nao_consulta_nem_revoga_sessao()
    {
        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        var sessionRepo = Substitute.For<IClienteSessionRepository>();
        var uow = new FakeUnitOfWork();
        await new EncerrarSessaoUseCase(storefrontRepo, sessionRepo, Substitute.For<ISessaoChatSiteRepository>(), uow, TimeProvider.System).ExecuteAsync("casa-da-baba", null);
        await sessionRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        uow.CommitCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Logout_encerra_chat_somente_da_loja_do_token(bool mesmaLoja)
    {
        var empresaId = Guid.NewGuid();
        var storefront = StorefrontEntity.Criar(empresaId, "casa-da-baba", "Casa da Baba", 0m);
        const string token = "token-local-de-teste";
        var agora = DateTime.UtcNow;
        var chat = SessaoChatSite.Abrir(empresaId, mesmaLoja ? storefront.Id : Guid.NewGuid(), AcessoChatSite.HashDoToken(token), agora);
        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(storefront);
        var chatRepo = Substitute.For<ISessaoChatSiteRepository>();
        chatRepo.ObterPorTokenHashAsync(empresaId, AcessoChatSite.HashDoToken(token), Arg.Any<CancellationToken>()).Returns(chat);
        var uow = new FakeUnitOfWork();
        var sut = new EncerrarSessaoUseCase(storefrontRepo, Substitute.For<IClienteSessionRepository>(), chatRepo, uow, TimeProvider.System);

        await sut.ExecuteAsync("casa-da-baba", null, token);
        await sut.ExecuteAsync("casa-da-baba", null, token);

        chat.EstaValida(DateTime.UtcNow).Should().Be(!mesmaLoja);
        uow.CommitCount.Should().Be(mesmaLoja ? 1 : 0);
    }

}
