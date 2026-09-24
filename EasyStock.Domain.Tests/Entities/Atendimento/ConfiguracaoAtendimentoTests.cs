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
}
