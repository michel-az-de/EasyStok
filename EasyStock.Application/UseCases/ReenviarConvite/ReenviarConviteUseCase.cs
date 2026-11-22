using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.ReenviarConvite;

public sealed record ReenviarConviteCommand(Guid UsuarioId) : ICommand;

/// <summary>
/// Reenvio do convite pela dona (N9): revoga os convites abertos e emite novos, no máximo
/// <see cref="ConvitesDeAcesso.EmissoesPorHora"/> por hora por usuário, contados nas linhas <c>Convite</c> de <c>reset_tokens</c>
/// (a criação do usuário também ocupa uma vaga). Só vale para convite pendente e nunca para superadmin. O WhatsApp vai junto
/// quando o telefone tem o atestado da dona, menos para a dona, que só recebe por e-mail (#2070). Mesma guarda de tenant do
/// <c>AtualizarUsuarioUseCase</c> (#764), com a mesma mensagem do não encontrado para não confirmar a existência entre
/// empresas. Quem pode reenviar a quem é da política de hierarquia (#2070).
/// </summary>
public sealed class ReenviarConviteUseCase(
    IUsuarioRepository usuarioRepository,
    ICurrentUserAccessor currentUser,
    ConvitesDeAcesso convites,
    HierarquiaDeUsuarios hierarquia,
    ITenantContextAccessor tenant,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReenviarConviteUseCase> logger) : IUseCase<ReenviarConviteCommand, bool>
{
    public async Task<bool> ExecuteAsync(ReenviarConviteCommand command)
    {
        var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
            ?? throw new UseCaseValidationException("Usuario nao encontrado.");

        var nivel = currentUser.Nivel;
        if (nivel != NivelAcesso.SuperAdmin && !usuario.Empresas.Any(ue => ue.Ativo && ue.EmpresaId == currentUser.EmpresaId))
            throw new UseCaseValidationException("Usuario nao encontrado.");

        if (usuario.EhSuperAdmin())
            throw new UseCaseValidationException("CONVITE_SUPERADMIN", "Superadmin não recebe convite.");
        if (!usuario.Ativo || !usuario.ConvitePendente) throw NaoPendente();

        if (await convites.EmissoesNaUltimaHoraAsync(usuario.Id) >= ConvitesDeAcesso.EmissoesPorHora)
        {
            logger.LogWarning("Reenvio de convite limitado para o usuario {UsuarioId} (3 por hora)", usuario.Id);
            throw new LimitePedidosAcessoExcedidoException((int)TimeSpan.FromHours(1).TotalSeconds);
        }

        var empresaId = EmpresaDoEvento(usuario);
        return await hierarquia.ExecutarAsync(
            new PedidoDeHierarquia(empresaId, usuario.Id, AcaoSobreUsuario.ReenviarConvite),
            async (contexto, _) =>
            {
                var alvo = contexto.Alvo!;
                if (!alvo.Ativo || !alvo.ConvitePendente) throw NaoPendente();

                var comWhatsApp = !contexto.ParticipanteAlvo.EhDona && await convites.ElegivelAoWhatsAppAsync(alvo);
                await convites.EmitirAsync(alvo, empresaId, comWhatsApp, currentUser.Ip, currentUser.UserAgent);

                var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
                await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                    alvo.Id, "convite-reenviado",
                    $"Convite reenviado por {autor}{(comWhatsApp ? ", também pelo WhatsApp" : string.Empty)}",
                    currentUser, empresaId));
                await unitOfWork.CommitAsync();

                logger.LogInformation("Convite reenviado ao usuario {UsuarioId} por {Executor}", alvo.Id, currentUser.UsuarioId);
                return true;
            });
    }

    /// <summary>
    /// A empresa em que nasce o evento: a do token da dona. O superadmin (sem empresa no token) usa a empresa ativa do
    /// próprio usuário e liga o tenant no escopo, senão a RLS barra o INSERT do evento.
    /// </summary>
    private Guid EmpresaDoEvento(Usuario usuario)
    {
        if (currentUser.EmpresaId != Guid.Empty) return currentUser.EmpresaId;

        var empresaId = usuario.Empresas.FirstOrDefault(ue => ue.Ativo)?.EmpresaId
            ?? throw new UseCaseValidationException("Usuario nao encontrado.");
        tenant.SetCurrentTenant(empresaId);
        return empresaId;
    }

    private static UseCaseValidationException NaoPendente() =>
        new("CONVITE_NAO_PENDENTE", "Este usuário não tem convite pendente.");
}
