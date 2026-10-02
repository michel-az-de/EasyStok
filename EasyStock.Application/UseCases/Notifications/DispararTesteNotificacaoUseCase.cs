using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.Notifications;

/// <summary>Uma mensagem do outbox no resultado da consulta do disparo de teste. Nunca leva destinatário nem corpo.</summary>
public sealed record DisparoTesteMensagem(string Canal, string Status, string? Provider, int Tentativas, string? Erro);

/// <summary>Estado de um disparo de teste: o evento e, por mensagem do outbox, canal, status, provider, tentativas e erro curto.</summary>
public sealed record DisparoTesteConsulta(
    Guid EventoId, string Tipo, string EventoStatus, string? EventoErro, IReadOnlyList<DisparoTesteMensagem> Mensagens);

/// <summary>
/// Disparo de teste por tipo do catálogo de plataforma (N13). Publica o exemplo do tipo (<see cref="ExemplosDeEvento"/>)
/// com <c>teste: true</c> pelo caminho real (<see cref="INotificadorService.EnfileirarEventoAsync"/>): a rotina, os canais
/// e o remetente são os do catálogo. O destinatário é sempre o contato do próprio superadmin que chamou, lido de
/// <c>Usuario</c> com e-mail confirmado, nunca do corpo. A empresa é a do token ou, sem ela, a empresa padrão, com o
/// tenant ligado no escopo.
/// </summary>
public sealed class DispararTesteNotificacaoUseCase(
    ICurrentUserAccessor usuarioAtual,
    IUsuarioRepository usuarios,
    IEmpresaPadraoResolver empresaPadrao,
    INotificadorService notificador,
    IEventoNotificacaoRepository eventos,
    IOutboxNotificacaoRepository outbox,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    ILogger<DispararTesteNotificacaoUseCase> logger)
{
    private const int TamanhoMaximoDoErro = 200;

    /// <summary>Os tipos que aceitam disparo de teste, pelo nome (a lista que o 400 devolve).</summary>
    public static IReadOnlyList<string> TiposValidos { get; } =
        ExemplosDeEvento.Tipos.Select(t => t.ToString()).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <returns>O id do evento publicado, para a consulta do desfecho.</returns>
    public async Task<Guid> ExecuteAsync(TipoEventoNotificacao tipo, CancellationToken ct = default)
    {
        if (!ExemplosDeEvento.TryObter(tipo, out var exemplo))
            throw new UseCaseValidationException(
                "TIPO_FORA_DO_CATALOGO",
                $"Tipo fora do catálogo. Válidos: {string.Join(", ", TiposValidos)}.",
                TiposValidos);

        var superadmin = await usuarios.GetByIdAsync(usuarioAtual.UsuarioId)
            ?? throw new UseCaseValidationException("USUARIO_NAO_ENCONTRADO", "Superadmin não encontrado.");
        if (!superadmin.Ativo || !superadmin.EmailConfirmado || string.IsNullOrWhiteSpace(superadmin.Email))
            throw new UseCaseValidationException(
                "EMAIL_NAO_CONFIRMADO", "O disparo de teste vai para o e-mail do superadmin, e ele precisa estar confirmado.");

        var empresaId = await ResolverEmpresaAsync(ct);
        tenantContext.SetCurrentTenant(empresaId);

        var payload = new Dictionary<string, object?>(exemplo)
        {
            ["email"] = superadmin.Email,
            ["usuarioId"] = superadmin.Id.ToString(),
            ["teste"] = true,
        };

        var eventoId = await notificador.EnfileirarEventoAsync(tipo, empresaId, JsonSerializer.Serialize(payload), ct: ct);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Disparo de teste de notificação enfileirado tipo={Tipo} eventoId={EventoId} empresaId={EmpresaId}",
            tipo, eventoId, empresaId);
        return eventoId;
    }

    /// <summary>
    /// O desfecho do disparo, para o smoke de produção sem SQL. Resolve a empresa como <see cref="ExecuteAsync"/>. Nunca
    /// devolve texto: a categoria <c>Seguranca</c> não guarda corpo depois de terminar (N2).
    /// </summary>
    /// <returns><c>null</c> quando o evento não existe na empresa.</returns>
    public async Task<DisparoTesteConsulta?> ConsultarAsync(Guid eventoId, CancellationToken ct = default)
    {
        var empresaId = await ResolverEmpresaAsync(ct);
        tenantContext.SetCurrentTenant(empresaId);

        var evento = await eventos.ObterAsync(empresaId, eventoId, ct);
        if (evento is null) return null;

        var mensagens = await outbox.ListarDoEventoAsync(empresaId, eventoId, ct);
        return new DisparoTesteConsulta(
            evento.Id,
            evento.Tipo.ToString(),
            evento.Status.ToString(),
            Curto(evento.ErroProcessamento),
            mensagens
                .Select(m => new DisparoTesteMensagem(
                    m.Canal.ToString(), m.Status.ToString(), m.ProviderUsado, m.Tentativas, Curto(m.ErroUltimaTentativa)))
                .ToList());
    }

    private async Task<Guid> ResolverEmpresaAsync(CancellationToken ct)
    {
        if (usuarioAtual.EmpresaId != Guid.Empty) return usuarioAtual.EmpresaId;

        var padrao = await empresaPadrao.ResolverAsync(ct);
        if (padrao is { } id && id != Guid.Empty) return id;

        logger.LogWarning(
            "Disparo de teste sem empresa: o token não traz empresa e {Chave} não resolve.", EmpresaPadraoResolver.Chave);
        throw new UseCaseValidationException(
            "EMPRESA_PADRAO_NAO_RESOLVIDA",
            $"Sem empresa no token e sem empresa padrão: configure {EmpresaPadraoResolver.Chave} (CNPJ ou nome exato).");
    }

    private static string? Curto(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Length <= TamanhoMaximoDoErro ? texto : texto[..TamanhoMaximoDoErro];
}
