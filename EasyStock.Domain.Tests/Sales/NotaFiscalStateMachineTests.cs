using System.Collections.Generic;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.Exceptions.Fiscal;
using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

public class NotaFiscalStateMachineTests
{
    public static IEnumerable<object[]> TransicoesValidas() => new[]
    {
        new object[] { StatusNotaFiscal.EmEmissao, StatusNotaFiscal.Autorizada },
        new object[] { StatusNotaFiscal.EmEmissao, StatusNotaFiscal.Rejeitada },
        new object[] { StatusNotaFiscal.EmEmissao, StatusNotaFiscal.Denegada },
        new object[] { StatusNotaFiscal.EmEmissao, StatusNotaFiscal.EmContingencia },
        new object[] { StatusNotaFiscal.EmContingencia, StatusNotaFiscal.Autorizada },
        new object[] { StatusNotaFiscal.EmContingencia, StatusNotaFiscal.Rejeitada },
        new object[] { StatusNotaFiscal.Autorizada, StatusNotaFiscal.CancelamentoEmAndamento },
        new object[] { StatusNotaFiscal.Autorizada, StatusNotaFiscal.Denegada },
        new object[] { StatusNotaFiscal.CancelamentoEmAndamento, StatusNotaFiscal.Cancelada },
        new object[] { StatusNotaFiscal.CancelamentoEmAndamento, StatusNotaFiscal.Autorizada },
    };

    public static IEnumerable<object[]> TransicoesInvalidas() => new[]
    {
        new object[] { StatusNotaFiscal.EmEmissao, StatusNotaFiscal.Cancelada },
        new object[] { StatusNotaFiscal.EmEmissao, StatusNotaFiscal.CancelamentoEmAndamento },
        new object[] { StatusNotaFiscal.Cancelada, StatusNotaFiscal.Autorizada },
        new object[] { StatusNotaFiscal.Cancelada, StatusNotaFiscal.EmEmissao },
        new object[] { StatusNotaFiscal.Rejeitada, StatusNotaFiscal.Autorizada },
        new object[] { StatusNotaFiscal.Denegada, StatusNotaFiscal.Autorizada },
        new object[] { StatusNotaFiscal.Inutilizada, StatusNotaFiscal.Autorizada },
        new object[] { StatusNotaFiscal.Autorizada, StatusNotaFiscal.EmContingencia },
    };

    [Theory]
    [MemberData(nameof(TransicoesValidas))]
    public void TransicaoValida_retorna_true_em_transicao_valida(StatusNotaFiscal de, StatusNotaFiscal para)
    {
        NotaFiscalStateMachine.TransicaoValida(de, para).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(TransicoesInvalidas))]
    public void TransicaoValida_retorna_false_em_transicao_invalida(StatusNotaFiscal de, StatusNotaFiscal para)
    {
        NotaFiscalStateMachine.TransicaoValida(de, para).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(TransicoesInvalidas))]
    public void EnsureTransicaoValida_lanca_em_transicao_invalida(StatusNotaFiscal de, StatusNotaFiscal para)
    {
        var act = () => NotaFiscalStateMachine.EnsureTransicaoValida(de, para);
        act.Should().Throw<TransicaoNotaFiscalInvalidaException>();
    }

    [Theory]
    [InlineData(StatusNotaFiscal.EmEmissao)]
    [InlineData(StatusNotaFiscal.Autorizada)]
    [InlineData(StatusNotaFiscal.Cancelada)]
    public void EnsureTransicaoValida_de_para_mesmo_status_e_no_op(StatusNotaFiscal status)
    {
        var act = () => NotaFiscalStateMachine.EnsureTransicaoValida(status, status);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(StatusNotaFiscal.Cancelada, true)]
    [InlineData(StatusNotaFiscal.Rejeitada, true)]
    [InlineData(StatusNotaFiscal.Denegada, true)]
    [InlineData(StatusNotaFiscal.Inutilizada, true)]
    [InlineData(StatusNotaFiscal.EmEmissao, false)]
    [InlineData(StatusNotaFiscal.Autorizada, false)]
    [InlineData(StatusNotaFiscal.EmContingencia, false)]
    [InlineData(StatusNotaFiscal.CancelamentoEmAndamento, false)]
    public void EhTerminal_classifica_corretamente(StatusNotaFiscal status, bool esperado)
    {
        NotaFiscalStateMachine.EhTerminal(status).Should().Be(esperado);
    }
}
