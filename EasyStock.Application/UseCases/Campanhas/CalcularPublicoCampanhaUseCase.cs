using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;

namespace EasyStock.Application.UseCases.Campanhas;

/// <summary>
/// O que a dona vê antes de enviar (S29): quantos vão receber, quantos saíram e por quê. A soma de
/// <see cref="ExcluidosPorMotivo"/> é o número de excluídos; <see cref="Preservados"/> são os que já
/// saíram (enfileirados, enviados, com falha ou que pediram) e não foram recalculados.
/// </summary>
public sealed record PublicoCampanhaResult(
    int Total,
    int Pendentes,
    int Preservados,
    IReadOnlyDictionary<string, int> ExcluidosPorMotivo,
    IReadOnlyList<string> Amostra);

/// <summary>
/// S29 (US-052, US-056, US-058, RN-39, RN-40): calcula o público e materializa os
/// <see cref="CampanhaDestinatario"/>. O filtro decide quem entra; cada um que entra fica
/// <see cref="StatusCampanhaDestinatario.Pendente"/> ou é excluído pelo primeiro motivo da ordem
/// bloqueado, sem consentimento, restrição, limite semanal, sem telefone. Idempotente: recalcular
/// refaz pendentes e excluídos, tira quem saiu do filtro e não toca em quem já saiu.
/// </summary>
public sealed class CalcularPublicoCampanhaUseCase(
    ICampanhaRepository repository, ICampanhaPublicoQueries queries, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    /// <summary>RN-40: no máximo uma campanha por cliente a cada 7 dias.</summary>
    public static readonly TimeSpan JanelaLimiteSemanal = TimeSpan.FromDays(7);

    public const int TamanhoAmostra = 10;

    public async Task<PublicoCampanhaResult> ExecuteAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default)
    {
        var campanha = await repository.ObterAsync(empresaId, campanhaId, ct)
            ?? throw new CampanhaNaoEncontradaException(campanhaId);
        campanha.GarantirPublicoRecalculavel();

        var agora = relogio.GetUtcNow().UtcDateTime;
        var filtro = campanha.Filtro;
        var candidatos = await queries.ListarCandidatosAsync(empresaId, campanhaId, filtro.ComprouItemId, ct);
        var publico = candidatos.Where(c => EntraNoFiltro(filtro, c, agora)).ToList();

        var existentes = (await repository.ListarDestinatariosAsync(empresaId, campanhaId, ct))
            .ToDictionary(d => d.ClienteId);
        var restricoes = campanha.TagsRestricao.ToHashSet(StringComparer.Ordinal);
        var destinatarios = new List<CampanhaDestinatario>();
        var criados = new List<CampanhaDestinatario>();
        var nomes = new Dictionary<Guid, string>();

        foreach (var cliente in publico)
        {
            nomes[cliente.ClienteId] = cliente.Nome;
            var motivo = MotivoExclusao(cliente, restricoes, agora);
            if (existentes.Remove(cliente.ClienteId, out var existente))
            {
                if (existente.Recalculavel) existente.Reclassificar(motivo);
                destinatarios.Add(existente);
                continue;
            }

            var destinatario = CampanhaDestinatario.Criar(campanha, cliente.ClienteId);
            if (motivo is not null) destinatario.Excluir(motivo);
            criados.Add(destinatario);
        }

        if (criados.Count > 0) await repository.AddDestinatariosAsync(criados, ct);
        destinatarios.AddRange(criados);

        // Quem sobrou saiu do filtro: fora se ainda não saiu; quem já saiu fica na campanha.
        var foraDoFiltro = existentes.Values.Where(d => d.Recalculavel).ToList();
        if (foraDoFiltro.Count > 0) repository.RemoverDestinatarios(foraDoFiltro);
        destinatarios.AddRange(existentes.Values.Where(d => !d.Recalculavel));

        await unitOfWork.CommitAsync();
        return Resumir(destinatarios, nomes);
    }

    private static bool EntraNoFiltro(FiltroCampanha filtro, CandidatoPublicoCampanha cliente, DateTime agora)
    {
        if (filtro.TagsExcluir.Any(cliente.Tags.Contains)) return false;
        if (filtro.Todos) return true;
        if (!filtro.TemPublico) return false;
        if (!filtro.TagsIncluir.All(cliente.Tags.Contains)) return false;
        return filtro.ComprouItemId is null
            || (cliente.UltimaCompraItemEm is { } compra
                && compra >= agora.AddDays(-(filtro.ComprouNosUltimosDias ?? FiltroCampanha.DiasMaximo)));
    }

    private static string? MotivoExclusao(CandidatoPublicoCampanha cliente, HashSet<string> restricoes, DateTime agora)
    {
        if (cliente.Bloqueado) return MotivoExclusaoCampanha.Bloqueado;
        if (!cliente.ConsentiuMarketing) return MotivoExclusaoCampanha.SemConsentimento;
        if (cliente.Tags.Any(restricoes.Contains)) return MotivoExclusaoCampanha.Restricao;
        if (cliente.UltimaCampanhaRecebidaEm > agora - JanelaLimiteSemanal) return MotivoExclusaoCampanha.LimiteSemanal;
        if (!cliente.TemTelefone) return MotivoExclusaoCampanha.SemTelefone;
        return null;
    }

    private static PublicoCampanhaResult Resumir(IReadOnlyList<CampanhaDestinatario> destinatarios, Dictionary<Guid, string> nomes)
    {
        var pendentes = destinatarios.Where(d => d.Status == StatusCampanhaDestinatario.Pendente).ToList();
        var porMotivo = destinatarios
            .Where(d => d.Status == StatusCampanhaDestinatario.Excluido)
            .GroupBy(d => d.MotivoExclusao!)
            .OrderBy(g => IndiceMotivo(g.Key))
            .ToDictionary(g => g.Key, g => g.Count());
        var amostra = pendentes
            .Select(d => nomes[d.ClienteId])
            .Order(StringComparer.CurrentCulture)
            .Take(TamanhoAmostra)
            .ToList();

        return new PublicoCampanhaResult(
            destinatarios.Count, pendentes.Count, destinatarios.Count(d => !d.Recalculavel), porMotivo, amostra);
    }

    private static int IndiceMotivo(string motivo)
    {
        var indice = MotivoExclusaoCampanha.Todos.ToList().IndexOf(motivo);
        return indice < 0 ? int.MaxValue : indice;
    }
}

/// <summary>S29: lista os destinatários da campanha, com o nome, para a dona conferir quem saiu e por quê.</summary>
public sealed class ListarDestinatariosCampanhaUseCase(ICampanhaRepository repository, ICampanhaPublicoQueries queries)
{
    public const int LimitePadrao = 500;

    public async Task<IReadOnlyList<DestinatarioCampanhaResumo>> ExecuteAsync(
        Guid empresaId, Guid campanhaId, StatusCampanhaDestinatario? status, CancellationToken ct = default)
    {
        _ = await repository.ObterAsync(empresaId, campanhaId, ct) ?? throw new CampanhaNaoEncontradaException(campanhaId);
        return await queries.ListarDestinatariosAsync(empresaId, campanhaId, status, LimitePadrao, ct);
    }
}
