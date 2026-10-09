using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

public class ConfiguracaoAtendimentoTests
{
    private static ConfiguracaoAtendimento Padrao() => ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());

    [Fact]
    public void PadraoValido()
    {
        var config = Padrao();

        config.NivelSugestao.Should().Be(NivelSugestaoAtendimento.Discreto);
        config.RespiroMinutos.Should().Be(40);
        config.TempoPreparoPadraoMinutos.Should().Be(60);
        config.Ativo.Should().BeTrue();
        config.SlaRespostaMinutos.Should().Be(ConfiguracaoAtendimento.SlaRespostaPadraoMinutos).And.Be(5);
        config.Tom.Should().NotBeNullOrWhiteSpace();
        config.SaudacaoPrimeiroContato.Should().NotBeNullOrWhiteSpace();
        config.SaudacaoRetorno.Should().NotBeNullOrWhiteSpace();
        config.WebhookVerificadoEm.Should().BeNull();
        config.UltimaMensagemRecebidaEm.Should().BeNull();
    }

    [Fact]
    public void Atualizar_com_respiro_negativo_lanca()
    {
        var config = Padrao();
        var act = () => config.Atualizar(null, null, null, null, null, null, respiroMinutos: -1, null, null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Atualizar_com_tempo_preparo_zero_lanca()
    {
        var config = Padrao();
        var act = () => config.Atualizar(null, null, null, null, null, null, null, tempoPreparoPadraoMinutos: 0, null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Atualizar_muda_tom_e_nivel()
    {
        var config = Padrao();
        config.Atualizar("direto e rápido", NivelSugestaoAtendimento.Ativo, null, null, null, null, null, null, null);

        config.Tom.Should().Be("direto e rápido");
        config.NivelSugestao.Should().Be(NivelSugestaoAtendimento.Ativo);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(240)]
    public void Atualizar_aceita_sla_dentro_da_faixa(int minutos)
    {
        var config = Padrao();
        config.Atualizar(null, null, null, null, null, null, null, null, null, slaRespostaMinutos: minutos);

        config.SlaRespostaMinutos.Should().Be(minutos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(241)]
    public void Atualizar_com_sla_fora_da_faixa_lanca_e_nao_muda(int minutos)
    {
        var config = Padrao();
        var act = () => config.Atualizar(null, null, null, null, null, null, null, null, null, slaRespostaMinutos: minutos);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*1 e 240*");
        config.SlaRespostaMinutos.Should().Be(5);
    }

    [Fact]
    public void Atualizar_sem_sla_mantem_o_atual()
    {
        var config = Padrao();
        config.Atualizar(null, null, null, null, null, null, null, null, null, slaRespostaMinutos: 12);
        config.Atualizar("outro tom", null, null, null, null, null, null, null, null);

        config.SlaRespostaMinutos.Should().Be(12);
    }
}
