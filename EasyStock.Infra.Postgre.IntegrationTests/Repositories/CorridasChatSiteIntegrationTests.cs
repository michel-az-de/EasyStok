using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1797 em Postgres real, continuação do #1792: o chat do site e o pedido feito pela página do chat contam a entrada
/// sobre a linha travada e relida. O DbContext que já leu a conversa antes do marcar-lida é o processo concorrente.
/// </summary>
public class CorridasChatSiteIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Token = "token-do-visitante-corridas";

    private async Task<(SessaoChatSite Sessao, Conversa Conversa)> SemearAsync(Guid empresa, Guid storefront)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var agora = DateTime.UtcNow;
        var sessao = SessaoChatSite.Abrir(empresa, storefront, AcessoChatSite.HashDoToken(Token), agora.AddMinutes(-10));
        var conversa = Conversa.Abrir(empresa, sessao.ContatoIdExterno, agora.AddMinutes(-10), "Visitante do site",
            canal: CanalConversa.ChatSite);
        for (var i = 0; i < 3; i++) conversa.RegistrarEntrada(agora.AddMinutes(-5));
        sessao.VincularConversa(conversa.Id);
        await using var db = Contexto(empresa);
        db.AtendimentoConversas.Add(conversa);
        await db.SaveChangesAsync();
        return (sessao, conversa);
    }

    private EasyStockDbContext Contexto(Guid empresa)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        return db;
    }

    private async Task<Conversa> LerAsync(Guid empresa, Guid id)
    {
        await using var db = Contexto(empresa);
        return await db.AtendimentoConversas.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task MarcarLidaAsync(Guid empresa, Guid conversaId)
    {
        await using var db = Contexto(empresa);
        await new GerenciarConversaAtendimentoUseCase(new ConversaRepository(db), db)
            .MarcarLidaAsync(new AcaoConversaCommand(empresa, Guid.NewGuid(), conversaId));
    }

    [SkippableFact]
    public async Task MensagemDoVisitanteSobreLeituraVelha_ContaSoAMensagemNova()
    {
        var empresa = Guid.NewGuid();
        var storefront = StorefrontEntity.Criar(empresa, $"chat-{Guid.NewGuid():N}"[..20], "Loja", 0m);
        storefront.Ativar();
        var (sessao, conversa) = await SemearAsync(empresa, storefront.Id);
        await using var dbChat = Contexto(empresa);
        await dbChat.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);

        await MarcarLidaAsync(empresa, conversa.Id);
        await Visitante(dbChat, storefront, sessao).ExecuteAsync(storefront.Slug, Token, "Ainda tem bolo?");

        (await LerAsync(empresa, conversa.Id)).NaoLidas.Should().Be(1, "as 3 já lidas não voltam com a mensagem nova");
    }

    [SkippableFact]
    public async Task PedidoDaPaginaSobreLeituraVelha_ContaSoOPedido()
    {
        var empresa = Guid.NewGuid();
        var (_, conversa) = await SemearAsync(empresa, Guid.NewGuid());
        await using var dbCheckout = Contexto(empresa);
        var lida = await dbCheckout.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);

        await MarcarLidaAsync(empresa, conversa.Id);
        var pedidoId = Guid.NewGuid();
        await new ConversaChatSiteService(new ConversaRepository(dbCheckout), dbCheckout)
            .RegistrarPedidoAsync(empresa, lida.Id, pedidoId, "Pedido do site #1", DateTime.UtcNow);

        var salva = await LerAsync(empresa, conversa.Id);
        salva.NaoLidas.Should().Be(1, "o pedido conta como uma entrada sobre o marcar-lida");
        salva.PedidoEmAndamentoId.Should().Be(pedidoId);
    }

    [SkippableFact]
    public async Task PedidoDaPaginaComConversaEncerradaNoMeio_GravaONaoReabre()
    {
        var empresa = Guid.NewGuid();
        var (_, conversa) = await SemearAsync(empresa, Guid.NewGuid());
        await using var dbCheckout = Contexto(empresa);
        var lida = await dbCheckout.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);

        await using (var dbConsole = Contexto(empresa))
            await new GerenciarConversaAtendimentoUseCase(new ConversaRepository(dbConsole), dbConsole)
                .EncerrarAsync(new AcaoConversaCommand(empresa, Guid.NewGuid(), conversa.Id));
        var pedidoId = Guid.NewGuid();
        await new ConversaChatSiteService(new ConversaRepository(dbCheckout), dbCheckout)
            .RegistrarPedidoAsync(empresa, lida.Id, pedidoId, "Pedido do site #2", DateTime.UtcNow);

        var salva = await LerAsync(empresa, conversa.Id);
        salva.Situacao.Should().Be(SituacaoConversa.Encerrada);
        salva.PedidoEmAndamentoId.Should().Be(pedidoId, "o pedido já foi criado e a dona precisa achá-lo pela conversa");
        await using var conferir = Contexto(empresa);
        (await conferir.AtendimentoMensagens.CountAsync(m => m.ConversaId == conversa.Id && m.Texto == "Pedido do site #2"))
            .Should().Be(1);
    }

    private static EnviarMensagemVisitanteUseCase Visitante(EasyStockDbContext db, StorefrontEntity storefront, SessaoChatSite sessao)
    {
        var lojas = Substitute.For<IStorefrontRepository>();
        lojas.GetBySlugAsync(storefront.Slug, Arg.Any<CancellationToken>()).Returns(storefront);
        var flags = Substitute.For<ITenantFeatureFlagRepository>();
        flags.ListarAtivasAsync(storefront.EmpresaId, Arg.Any<CancellationToken>())
            .Returns(new[] { FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite });
        var sessoes = Substitute.For<ISessaoChatSiteRepository>();
        sessoes.ObterPorTokenHashAsync(storefront.EmpresaId, AcessoChatSite.HashDoToken(Token), Arg.Any<CancellationToken>())
            .Returns(sessao);
        var conversas = new ConversaRepository(db);
        return new EnviarMensagemVisitanteUseCase(
            new AcessoChatSite(lojas, flags, Substitute.For<ITenantContextAccessor>(), sessoes),
            conversas, Substitute.For<IOperacaoEventPublisher>(), db, NullLogger<EnviarMensagemVisitanteUseCase>.Instance,
            new ConversaChatSiteService(conversas, db));
    }
}
