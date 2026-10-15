using System.Diagnostics;

namespace EasyStock.Application.UseCases.AutenticarUsuario
{
    public sealed record AutenticarUsuarioCommand(
        string Email,
        string Senha,
        Guid? EmpresaId);

    public sealed record AutenticarUsuarioResult(
        Guid UsuarioId,
        Guid? EmpresaId,
        string Nome,
        string Email,
        NivelAcesso Nivel,
        IReadOnlyCollection<Permissao> Permissoes,
        string? ModuloInicial = null,
        bool PermissoesExplicitas = false,
        bool SessaoPersistente = false);

    public class AutenticarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ILogger<AutenticarUsuarioUseCase> logger)
    {
        public async Task<AutenticarUsuarioResult> ExecuteAsync(AutenticarUsuarioCommand command)
        {
            var swTotal = Stopwatch.StartNew();
            logger.LogDebug("Tentativa de autenticacao iniciada");

            // --- etapa 1: query do usuário
            var swDb = Stopwatch.StartNew();
            var usuario = await usuarioRepository.GetByEmailAsync(command.Email);
            swDb.Stop();
            logger.LogDebug("Login etapa DB query: {ElapsedMs}ms", swDb.ElapsedMilliseconds);

            // #1781: conta inexistente, inativa ou bloqueada roda o BCrypt contra o hash fictício e responde como
            // senha errada: nem o tempo nem a mensagem distinguem os casos.
            if (usuario is null || !usuario.Ativo)
                throw RecusarSemConferir(command.Senha);

            if (usuario.EstaBloqueado())
            {
                logger.LogWarning("Tentativa de login para usuario bloqueado: {UsuarioId}", usuario.Id);
                throw RecusarSemConferir(command.Senha);
            }

            if (!usuario.EmailConfirmado)
            {
                logger.LogWarning("Tentativa de login com email não confirmado: {UsuarioId}", usuario.Id);
            }

            // #1781: a tentativa é contada no banco, num UPDATE atômico, antes do BCrypt; rajada paralela não passa
            // do limite. Recusada: outra requisição levou a última tentativa e a conta está bloqueada.
            if (!await usuarioRepository.ReservarTentativaDeSenhaAsync(usuario.Id, DateTime.UtcNow))
            {
                logger.LogWarning("Tentativa de login para usuario bloqueado: {UsuarioId}", usuario.Id);
                throw RecusarSemConferir(command.Senha);
            }

            // --- etapa 2: verificação do hash (CPU-bound ~200-800ms dependendo do work factor)
            var swHash = Stopwatch.StartNew();
            var senhaOk = passwordHasher.Verify(command.Senha, usuario.SenhaHash);
            swHash.Stop();
            logger.LogDebug("Login etapa hash verify: {ElapsedMs}ms", swHash.ElapsedMilliseconds);

            if (!senhaOk)
            {
                // #1352: regra única (contagem, bloqueio na 5ª e janela vencida) em Usuario, a mesma do passo 1.
                // #1781: a reserva já gravou a falha; a entidade só espelha (log). Gravar a linha lida antes da
                // reserva devolveria o contador ao valor velho e perderia as falhas paralelas.
                usuario.RegistrarFalhaDeSenha();
                if (usuario.EstaBloqueado())
                    logger.LogWarning("Usuario {UsuarioId} bloqueado apos {Limite} tentativas falhas", usuario.Id, Domain.Entities.Usuario.FalhasParaBloquear);
                throw new CredenciaisInvalidasException();
            }

            usuario.ResetarTentativasFalha();

            // #1342: superadmin que informa a empresa entra nela; sem empresaId, segue sem (painel admin).
            var resultado = await ConcluirAsync(usuario, command.EmpresaId ?? ResolveEmpresaIdPadrao(usuario), empresaDoSuperAdmin: command.EmpresaId);

            swTotal.Stop();
            logger.LogInformation(
                "Autenticacao bem-sucedida. UsuarioId={UsuarioId} | total={TotalMs}ms (db={DbMs}ms bcrypt={BcryptMs}ms)",
                usuario.Id, swTotal.ElapsedMilliseconds, swDb.ElapsedMilliseconds, swHash.ElapsedMilliseconds);
            return resultado;
        }

        /// <summary>
        /// #1324: final do login sem senha (Google), com a identidade já validada. Sem empresa pedida, entra
        /// na empresa ativa do usuário; com mais de uma, na primeira por nome (escolha de empresa fica para depois).
        /// </summary>
        /// <param name="empresaPadrao">#1326: empresa do superadmin no console (que recusa token sem empresa).</param>
        public Task<AutenticarUsuarioResult> ConcluirLoginGoogleAsync(Domain.Entities.Usuario usuario, Guid? empresaId, Guid? empresaPadrao = null) =>
            ConcluirAsync(usuario, empresaId ?? ResolveEmpresaIdPadrao(usuario) ?? PrimeiraEmpresaAtiva(usuario),
                empresaDoSuperAdmin: empresaId ?? empresaPadrao);

        /// <summary>SuperAdmin, empresa, nível e permissões, e o último acesso. Credencial já conferida.</summary>
        private async Task<AutenticarUsuarioResult> ConcluirAsync(Domain.Entities.Usuario usuario, Guid? empresaId, Guid? empresaDoSuperAdmin = null)
        {
            // SuperAdmin: perfil global com Perfil.EmpresaId=null. Nao tem vinculo
            // em UsuarioEmpresa, entao o fluxo padrao (que exige empresaId resolvido)
            // deixava nivel=Visualizador como default e a tela /Auth/Login do
            // EasyStock.Admin rejeitava o usuario com "Acesso restrito a administradores".
            var perfilSuperAdmin = usuario.Perfis?
                .Select(up => up.Perfil)
                .FirstOrDefault(p => p != null && p.Nivel == NivelAcesso.SuperAdmin);

            if (perfilSuperAdmin is not null)
            {
                var permissoesSuper = perfilSuperAdmin.Permissoes?
                    .Select(pp => pp.Permissao)
                    .Distinct()
                    .ToArray() ?? [];

                usuario.AtualizarUltimoAcesso();
                await usuarioRepository.UpdateAsync(usuario);
                await unitOfWork.CommitAsync();

                return new AutenticarUsuarioResult(
                    UsuarioId: usuario.Id,
                    EmpresaId: empresaDoSuperAdmin,
                    Nome: usuario.Nome,
                    Email: usuario.Email,
                    Nivel: NivelAcesso.SuperAdmin,
                    Permissoes: permissoesSuper,
                    ModuloInicial: perfilSuperAdmin.ModuloInicial,
                    PermissoesExplicitas: perfilSuperAdmin.PermissoesExplicitas);
            }

            if (empresaId.HasValue)
            {
                var linkEmpresa = usuario.Empresas?.FirstOrDefault(
                    e => e.EmpresaId == empresaId.Value && e.Ativo);

                if (linkEmpresa is null)
                    throw new CredenciaisInvalidasException();
            }
            // Desativar zera o vínculo, não o usuário: sem vínculo ativo não há sessão a abrir.
            else if (usuario.Empresas?.Any(e => e.Ativo) != true)
                throw new CredenciaisInvalidasException();

            var nivel = NivelAcesso.Visualizador;
            IReadOnlyCollection<Permissao> permissoes = [];
            string? moduloInicial = null;
            var permissoesExplicitas = false;
            var sessaoPersistente = false;

            if (empresaId.HasValue && usuario.Perfis is not null)
            {
                var perfilDaEmpresa = usuario.Perfis
                    .Where(p => p.EmpresaId == empresaId.Value)
                    .OrderBy(p => p.Perfil != null ? (int)p.Perfil.Nivel : int.MaxValue)
                    .FirstOrDefault();

                if (perfilDaEmpresa?.Perfil is not null)
                {
                    nivel = perfilDaEmpresa.Perfil.Nivel;
                    moduloInicial = perfilDaEmpresa.Perfil.ModuloInicial;
                    permissoesExplicitas = perfilDaEmpresa.Perfil.PermissoesExplicitas;
                    sessaoPersistente = ResolveEmpresaIdPadrao(usuario) == empresaId
                        && Domain.Services.PerfisCasaDaBaba.PermiteSessaoPersistente(perfilDaEmpresa.Perfil, empresaId.Value);
                    permissoes = perfilDaEmpresa.Perfil.Permissoes?
                        .Select(p => p.Permissao)
                        .Distinct()
                        .ToArray() ?? [];
                }
            }

            usuario.AtualizarUltimoAcesso();
            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();

            return new AutenticarUsuarioResult(
                UsuarioId: usuario.Id,
                EmpresaId: empresaId,
                Nome: usuario.Nome,
                Email: usuario.Email,
                Nivel: nivel,
                Permissoes: permissoes,
                ModuloInicial: moduloInicial,
                PermissoesExplicitas: permissoesExplicitas,
                SessaoPersistente: sessaoPersistente);
        }

        /// <summary>#1781: mesmo custo e mesma resposta da senha errada, sem conferir senha real nenhuma.</summary>
        private CredenciaisInvalidasException RecusarSemConferir(string senha)
        {
            passwordHasher.Verify(senha, SenhaFicticia.Hash);
            return new CredenciaisInvalidasException();
        }

        private static Guid? PrimeiraEmpresaAtiva(Domain.Entities.Usuario usuario) =>
            usuario.Empresas?
                .Where(e => e.Ativo)
                .OrderBy(e => e.Empresa?.Nome)
                .Select(e => (Guid?)e.EmpresaId)
                .FirstOrDefault();

        private static Guid? ResolveEmpresaIdPadrao(Domain.Entities.Usuario usuario)
        {
            var empresasAtivas = usuario.Empresas?
                .Where(e => e.Ativo)
                .Select(e => e.EmpresaId)
                .Distinct()
                .ToArray() ?? [];

            return empresasAtivas.Length == 1 ? empresasAtivas[0] : null;
        }
    }
}
