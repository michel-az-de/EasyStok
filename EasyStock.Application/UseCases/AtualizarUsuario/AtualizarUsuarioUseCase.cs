using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.AtualizarUsuario
{
    /// <param name="BaseUrl">Origem do front para o link de confirmação do e-mail novo, aceita só se estiver na allowlist.</param>
    /// <param name="Motivo">Justificativa do suporte ao trocar o e-mail da dona (#2070, 10+ caracteres).</param>
    public sealed record AtualizarUsuarioCommand(
        Guid UsuarioId,
        string Nome,
        string? Email,
        string? BaseUrl = null,
        string? Motivo = null);

    /// <param name="SuporteTrocouEmailDaDona">O suporte trocou o e-mail da dona: vai também para a auditoria da plataforma.</param>
    public sealed record AtualizarUsuarioResult(bool SuporteTrocouEmailDaDona = false, string? Motivo = null);

    public class AtualizarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        ICurrentUserAccessor currentUser,
        IUnitOfWork unitOfWork,
        TrocaDeContatoService trocaDeContato,
        ConvitesDeAcesso convites,
        HierarquiaDeUsuarios hierarquia,
        IAuditLogRepository auditLogRepository,
        ILogger<AtualizarUsuarioUseCase> logger)
    {
        public async Task<AtualizarUsuarioResult> ExecuteAsync(AtualizarUsuarioCommand command)
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

            var empresaId = await EmpresaDaHierarquiaAsync(usuario, trocaEmail);
            if (trocaEmail && usuario.ConvitePendente) convites.ExigirBaseDeLink();

            // #2070 (A1): trocar o e-mail de outra Administração ou da dona é da política, com ator e alvo relidos depois
            // da trava. O e-mail da dona, só o suporte troca, e com motivo.
            var acao = trocaEmail ? AcaoSobreUsuario.TrocarEmail : AcaoSobreUsuario.EditarNome;
            return await hierarquia.ExecutarAsync(
                new PedidoDeHierarquia(empresaId, usuario.Id, acao, Motivo: command.Motivo),
                async (contexto, _) =>
                {
                    var alvo = contexto.Alvo!;
                    if (trocaEmail)
                    {
                        // N9: quem nunca aceitou o convite nunca teve acesso. A troca e IMEDIATA e dispensa o EmailPendente da N4
                        // (a confirmacao em duas etapas protege conta que ja teve acesso), e o convite do contato antigo morre.
                        if (alvo.ConvitePendente)
                        {
                            await TrocarEmailDeConvidadoAsync(alvo, command.Email!.Trim(), empresaId, contexto.ParticipanteAlvo.EhDona);
                            alvo.Nome = command.Nome.Trim();
                            await usuarioRepository.UpdateAsync(alvo);
                            await unitOfWork.CommitAsync();
                            return Resultado(contexto);
                        }

                        // N4: o vinculo com a empresa corrente nao basta para trocar o e-mail (achado 6). Usuario de duas empresas
                        // ou com perfil SuperAdmin so troca o proprio contato, ou pelo suporte. Alterar so o nome segue permitido.
                        // A leitura de antes da trava enxerga todas as empresas da pessoa; a de depois, so a desta.
                        if (!contexto.Ator.EhSuporteGlobal && !PodeTrocarEmailPeloAdmin(usuario))
                            throw new UsuarioNaoAutorizadoException(
                                "Não é possível trocar o e-mail deste usuário por aqui: peça ao próprio usuário ou ao suporte.");

                        // Mesmo fluxo de duas etapas e mesmo aviso: o Email so muda no clique do link enviado ao endereco novo.
                        await trocaDeContato.SolicitarTrocaDeEmailAsync(alvo, command.Email!, command.BaseUrl, empresaId);

                        // Auditoria: quem pediu a troca, de quem, e por qual nivel (o SuperAdmin segue cross-tenant, com rastro).
                        await auditLogRepository.AddAsync(AuditLog.Criar(
                            alvo.Id, "admin-troca-email-solicitada", true,
                            $"Troca de e-mail solicitada por {currentUser.UsuarioId} ({currentUser.Nivel}); espera confirmacao no endereco novo",
                            currentUser.Ip, currentUser.UserAgent));
                    }

                    alvo.Nome = command.Nome.Trim();
                    alvo.AlteradoEm = DateTime.UtcNow;

                    await usuarioRepository.UpdateAsync(alvo);
                    await unitOfWork.CommitAsync();
                    return Resultado(contexto);
                });

            AtualizarUsuarioResult Resultado(ContextoDaHierarquia contexto) =>
                new(trocaEmail && contexto.SuporteSobreDona, contexto.Motivo);
        }

        /// <summary>
        /// Convidado pendente (N9): grava o e-mail novo na hora, revoga os convites abertos e emite um novo para o contato
        /// novo (WhatsApp junto quando o telefone tem o atestado da dona; a dona convidada, só por e-mail). Auditado com
        /// quem trocou e por qual nivel.
        /// </summary>
        private async Task TrocarEmailDeConvidadoAsync(Usuario usuario, string novoEmail, Guid empresaId, bool ehDona)
        {
            EmailValidator.EnsureValid(novoEmail);
            var existente = await usuarioRepository.GetByEmailAsync(novoEmail);
            if (existente is not null && existente.Id != usuario.Id)
                throw new UseCaseValidationException("Email ja cadastrado.");

            usuario.Email = Usuario.NormalizarEmail(novoEmail);
            usuario.EmailConfirmado = false;
            usuario.EmailPendente = null;
            usuario.AlteradoEm = DateTime.UtcNow;

            var comWhatsApp = !ehDona && await convites.ElegivelAoWhatsAppAsync(usuario);
            await convites.EmitirAsync(usuario, empresaId, comWhatsApp, currentUser.Ip, currentUser.UserAgent);

            await auditLogRepository.AddAsync(AuditLog.Criar(
                usuario.Id, "admin-troca-email-convidado", true,
                $"E-mail do convidado trocado por {currentUser.UsuarioId} ({currentUser.Nivel}); convites antigos revogados e novo emitido",
                currentUser.Ip, currentUser.UserAgent));
        }

        /// <summary>
        /// A empresa cuja hierarquia decide: a do token. O suporte sem empresa no token usa, na troca de e-mail, a empresa
        /// do evento (a padrão, com o tenant ligado, antes da trava, falhando cedo sem onde publicar); só no nome, a empresa
        /// ativa da pessoa.
        /// </summary>
        private async Task<Guid> EmpresaDaHierarquiaAsync(Usuario usuario, bool trocaEmail)
        {
            if (currentUser.EmpresaId != Guid.Empty) return currentUser.EmpresaId;
            if (!trocaEmail && usuario.Empresas.FirstOrDefault(e => e.Ativo) is { } vinculo) return vinculo.EmpresaId;
            return await trocaDeContato.ResolverEmpresaDoEventoAsync();
        }

        /// <summary>Exclusivo da empresa (um unico vinculo) e sem perfil de SuperAdmin.</summary>
        private static bool PodeTrocarEmailPeloAdmin(Usuario usuario) =>
            usuario.Empresas.Count == 1 && !usuario.EhSuperAdmin();
    }
}
