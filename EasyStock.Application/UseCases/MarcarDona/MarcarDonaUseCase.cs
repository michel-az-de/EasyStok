using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.MarcarDona;

/// <param name="Dona">Verdadeiro marca; falso tira a marca.</param>
/// <param name="Motivo">A justificativa do suporte ao tirar a marca (exigida, 10+ caracteres) ou ao marcar (opcional).</param>
public sealed record MarcarDonaCommand(Guid UsuarioId, Guid EmpresaId, bool Dona, string? Motivo = null);

/// <param name="Mudou">A marca mudou. Marcar quem já é dona, ou tirar de quem não é, não muda nada nem deixa rastro.</param>
/// <param name="PeloSuporte">Quem fez foi o suporte: vai também para a auditoria da plataforma, com o motivo.</param>
public sealed record MarcarDonaResult(bool Mudou, bool PeloSuporte, string? Motivo);

/// <summary>
/// Marca e desmarca a dona da empresa (#2070, ADR-0038, <c>PUT api/usuarios/{id}/dona</c>). Quem pode, a quem e quando é da
/// <c>PoliticaDeHierarquia</c>: a dona ou o suporte marcam alguém de Administração com conta provada; com zero donas só o
/// suporte marca a primeira; a última dona ativa nunca perde a marca. A marca é do vínculo e não viaja no JWT.
/// </summary>
public sealed class MarcarDonaUseCase(
    IUsuarioRepository usuarios,
    IUsuarioEmpresaRepository vinculos,
    HierarquiaDeUsuarios hierarquia,
    ICurrentUserAccessor atual,
    IAuditLogRepository auditoria,
    IUnitOfWork unitOfWork,
    ILogger<MarcarDonaUseCase> logger)
{
    public async Task<MarcarDonaResult> ExecuteAsync(MarcarDonaCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        var usuario = await usuarios.GetByIdAsync(command.UsuarioId)
            ?? throw new UseCaseValidationException("Usuario nao encontrado.");
        // Guarda de tenant antes da política, com a mesma resposta do não encontrado.
        if (!usuario.Empresas.Any(e => e.EmpresaId == command.EmpresaId))
            throw new UseCaseValidationException("Usuario nao encontrado.");

        var acao = command.Dona ? AcaoSobreUsuario.MarcarDona : AcaoSobreUsuario.DesmarcarDona;
        return await hierarquia.ExecutarAsync(
            new PedidoDeHierarquia(command.EmpresaId, usuario.Id, acao, Motivo: command.Motivo),
            async (contexto, token) =>
            {
                var mudou = await vinculos.DefinirDonaAsync(usuario.Id, command.EmpresaId, command.Dona, token) > 0;
                if (mudou)
                {
                    var autor = await RastroDeUsuarios.AutorAsync(usuarios, atual);
                    await auditoria.AddAsync(RastroDeUsuarios.Linha(
                        usuario.Id,
                        command.Dona ? RastroDeUsuarios.DonaMarcada : RastroDeUsuarios.DonaDesmarcada,
                        command.Dona ? $"Marcada como dona por {autor}" : $"Marca de dona retirada por {autor}",
                        atual, command.EmpresaId));
                    await unitOfWork.CommitAsync();
                    logger.LogInformation(
                        "Marca de dona {Estado} para o usuario {UsuarioId} na empresa {EmpresaId} por {AtorId}",
                        command.Dona ? "dada" : "retirada", usuario.Id, command.EmpresaId, atual.UsuarioId);
                }

                return new MarcarDonaResult(mudou, contexto.Ator.EhSuporteGlobal, contexto.Motivo);
            }, ct);
    }
}
