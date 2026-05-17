using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Financeiro;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Financeiro;

public class LancamentoTests
{
    private static Lancamento NovoReceber(decimal valor = 100m) =>
        Lancamento.Criar(
            empresaId: Guid.NewGuid(),
            tipo: TipoLancamento.Receber,
            descricao: "Venda balcao",
            valor: valor,
            dataEmissao: DateTime.UtcNow,
            dataVencimento: DateTime.UtcNow.AddDays(7));

    [Fact]
    public void Criar_define_estado_inicial_pendente()
    {
        var l = NovoReceber(250m);

        l.Id.Should().NotBeEmpty();
        l.Status.Should().Be(StatusLancamento.Pendente);
        l.Valor.Should().Be(250m);
        l.TotalBaixado.Should().Be(0m);
        l.ValorRestante.Should().Be(250m);
        l.Baixas.Should().BeEmpty();
        l.EventosPendentes.Should().BeEmpty();
    }

    [Fact]
    public void Criar_rejeita_valor_zero_ou_negativo()
    {
        Action zero = () => NovoReceber(0m);
        Action neg = () => NovoReceber(-1m);

        zero.Should().Throw<RegraDeDominioVioladaException>();
        neg.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Criar_rejeita_vencimento_antes_da_emissao()
    {
        var hoje = DateTime.UtcNow;
        Action act = () => Lancamento.Criar(
            empresaId: Guid.NewGuid(),
            tipo: TipoLancamento.Pagar,
            descricao: "Aluguel",
            valor: 100m,
            dataEmissao: hoje,
            dataVencimento: hoje.AddDays(-1));

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RegistrarBaixa_total_quita_e_zera_restante()
    {
        var l = NovoReceber(100m);

        var baixa = l.RegistrarBaixa(
            valor: 100m,
            dataBaixa: DateTime.UtcNow,
            meioPagamento: "pix");

        l.Status.Should().Be(StatusLancamento.Quitado);
        l.TotalBaixado.Should().Be(100m);
        l.ValorRestante.Should().Be(0m);
        l.Baixas.Should().ContainSingle();
        baixa.Valor.Should().Be(100m);
        baixa.MeioPagamento.Should().Be("pix");
    }

    [Fact]
    public void RegistrarBaixa_parcial_marca_status_parcial()
    {
        var l = NovoReceber(100m);

        l.RegistrarBaixa(40m, DateTime.UtcNow, "dinheiro");

        l.Status.Should().Be(StatusLancamento.Parcial);
        l.TotalBaixado.Should().Be(40m);
        l.ValorRestante.Should().Be(60m);
    }

    [Fact]
    public void RegistrarBaixa_multipla_acumula_e_quita_quando_total()
    {
        var l = NovoReceber(100m);

        l.RegistrarBaixa(40m, DateTime.UtcNow, "dinheiro");
        l.Status.Should().Be(StatusLancamento.Parcial);

        l.RegistrarBaixa(60m, DateTime.UtcNow, "pix");
        l.Status.Should().Be(StatusLancamento.Quitado);
        l.TotalBaixado.Should().Be(100m);
        l.Baixas.Should().HaveCount(2);
    }

    [Fact]
    public void RegistrarBaixa_excedendo_restante_lanca()
    {
        var l = NovoReceber(100m);
        l.RegistrarBaixa(40m, DateTime.UtcNow, "dinheiro");

        Action act = () => l.RegistrarBaixa(70m, DateTime.UtcNow, "pix");

        act.Should().Throw<RegraDeDominioVioladaException>()
           .WithMessage("*excede*");
    }

    [Fact]
    public void RegistrarBaixa_em_cancelado_lanca()
    {
        var l = NovoReceber(100m);
        l.Cancelar("teste");

        Action act = () => l.RegistrarBaixa(50m, DateTime.UtcNow, "pix");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RegistrarBaixa_em_quitado_lanca()
    {
        var l = NovoReceber(100m);
        l.RegistrarBaixa(100m, DateTime.UtcNow, "pix");

        Action act = () => l.RegistrarBaixa(1m, DateTime.UtcNow, "pix");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RegistrarBaixa_com_chave_externa_repetida_e_idempotente()
    {
        var l = NovoReceber(100m);

        var primeira = l.RegistrarBaixa(40m, DateTime.UtcNow, "pix", chaveExterna: "txid-abc");
        var segunda = l.RegistrarBaixa(40m, DateTime.UtcNow, "pix", chaveExterna: "txid-abc");

        primeira.Id.Should().Be(segunda.Id);
        l.Baixas.Should().ContainSingle();
        l.TotalBaixado.Should().Be(40m);
        l.Status.Should().Be(StatusLancamento.Parcial);
    }

    [Fact]
    public void RegistrarBaixa_publica_evento_pendente_com_status_resultante()
    {
        var l = NovoReceber(100m);

        l.RegistrarBaixa(60m, DateTime.UtcNow, "pix");

        l.EventosPendentes.Should().ContainSingle();
        var evt = l.EventosPendentes.First();
        evt.LancamentoId.Should().Be(l.Id);
        evt.ValorBaixado.Should().Be(60m);
        evt.ValorRestante.Should().Be(40m);
        evt.StatusResultante.Should().Be(StatusLancamento.Parcial);
    }

    [Fact]
    public void EstornarBaixa_recalcula_status_para_pendente()
    {
        var l = NovoReceber(100m);
        var baixa = l.RegistrarBaixa(100m, DateTime.UtcNow, "pix");
        l.Status.Should().Be(StatusLancamento.Quitado);

        l.EstornarBaixa(baixa.Id, "erro");

        l.Status.Should().Be(StatusLancamento.Pendente);
        l.TotalBaixado.Should().Be(0m);
        l.ValorRestante.Should().Be(100m);
        baixa.Ativa.Should().BeFalse();
    }

    [Fact]
    public void EstornarBaixa_em_baixa_inexistente_lanca()
    {
        var l = NovoReceber(100m);

        Action act = () => l.EstornarBaixa(Guid.NewGuid());

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void EstornarBaixa_idempotente_quando_ja_estornada()
    {
        var l = NovoReceber(100m);
        var b = l.RegistrarBaixa(50m, DateTime.UtcNow, "pix");
        l.EstornarBaixa(b.Id);

        Action act = () => l.EstornarBaixa(b.Id);
        act.Should().NotThrow();
    }

    [Fact]
    public void Cancelar_com_baixa_ativa_lanca()
    {
        var l = NovoReceber(100m);
        l.RegistrarBaixa(20m, DateTime.UtcNow, "pix");

        Action act = () => l.Cancelar("teste");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Cancelar_com_baixa_estornada_permite()
    {
        var l = NovoReceber(100m);
        var b = l.RegistrarBaixa(20m, DateTime.UtcNow, "pix");
        l.EstornarBaixa(b.Id);

        l.Cancelar("desistiu");

        l.Status.Should().Be(StatusLancamento.Cancelado);
    }

    [Fact]
    public void Cancelar_de_quitado_lanca()
    {
        var l = NovoReceber(100m);
        l.RegistrarBaixa(100m, DateTime.UtcNow, "pix");

        Action act = () => l.Cancelar("teste");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Cancelar_idempotente()
    {
        var l = NovoReceber(100m);
        l.Cancelar("teste");

        Action act = () => l.Cancelar("outro");
        act.Should().NotThrow();
        l.Status.Should().Be(StatusLancamento.Cancelado);
    }

    [Fact]
    public void LimparEventosPendentes_zera_a_fila()
    {
        var l = NovoReceber(100m);
        l.RegistrarBaixa(50m, DateTime.UtcNow, "pix");
        l.EventosPendentes.Should().ContainSingle();

        l.LimparEventosPendentes();

        l.EventosPendentes.Should().BeEmpty();
    }

    [Fact]
    public void RegistrarBaixa_arredonda_valor_para_duas_casas()
    {
        var l = NovoReceber(100m);

        var b = l.RegistrarBaixa(33.333m, DateTime.UtcNow, "pix");

        b.Valor.Should().Be(33.33m);
    }
}
