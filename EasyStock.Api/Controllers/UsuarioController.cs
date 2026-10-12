using EasyStock.Application.UseCases.AlterarSenhaUsuario;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.ListarUsuarios;
using EasyStock.Application.UseCases.ReenviarConvite;
using EasyStock.Application.Services.Auth;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record AtribuirPerfilRequest(Guid EmpresaId, Guid PerfilId, Guid? LojaId);
public sealed record AlterarSenhaRequest(string SenhaAtual, string NovaSenha);

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
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List users (Admin only, paginated)")]
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
                      "como antes; o campo sai na M7.2, quando o console substituir o formulario do Web.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
        Description = "Revoga os convites abertos e emite novos (N9). 3 por hora por usuario (429 com Retry-After). So para convite pendente.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

    [SwaggerOperation(Summary = "Update user (Admin only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarUsuarioCommand command)
    {
        if (id != command.UsuarioId)
            return DataBadRequest("Id da rota nao corresponde ao Id do comando.");
        await atualizarUseCase.ExecuteAsync(command);
        return NoContent();
    }

    [SwaggerOperation(Summary = "Deactivate user (Admin only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await desativarUseCase.ExecuteAsync(new DesativarUsuarioCommand(id, empresaId));
        return NoContent();
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

    [SwaggerOperation(Summary = "Assign role to user (Admin only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut("{id}/perfis")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> AtribuirPerfil(Guid id, [FromBody] AtribuirPerfilRequest request)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != request.EmpresaId)
            return Forbid();

        await atribuirPerfilUseCase.ExecuteAsync(new AtribuirPerfilUsuarioCommand(id, request.EmpresaId, request.PerfilId, request.LojaId));
        return NoContent();
    }
}
