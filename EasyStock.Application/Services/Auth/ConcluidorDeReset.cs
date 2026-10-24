using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// O que acontece depois que o segredo (link ou código) foi consumido (N8): troca a senha pela política existente, zera o
/// contador de falhas, derruba as sessões (<see cref="RevogadorSessoes"/>, N7), invalida os demais segredos abertos,
/// audita com IP e agente e enfileira o aviso <c>SenhaAlterada</c> (categoria Segurança, todos os canais verificados). Uma
/// regra só para o reset por link e por código não divergirem. Não faz commit: o use case que chama faz.
/// </summary>
public sealed class ConcluidorDeReset(
    IUsuarioRepository usuarios,
    IResetTokenRepository tokens,
    IAuditLogRepository auditLog,
    RevogadorSessoes revogadorSessoes,
    INotificadorService notificador,
    EmpresaDoEventoAnonimo empresaDoEvento,
    IPasswordHasher passwordHasher,
    TimeProvider relogio,
    ILogger<ConcluidorDeReset> logger)
{
    public async Task ConcluirAsync(Usuario usuario, string novaSenha, string acao, string? ip, string? userAgent)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;

        usuario.SenhaHash = passwordHasher.Hash(novaSenha);
        usuario.AlteradoEm = agora;
        usuario.ResetarTentativasFalha();
        await usuarios.UpdateAsync(usuario);

        // #1352: carimbo, refresh tokens num UPDATE e cache. O JWT em circulação deixa de valer.
        var tokensRevogados = await revogadorSessoes.RevogarAsync(usuario);

        // Usar um dos segredos mata o outro, e qualquer pedido anterior ainda aberto.
        await tokens.InvalidarAbertosAsync(usuario.Id, agora);

        await auditLog.AddAsync(AuditLog.Criar(
            usuario.Id, acao, true, $"Senha redefinida. {tokensRevogados} refresh tokens revogados.", ip, userAgent));

        await AvisarSenhaAlteradaAsync(usuario, agora);

        logger.LogInformation("Senha redefinida ({Acao}) para o usuario {UsuarioId}", acao, usuario.Id);
    }

    private async Task AvisarSenhaAlteradaAsync(Usuario usuario, DateTime agora)
    {
        var empresaId = await empresaDoEvento.ResolverAsync(usuario);
        if (empresaId is null) return;

        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.SenhaAlterada, empresaId.Value,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["usuarioId"] = usuario.Id,
                ["nome"] = usuario.Nome,
                ["email"] = usuario.Email,
                ["data"] = FormatarInstante(agora),
            }));
    }

    private static string FormatarInstante(DateTime utc) => HorarioBrasil.Formatar(utc);
}
