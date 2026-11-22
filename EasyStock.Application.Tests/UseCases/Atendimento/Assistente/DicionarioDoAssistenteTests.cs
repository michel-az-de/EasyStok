using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Assistente;

/// <summary>#3139: dicionário de termos da loja gravado pela configuração; campo ausente mantém, lista vazia apaga.</summary>
public class DicionarioDoAssistenteTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly ConfiguracaoAtendimento _configuracao;

    public DicionarioDoAssistenteTests()
    {
        _configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _configuracoes.GetByEmpresaIdAsync(_empresaId).Returns(_configuracao);
        _configuracoes.GetOrDefaultAsync(_empresaId).Returns(_configuracao);
    }

    private Task<ConfiguracaoAtendimentoResult> Gravar(IReadOnlyList<TermoDoDicionario>? termos) =>
        new AtualizarConfiguracaoAtendimentoUseCase(_configuracoes, Substitute.For<IUnitOfWork>())
            .ExecuteAsync(new AtualizarConfiguracaoAtendimentoCommand(_empresaId, null, null, null, null, null, null, null, null, null,
                Dicionario: termos));

    [Fact]
    public async Task ListaValidaGravaAparadaNaOrdemEVoltaNoResult()
    {
        var resultado = await Gravar([new(" Açaí ", " fruta roxa "), new("pote", "bolo no copo")]);

        resultado.Dicionario.Should().Equal(new TermoDoDicionario("Açaí", "fruta roxa"), new TermoDoDicionario("pote", "bolo no copo"));
        DicionarioDaLoja.DaLoja(_configuracao.DicionarioJson).Select(t => t.Termo).Should().Equal("Açaí", "pote");
    }

    [Fact]
    public async Task LeituraDevolveOsTermosGravados()
    {
        await Gravar([new("pote", "bolo no copo")]);

        var lido = await new ObterConfiguracaoAtendimentoUseCase(_configuracoes)
            .ExecuteAsync(new ObterConfiguracaoAtendimentoQuery(_empresaId));

        lido.Dicionario.Should().Equal(new TermoDoDicionario("pote", "bolo no copo"));
    }

    [Fact]
    public async Task CampoAusenteMantemODicionarioGravado()
    {
        await Gravar([new("pote", "bolo no copo")]);

        var resultado = await Gravar(null);

        resultado.Dicionario.Should().Equal(new TermoDoDicionario("pote", "bolo no copo"));
        _configuracao.DicionarioJson.Should().NotBeNull();
    }

    [Fact]
    public async Task ListaVaziaApagaODicionario()
    {
        await Gravar([new("pote", "bolo no copo")]);

        var resultado = await Gravar([]);

        resultado.Dicionario.Should().BeEmpty();
        _configuracao.DicionarioJson.Should().BeNull();
    }

    [Fact]
    public async Task TermoRepetidoSemDiferenciarAcentoEhRecusadoSemAlterarAConfiguracao()
    {
        await Gravar([new("pote", "bolo no copo")]);
        var antes = _configuracao.DicionarioJson;

        var acao = () => Gravar([new("Açaí", "fruta"), new("acai", "outra")]);

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*repetido*");
        _configuracao.DicionarioJson.Should().Be(antes);
    }
}
