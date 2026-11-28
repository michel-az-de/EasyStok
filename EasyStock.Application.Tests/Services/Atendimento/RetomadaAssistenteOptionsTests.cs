using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#3412: sem a seção na configuração, a equipe tem o prazo de primeira resposta padrão antes de o Assistente retomar.</summary>
public class RetomadaAssistenteOptionsTests
{
    [Fact]
    public void Sem_configuracao_o_prazo_e_o_de_primeira_resposta_padrao()
    {
        var opcoes = new RetomadaAssistenteOptions();
        opcoes.Ler(new ConfigurationBuilder().Build());

        opcoes.Prazo.Should().Be(TimeSpan.FromMinutes(ConfiguracaoAtendimento.SlaRespostaPadraoMinutos));
        opcoes.Prazo.Should().BeLessThan(opcoes.Janela);
    }

    [Fact]
    public void A_configuracao_ainda_sobrepoe_o_padrao()
    {
        var opcoes = new RetomadaAssistenteOptions();
        opcoes.Ler(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{RetomadaAssistenteOptions.Secao}:Segundos"] = "60" })
            .Build());

        opcoes.Prazo.Should().Be(TimeSpan.FromSeconds(60));
    }
}
