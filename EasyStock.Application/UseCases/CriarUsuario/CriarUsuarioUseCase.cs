using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Auth;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Services;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.CriarUsuario
{
    /// <param name="Senha">
    /// Opcional desde a N9. Sem senha nasce o convidado (hash inutilizável) e sai um convite com link; com senha (legado)
    /// nada muda. Fica obsoleto até a M7.2 trocar o formulário.
    /// </param>
    /// <param name="Telefone">Opcional. Só vira canal de convite com <paramref name="AtestaOptInWhatsApp"/>.</param>
    /// <param name="AtestaOptInWhatsApp">A dona atesta que a pessoa aceitou receber o convite por WhatsApp.</param>
    /// <param name="Dona">
    /// #2070: nasce dona. Só a dona ou o suporte pedem; exige perfil de nível Admin e e-mail novo, e o convite vai só por
    /// e-mail (o WhatsApp atestado é ignorado), porque a conta da dona é provada pela caixa de e-mail.
    /// </param>
    public sealed record CriarUsuarioCommand(
        Guid EmpresaId,
        string Nome,
        string Email,
        string? Senha,
        Guid? PerfilId,
        Guid? LojaId,
        string? Telefone = null,
        bool AtestaOptInWhatsApp = false,
        bool Dona = false);

    /// <param name="ConviteEnviado">Nasceu convidado: o convite foi enfileirado (e-mail, e WhatsApp quando atestado).</param>
    public sealed record CriarUsuarioResult(
        Guid UsuarioId,
        string Nome,
        string Email,
        bool ConviteEnviado = false);

    public class CriarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IPerfilRepository perfilRepository,
        IConsentimentoRepository consentimentoRepository,
        ConvitesDeAcesso convites,
        HierarquiaDeUsuarios hierarquia,
        ICurrentUserAccessor currentUser,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ILogger<CriarUsuarioUseCase> logger)
    {
        public async Task<CriarUsuarioResult> ExecuteAsync(CriarUsuarioCommand command)
        {
            logger.LogInformation("Criando usuario para empresa {EmpresaId}", command.EmpresaId);

            EmailValidator.EnsureValid(command.Email);

            var convidar = string.IsNullOrEmpty(command.Senha);
            if (command.Dona && !convidar)
                throw new UseCaseValidationException("DONA_SO_POR_CONVITE", "A dona entra pelo convite por e-mail: não informe senha.");
            TelefoneE164? telefone = null;
            if (convidar)
            {
                // Tudo o que pode recusar vem antes de gravar: o convite nunca nasce pela metade.
                convites.ExigirBaseDeLink();
                telefone = LerTelefone(command.Telefone);
            }
            // Vale tambem para o cadastro legado com senha: antes so o convite barrava SuperAdmin.
            var perfil = await ExigirPerfilAtribuivelAsync(command.PerfilId, command.EmpresaId);

            // #2070: quem cria quem é da política (a Administração não dá perfil de nível Admin nem cria dona), com o ator
            // relido depois da trava da hierarquia da empresa.
            var resultado = await hierarquia.ExecutarAsync(
                new PedidoDeHierarquia(command.EmpresaId, null, AcaoSobreUsuario.Criar, perfil?.NivelNaEmpresa, command.Dona),
                (_, _) => CriarAsync(command, perfil, convidar, telefone));

            logger.LogInformation("Usuario criado: {UsuarioId} (convidado: {Convidado})", resultado.UsuarioId, convidar);
            return resultado;
        }

        private async Task<CriarUsuarioResult> CriarAsync(CriarUsuarioCommand command, Perfil? perfil, bool convidar, TelefoneE164? telefone)
        {
            // A dona nova nunca herda uma conta: o e-mail precisa ser novo, e a resposta não sugere marcar quem já existe (A4).
            var emailExistente = await usuarioRepository.GetByEmailAsync(command.Email);
            if (emailExistente is not null)
                throw command.Dona
                    ? new HierarquiaRecusadaException(
                        CodigosDaHierarquia.DonaEmailExistente, HierarquiaDeUsuarios.Mensagem(CodigosDaHierarquia.DonaEmailExistente))
                    : new UseCaseValidationException("Email ja cadastrado.");

            var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);
            var totalUsuarios = await usuarioRepository.CountByEmpresaAsync(command.EmpresaId);
            if (assinatura?.Plano is not null && !assinatura.Plano.UsuariosSaoIlimitados && totalUsuarios >= assinatura.Plano.LimiteUsuarios)
                throw new PlanoLimiteAtingidoException("usuarios");

            var agora = DateTime.UtcNow;
            var usuario = convidar
                ? Usuario.CriarConvidado(command.Nome.Trim(), command.Email.Trim())
                : Usuario.Criar(command.Nome.Trim(), command.Email.Trim(), passwordHasher.Hash(command.Senha!));
            if (telefone is not null) usuario.DefinirTelefone(telefone);

            await usuarioRepository.AddAsync(usuario);

            var usuarioEmpresa = new UsuarioEmpresa
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                EmpresaId = command.EmpresaId,
                Ativo = true,
                EhDona = command.Dona,
                CriadoEm = agora
            };

            await usuarioEmpresaRepository.AddAsync(usuarioEmpresa);

            if (command.PerfilId.HasValue)
            {
                var usuarioPerfil = new UsuarioPerfil
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = usuario.Id,
                    EmpresaId = command.EmpresaId,
                    PerfilId = command.PerfilId.Value,
                    LojaId = command.LojaId,
                    AtribuidoEm = agora,
                    AtribuidoPorId = currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId
                };

                await usuarioPerfilRepository.AddAsync(usuarioPerfil);
            }

            if (convidar)
                await EmitirConviteAsync(usuario, command, !command.Dona && telefone is not null && command.AtestaOptInWhatsApp);

            // #1753: o nascimento do acesso deixa rastro com o perfil dado e o nome de quem convidou.
            var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
            var comPerfil = perfil is null ? "sem perfil" : $"com o perfil {perfil.Nome}";
            var fato = convidar ? "Convite enviado" : "Acesso criado";
            await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                usuario.Id, convidar ? RastroDeUsuarios.Convidado : RastroDeUsuarios.Criado,
                $"{fato} {comPerfil} por {autor}", currentUser, command.EmpresaId));
            if (command.Dona)
                await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                    usuario.Id, RastroDeUsuarios.DonaMarcada, $"Cadastrada como dona por {autor}", currentUser, command.EmpresaId));

            await unitOfWork.CommitAsync();

            return new CriarUsuarioResult(usuario.Id, usuario.Nome, usuario.Email, convidar);
        }

        /// <summary>
        /// O atestado da dona vira o consentimento explícito de WhatsApp em Segurança, com ela como autora e o IP dela.
        /// Sem atestado nada é gravado e o convite sai só por e-mail.
        /// </summary>
        private async Task EmitirConviteAsync(Usuario usuario, CriarUsuarioCommand command, bool comWhatsApp)
        {
            if (comWhatsApp)
                await consentimentoRepository.AddAsync(ConsentimentoNotificacao.Registrar(
                    usuario.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca, optIn: true,
                    atualizadoPor: $"usuario:{currentUser.UsuarioId}", ipOrigem: currentUser.Ip));

            await convites.EmitirAsync(
                usuario, command.EmpresaId, comWhatsApp, currentUser.Ip, currentUser.UserAgent, revogarAbertos: false);
        }

        private static TelefoneE164? LerTelefone(string? telefone)
        {
            if (string.IsNullOrWhiteSpace(telefone)) return null;
            try
            {
                return TelefoneE164.From(telefone);
            }
            catch (TelefoneInvalidoException ex)
            {
                throw new UseCaseValidationException("TELEFONE_INVALIDO", ex.Message);
            }
        }

        /// <summary>
        /// Superadmin nunca nasce por convite: o perfil global exige a criação administrativa, com senha própria.
        /// #1753: perfil de outra empresa também não entra, a mesma regra de <c>PUT {id}/perfis</c>. #2070: nem perfil de
        /// empresa com nível SuperAdmin (<see cref="Perfil.AtribuivelNaEmpresa"/>).
        /// </summary>
        private async Task<Perfil?> ExigirPerfilAtribuivelAsync(Guid? perfilId, Guid empresaId)
        {
            if (perfilId is null) return null;

            var perfil = await perfilRepository.GetByIdAsync(perfilId.Value);
            if (perfil is { EhSuporteGlobal: true })
                throw new UseCaseValidationException(
                    "CONVITE_SUPERADMIN", "Superadmin não é criado por convite.");
            if (perfil is null || !perfil.AtribuivelNaEmpresa(empresaId))
                throw new UseCaseValidationException("Perfil invalido.");
            return perfil;
        }
    }
}
