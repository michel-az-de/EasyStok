using EasyStock.Application.UseCases.AlterarSenhaUsuario;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.ListarUsuarios;
using EasyStock.Application.UseCases.MarcarDona;
using EasyStock.Application.UseCases.ReativarUsuario;
using EasyStock.Application.UseCases.ReenviarConvite;
using EasyStock.Application.Services.Auth;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record AtribuirPerfilRequest(Guid EmpresaId, Guid PerfilId, Guid? LojaId);
public sealed record AlterarSenhaRequest(string SenhaAtual, string NovaSenha);

/// <summary>
/// Marca ou tira a marca de dona (#2070). <c>Motivo</c>: 10+ caracteres, exigido do suporte ao tirar a marca da dona.
/// <c>EmpresaId</c>: a do token quando omitida.
/// </summary>
public sealed record MarcarDonaRequest(bool Dona, string? Motivo = null, Guid? EmpresaId = null);

[SwaggerTag("Users / Usuários")]
[ApiController]
[Route("api/usuarios")]
public class UsuarioController(
    CriarUsuarioUseCase criarUseCase,
    AtualizarUsuarioUseCase atualizarUseCase,
    AlterarSenhaUsuarioUseCase alterarSenhaUseCase,
    DesativarUsuarioUseCase desativarUseCase,
    ListarUsuariosUseCase listarUseCase,
    AtribuirPerfilUsuarioUseCase atribuirPerfilUseCase,
    ReenviarConviteUseCase reenviarConviteUseCase,
    ReativarUsuarioUseCase reativarUseCase,
    ListarPerfisAtribuiveisUseCase listarPerfisUseCase,
    ListarAuditoriaDeAcessoUseCase listarAuditoriaUseCase,
    MarcarDonaUseCase marcarDonaUseCase,
    AdminAuditService adminAudit,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(
        Summary = "List users (Admin only, paginated)",
        Description = "#2070: cada linha traz dona (a marca do vínculo) e acoes {editarNome, trocarEmail, trocarPerfil, desativar, " +
                      "reativar, reenviarConvite, marcarDona, desmarcarDona}, pela política de hierarquia para quem pede. " +
                      "Sem perfil, o nivel é Visualizador.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var (usuarios, total) = await listarUseCase.ExecuteAsync(new ListarUsuariosQuery(empresaId, page, pageSize));
        return DataPaged(usuarios, total, page, pageSize);
    }

    [SwaggerOperation(
        Summary = "Create user (Admin only)",
        Description = "Sem Senha (o caminho novo, N9) nasce o convidado e sai um convite com link por e-mail (e por WhatsApp quando " +
                      "Telefone e AtestaOptInWhatsApp). Superadmin nunca nasce por convite. OBSOLETO: Senha. Com Senha o usuario nasce " +
                      "como antes; o campo sai na M7.2, quando o console substituir o formulario do Web. " +
                      "#2070: Dona=true (só a dona ou o suporte) exige perfil de nível Admin, e-mail novo (409 DONA_EMAIL_EXISTENTE) e " +
                      "convite só por e-mail. A Administração não dá perfil de nível Admin (403 HIERARQUIA).")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create([FromBody] CriarUsuarioCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var resultado = await criarUseCase.ExecuteAsync(command);
        return DataCreated($"/api/usuarios/{resultado.UsuarioId}", resultado);
    }

    [SwaggerOperation(
        Summary = "Resend the access invite (Admin only)",
        Description = "Revoga os convites abertos e emite novos (N9). 3 por hora por usuario (429 com Retry-After). So para convite pendente. " +
                      "#2070: a Administração não reenvia o da dona nem o de outra Administração (403 HIERARQUIA); o da dona vai só por e-mail.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [HttpPost("{id}/convite")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> ReenviarConvite(Guid id)
    {
        try
        {
            await reenviarConviteUseCase.ExecuteAsync(new ReenviarConviteCommand(id));
            return NoContent();
        }
        catch (LimitePedidosAcessoExcedidoException ex)
        {
            Response.Headers.Append("Retry-After", ex.RetryAfterSeconds.ToString());
            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(new ApiError(
                "TOO_MANY_REQUESTS", "Muitos reenvios para este usuario. Tente de novo em alguns minutos.", null, null)));
        }
    }

    [SwaggerOperation(
        Summary = "Update user (Admin only)",
        Description = "#2070: a Administração edita só da Gerência para baixo (403 HIERARQUIA); o e-mail da dona só o suporte troca, " +
                      "com Motivo de 10+ caracteres (400 MOTIVO_OBRIGATORIO), que vai também para a auditoria da plataforma.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarUsuarioCommand command)
    {
        if (id != command.UsuarioId)
            return DataBadRequest("Id da rota nao corresponde ao Id do comando.");
        var resultado = await atualizarUseCase.ExecuteAsync(command);
        if (resultado.SuporteTrocouEmailDaDona)
            await adminAudit.LogAsync("SuporteTrocouEmailDaDona", $"UsuarioId={id}",
                tenantId: EmpresaDoToken(), motivo: resultado.Motivo, entidadeAfetadaId: id);
        return NoContent();
    }

    [SwaggerOperation(
        Summary = "Mark or unmark the company owner (dona) (Admin only)",
        Description = "#2070 (ADR-0038). Só a dona ou o suporte; com zero donas, só o suporte marca a primeira. Marcar exige " +
                      "acesso ativo de nível Admin (409 DONA_NAO_ADMIN) e conta provada por e-mail ou Google (409 DONA_CONTA_NAO_PROVADA); " +
                      "o suporte nunca é marcado (400 DONA_SUPORTE); a última dona ativa não perde a marca (409 ULTIMA_DONA). " +
                      "O suporte tirando a marca escreve Motivo (10+). Repetir não muda nada. EmpresaId: a do token quando omitida.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPut("{id}/dona")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> MarcarDona(Guid id, [FromBody] MarcarDonaRequest request, CancellationToken ct)
    {
        var empresaId = request.EmpresaId ?? currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
            return DataBadRequest("Informe a empresa.");
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var resultado = await marcarDonaUseCase.ExecuteAsync(new MarcarDonaCommand(id, empresaId, request.Dona, request.Motivo), ct);
        if (resultado.PeloSuporte && resultado.Mudou)
            await adminAudit.LogAsync(request.Dona ? "SuporteMarcouDona" : "SuporteDesmarcouDona", $"UsuarioId={id}",
                tenantId: empresaId, motivo: resultado.Motivo, entidadeAfetadaId: id);
        return NoContent();
    }

    [SwaggerOperation(
        Summary = "Deactivate user (Admin only)",
        Description = "#2070: ninguém desativa a si mesmo (400 USUARIO_PROPRIO); a dona, só o suporte (409 DONA_PROTEGIDA para a dona, " +
                      "403 HIERARQUIA para a Administração); a última dona ativa nunca (409 ULTIMA_DONA).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpDelete("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await desativarUseCase.ExecuteAsync(new DesativarUsuarioCommand(id, empresaId));
        return NoContent();
    }

    [SwaggerOperation(
        Summary = "Reactivate user (Admin only)",
        Description = "Desfaz a desativacao: religa o vinculo com a empresa e grava a auditoria. Repetir nao muda nada. " +
                      "#2070: a Administração reativa só da Gerência para baixo (403 HIERARQUIA).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/reativar")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Reativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await reativarUseCase.ExecuteAsync(new ReativarUsuarioCommand(id, empresaId));
        return NoContent();
    }

    [SwaggerOperation(
        Summary = "List the roles an Admin can assign in the company (Admin only)",
        Description = "#2070: atribuivel diz se quem pede pode dar o perfil (a Administração não dá perfil de nível Admin).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("perfis")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Perfis([FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        return DataOk(await listarPerfisUseCase.ExecuteAsync(empresaId));
    }

    [SwaggerOperation(
        Summary = "Access audit trail of the company users (Admin only, paginated)",
        Description = "Login, convite, troca de perfil, desativacao e reativacao, da mais recente para a mais antiga. " +
                      "usuarioId filtra pela pessoa afetada. IP e agente nao saem.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("auditoria")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Auditoria(
        [FromQuery] Guid empresaId, [FromQuery] Guid? usuarioId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (itens, total) = await listarAuditoriaUseCase.ExecuteAsync(
            new ListarAuditoriaDeAcessoQuery(empresaId, usuarioId, page, pageSize), ct);
        return DataPaged(itens, total, page, pageSize);
    }

    [SwaggerOperation(Summary = "Change user password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut("{id}/senha")]
    [Authorize]
    public async Task<IActionResult> AlterarSenha(Guid id, [FromBody] AlterarSenhaRequest request)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.UsuarioId != id)
            return Forbid();

        await alterarSenhaUseCase.ExecuteAsync(new AlterarSenhaCommand(id, request.SenhaAtual, request.NovaSenha));
        return NoContent();
    }

    [SwaggerOperation(
        Summary = "Assign role to user (Admin only)",
        Description = "#2070: o alvo precisa de acesso ativo na empresa (400 USUARIO_DESATIVADO); a Administração não dá perfil de " +
                      "nível Admin nem mexe na dona ou em outra Administração (403 HIERARQUIA); a dona não rebaixa outra dona " +
                      "(409 DONA_PROTEGIDA); a última dona ativa não é rebaixada (409 ULTIMA_DONA).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPut("{id}/perfis")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> AtribuirPerfil(Guid id, [FromBody] AtribuirPerfilRequest request)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != request.EmpresaId)
            return Forbid();

        await atribuirPerfilUseCase.ExecuteAsync(new AtribuirPerfilUsuarioCommand(id, request.EmpresaId, request.PerfilId, request.LojaId));
        return NoContent();
    }

    private Guid? EmpresaDoToken() => currentUser.EmpresaId == Guid.Empty ? null : currentUser.EmpresaId;
}
