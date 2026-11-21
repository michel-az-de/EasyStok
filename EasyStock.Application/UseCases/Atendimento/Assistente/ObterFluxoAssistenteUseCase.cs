using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Assistente;

/// <param name="Travada">Saída para a equipe: não pode ser desligada.</param>
public sealed record FerramentaDoFluxo(string Nome, string Descricao, bool Ligada, bool Travada);

/// <param name="Portoes">Etapas que decidem se o robô responde, na ordem do turno.</param>
/// <param name="Roteiro">Roteiro efetivo da loja (o dela ou o padrão), com as etapas desligadas.</param>
/// <param name="Prompt">Instruções da loja como o modelo recebe, antes do caderno e do dossiê de cada conversa.</param>
/// <param name="ObservacoesLoja">O trecho do prompt que a loja escreve (S68), para o painel editar.</param>
public sealed record FluxoAssistenteResult(
    bool Ativo,
    bool ModeloDisponivel,
    IReadOnlyList<PortaoDoFluxo> Portoes,
    IReadOnlyList<EtapaRoteiroAtendimento> Roteiro,
    bool RoteiroPersonalizado,
    IReadOnlyList<FerramentaDoFluxo> Ferramentas,
    string Prompt,
    string? ObservacoesLoja = null);

/// <summary>
/// #2853: o fluxo do Assistente da loja para o mapa do console, da mesma fonte que o turno usa: as etapas de
/// <see cref="PortoesDoAssistente"/>, o roteiro de <see cref="RoteiroAtendimento"/>, as ferramentas registradas e o prompt.
/// </summary>
public sealed class ObterFluxoAssistenteUseCase(
    IConfiguracaoAtendimentoRepository configuracoes,
    IAgenteLlmClient llm,
    IEnumerable<IFerramentaAgente> ferramentas)
{
    public async Task<FluxoAssistenteResult> ExecuteAsync(Guid empresaId)
    {
        var configuracao = await configuracoes.GetByEmpresaIdAsync(empresaId) ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);
        var roteiro = RoteiroAtendimento.DaLoja(configuracao.RoteiroJson);
        var desligadas = FerramentasDaLoja.DaLoja(configuracao.FerramentasDesligadasJson);

        return new FluxoAssistenteResult(
            configuracao.Ativo,
            llm.Disponivel,
            PortoesDoAssistente.Catalogo,
            roteiro,
            !ReferenceEquals(roteiro, RoteiroAtendimento.Padrao),
            ferramentas
                .Select(f => new FerramentaDoFluxo(f.Nome, f.Descricao, !desligadas.Contains(f.Nome), FerramentasDaLoja.Travadas.Contains(f.Nome)))
                .OrderBy(f => f.Nome, StringComparer.Ordinal)
                .ToList(),
            PromptAtendimento.Montar(configuracao),
            configuracao.ObservacoesLoja);
    }
}
