using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>#2096: preferências dos avisos da operação e confirmação da encomenda pelo cliente.</summary>
public class AvisosOperacaoTests
{
    private static readonly DateTime Agora = new(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PadraoLigaTudoComOsTemposDaTela()
    {
        var a = AvisosOperacao.Padrao;

        a.ProntoSemEntregadorMinutos.Should().Be(15);
        a.EncomendaProximaHoras.Should().Be(2);
        a.EncomendaSemConfirmacaoHoras.Should().Be(24);
        a.EstoqueVencendoDias.Should().Be(2);
        a.EstoqueParadoDias.Should().Be(10);
        new[]
        {
            CodigosAviso.ClienteSemResposta, CodigosAviso.PedidoAtrasado, CodigosAviso.ImpressaoTravada,
            CodigosAviso.CaixaEsquecido, CodigosAviso.ProntoSemEntregador, CodigosAviso.EncomendaProxima,
            CodigosAviso.EncomendaSemConfirmacao, CodigosAviso.EstoqueParaConferir, CodigosAviso.ReclamacaoCliente,
            CodigosAviso.SuspeitaAtendimento,
        }.Should().OnlyContain(c => a.Ligado(c));
    }

    [Fact]
    public void TempoZeroDesligaEInterruptorFalsoDesliga()
    {
        var a = new AvisosOperacao { ProntoSemEntregadorMinutos = 0, PedidoAtrasado = false, EstoqueVencendoDias = 0 };

        a.Ligado(CodigosAviso.ProntoSemEntregador).Should().BeFalse();
        a.Ligado(CodigosAviso.PedidoAtrasado).Should().BeFalse();
        a.Ligado(CodigosAviso.EstoqueParaConferir).Should().BeTrue("o lote parado continua ligado");
        (a with { EstoqueParadoDias = 0 }).Ligado(CodigosAviso.EstoqueParaConferir).Should().BeFalse();
    }

    [Fact]
    public void CodigoDesconhecidoFicaLigado() =>
        new AvisosOperacao { ReclamacaoCliente = false }.Ligado("aviso_que_nasce_depois").Should().BeTrue();

    [Theory]
    [InlineData(4, 2, 24, 2, 10)]
    [InlineData(241, 2, 24, 2, 10)]
    [InlineData(15, 49, 24, 2, 10)]
    [InlineData(15, 2, 73, 2, 10)]
    [InlineData(15, 2, 24, 31, 10)]
    [InlineData(15, 2, 24, 2, 2)]
    [InlineData(-1, 2, 24, 2, 10)]
    public void ForaDaFaixaRecusaComFraseParaATela(int pronto, int proxima, int semConfirmacao, int vencendo, int parado)
    {
        var a = new AvisosOperacao
        {
            ProntoSemEntregadorMinutos = pronto, EncomendaProximaHoras = proxima, EncomendaSemConfirmacaoHoras = semConfirmacao,
            EstoqueVencendoDias = vencendo, EstoqueParadoDias = parado,
        };

        a.Invoking(x => x.Validado()).Should().Throw<RegraDeDominioVioladaException>().WithMessage("*ou 0 para desligar*");
    }

    [Fact]
    public void ConfiguracaoGuardaOsAvisosValidados()
    {
        var c = ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());

        c.DefinirAvisos(new AvisosOperacao { ProntoSemEntregadorMinutos = 30, SuspeitaAtendimento = false });

        c.Avisos.ProntoSemEntregadorMinutos.Should().Be(30);
        c.Avisos.SuspeitaAtendimento.Should().BeFalse();
        c.Invoking(x => x.DefinirAvisos(new AvisosOperacao { EncomendaProximaHoras = 100 }))
            .Should().Throw<RegraDeDominioVioladaException>();
        c.Avisos.EncomendaProximaHoras.Should().Be(2, "a gravação recusada não muda nada");
    }

    [Fact]
    public void DocumentoVazioDoBancoLeOPadrao()
    {
        // A migração grava '{}' nas linhas antigas: cada campo cai no padrão do record.
        var lido = JsonSerializer.Deserialize<AvisosOperacao>("{}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        lido.Should().Be(AvisosOperacao.Padrao);
    }

    private static Pedido Encomenda(string status = StatusPedidoMapper.Aguardando) =>
        new() { Id = Guid.NewGuid(), EmpresaId = Guid.NewGuid(), Status = status, AgendadoParaEm = Agora.AddDays(1) };

    [Fact]
    public void ConfirmarGuardaOPrimeiroInstante()
    {
        var p = Encomenda();

        p.ConfirmarPeloCliente(Agora).Should().BeTrue();
        p.ConfirmarPeloCliente(Agora.AddHours(1)).Should().BeFalse();

        p.ConfirmadoPeloClienteEm.Should().Be(Agora);
    }

    [Fact]
    public void DesfazerLimpaEDepoisEhNoOp()
    {
        var p = Encomenda();
        p.ConfirmarPeloCliente(Agora);

        p.DesfazerConfirmacaoDoCliente().Should().BeTrue();
        p.DesfazerConfirmacaoDoCliente().Should().BeFalse();
        p.ConfirmadoPeloClienteEm.Should().BeNull();
    }

    [Fact]
    public void PedidoSemDataOuFinalizadoNaoConfirma()
    {
        var semData = Encomenda();
        semData.AgendadoParaEm = null;
        semData.Invoking(p => p.ConfirmarPeloCliente(Agora)).Should().Throw<RegraDeDominioVioladaException>();

        Encomenda(StatusPedidoMapper.Entregue).Invoking(p => p.ConfirmarPeloCliente(Agora))
            .Should().Throw<RegraDeDominioVioladaException>();
    }
}
