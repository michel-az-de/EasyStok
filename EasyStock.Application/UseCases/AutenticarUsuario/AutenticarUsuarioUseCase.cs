using System.Diagnostics;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

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
        IReadOnlyCollection<Permissao> Permissoes);

    public class AutenticarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUnitOfWork unitOfWork,
        ILogger<AutenticarUsuarioUseCase> logger)
    {
        public async Task<AutenticarUsuarioResult> ExecuteAsync(AutenticarUsuarioCommand command)
        {
            var swTotal = Stopwatch.StartNew();
            logger.LogDebug("Tentativa de autenticacao para o email: {Email}", command.Email);

            // --- etapa 1: query do usuário
            var swDb = Stopwatch.StartNew();
            var usuario = await usuarioRepository.GetByEmailAsync(command.Email);
            swDb.Stop();
            logger.LogDebug("Login etapa DB query: {ElapsedMs}ms", swDb.ElapsedMilliseconds);

            if (usuario is null || !usuario.Ativo)
                throw new CredenciaisInvalidasException();

            if (usuario.EstaBloqueado())
            {
                logger.LogWarning("Tentativa de login para usuario bloqueado: {Email}", command.Email);
                throw new CredenciaisInvalidasException("Conta bloqueada temporariamente.");
            }

            // Se a janela de lockout expirou, zera o contador de falhas para não
            // bloquear o usuário na próxima falha "herdada" da sessão anterior.
            if (usuario.LockoutEnd.HasValue && usuario.LockoutEnd.Value <= DateTime.UtcNow)
            {
                usuario.ResetarTentativasFalha();
            }

            // --- etapa 2: verificação bcrypt (CPU-bound ~200-800ms dependendo do work factor)
            var swHash = Stopwatch.StartNew();
            var senhaOk = BCrypt.Net.BCrypt.Verify(command.Senha, usuario.SenhaHash);
            swHash.Stop();
            logger.LogDebug("Login etapa bcrypt verify: {ElapsedMs}ms", swHash.ElapsedMilliseconds);

            if (!senhaOk)
            {
                usuario.IncrementarTentativasFalha();
                if (usuario.FailedLoginAttempts >= 5)
                {
                    usuario.BloquearPorTentativas(15);
                    logger.LogWarning("Usuario bloqueado apos 5 tentativas falhidas: {Email}", command.Email);
                }
                await usuarioRepository.UpdateAsync(usuario);
                await unitOfWork.CommitAsync();
                throw new CredenciaisInvalidasException();
            }

            usuario.ResetarTentativasFalha();

            var empresaId = command.EmpresaId ?? ResolveEmpresaIdPadrao(usuario);

            if (empresaId.HasValue)
            {
                var linkEmpresa = usuario.Empresas?.FirstOrDefault(
                    e => e.EmpresaId == empresaId.Value && e.Ativo);

                if (linkEmpresa is null)
                    throw new CredenciaisInvalidasException();
            }

            var nivel = NivelAcesso.Visualizador;
            IReadOnlyCollection<Permissao> permissoes = [];

            if (empresaId.HasValue && usuario.Perfis is not null)
            {
                var perfilDaEmpresa = usuario.Perfis
                    .Where(p => p.EmpresaId == empresaId.Value)
                    .OrderBy(p => p.Perfil != null ? (int)p.Perfil.Nivel : int.MaxValue)
                    .FirstOrDefault();

                if (perfilDaEmpresa?.Perfil is not null)
                {
                    nivel = perfilDaEmpresa.Perfil.Nivel;
                    permissoes = perfilDaEmpresa.Perfil.Permissoes?
                        .Select(p => p.Permissao)
                        .Distinct()
                        .ToArray() ?? [];
                }
            }

            // --- etapa 3: atualizar último acesso
            var swUpdate = Stopwatch.StartNew();
            usuario.AtualizarUltimoAcesso();
            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
            swUpdate.Stop();

            swTotal.Stop();
            logger.LogInformation(
                "Autenticacao bem-sucedida. UsuarioId={UsuarioId} | total={TotalMs}ms (db={DbMs}ms bcrypt={BcryptMs}ms update={UpdateMs}ms)",
                usuario.Id, swTotal.ElapsedMilliseconds, swDb.ElapsedMilliseconds,
                swHash.ElapsedMilliseconds, swUpdate.ElapsedMilliseconds);

            return new AutenticarUsuarioResult(
                UsuarioId: usuario.Id,
                EmpresaId: empresaId,
                Nome: usuario.Nome,
                Email: usuario.Email,
                Nivel: nivel,
                Permissoes: permissoes);
        }

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
