using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// Ordenação "urgência" do cockpit de Pedidos (#591). Roda em LINQ-to-objects — a
/// mesma árvore de expressão que o Npgsql traduz — então valida a lógica sem banco.
/// </summary>
public class PedidoOrderingTests
{
    private static readonly DateTime Agora = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    private static Pedido P(string status, DateTime criadoEm, DateTime? agendado = null)
    {
        var p = Pedido.Criar(Guid.NewGuid());
        p.Status = status;
        p.CriadoEm = criadoEm;
        p.AgendadoParaEm = agendado;
        return p;
    }

    [Fact]
    public void Ordena_abertos_antes_de_terminais_e_por_urgencia()
    {
        var a = P(StatusPedidoMapper.Aguardando, Agora.AddDays(-5));                       // aberto, antigo, sem agenda
        var b = P(StatusPedidoMapper.Entregue,   Agora.AddMinutes(-1));                    // terminal recente
        var c = P(StatusPedidoMapper.Pronto,     Agora.AddHours(-3), Agora.AddHours(-1));  // aberto, agendado vencido
        var d = P(StatusPedidoMapper.Preparando, Agora.AddHours(-2), Agora.AddHours(2));   // aberto, agendado futuro
        var e = P(StatusPedidoMapper.Cancelado,  Agora.AddDays(-1));                       // terminal

        var ordered = PedidoOrdering
            .PorUrgencia(new[] { b, a, e, d, c }.AsQueryable(), Agora)
            .ToList();

        // atrasado → agendado futuro → aberto sem agenda → terminais (recente primeiro)
        ordered.Should().Equal(c, d, a, b, e);
    }

    [Fact]
    public void Pedido_ativo_antigo_nunca_fica_atras_de_terminais_recentes()
    {
        // Invariante de cap (TIER1-3): com o cap de página, terminais recentes não podem
        // empurrar um pedido aberto antigo pra fora. Aqui ele tem que vir SEMPRE primeiro.
        var antigoAberto = P(StatusPedidoMapper.Aguardando, Agora.AddDays(-10));
        var terminaisRecentes = Enumerable.Range(0, 20)
            .Select(i => P(StatusPedidoMapper.Entregue, Agora.AddMinutes(-i)))
            .ToList();

        var entrada = terminaisRecentes.Append(antigoAberto).ToArray();
        var ordered = PedidoOrdering.PorUrgencia(entrada.AsQueryable(), Agora).ToList();

        ordered[0].Should().BeSameAs(antigoAberto);
    }
}
