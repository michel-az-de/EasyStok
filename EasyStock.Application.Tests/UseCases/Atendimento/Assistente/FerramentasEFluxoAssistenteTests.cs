using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Assistente;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Assistente;

/// <summary>#2853: chave por loja para as ferramentas do Assistente e o fluxo que o console desenha.</summary>
public class FerramentasEFluxoAssistenteTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly ConfiguracaoAtendimento _configuracao;
    private readonly IFerramentaAgente[] _ferramentas =
        [Ferramenta("consultar_pedido"), Ferramenta("enviar_link_pagamento"), Ferramenta("escalar_para_dona")];

    public FerramentasEFluxoAssistenteTests()
    {
        _configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _configuracoes.GetByEmpresaIdAsync(_empresaId).Returns(_configuracao);
        _configuracoes.GetOrDefaultAsync(_empresaId).Returns(_configuracao);
    }

    private static IFerramentaAgente Ferramenta(string nome)
    {
        var f = Substitute.For<IFerramentaAgente>();
        f.Nome.Returns(nome);
        f.Descricao.Returns($"Faz {nome}");
        return f;
    }

    private Task<ConfiguracaoAtendimentoResult> Desligar(params string[] nomes) =>
        new AtualizarConfiguracaoAtendimentoUseCase(_configuracoes, Substitute.For<IUnitOfWork>(), ferramentas: _ferramentas)
            .ExecuteAsync(new AtualizarConfiguracaoAtendimentoCommand(_empresaId, null, null, null, null, null, null, null, null, null,
                FerramentasDesligadas: nomes));

    [Fact]
    public async Task DesligarFerramentaGravaEVoltaNoResultado()
    {
        var resultado = await Desligar("enviar_link_pagamento", "consultar_pedido", "consultar_pedido");

        resultado.FerramentasDesligadas.Should().Equal("consultar_pedido", "enviar_link_pagamento");
    }

    [Fact]
    public async Task ListaVaziaLigaTodasDeNovo()
    {
        await Desligar("consultar_pedido");

        var resultado = await Desligar();

        resultado.FerramentasDesligadas.Should().BeEmpty();
        _configuracao.FerramentasDesligadasJson.Should().BeNull();
    }

    [Fact]
    public async Task SaidaParaAEquipeNaoPodeSerDesligada()
    {
        var acao = () => Desligar("escalar_para_dona");

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*escalar_para_dona*equipe*");
        _configuracao.FerramentasDesligadasJson.Should().BeNull();
    }

    [Fact]
    public async Task FerramentaDesconhecidaEhRecusada()
    {
        var acao = () => Desligar("apagar_tudo");

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Ferramenta desconhecida: apagar_tudo.");
    }

    [Fact]
    public async Task FluxoTrazEtapasNaOrdemDoTurnoRoteiroEFerramentasComEstado()
    {
        _configuracao.DefinirFerramentasDesligadas(FerramentasDaLoja.Serializar(["consultar_pedido"]));
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true);

        var fluxo = await new ObterFluxoAssistenteUseCase(_configuracoes, llm, _ferramentas).ExecuteAsync(_empresaId);

        fluxo.ModeloDisponivel.Should().BeTrue();
        fluxo.Portoes.Select(p => p.Codigo).Should().Equal(
            PortoesDoAssistente.Situacao, PortoesDoAssistente.Vinculo, PortoesDoAssistente.Janela, PortoesDoAssistente.Chave,
            PortoesDoAssistente.Modelo, PortoesDoAssistente.Canal, PortoesDoAssistente.Equipe, PortoesDoAssistente.Bloqueado,
            PortoesDoAssistente.Pausado, PortoesDoAssistente.Limite, PortoesDoAssistente.Cota);
        fluxo.Roteiro.Should().BeEquivalentTo(RoteiroAtendimento.Padrao);
        fluxo.RoteiroPersonalizado.Should().BeFalse();
        fluxo.Ferramentas.Should().Equal(
            new FerramentaDoFluxo("consultar_pedido", "Faz consultar_pedido", Ligada: false, Travada: false),
            new FerramentaDoFluxo("enviar_link_pagamento", "Faz enviar_link_pagamento", Ligada: true, Travada: false),
            new FerramentaDoFluxo("escalar_para_dona", "Faz escalar_para_dona", Ligada: true, Travada: true));
        fluxo.Prompt.Should().Contain("Desligadas pela loja: consultar_pedido.");
        fluxo.ObservacoesLoja.Should().Be(_configuracao.ObservacoesLoja);
    }

    [Fact]
    public void PromptSemNadaDesligadoNaoGanhaOAviso()
    {
        PromptAtendimento.Montar(_configuracao).Should().NotContain("Desligadas pela loja");
    }
}
