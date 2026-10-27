using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.AtribuirPerfilUsuario
{
    public sealed record AtribuirPerfilUsuarioCommand(Guid UsuarioId, Guid EmpresaId, Guid PerfilId, Guid? LojaId);

    /// <summary>
    /// Define o perfil do usuário na empresa. #1753: um perfil por empresa, como o login já lê; antes a atribuição só
    /// acumulava, e quem virava Cozinha continuava com o Atendimento. #2070: quem dá qual perfil a quem é da política de
    /// hierarquia, e o alvo precisa de vínculo ativo com a empresa (B3).
    /// </summary>
    public class AtribuirPerfilUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IPerfilRepository perfilRepository,
        RevogadorSessoes revogadorSessoes,
        HierarquiaDeUsuarios hierarquia,
        ICurrentUserAccessor currentUser,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<AtribuirPerfilUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AtribuirPerfilUsuarioCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

            logger.LogInformation("Atribuindo perfil {PerfilId} ao usuario {UsuarioId}", command.PerfilId, command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            // Guarda de tenant antes da política, com a mesma resposta do não encontrado.
            var vinculo = usuario.Empresas.FirstOrDefault(e => e.EmpresaId == command.EmpresaId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");
            if (!vinculo.Ativo) throw VinculoInativo();

            // Perfil global de suporte, de nível SuperAdmin ou de outra empresa nunca é atribuído por aqui.
            var perfil = await perfilRepository.GetByIdAsync(command.PerfilId)
                ?? throw new UseCaseValidationException("Perfil invalido.");
            if (!perfil.AtribuivelNaEmpresa(command.EmpresaId))
                throw new UseCaseValidationException("Perfil invalido.");

            await hierarquia.ExecutarAsync(
                new PedidoDeHierarquia(command.EmpresaId, usuario.Id, AcaoSobreUsuario.TrocarPerfil, perfil.NivelNaEmpresa),
                async (contexto, token) =>
                {
                    // B3 relido depois da trava: a desativação concorrente vence a troca de perfil.
                    if (!contexto.ParticipanteAlvo.VinculoAtivo) throw VinculoInativo();
                    await TrocarAsync(command, perfil, contexto, token);
                    return true;
                });
        }

        private async Task TrocarAsync(AtribuirPerfilUsuarioCommand command, Perfil perfil, ContextoDaHierarquia contexto, CancellationToken token)
        {
            var usuario = contexto.Alvo!;
            var atuais = await usuarioPerfilRepository.ListarPorUsuarioEEmpresaAsync(command.UsuarioId, command.EmpresaId);
            var mantido = atuais.FirstOrDefault(p => p.PerfilId == command.PerfilId);
            var anteriores = atuais.Where(p => p.PerfilId != command.PerfilId).ToList();

            if (mantido is not null && anteriores.Count == 0)
            {
                // O perfil é o mesmo: não há troca a registrar. Só a loja muda o que o token carrega.
                if (mantido.LojaId == command.LojaId) return;
                mantido.LojaId = command.LojaId;
                mantido.AtribuidoEm = DateTime.UtcNow;
                await usuarioPerfilRepository.UpdateAsync(mantido);
                await revogadorSessoes.RevogarAsync(usuario);
                await unitOfWork.CommitAsync();
                return;
            }

            foreach (var anterior in anteriores)
                await usuarioPerfilRepository.RemoveAsync(anterior);

            if (mantido is null)
                await usuarioPerfilRepository.AddAsync(new UsuarioPerfil
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = command.UsuarioId,
                    EmpresaId = command.EmpresaId,
                    PerfilId = command.PerfilId,
                    LojaId = command.LojaId,
                    AtribuidoEm = DateTime.UtcNow,
                    AtribuidoPorId = currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId
                });

            // #1352: nível e permissões viajam no JWT, então quem tinha token com o perfil antigo cai. O vínculo ativo com a
            // empresa da atribuição é exigido acima (B3): o corte, que é do usuário todo, nunca desloga quem não é da empresa.
            await revogadorSessoes.RevogarAsync(usuario);

            var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);

            // #2070: a dona rebaixada (só o suporte chega aqui, e nunca com a última) deixa de ser dona: toda dona tem nível Admin.
            if (contexto.ParticipanteAlvo.EhDona && perfil.NivelNaEmpresa != NivelAcesso.Admin
                && await usuarioEmpresaRepository.DefinirDonaAsync(usuario.Id, command.EmpresaId, dona: false, token) > 0)
                await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(usuario.Id, RastroDeUsuarios.DonaDesmarcada,
                    $"Marca de dona retirada com a troca para {perfil.Nome}, por {autor}", currentUser, command.EmpresaId));

            var nomesAnteriores = anteriores.Select(p => p.Perfil?.Nome).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            var de = anteriores.Count == 0 ? "sem perfil"
                : nomesAnteriores.Count == 0 ? "outro perfil"
                : string.Join(" e ", nomesAnteriores);
            await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                usuario.Id, RastroDeUsuarios.PerfilTrocado, $"Perfil trocado de {de} para {perfil.Nome} por {autor}", currentUser, command.EmpresaId));

            await unitOfWork.CommitAsync();
        }

        private static UseCaseValidationException VinculoInativo() =>
            new("USUARIO_DESATIVADO", "Este acesso está desativado: reative antes de trocar o perfil.");
    }
}
