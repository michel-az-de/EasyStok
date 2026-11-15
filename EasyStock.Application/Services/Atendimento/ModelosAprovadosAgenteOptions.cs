using EasyStock.Application.Ports.Output.Ai;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>Modelo que a loja pode escolher: id do provedor, rótulo da tela e custo relativo ("padrão", "econômico").</summary>
public sealed record ModeloAprovadoAgente(string Id, string Rotulo, string CustoRelativo);

/// <summary>
/// S68 (#2597, ADR-0066): lista aprovada do Assistente em <c>Anthropic:ModelosAprovados</c> (<c>Id</c>, <c>Rotulo</c>,
/// <c>CustoRelativo</c>). Um modelo só entra com eval verde. A lista nasce com o modelo atual (<c>Anthropic:ModeloAgente</c>,
/// ou <see cref="RequisicaoLlm.ModeloPadrao"/> sem a chave, como no cliente), sempre em primeiro. O turno só pede o modelo da loja quando ele está aqui; fora da lista vale o padrão.
/// </summary>
public sealed class ModelosAprovadosAgenteOptions
{
    public const string Secao = "Anthropic:ModelosAprovados";
    public const string ChaveModeloAtual = "Anthropic:ModeloAgente";

    public List<ModeloAprovadoAgente> Modelos { get; } = [];

    public bool Aprovado(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Modelos.Exists(m => string.Equals(m.Id, id.Trim(), StringComparison.Ordinal));

    /// <summary>O modelo que o turno pede ao provedor; nulo deixa o padrão da infraestrutura.</summary>
    public string? DoTurno(string? modeloDaLoja) => Aprovado(modeloDaLoja) ? modeloDaLoja!.Trim() : null;

    public void Ler(IConfiguration configuracao)
    {
        foreach (var secao in configuracao.GetSection(Secao).GetChildren())
        {
            var id = secao["Id"]?.Trim();
            if (string.IsNullOrEmpty(id) || Aprovado(id)) continue;
            var rotulo = secao["Rotulo"]?.Trim();
            Modelos.Add(new ModeloAprovadoAgente(id, string.IsNullOrEmpty(rotulo) ? id : rotulo, secao["CustoRelativo"]?.Trim() ?? string.Empty));
        }

        var atual = configuracao[ChaveModeloAtual]?.Trim();
        if (string.IsNullOrEmpty(atual)) atual = RequisicaoLlm.ModeloPadrao;
        var listado = Modelos.Find(m => string.Equals(m.Id, atual, StringComparison.Ordinal));
        if (listado is not null) Modelos.Remove(listado);
        Modelos.Insert(0, listado ?? new ModeloAprovadoAgente(atual, "Modelo atual", "padrão"));
    }
}
