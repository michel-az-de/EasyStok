using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.AtribuirPerfilUsuario
{
    public sealed record AtribuirPerfilUsuarioCommand(Guid UsuarioId, Guid EmpresaId, Guid PerfilId, Guid? LojaId);

    /// <summary>
    /// Define o perfil do usuário na empresa. #1753: um perfil por empresa, como o login já lê; antes a atribuição só
    /// acumulava, e quem virava Cozinha continuava com o Atendimento.
    /// </summary>
    public class AtribuirPerfilUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IPerfilRepository perfilRepository,
        RevogadorSessoes revogadorSessoes,
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

            // Perfil global SuperAdmin ou de outra empresa nao pode ser atribuido por um Admin de tenant
            // (o login promove a SuperAdmin quem tiver QUALQUER perfil com esse nivel).
            var perfil = await perfilRepository.GetByIdAsync(command.PerfilId)
                ?? throw new UseCaseValidationException("Perfil invalido.");
            if (perfil.Nivel == NivelAcesso.SuperAdmin || (perfil.EmpresaId is not null && perfil.EmpresaId != command.EmpresaId))
                throw new UseCaseValidationException("Perfil invalido.");

            var vinculoAtivo = usuario.Empresas.Any(e => e.EmpresaId == command.EmpresaId && e.Ativo);

            // Quem se rebaixa perde a tela que desfaz o engano; a troca do próprio perfil é de outro administrador.
            if (currentUser.Nivel != NivelAcesso.SuperAdmin && command.UsuarioId == currentUser.UsuarioId)
                throw new UseCaseValidationException(
                    "USUARIO_PROPRIO", "Você não pode trocar o seu próprio perfil. Peça a outro administrador.");

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
                if (vinculoAtivo) await revogadorSessoes.RevogarAsync(usuario);
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

            // #1352: nível e permissões viajam no JWT, então quem tinha token com o perfil antigo cai. O corte é do
            // usuário todo e esta rota só confere a empresa de quem chama: sem vínculo ativo com a empresa da
            // atribuição, o Admin de outra empresa deslogaria quem não é dele.
            if (vinculoAtivo)
                await revogadorSessoes.RevogarAsync(usuario);

            var nomesAnteriores = anteriores.Select(p => p.Perfil?.Nome).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            var de = anteriores.Count == 0 ? "sem perfil"
                : nomesAnteriores.Count == 0 ? "outro perfil"
                : string.Join(" e ", nomesAnteriores);
            var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
            await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                usuario.Id, RastroDeUsuarios.PerfilTrocado, $"Perfil trocado de {de} para {perfil.Nome} por {autor}", currentUser, command.EmpresaId));

            await unitOfWork.CommitAsync();
        }
    }
}
