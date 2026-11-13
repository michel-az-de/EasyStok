using System.Data.Common;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Agente_PedidoEVagaCommitadosSobrevivemAFalhaSqlDepoisDoProvedor()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var falha = new FalhaSqlAposPreferencia();
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true);
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        var mp = Substitute.For<IMercadoPagoClient>();
        IServiceProvider? servicos = null;
        EasyStockDbContext? bancoDoTurno = null;
        Guid pedidoDuravel = default;
        Guid vagaDuravel = default;
        var provedorConfirmou = false;
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(async chamada =>
        {
            var command = chamada.Arg<CriarPreferenceCommand>();
            bancoDoTurno!.Database.CurrentTransaction.Should().BeNull("a ferramenta do agente não mantém transação durante o provedor");
            // Outra unidade de trabalho só vê pedido e vaga se o commit anterior realmente ocorreu no PostgreSQL.
            using var consulta = servicos!.CreateScope();
            var banco = consulta.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            banco.SetMobileTenantContext(EmpresaId);
            var pedido = await banco.Pedidos.AsNoTracking().SingleAsync(p => p.Id == command.PedidoId && p.EmpresaId == EmpresaId);
            var vaga = await banco.VagasOcupadas.AsNoTracking().SingleAsync(v => v.PedidoId == pedido.Id && v.LiberadoEm == null);
            pedido.StatusEnum.Should().Be(StatusPedido.AguardandoPagamento);
            (await banco.AtendimentoConversas.AsNoTracking().SingleAsync(c => c.PedidoEmAndamentoId == pedido.Id)).ClienteId.Should().Be(pedido.ClienteId);
            pedidoDuravel = pedido.Id;
            vagaDuravel = vaga.Id;
            provedorConfirmou = true;
            falha.Armar(bancoDoTurno);
            return new PreferenceCriadaResult("pref-w5-controlada", "https://mp.test/w5-controlada");
        });
        await using var factory = CriarFactory(mp).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton(llm);
            s.AddSingleton(cloud);
            s.ConfigureDbContext<EasyStockDbContext>((_, options) => options.AddInterceptors(falha));
        }));
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        servicos = factory.Services;
        using (var preparar = servicos.CreateScope())
        {
            var banco = preparar.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            banco.SetMobileTenantContext(EmpresaId);
            var conversa = await banco.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
            var agora = DateTime.UtcNow;
            conversa.LiberarAutomatico();
            conversa.RegistrarEntrada(agora);
            banco.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, agora,
                TipoConteudoMensagem.Texto, "Pode fechar o pedido sintético.", "wamid.w5.controlado"));
            await banco.SaveChangesAsync();
        }
        var entrada = JsonSerializer.SerializeToElement(new
        {
            itens = new[] { new { cardapio_item_id = cenario.CardapioItemId, quantidade = 1 } },
            data_entrega = cenario.DataEntrega.ToString("yyyy-MM-dd"), janela_id = cenario.JanelaId,
        });
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            new RespostaLlm(RespostaLlm.StopToolUse, [new BlocoUsoFerramentaLlm("w5-pedido", "criar_pedido", entrada)], 1, 1));
        using (var turno = servicos.CreateScope())
        {
            bancoDoTurno = turno.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            bancoDoTurno.SetMobileTenantContext(EmpresaId);
            var agente = turno.ServiceProvider.GetRequiredService<AgenteAtendimentoService>();
            var resultado = await agente.ProcessarTurnoAsync(EmpresaId, cenario.ConversaId, DateTime.UtcNow);
            resultado.ChamouLlm.Should().BeTrue();
            resultado.Respondeu.Should().BeFalse();
        }

        provedorConfirmou.Should().BeTrue("o provedor controlado aceitou uma preferência após a leitura independente do pedido e da vaga");
        falha.Injetada.Should().BeTrue();
        falha.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero, "a gravação posterior falhou no PostgreSQL real, sem substituir o UoW");
        await llm.Received(1).EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
        await mp.Received(1).CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>());
        await cloud.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        using var final = servicos.CreateScope();
        var leitura = final.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        leitura.SetMobileTenantContext(EmpresaId);
        var persistidos = await leitura.Pedidos.AsNoTracking().Include(p => p.Pagamentos)
            .Where(p => p.EmpresaId == EmpresaId && p.ClienteId == cenario.ClienteId).ToListAsync();
        var persistido = persistidos.Should().ContainSingle().Subject;
        persistido.Id.Should().Be(pedidoDuravel);
        persistido.StatusEnum.Should().Be(StatusPedido.AguardandoPagamento);
        persistido.Pagamentos.Should().BeEmpty();
        (await leitura.AtendimentoConversas.AsNoTracking().SingleAsync(c => c.Id == cenario.ConversaId)).PedidoEmAndamentoId.Should().Be(pedidoDuravel);
        var reserva = await leitura.VagasOcupadas.AsNoTracking().SingleAsync(v => v.PedidoId == pedidoDuravel);
        reserva.Id.Should().Be(vagaDuravel);
        reserva.JanelaEntregaId.Should().Be(cenario.JanelaId);
        reserva.DataEntrega.Should().Be(cenario.DataEntrega);
        reserva.LiberadoEm.Should().BeNull();
        (await leitura.CobrancasPedido.CountAsync(c => c.EmpresaId == EmpresaId && c.PedidoId == pedidoDuravel)).Should().Be(0);
    }

    // Falha só no INSERT da cobrança, depois de o stub retornar sucesso. O PostgreSQL devolve 22012.
    private sealed class FalhaSqlAposPreferencia : DbCommandInterceptor
    {
        private DbContext? _contexto;
        public bool Injetada { get; private set; }
        public string? SqlState { get; private set; }
        public void Armar(DbContext contexto) => _contexto = contexto;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Injetada && ReferenceEquals(eventData.Context, _contexto)
                && command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                && command.CommandText.Contains("cobrancas_pedido", StringComparison.Ordinal))
            {
                Injetada = true;
                command.CommandText = "SELECT 1 / 0;\n" + command.CommandText;
            }
            return ValueTask.FromResult(result);
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Injetada && ReferenceEquals(eventData.Context, _contexto) && eventData.Exception is PostgresException erro)
                SqlState = erro.SqlState;
            return Task.CompletedTask;
        }
    }
}
