using EasyStock.Application.UseCases.GerenciarUsuario;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

public sealed record AtribuirPerfilRequest(Guid EmpresaId, Guid PerfilId, Guid? LojaId);

[ApiController]
[Route("api/usuarios")]
public class UsuarioController(GerenciarUsuarioUseCase usuarioUseCase) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (usuarios, total) = await usuarioUseCase.ListarAsync(empresaId, page, pageSize);
        return Ok(new { Usuarios = usuarios, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Create([FromBody] CriarUsuarioCommand command)
    {
        var resultado = await usuarioUseCase.CriarAsync(command);
        return Created("", resultado);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarUsuarioCommand command)
    {
        await usuarioUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        await usuarioUseCase.DesativarAsync(id, empresaId);
        return NoContent();
    }

    [HttpPut("{id}/senha")]
    [Authorize]
    public async Task<IActionResult> AlterarSenha(Guid id, [FromBody] AlterarSenhaCommand command)
    {
        await usuarioUseCase.AlterarSenhaAsync(command);
        return NoContent();
    }

    [HttpPut("{id}/perfis")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> AtribuirPerfil(Guid id, [FromBody] AtribuirPerfilRequest request)
    {
        await usuarioUseCase.AtribuirPerfilAsync(id, request.EmpresaId, request.PerfilId, request.LojaId);
        return NoContent();
    }
}
