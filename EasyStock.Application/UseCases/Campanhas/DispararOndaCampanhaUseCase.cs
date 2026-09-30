using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Campanhas;

/// <summary>Quem pediu a onda: o horário agendado (só a primeira) ou a dona (as seguintes, RN-42).</summary>
public enum OrigemOndaCampanha
{
    Agendamento = 1,
    Dona = 2
}

/// <param name="Excluidos">Pendentes que saíram no disparo (bloqueio, SAIR, outra campanha na semana, telefone).</param>
/// <param name="PendentesRestantes">Quem fica para a próxima onda.</param>
public sealed record OndaCampanhaResult(CampanhaResult Campanha, int Onda, int Enfileirados, int Excluidos, int PendentesRestantes);

/// <summary>
/// S30 (US-053, US-054, RN-41, RN-42): dispara a onda N. Revalida as exclusões do público no instante
/// do disparo, escolhe até <c>TamanhoOnda</c> pendentes (quem já comprou o item da campanha primeiro,
/// a compra mais recente na frente; depois por nome), enfileira no outbox com
/// <c>ProximaTentativaEm = DisparoEm</c> e marca cada um <c>Enfileirado</c>. A primeira onda sai pelo
/// job no horário agendado; as seguintes só pela dona.
/// </summary>
public sealed class DispararOndaCampanhaUseCase(
    ICampanhaRepository repository,
    ICampanhaPublicoQueries queries,
    EnfileiradorMensagensCampanha enfileirador,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<OndaCampanhaResult> ExecuteAsync(
        Guid empresaId, Guid campanhaId, OrigemOndaCampanha origem, CancellationToken ct = default)
    {
        var campanha = await repository.ObterAsync(empresaId, campanhaId, ct)
            ?? throw new CampanhaNaoEncontradaException(campanhaId);

        if (origem == OrigemOndaCampanha.Agendamento && campanha.Status != StatusCampanha.Agendada)
            throw new RegraDeDominioVioladaException($"Campanha {campanha.Status}: a onda automática é só a primeira.");
        if (origem == OrigemOndaCampanha.Dona && campanha.OndaAtual == 0)
            throw new RegraDeDominioVioladaException("A primeira onda sai sozinha no horário agendado.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        var pendentes = await repository.ListarPendentesAsync(empresaId, campanhaId, ct);
        var candidatos = (await queries.ListarCandidatosAsync(empresaId, campanhaId, campanha.Filtro.ComprouItemId, ct))
            .ToDictionary(c => c.ClienteId);
        var restricoes = campanha.TagsRestricao.ToHashSet(StringComparer.Ordinal);

        // O público pode ter sido calculado dias antes: bloqueio, SAIR, outra campanha na semana e
        // telefone são conferidos de novo agora. Cliente que deixou de estar ativo fica pendente, fora da onda.
        var elegiveis = new List<(CampanhaDestinatario Destinatario, CandidatoPublicoCampanha Cliente, string Telefone)>();
        var excluidos = 0;
        foreach (var destinatario in pendentes)
        {
            if (!candidatos.TryGetValue(destinatario.ClienteId, out var cliente)) continue;

            var telefone = TelefoneE164(cliente.Telefone);
            var motivo = CalcularPublicoCampanhaUseCase.MotivoExclusao(cliente, restricoes, agora)
                ?? (telefone is null ? MotivoExclusaoCampanha.SemTelefone : null);
            if (motivo is not null)
            {
                destinatario.Reclassificar(motivo);
                excluidos++;
                continue;
            }

            elegiveis.Add((destinatario, cliente, telefone!));
        }

        if (origem == OrigemOndaCampanha.Dona && elegiveis.Count == 0)
            throw new RegraDeDominioVioladaException("Nenhum cliente pendente para a próxima onda.");

        // RN-41: estoque menor que o público → quem já comprou o item vai primeiro, a compra mais recente na frente.
        var onda = elegiveis
            .OrderByDescending(e => e.Cliente.UltimaCompraItemEm.HasValue)
            .ThenByDescending(e => e.Cliente.UltimaCompraItemEm)
            .ThenBy(e => e.Cliente.Nome, StringComparer.CurrentCulture)
            .ThenBy(e => e.Cliente.ClienteId)
            .Take(campanha.TamanhoOnda ?? int.MaxValue)
            .ToList();

        var numero = campanha.IniciarOnda(agora);
        var mensagens = await enfileirador.EnfileirarAsync(
            campanha,
            MensagemCampanha.Onda,
            onda.Select(e => new ContatoCampanha(e.Cliente.ClienteId, e.Cliente.Nome, e.Telefone)).ToList(),
            campanha.DisparoEm!.Value,
            agora,
            ct);
        foreach (var (destinatario, cliente, _) in onda)
            destinatario.Enfileirar(numero, mensagens[cliente.ClienteId]);

        await unitOfWork.CommitAsync();

        return new OndaCampanhaResult(
            CampanhaResult.De(campanha),
            numero,
            onda.Count,
            excluidos,
            pendentes.Count(d => d.Status == StatusCampanhaDestinatario.Pendente));
    }

    internal static string? TelefoneE164(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        try
        {
            return NormalizadorTelefone.NormalizarE164Br(telefone);
        }
        catch (TelefoneInvalidoException)
        {
            return null;
        }
    }
}
