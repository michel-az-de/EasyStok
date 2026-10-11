using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1575: o resumo da inbox (polling de 5 s) em Postgres real. O JSON que o console lê fica igual campo a
/// campo, o status do pedido em andamento sai igual ao da rota do pedido, e a consulta não lê o contexto.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class InboxResumoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    // As mesmas opções da API (CoreMvcExtensions).
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [SkippableFact]
    public async Task Resumo_MantemOJsonCampoACampo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var cenario = await SemearAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(cenario.EmpresaId);
        var resumos = await Inbox(db).ExecuteAsync(new ListarConversasAtendimentoQuery(cenario.EmpresaId, null, null, 1, 10));

        var real = JsonSerializer.SerializeToNode(resumos, Json)!.AsArray();
        // Campo aditivo do #1575: fica fora desta comparação, que fixa o que já existia.
        foreach (var linha in real) linha!.AsObject().Remove("pedidoStatus");

        var esperado = JsonSerializer.SerializeToNode(new object[]
        {
            new
            {
                id = cenario.ComPedido.Id,
                canal = "WhatsApp",
                contatoIdExterno = "5511999990001",
                contatoNome = "Maria Souza",
                clienteId = (Guid?)cenario.ClienteId,
                situacao = "Assumida",
                naoLidas = 1,
                ultimaMensagemEm = cenario.Agora.AddHours(-1),
                ultimaMensagemTexto = "quero uma lasanha",
                pedidoEmAndamentoId = (Guid?)cenario.PedidoId,
                assumidaPorUsuarioId = (Guid?)cenario.AnaId,
                dentroDaJanela = true,
                motivoEscalada = (string?)null,
                ultimaMensagemEntradaEm = (DateTime?)cenario.Agora.AddHours(-1),
                aguardaResposta = true,
                slaRespostaMinutos = 9,
                assumidaPorNome = "Ana",
            },
            new
            {
                id = cenario.Escalada.Id,
                canal = "WhatsApp",
                contatoIdExterno = "5511999990002",
                contatoNome = (string?)null,
                clienteId = (Guid?)null,
                situacao = "Assumida",
                naoLidas = 1,
                ultimaMensagemEm = cenario.Agora.AddHours(-29),
                ultimaMensagemTexto = "já te respondo",
                pedidoEmAndamentoId = (Guid?)null,
                assumidaPorUsuarioId = (Guid?)null,
                dentroDaJanela = false,
                motivoEscalada = "Cliente pediu para falar com a dona",
                ultimaMensagemEntradaEm = (DateTime?)cenario.Agora.AddHours(-30),
                aguardaResposta = false,
                slaRespostaMinutos = 9,
                assumidaPorNome = (string?)null,
            },
        }, Json)!;

        real.ToJsonString().Should().Be(esperado.ToJsonString());
    }

    [SkippableFact]
    public async Task Resumo_TrazOStatusDoPedido_IgualAoDaRotaDoPedido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var cenario = await SemearAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(cenario.EmpresaId);
        var resumos = await Inbox(db).ExecuteAsync(new ListarConversasAtendimentoQuery(cenario.EmpresaId, null, null, 1, 10));
        var conversas = new ConversaRepository(db);
        var daRota = await new ObterPedidoConversaUseCase(
                conversas, new PedidoRepository(db), new CobrancaPedidoRepository(db), new VagaOcupadaRepository(db))
            .ExecuteAsync(cenario.EmpresaId, cenario.ComPedido.Id);

        daRota!.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        resumos.Single(r => r.Id == cenario.ComPedido.Id).PedidoStatus.Should().Be(daRota.Status);
        resumos.Single(r => r.Id == cenario.Escalada.Id).PedidoStatus.Should().BeNull("conversa sem pedido");

        // Pedido de outra empresa apontado na conversa: a rota do pedido devolve nulo, o resumo também.
        await using (var alterar = fixture.CreateDbContext())
        {
            alterar.SetMobileTenantContext(cenario.EmpresaId);
            var escalada = await alterar.AtendimentoConversas.SingleAsync(c => c.Id == cenario.Escalada.Id);
            escalada.DefinirPedidoEmAndamento(cenario.PedidoAlheioId);
            await alterar.SaveChangesAsync();
        }
        var depois = await Inbox(db).ExecuteAsync(new ListarConversasAtendimentoQuery(cenario.EmpresaId, null, null, 1, 10));
        depois.Single(r => r.Id == cenario.Escalada.Id).PedidoStatus.Should().BeNull();
        var json = JsonSerializer.SerializeToNode(depois.Single(r => r.Id == cenario.ComPedido.Id), Json)!.AsObject();
        json["pedidoStatus"]!.GetValue<string>().Should().Be("aguardando_pagamento");
    }

    [SkippableFact]
    public async Task Inbox_NaoLeOContextoDaConversa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var cenario = await SemearAsync();

        var sql = new CapturaDeSql();
        using var modelo = fixture.CreateDbContext();
        await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(modelo.Database.GetConnectionString())
            .AddInterceptors(sql)
            .Options);
        db.SetMobileTenantContext(cenario.EmpresaId);

        var itens = await new ConversaRepository(db).ListarInboxAsync(cenario.EmpresaId, null, null, null, 1, 10);

        itens.Should().HaveCount(2);
        sql.Comandos.Should().ContainSingle("a inbox é uma consulta só")
            .Which.Should().NotContain("ContextoJson", "o resumo não mostra o contexto (jsonb do carrinho)");
    }

    private static ListarConversasAtendimentoUseCase Inbox(EasyStockDbContext db)
    {
        var configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
        configuracoes.GetByEmpresaIdAsync(Arg.Any<Guid>()).Returns(ci =>
        {
            var salvo = ConfiguracaoAtendimento.CriarPadrao(ci.Arg<Guid>());
            salvo.SlaRespostaMinutos = 9;
            return salvo;
        });
        return new ListarConversasAtendimentoUseCase(
            new ConversaRepository(db), configuracoes, Options.Create(new PrazosOptions { ClienteSemRespostaMin = 7 }));
    }

    private sealed record Cenario(
        Guid EmpresaId, DateTime Agora, Conversa ComPedido, Conversa Escalada, Guid ClienteId, Guid PedidoId, Guid PedidoAlheioId, Guid AnaId);

    /// <summary>
    /// Duas conversas da empresa: uma assumida pela Ana, com contexto, cliente, pedido e o cliente falando
    /// por último dentro da janela; outra escalada, sem dono, respondida e fora da janela de 24 h.
    /// </summary>
    private async Task<Cenario> SemearAsync()
    {
        // Segundo cheio: o timestamptz guarda microssegundo e o JSON escreveria a fração.
        var agora = new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var empresa = Empresa.Criar("Inbox resumo", null);
        var outra = Empresa.Criar("Inbox resumo outra", null);
        var ana = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@local.test", "hash");
        var cliente = Cliente.Criar(empresa.Id, "Maria Souza");
        var pedido = Pedido.Criar(empresa.Id, cliente);
        pedido.Status = StatusPedidoMapper.AguardandoPagamento;
        var pedidoAlheio = Pedido.Criar(outra.Id);
        pedidoAlheio.Status = StatusPedidoMapper.Preparando;

        var comPedido = Conversa.Abrir(empresa.Id, "5511999990001", agora.AddHours(-2), "Maria Souza", cliente.Id);
        comPedido.Assumir(agora.AddHours(-2), ana.Id);
        comPedido.RegistrarEntrada(agora.AddHours(-1));
        comPedido.DefinirContexto("""{"carrinho":[{"item":"lasanha","qtd":2}],"etapa":"endereco"}""");
        comPedido.DefinirPedidoEmAndamento(pedido.Id);

        var escalada = Conversa.Abrir(empresa.Id, "5511999990002", agora.AddHours(-31));
        escalada.RegistrarEntrada(agora.AddHours(-30));
        escalada.Escalar(agora.AddHours(-30), "Cliente pediu para falar com a dona");
        escalada.RegistrarSaida(agora.AddHours(-29));

        await using var seed = fixture.CreateDbContext();
        using var bypass = seed.UseRowLevelSecurityBypass();
        seed.Empresas.AddRange(empresa, outra);
        seed.Usuarios.Add(ana);
        seed.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), EmpresaId = empresa.Id, UsuarioId = ana.Id, Ativo = true, CriadoEm = agora });
        seed.Clientes.Add(cliente);
        seed.Pedidos.AddRange(pedido, pedidoAlheio);
        seed.AtendimentoConversas.AddRange(comPedido, escalada);
        seed.AtendimentoMensagens.AddRange(
            Mensagem.Entrada(empresa.Id, comPedido.Id, agora.AddHours(-1), TipoConteudoMensagem.Texto, "quero uma lasanha", externoId: $"wamid.{Guid.NewGuid():N}"),
            Mensagem.Entrada(empresa.Id, escalada.Id, agora.AddHours(-30), TipoConteudoMensagem.Texto, "chama a dona", externoId: $"wamid.{Guid.NewGuid():N}"),
            Mensagem.Saida(empresa.Id, escalada.Id, AutorMensagem.Dona, agora.AddHours(-29), TipoConteudoMensagem.Texto, "já te respondo", externoId: $"wamid.{Guid.NewGuid():N}"));
        await seed.SaveChangesAsync();

        return new Cenario(empresa.Id, agora, comPedido, escalada, cliente.Id, pedido.Id, pedidoAlheio.Id, ana.Id);
    }

    private sealed class CapturaDeSql : DbCommandInterceptor
    {
        public List<string> Comandos { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Comandos.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
