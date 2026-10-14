using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Notifications.Dispatcher;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

public class DispatcherPedidoCanceladoTests
{
    [Theory]
    [InlineData(TipoEventoNotificacao.PedidoPagoConfirmado, true, true, false)]
    [InlineData(TipoEventoNotificacao.PedidoPagoConfirmado, false, true, true)]
    [InlineData(TipoEventoNotificacao.ComandaAtualizada, true, true, true)]
    [InlineData(TipoEventoNotificacao.PedidoPagoConfirmado, true, false, true)]
    public async Task RevalidaPedidoAntesDeEnviarConfirmacaoJaEnfileirada(
        TipoEventoNotificacao tipo, bool cancelado, bool mesmaEmpresa, bool deveEnviar)
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(mesmaEmpresa ? empresaId : Guid.NewGuid());
        if (cancelado) pedido.Cancelar();
        var evento = EventoNotificacao.Criar(tipo, empresaId, "{}", pedido.Id);
        var mensagem = OutboxMensagemNotificacao.Criar(evento.Id, Guid.NewGuid(), empresaId,
            CanalNotificacao.WhatsApp, "+5511999990001", "", "Pedido confirmado", CategoriaConteudoNotificacao.Transacional);
        mensagem.MarcarEmEnvio();
        var outbox = Substitute.For<IOutboxNotificacaoRepository>();
        outbox.ExisteMensagemAbertaAsync(Arg.Any<CancellationToken>()).Returns(true);
        outbox.ReservarParaEnvioAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([mensagem]);
        outbox.ObterAsync(empresaId, mensagem.Id, Arg.Any<CancellationToken>()).Returns(mensagem);
        var eventos = Substitute.For<IEventoNotificacaoRepository>();
        eventos.ObterAsync(empresaId, evento.Id, Arg.Any<CancellationToken>()).Returns(evento);
        var canal = Substitute.For<ICanalNotificacao>();
        canal.Canal.Returns(CanalNotificacao.WhatsApp);
        canal.EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(true, "fake") { IdExterno = "wamid-teste" });
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using (var db = new EasyStockDbContext(options))
        {
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddSingleton(outbox);
        services.AddSingleton(eventos);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddSingleton(Substitute.For<IRowLevelSecurityBypass>());
        services.AddSingleton(Substitute.For<ITenantContextAccessor>());
        services.AddSingleton(new PoliticaValidadeNotificacao());
        services.AddSingleton(Substitute.For<ILogEnvioNotificacaoRepository>());
        services.AddSingleton(Substitute.For<ITemplateRepository>());
        services.AddSingleton(Substitute.For<IRotinaRepository>());
        services.AddSingleton(Substitute.For<IBloqueioNotificacaoRepository>());
        services.AddSingleton(Substitute.For<IRendererTemplate>());
        services.AddSingleton(canal);
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.IsAuthenticated.Returns(true);
        usuario.EmpresaId.Returns(empresaId);
        services.AddScoped(_ => new EasyStockDbContext(options, usuario));
        using var provider = services.BuildServiceProvider();
        var dispatcher = new NotificacoesDispatcherOrchestrator(provider, NullLogger<NotificacoesDispatcherOrchestrator>.Instance);

        await dispatcher.ProcessarBatchAsync(0);

        await canal.Received(deveEnviar ? 1 : 0).EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>());
        mensagem.Status.Should().Be(deveEnviar ? StatusOutbox.Enviado : StatusOutbox.Suprimido);
        if (!deveEnviar)
        {
            mensagem.EnviadoEm.Should().BeNull();
            mensagem.ErroUltimaTentativa.Should().Contain("Pedido cancelado");
        }
    }
}
