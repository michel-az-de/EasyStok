using EasyStock.Domain.Events;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Events;

/// <summary>
/// Smoke tests para records de Domain.Events: garantem construcao com todos
/// os campos, equality estrutural e propriedades herdadas de DomainEvent.
/// Cada record sem teste contava 0% — esses cobrem com 1 round-trip.
/// </summary>
public class EventsCoverageTests
{
    [Fact]
    public void ProdutoCadastrado_construtor_preserva_campos_e_eh_DomainEvent()
    {
        var eventoId = Guid.NewGuid();
        var ocorrido = DateTime.UtcNow;
        var produtoId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();

        var ev = new ProdutoCadastrado(eventoId, ocorrido, produtoId, empresaId, "Galaxy Buds");

        ev.EventoId.Should().Be(eventoId);
        ev.OcorridoEm.Should().Be(ocorrido);
        ev.ProdutoId.Should().Be(produtoId);
        ev.EmpresaId.Should().Be(empresaId);
        ev.Nome.Should().Be("Galaxy Buds");
        ev.Should().BeAssignableTo<DomainEvent>();
    }

    [Fact]
    public void EstoqueBaixoIdentificado_construtor_preserva_quantidade_e_limite()
    {
        var ev = new EstoqueBaixoIdentificado(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), 2, 10);

        ev.QuantidadeAtual.Should().Be(2);
        ev.Limite.Should().Be(10);
    }

    [Fact]
    public void ItemEstoqueVencidoIdentificado_preserva_data_validade()
    {
        var validade = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var ev = new ItemEstoqueVencidoIdentificado(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), validade);

        ev.DataValidade.Should().Be(validade);
    }

    [Fact]
    public void PedidoFornecedorRecebido_preserva_total_e_data()
    {
        var data = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var ev = new PedidoFornecedorRecebido(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7, data);

        ev.TotalItensRecebidos.Should().Be(7);
        ev.DataRecebimento.Should().Be(data);
    }

    [Fact]
    public void PedidoFornecedorItemRecebido_preserva_quantidade_decimal()
    {
        var ev = new PedidoFornecedorItemRecebido(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            QuantidadeRecebida: 3.5m,
            DataRecebimento: DateTime.UtcNow);

        ev.QuantidadeRecebida.Should().Be(3.5m);
    }

    [Fact]
    public void ReposicaoEstoqueRegistrada_preserva_fonte_opcional()
    {
        var ev1 = new ReposicaoEstoqueRegistrada(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 50, Fonte: "manual");
        ev1.Fonte.Should().Be("manual");

        var ev2 = new ReposicaoEstoqueRegistrada(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 50, Fonte: null);
        ev2.Fonte.Should().BeNull();
    }

    [Fact]
    public void Equality_estrutural_para_records_com_todos_os_campos_iguais()
    {
        var id = Guid.NewGuid();
        var t = DateTime.UtcNow;
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();

        var a = new ProdutoCadastrado(id, t, pid, eid, "X");
        var b = new ProdutoCadastrado(id, t, pid, eid, "X");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_diferencia_quando_campo_diverge()
    {
        var a = new EstoqueBaixoIdentificado(Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), 5, 10);
        var b = a with { QuantidadeAtual = 4 };

        a.Should().NotBe(b);
    }
}
