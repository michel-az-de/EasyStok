using FluentAssertions;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Enums.Operacao;

namespace EasyStock.Domain.Tests.Entities.Operacao;

/// <summary>S20 (#1156): fila de impressão do canhoto.</summary>
public class ImpressaoPendenteTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriarCanhoto_NasceNaFila()
    {
        var empresa = Guid.NewGuid();
        var pedido = Guid.NewGuid();

        var i = ImpressaoPendente.CriarCanhoto(empresa, null, pedido, Agora);

        i.Id.Should().NotBeEmpty();
        i.EmpresaId.Should().Be(empresa);
        i.PedidoId.Should().Be(pedido);
        i.Tipo.Should().Be(TipoImpressao.Canhoto);
        i.Status.Should().Be(StatusImpressao.Pendente);
        i.CriadaEm.Should().Be(Agora);
        i.Tentativas.Should().Be(0);
        i.ImpressaEm.Should().BeNull();
    }

    [Fact]
    public void MarcarImpressa_SegundaVezNaoMudaNada()
    {
        var i = ImpressaoPendente.CriarCanhoto(Guid.NewGuid(), null, Guid.NewGuid(), Agora);

        i.MarcarImpressa(Agora.AddMinutes(1)).Should().BeTrue();
        i.MarcarImpressa(Agora.AddMinutes(5)).Should().BeFalse();

        i.Status.Should().Be(StatusImpressao.Impressa);
        i.ImpressaEm.Should().Be(Agora.AddMinutes(1));
        i.Tentativas.Should().Be(1);
    }

    [Fact]
    public void RegistrarFalha_GuardaErroTruncadoEContaTentativa()
    {
        var i = ImpressaoPendente.CriarCanhoto(Guid.NewGuid(), null, Guid.NewGuid(), Agora);

        i.RegistrarFalha(new string('x', 600)).Should().BeTrue();

        i.Status.Should().Be(StatusImpressao.Falhou);
        i.Erro.Should().HaveLength(ImpressaoPendente.ErroTamanhoMaximo);
        i.Tentativas.Should().Be(1);
    }

    [Fact]
    public void RegistrarFalha_DepoisDeImpressaEIgnorada()
    {
        var i = ImpressaoPendente.CriarCanhoto(Guid.NewGuid(), null, Guid.NewGuid(), Agora);
        i.MarcarImpressa(Agora);

        i.RegistrarFalha("sem papel").Should().BeFalse();

        i.Status.Should().Be(StatusImpressao.Impressa);
        i.Erro.Should().BeNull();
    }

    [Fact]
    public void CriarCanhoto_SemEmpresaLanca()
    {
        var act = () => ImpressaoPendente.CriarCanhoto(Guid.Empty, null, Guid.NewGuid(), Agora);
        act.Should().Throw<ArgumentException>();
    }
}
