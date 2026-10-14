using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1792 em Postgres real. Cada processo concorrente é um DbContext que já leu a linha antes do outro gravar: é o que
/// acontece quando dois webhooks, ou o console e o serviço de fundo, rodam ao mesmo tempo. Sem trava, quem grava por
/// último escreve o valor velho que tinha em memória.
/// </summary>
public class CorridasAtendimentoIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string PhoneNumberId = "pn-corridas";
    private const string ContatoWaId = "5511999990077";

    private async Task<Conversa> SemearConversaAsync(Guid empresa, int naoLidas = 0)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var conversa = Conversa.Abrir(empresa, ContatoWaId, DateTime.UtcNow.AddMinutes(-10), "Maria");
        for (var i = 0; i < naoLidas; i++) conversa.RegistrarEntrada(DateTime.UtcNow.AddMinutes(-5));
        db.AtendimentoConversas.Add(conversa);
        await db.SaveChangesAsync();
        return conversa;
    }

    private EasyStockDbContext Contexto(Guid empresa)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        return db;
    }

    private async Task<Conversa> LerAsync(Guid empresa, Guid conversaId)
    {
        await using var db = Contexto(empresa);
        return await db.AtendimentoConversas.AsNoTracking().SingleAsync(c => c.Id == conversaId);
    }

    // ---------- 1. Não lidas ----------

    [SkippableFact]
    public async Task DoisWebhooksSobreAMesmaLeitura_ContamAsDuasNaoLidas()
    {
        var empresa = Guid.NewGuid();
        var conversa = await SemearConversaAsync(empresa);
        await using var dbA = Contexto(empresa);
        await using var dbB = Contexto(empresa);
        await dbA.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);
        await dbB.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);

        (await Webhook(dbA, empresa).ExecuteAsync(PayloadTexto("wamid.corrida.a"))).Should().BeTrue();
        (await Webhook(dbB, empresa).ExecuteAsync(PayloadTexto("wamid.corrida.b"))).Should().BeTrue();

        var salva = await LerAsync(empresa, conversa.Id);
        salva.NaoLidas.Should().Be(2, "o segundo webhook não pode gravar o contador que leu antes do primeiro");
        await using var conferir = Contexto(empresa);
        (await conferir.AtendimentoMensagens.CountAsync(m => m.ConversaId == conversa.Id)).Should().Be(2);
    }

    [SkippableFact]
    public async Task WebhookDepoisDeMarcarLidaSobreLeituraVelha_ContaSoAMensagemNova()
    {
        var empresa = Guid.NewGuid();
        var conversa = await SemearConversaAsync(empresa, naoLidas: 3);
        await using var dbWebhook = Contexto(empresa);
        await dbWebhook.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);

        await using (var dbConsole = Contexto(empresa))
            await Gerenciar(dbConsole).MarcarLidaAsync(new AcaoConversaCommand(empresa, Guid.NewGuid(), conversa.Id));
        await Webhook(dbWebhook, empresa).ExecuteAsync(PayloadTexto("wamid.corrida.nova"));

        (await LerAsync(empresa, conversa.Id)).NaoLidas.Should().Be(1, "as 3 já lidas não voltam com a mensagem nova");
    }

    [SkippableFact]
    public async Task AssumirSobreLeituraVelha_NaoDesfazOEncerramento()
    {
        var empresa = Guid.NewGuid();
        var conversa = await SemearConversaAsync(empresa, naoLidas: 1);
        await using var dbAssumir = Contexto(empresa);
        await dbAssumir.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);

        await using (var dbEncerrar = Contexto(empresa))
            await Gerenciar(dbEncerrar).EncerrarAsync(new AcaoConversaCommand(empresa, Guid.NewGuid(), conversa.Id));
        var act = () => Gerenciar(dbAssumir).AssumirAsync(new AcaoConversaCommand(empresa, Guid.NewGuid(), conversa.Id));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>("outra aba já encerrou a conversa");

        var salva = await LerAsync(empresa, conversa.Id);
        salva.Situacao.Should().Be(SituacaoConversa.Encerrada);
        salva.EncerradaEm.Should().NotBeNull();
    }

    // ---------- 2. Envio do console sobre conversa encerrada ----------

    [SkippableFact]
    public async Task EnvioDoConsoleEnquantoEncerram_GravaAMensagemSemReabrirAConversa()
    {
        var empresa = Guid.NewGuid();
        var conversa = await SemearConversaAsync(empresa, naoLidas: 1);
        await using var dbConsole = Contexto(empresa);
        await dbConsole.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);

        var canal = Substitute.For<ICanalMensageria>();
        canal.Canal.Returns(CanalConversa.WhatsApp);
        canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // Outra aba encerra enquanto a Meta responde o envio.
                await using var dbEncerrar = Contexto(empresa);
                await Gerenciar(dbEncerrar).EncerrarAsync(new AcaoConversaCommand(empresa, Guid.NewGuid(), conversa.Id));
                return "wamid.console";
            });
        var console = new EnviarMensagemConsoleUseCase(new ConversaRepository(dbConsole), new ResolvedorCanal([canal]), null!, dbConsole);

        await console.EnviarTextoAsync(new EnviarTextoConsoleCommand(empresa, Guid.NewGuid(), conversa.Id, "Já separo"));

        var salva = await LerAsync(empresa, conversa.Id);
        salva.Situacao.Should().Be(SituacaoConversa.Encerrada, "o envio não reabre por cima do encerramento");
        salva.EncerradaEm.Should().NotBeNull();
        await using var conferir = Contexto(empresa);
        (await conferir.AtendimentoMensagens.SingleAsync(m => m.ConversaId == conversa.Id)).ExternoId
            .Should().Be("wamid.console", "a mensagem saiu e fica no histórico");
    }

    // ---------- 3. Reenvio duplo ----------

    [SkippableFact]
    public async Task ReenvioDuploSobreAMesmaLeitura_Manda1Vez()
    {
        var empresa = Guid.NewGuid();
        var conversa = await SemearConversaAsync(empresa, naoLidas: 1);
        var falhou = Mensagem.Saida(empresa, conversa.Id, AutorMensagem.Dona, DateTime.UtcNow.AddMinutes(-1),
            TipoConteudoMensagem.Texto, "Seu bolo saiu");
        falhou.RegistrarFalhaEnvio("timeout", TipoFalhaEnvio.Temporaria, DateTime.UtcNow.AddMinutes(-1));
        await using (var db = Contexto(empresa))
        {
            db.AtendimentoMensagens.Add(falhou);
            await db.SaveChangesAsync();
        }

        var canal = Substitute.For<ICanalMensageria>();
        canal.Canal.Returns(CanalConversa.WhatsApp);
        canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.reenvio");
        await using var dbClique = Contexto(empresa);
        await using var dbFundo = Contexto(empresa);
        await dbClique.AtendimentoMensagens.SingleAsync(m => m.Id == falhou.Id);
        await dbFundo.AtendimentoMensagens.SingleAsync(m => m.Id == falhou.Id);

        await Reenviar(dbClique, canal).ExecuteAsync(empresa, conversa.Id, falhou.Id);
        var segundo = () => Reenviar(dbFundo, canal).ExecuteAsync(empresa, conversa.Id, falhou.Id);

        await segundo.Should().ThrowAsync<UseCaseValidationException>();
        await canal.Received(1).EnviarTextoAsync(Arg.Any<string>(), "Seu bolo saiu", Arg.Any<CancellationToken>());
        await using var conferir = Contexto(empresa);
        (await conferir.AtendimentoMensagens.SingleAsync(m => m.Id == falhou.Id)).Status.Should().Be(StatusMensagem.Enviada);
    }

    // ---------- montagem ----------

    private static GerenciarConversaAtendimentoUseCase Gerenciar(EasyStockDbContext db) =>
        new(new ConversaRepository(db), db, Substitute.For<IPublicadorEventoIntegracao>());

    private static ReenviarMensagemUseCase Reenviar(EasyStockDbContext db, ICanalMensageria canal)
    {
        var canais = new ResolvedorCanal([canal]);
        return new(new ConversaRepository(db), Substitute.For<IConfiguracaoAtendimentoRepository>(), canais,
            new ReservaSmsAtendimento(canais, ReservaSmsOpcoes.Desligada, NullLogger<ReservaSmsAtendimento>.Instance),
            db, TimeProvider.System);
    }

    private static ProcessarEventoWhatsAppUseCase Webhook(EasyStockDbContext db, Guid empresaId)
    {
        var empresa = Empresa.Criar("Casa da Baba Corridas", "11111111000191");
        empresa.Id = empresaId;
        var empresas = Substitute.For<IEmpresaRepository>();
        empresas.GetByWhatsAppPhoneNumberIdAsync(PhoneNumberId, Arg.Any<CancellationToken>()).Returns(empresa);
        var flags = Substitute.For<ITenantFeatureFlagRepository>();
        flags.ListarAtivasAsync(empresaId, Arg.Any<CancellationToken>()).Returns(new[] { FeatureCatalogo.ModuloAtendimento });
        var conversas = new ConversaRepository(db);
        var canal = Substitute.For<ICanalMensageria>();
        canal.Canal.Returns(CanalConversa.WhatsApp);
        return new ProcessarEventoWhatsAppUseCase(
            empresas, flags, Substitute.For<IConfiguracaoAtendimentoRepository>(), conversas,
            new WebhookRecebidoRepository(db), Substitute.For<IWhatsAppCloudClient>(), Substitute.For<IQueueService>(),
            Substitute.For<IOperacaoEventPublisher>(), Substitute.For<ITenantContextAccessor>(), db,
            new IdentificarClientePorTelefoneUseCase(Substitute.For<IClienteRepository>(),
                Substitute.For<IClienteStorefrontRepository>(), NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
            new SaudacaoAtendimento(Substitute.For<IStorefrontRepository>(), new ConfigurationBuilder().Build()),
            new RoteadorAcoesBotao([], NullLogger<RoteadorAcoesBotao>.Instance),
            new OptOutPorPalavra(Substitute.For<IConsentimentoContatoRepository>(), conversas, new ResolvedorCanal([canal]),
                db, NullLogger<OptOutPorPalavra>.Instance),
            Substitute.For<IEscaladorConversa>(), Substitute.For<IExpedienteLojaRepository>(), TimeProvider.System,
            NullLogger<ProcessarEventoWhatsAppUseCase>.Instance);
    }

    private static string PayloadTexto(string wamid) => """
        {"entry":[{"changes":[{"value":{
            "metadata":{"phone_number_id":"__PHONE__"},
            "contacts":[{"profile":{"name":"Maria"},"wa_id":"__WAID__"}],
            "messages":[{"from":"__WAID__","id":"__WAMID__","timestamp":"__TS__","type":"text","text":{"body":"Oi"}}]
        }}]}]}
        """
        .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId).Replace("__WAMID__", wamid)
        .Replace("__TS__", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
}
