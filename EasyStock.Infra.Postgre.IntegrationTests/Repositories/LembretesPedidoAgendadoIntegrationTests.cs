using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class LembretesPedidoAgendadoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task VesperaUmaVezInicioPrevistoListaSseECancelamentoComRlsReal()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Encomendas lembretes", null);
        var outra = Empresa.Criar("Encomendas outra empresa", null);
        var entrega = Agora.AddDays(1).AddHours(6);
        var pedido = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, entrega);
        pedido.DefinirInicioPrevisto(entrega.AddHours(-1));
        var externo = NovoPedido(outra.Id, StatusPedidoMapper.Aguardando, entrega);
        var cancelado = NovoPedido(casa.Id, StatusPedidoMapper.Cancelado, entrega);
        var concluido = NovoPedido(casa.Id, StatusPedidoMapper.Entregue, entrega);
        var conversa = Conversa.Abrir(casa.Id, "5511999990181", Agora, "Encomenda");
        conversa.DefinirPedidoEmAndamento(pedido.Id);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(casa, outra);
            db.Pedidos.AddRange(pedido, externo, cancelado, concluido);
            db.AtendimentoConversas.Add(conversa);
            await db.SaveChangesAsync();
        }

        var publisher = Substitute.For<IOperacaoEventPublisher>();
        (await RodarAsync(Agora.AddSeconds(-1), publisher)).Criados.Should().Be(0);
        (await RodarAsync(Agora, publisher)).Criados.Should().Be(2);
        (await RodarAsync(Agora, publisher)).Criados.Should().Be(0);
        await publisher.Received(1).PublicarAsync(AvaliarLembretesUseCase.EventoSse, casa.Id,
            Arg.Is<object>(p => p.GetType().GetProperty("pedidoId")!.GetValue(p)!.Equals(pedido.Id)), Arg.Any<CancellationToken>());
        await using (var db = fixture.CreateRlsClientDbContext())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {casa.Id.ToString()}, false)");
            db.SetMobileTenantContext(casa.Id);
            var lista = await new ListarLembretesUseCase(new LembreteRepository(db)).ExecuteAsync(casa.Id, Guid.NewGuid(), false, false);
            lista.Should().ContainSingle(l => l.PedidoId == pedido.Id && l.ConversaId == conversa.Id && l.Tipo == TipoLembrete.PedidoAgendado);
            (await db.Lembretes.IgnoreQueryFilters().ToListAsync()).Should().ContainSingle(l => l.EmpresaId == casa.Id);
            var candidatos = await new CandidatosLembreteQuery(db).ListarPedidosAgendadosAsync();
            candidatos.Should().ContainSingle(l => l.EmpresaId == casa.Id && l.PedidoId == pedido.Id);
        }

        (await RodarAsync(pedido.InicioPrevistoEm!.Value, publisher)).Criados.Should().Be(1);
        (await RodarAsync(pedido.InicioPrevistoEm.Value, publisher)).Criados.Should().Be(0);
        await using (var db = fixture.CreateDbContext())
        {
            var avisos = await db.Lembretes.IgnoreQueryFilters().Where(l => l.PedidoId == pedido.Id).ToListAsync();
            avisos.Should().HaveCount(2).And.OnlyContain(l => l.AvisadoEm != null && l.EstaAberto);
            (await db.Lembretes.IgnoreQueryFilters().AnyAsync(l => l.PedidoId == cancelado.Id || l.PedidoId == concluido.Id)).Should().BeFalse();
            var salvo = await db.Pedidos.IgnoreQueryFilters().SingleAsync(p => p.Id == pedido.Id);
            salvo.Status = StatusPedidoMapper.Cancelado;
            await db.SaveChangesAsync();
        }
        (await RodarAsync(pedido.InicioPrevistoEm.Value.AddMinutes(1), publisher)).Resolvidos.Should().Be(2);
        await using (var db = fixture.CreateDbContext())
            (await db.Lembretes.IgnoreQueryFilters().Where(l => l.PedidoId == pedido.Id).ToListAsync())
                .Should().OnlyContain(l => l.Situacao == SituacaoLembrete.Concluido);
    }

    [SkippableFact]
    public async Task VagaAtivaPrevaleceHistoricoLiberadoEJanelaOuConversaDeOutraEmpresaNaoEntram()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Vaga lembrete", null);
        var outra = Empresa.Criar("Vaga lembrete externa", null);
        var loja = StorefrontEntity.Criar(casa.Id, "vaga-lembrete", "Vaga", 0);
        var lojaExterna = StorefrontEntity.Criar(outra.Id, "vaga-lembrete-externa", "Outra", 0);
        var janela = JanelaEntrega.Criar(loja.Id, 0, new TimeOnly(13, 0), new TimeOnly(14, 0), 10, "Almoço");
        var janelaExterna = JanelaEntrega.Criar(lojaExterna.Id, 0, new TimeOnly(18, 0), new TimeOnly(19, 0), 10, "Externa");
        var comVaga = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, Agora.AddDays(9));
        var soVaga = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, null);
        var liberado = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, null);
        var vinculoInvalido = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, null);
        var dia = new DateOnly(2026, 10, 11);
        var antiga = VagaOcupada.Ocupar(janela.Id, dia.AddDays(-7), comVaga.Id);
        antiga.Liberar("reagendado");
        var vagaLiberada = VagaOcupada.Ocupar(janela.Id, dia, liberado.Id);
        vagaLiberada.Liberar("cancelado");
        var conversaExterna = Conversa.Abrir(outra.Id, "5511999990182", Agora);
        conversaExterna.DefinirPedidoEmAndamento(comVaga.Id);
        var expediente = ExpedienteLoja.CriarPadrao(casa.Id);
        expediente.DefinirHorarios([new(6, new TimeOnly(9, 30), new TimeOnly(20, 0))]);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(casa, outra);
            db.Storefronts.AddRange(loja, lojaExterna);
            db.JanelasEntrega.AddRange(janela, janelaExterna);
            db.Pedidos.AddRange(comVaga, soVaga, liberado, vinculoInvalido);
            db.VagasOcupadas.AddRange(antiga, vagaLiberada,
                VagaOcupada.Ocupar(janela.Id, dia, comVaga.Id), VagaOcupada.Ocupar(janela.Id, dia, soVaga.Id),
                VagaOcupada.Ocupar(janelaExterna.Id, dia, vinculoInvalido.Id));
            db.AtendimentoConversas.Add(conversaExterna);
            db.ExpedientesLoja.Add(expediente);
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            var candidatos = await new CandidatosLembreteQuery(db).ListarPedidosAgendadosAsync();
            var ids = new[] { comVaga.Id, soVaga.Id, liberado.Id, vinculoInvalido.Id };
            candidatos.Where(l => ids.Contains(l.PedidoId)).Should().HaveCount(2).And.OnlyContain(l => l.EmpresaId == casa.Id);
            var escolhido = candidatos.Single(l => l.PedidoId == comVaga.Id);
            escolhido.EntregaEm.Should().Be(HorarioBrasil.InstanteUtc(dia, new TimeOnly(13, 0)));
            escolhido.ConversaId.Should().BeNull();
            escolhido.Expediente!.EmpresaId.Should().Be(casa.Id);
        }
        (await RodarAsync(Agora.AddHours(1))).Criados.Should().Be(0);
        (await RodarAsync(Agora.AddMinutes(90))).Criados.Should().Be(2);
    }

    private async Task<ResultadoAvaliacaoLembretes> RodarAsync(DateTime instante, IOperacaoEventPublisher? publisher = null)
    {
        await using var db = fixture.CreateDbContext();
        using var bypass = db.UseRowLevelSecurityBypass();
        return await new AvaliarLembretesUseCase(new LembreteRepository(db), new CandidatosLembreteQuery(db),
            Substitute.For<INotificadorService>(), publisher ?? Substitute.For<IOperacaoEventPublisher>(), db,
            new RelogioFixo(instante), Options.Create(new PrazosOptions())).ExecuteAsync();
    }

    private static Pedido NovoPedido(Guid empresaId, string status, DateTime? entrega) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, Status = status, ClienteNome = "Fulana",
        AgendadoParaEm = entrega, Total = Dinheiro.Zero, CriadoEm = Agora, AlteradoEm = Agora,
    };

    private sealed class RelogioFixo(DateTime agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(agora, TimeSpan.Zero);
    }
}
