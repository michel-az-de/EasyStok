using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Auth;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.CriarUsuario
{
    /// <param name="Senha">
    /// Opcional desde a N9. Sem senha nasce o convidado (hash inutilizável) e sai um convite com link; com senha (legado)
    /// nada muda. Fica obsoleto até a M7.2 trocar o formulário.
    /// </param>
    /// <param name="Telefone">Opcional. Só vira canal de convite com <paramref name="AtestaOptInWhatsApp"/>.</param>
    /// <param name="AtestaOptInWhatsApp">A dona atesta que a pessoa aceitou receber o convite por WhatsApp.</param>
    public sealed record CriarUsuarioCommand(
        Guid EmpresaId,
        string Nome,
        string Email,
        string? Senha,
        Guid? PerfilId,
        Guid? LojaId,
        string? Telefone = null,
        bool AtestaOptInWhatsApp = false);

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
        ICurrentUserAccessor currentUser,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ILogger<CriarUsuarioUseCase> logger)
    {
        public async Task<CriarUsuarioResult> ExecuteAsync(CriarUsuarioCommand command)
        {
            logger.LogInformation("Criando usuario para empresa {EmpresaId}", command.EmpresaId);

            EmailValidator.EnsureValid(command.Email);

            var convidar = string.IsNullOrEmpty(command.Senha);
            TelefoneE164? telefone = null;
            if (convidar)
            {
                // Tudo o que pode recusar vem antes de gravar: o convite nunca nasce pela metade.
                convites.ExigirBaseDeLink();
                telefone = LerTelefone(command.Telefone);
            }
            // Vale tambem para o cadastro legado com senha: antes so o convite barrava SuperAdmin.
            await ExigirQueNaoSejaSuperAdminAsync(command.PerfilId);

            var emailExistente = await usuarioRepository.GetByEmailAsync(command.Email);
            if (emailExistente is not null)
                throw new UseCaseValidationException("Email ja cadastrado.");

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
                    AtribuidoEm = agora
                };

                await usuarioPerfilRepository.AddAsync(usuarioPerfil);
            }

            if (convidar)
                await EmitirConviteAsync(usuario, command, telefone is not null && command.AtestaOptInWhatsApp);

            await unitOfWork.CommitAsync();

            logger.LogInformation("Usuario criado: {UsuarioId} (convidado: {Convidado})", usuario.Id, convidar);

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

        /// <summary>Superadmin nunca nasce por convite: o perfil global exige a criação administrativa, com senha própria.</summary>
        private async Task ExigirQueNaoSejaSuperAdminAsync(Guid? perfilId)
        {
            if (perfilId is null) return;

            var perfil = await perfilRepository.GetByIdAsync(perfilId.Value);
            if (perfil is { Nivel: NivelAcesso.SuperAdmin })
                throw new UseCaseValidationException(
                    "CONVITE_SUPERADMIN", "Superadmin não é criado por convite.");
        }
    }
}
