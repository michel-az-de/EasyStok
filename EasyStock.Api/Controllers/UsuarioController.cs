using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.GerenciarUsuario;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

public sealed record AtribuirPerfilRequest(Guid EmpresaId, Guid PerfilId, Guid? LojaId);
public sealed record AlterarSenhaRequest(string SenhaAtual, string NovaSenha);

[ApiController]
[Route("api/usuarios")]
public class UsuarioController(
    GerenciarUsuarioUseCase usuarioUseCase,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var (usuarios, total) = await usuarioUseCase.ListarAsync(empresaId, page, pageSize);
        return Ok(new { Usuarios = usuarios, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create([FromBody] CriarUsuarioCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var resultado = await usuarioUseCase.CriarAsync(command);
        return Created($"/api/usuarios/{resultado.UsuarioId}", resultado);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarUsuarioCommand command)
    {
        if (id != command.UsuarioId)
            return BadRequest("O id da rota difere do corpo da requisicao.");

        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty)
            return Forbid();

        await usuarioUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await usuarioUseCase.DesativarAsync(id, empresaId);
        return NoContent();
    }

    [HttpPut("{id}/senha")]
    [Authorize]
    public async Task<IActionResult> AlterarSenha(Guid id, [FromBody] AlterarSenhaRequest request)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.UsuarioId != id)
            return Forbid();

        await usuarioUseCase.AlterarSenhaAsync(new AlterarSenhaCommand(id, request.SenhaAtual, request.NovaSenha));
        return NoContent();
    }

    [HttpPut("{id}/perfis")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> AtribuirPerfil(Guid id, [FromBody] AtribuirPerfilRequest request)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != request.EmpresaId)
            return Forbid();

        await usuarioUseCase.AtribuirPerfilAsync(id, request.EmpresaId, request.PerfilId, request.LojaId);
        return NoContent();
    }
}
