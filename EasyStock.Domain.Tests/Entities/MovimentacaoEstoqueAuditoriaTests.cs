using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class MovimentacaoEstoqueAuditoriaTests
{
    private static ItemEstoque CriarItem() => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = Guid.NewGuid(),
        ProdutoId = Guid.NewGuid(),
        QuantidadeInicial = Quantidade.From(10),
        QuantidadeAtual = Quantidade.From(10),
        CustoUnitario = Dinheiro.FromDecimal(100m),
        Status = StatusItemEstoque.Ok,
        EntradaEm = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow
    };

    [Fact]
    public void CriarEntrada_sem_auditoria_mantem_campos_nulos()
    {
        var item = CriarItem();
        var mov = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(), item.EmpresaId, item, NaturezaMovimentacaoEstoque.Compra,
            Quantidade.From(5), Dinheiro.FromDecimal(10m), DateTime.UtcNow, null, null, DateTime.UtcNow);

        mov.UsuarioId.Should().BeNull();
        mov.Ip.Should().BeNull();
        mov.UserAgent.Should().BeNull();
        mov.DispositivoId.Should().BeNull();
    }

    [Fact]
    public void CriarEntrada_com_auditoria_persiste_quem_e_de_onde()
    {
        var item = CriarItem();
        var usuarioId = Guid.NewGuid();
        var auditoria = new AuditoriaContexto(usuarioId, "10.0.0.1", "Mozilla/5.0", "device-abc");

        var mov = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(), item.EmpresaId, item, NaturezaMovimentacaoEstoque.Compra,
            Quantidade.From(5), Dinheiro.FromDecimal(10m), DateTime.UtcNow, null, null, DateTime.UtcNow,
            auditoria);

        mov.UsuarioId.Should().Be(usuarioId);
        mov.Ip.Should().Be("10.0.0.1");
        mov.UserAgent.Should().Be("Mozilla/5.0");
        mov.DispositivoId.Should().Be("device-abc");
    }

    [Fact]
    public void CriarSaida_com_auditoria_propaga_para_entidade()
    {
        var item = CriarItem();
        var usuarioId = Guid.NewGuid();
        var auditoria = new AuditoriaContexto(usuarioId, "192.168.1.10", "Chrome/120", null);

        var mov = MovimentacaoEstoque.CriarSaida(
            Guid.NewGuid(), item.EmpresaId, item, Guid.NewGuid(), NaturezaMovimentacaoEstoque.Venda,
            Quantidade.From(3), Dinheiro.FromDecimal(50m), DateTime.UtcNow, null, null, DateTime.UtcNow,
            auditoria);

        mov.UsuarioId.Should().Be(usuarioId);
        mov.Ip.Should().Be("192.168.1.10");
        mov.DispositivoId.Should().BeNull();
    }

    [Fact]
    public void CriarEstorno_com_motivo_e_auditoria_grava_ambos()
    {
        var item = CriarItem();
        var saida = MovimentacaoEstoque.CriarSaida(
            Guid.NewGuid(), item.EmpresaId, item, Guid.NewGuid(), NaturezaMovimentacaoEstoque.Venda,
            Quantidade.From(2), Dinheiro.FromDecimal(50m), DateTime.UtcNow, null, null, DateTime.UtcNow);

        var auditoria = new AuditoriaContexto(Guid.NewGuid(), "10.0.0.5", "Edge/120", null);
        var estorno = MovimentacaoEstoque.CriarEstorno(
            Guid.NewGuid(), saida, DateTime.UtcNow, "Estorno", DateTime.UtcNow,
            "Cliente devolveu", auditoria);

        estorno.MotivoEstorno.Should().Be("Cliente devolveu");
        estorno.UsuarioId.Should().NotBeNull();
        estorno.Ip.Should().Be("10.0.0.5");
        estorno.MovimentacaoEstornadaId.Should().Be(saida.Id);
    }

    [Fact]
    public void CriarEstorno_motivo_apenas_whitespace_persiste_null()
    {
        var item = CriarItem();
        var saida = MovimentacaoEstoque.CriarSaida(
            Guid.NewGuid(), item.EmpresaId, item, Guid.NewGuid(), NaturezaMovimentacaoEstoque.Venda,
            Quantidade.From(2), Dinheiro.FromDecimal(50m), DateTime.UtcNow, null, null, DateTime.UtcNow);

        var estorno = MovimentacaoEstoque.CriarEstorno(
            Guid.NewGuid(), saida, DateTime.UtcNow, "Estorno", DateTime.UtcNow, "   ", null);

        estorno.MotivoEstorno.Should().BeNull();
    }
}
