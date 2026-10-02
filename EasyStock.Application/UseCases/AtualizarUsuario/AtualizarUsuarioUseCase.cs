using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.AtualizarUsuario
{
    /// <param name="BaseUrl">Origem do front para o link de confirmação do e-mail novo, aceita só se estiver na allowlist.</param>
    public sealed record AtualizarUsuarioCommand(
        Guid UsuarioId,
        string Nome,
        string? Email,
        string? BaseUrl = null);

    public class AtualizarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        ICurrentUserAccessor currentUser,
        IUnitOfWork unitOfWork,
        TrocaDeContatoService trocaDeContato,
        ConvitesDeAcesso convites,
        IAuditLogRepository auditLogRepository,
        ILogger<AtualizarUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AtualizarUsuarioCommand command)
        {
            logger.LogInformation("Atualizando usuario {UsuarioId}", command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            // Isolamento multi-tenant: Usuario nao tem EmpresaId (escapa do filtro
            // global e da RLS), e GetByIdAsync roda com bypass de RLS por ser reusado
            // no fluxo pre-auth de refresh-token. Sem esta guarda, um Admin de tenant
            // poderia alterar nome/e-mail (vetor de account takeover) de usuario de
            // OUTRO tenant via PUT /api/usuarios/{id} (#764). SuperAdmin e cross-tenant
            // por design. Mesma mensagem do not-found para nao confirmar existencia.
            if (currentUser.Nivel != NivelAcesso.SuperAdmin &&
                !usuario.Empresas.Any(ue => ue.EmpresaId == currentUser.EmpresaId))
            {
                throw new UseCaseValidationException("Usuario nao encontrado.");
            }

            var trocaEmail = !string.IsNullOrWhiteSpace(command.Email) && command.Email.Trim() != usuario.Email;
            if (trocaEmail)
            {
                // N9: quem nunca aceitou o convite nunca teve acesso. A troca e IMEDIATA e dispensa o EmailPendente da N4
                // (a confirmacao em duas etapas protege conta que ja teve acesso), e o convite do contato antigo morre.
                if (usuario.ConvitePendente)
                {
                    await TrocarEmailDeConvidadoAsync(usuario, command.Email!.Trim());
                    usuario.Nome = command.Nome.Trim();
                    await usuarioRepository.UpdateAsync(usuario);
                    await unitOfWork.CommitAsync();
                    return;
                }

                // N4: o vinculo com a empresa corrente nao basta para trocar o e-mail (achado 6). Usuario de duas empresas
                // ou com perfil SuperAdmin so troca o proprio contato, ou pelo suporte. Alterar so o nome segue permitido.
                if (currentUser.Nivel != NivelAcesso.SuperAdmin && !PodeTrocarEmailPeloAdmin(usuario))
                    throw new UsuarioNaoAutorizadoException(
                        "Não é possível trocar o e-mail deste usuário por aqui: peça ao próprio usuário ou ao suporte.");

                // Mesmo fluxo de duas etapas e mesmo aviso: o Email so muda no clique do link enviado ao endereco novo.
                var empresaId = await trocaDeContato.ResolverEmpresaDoEventoAsync();
                await trocaDeContato.SolicitarTrocaDeEmailAsync(usuario, command.Email!, command.BaseUrl, empresaId);

                // Auditoria: quem pediu a troca, de quem, e por qual nivel (o SuperAdmin segue cross-tenant, com rastro).
                await auditLogRepository.AddAsync(AuditLog.Criar(
                    usuario.Id, "admin-troca-email-solicitada", true,
                    $"Troca de e-mail solicitada por {currentUser.UsuarioId} ({currentUser.Nivel}); espera confirmacao no endereco novo",
                    currentUser.Ip, currentUser.UserAgent));
            }

            usuario.Nome = command.Nome.Trim();
            usuario.AlteradoEm = DateTime.UtcNow;

            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
        }

        /// <summary>
        /// Convidado pendente (N9): grava o e-mail novo na hora, revoga os convites abertos e emite um novo para o contato
        /// novo (WhatsApp junto quando o telefone tem o atestado da dona). Auditado com quem trocou e por qual nivel.
        /// </summary>
        private async Task TrocarEmailDeConvidadoAsync(Usuario usuario, string novoEmail)
        {
            EmailValidator.EnsureValid(novoEmail);
            var existente = await usuarioRepository.GetByEmailAsync(novoEmail);
            if (existente is not null && existente.Id != usuario.Id)
                throw new UseCaseValidationException("Email ja cadastrado.");

            // Resolve a empresa (e liga o tenant do superadmin) antes de ler ou gravar, e falha cedo sem onde publicar.
            var empresaId = await trocaDeContato.ResolverEmpresaDoEventoAsync();
            convites.ExigirBaseDeLink();

            usuario.Email = novoEmail;
            usuario.EmailConfirmado = false;
            usuario.EmailPendente = null;
            usuario.AlteradoEm = DateTime.UtcNow;

            var comWhatsApp = await convites.ElegivelAoWhatsAppAsync(usuario);
            await convites.EmitirAsync(usuario, empresaId, comWhatsApp, currentUser.Ip, currentUser.UserAgent);

            await auditLogRepository.AddAsync(AuditLog.Criar(
                usuario.Id, "admin-troca-email-convidado", true,
                $"E-mail do convidado trocado por {currentUser.UsuarioId} ({currentUser.Nivel}); convites antigos revogados e novo emitido",
                currentUser.Ip, currentUser.UserAgent));
        }

        /// <summary>Exclusivo da empresa (um unico vinculo) e sem perfil de SuperAdmin.</summary>
        private static bool PodeTrocarEmailPeloAdmin(Usuario usuario) =>
            usuario.Empresas.Count == 1
            && !usuario.Perfis.Any(up => up.Perfil is { Nivel: NivelAcesso.SuperAdmin });
    }
}
