using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarComposicao;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Produtos: composicao (receita)")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/produtos/{produtoId:guid}/composicao")]
public class ProdutoComposicaoController(
    GerenciarComposicaoUseCase gerenciarUseCase,
    IProdutoComposicaoRepository composicaoRepository,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(
        Summary = "Obter composicao (receita) de um produto",
        Description = "Retorna o rendimento + linhas da receita. Se lojaId informado, tenta override; fallback receita padrao (LojaId null).")]
    [HttpGet]
    public async Task<IActionResult> Obter(
        [FromRoute] Guid produtoId,
        [FromQuery] Guid? empresaId,
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var error)) return error!;

        var result = await gerenciarUseCase.ObterAsync(
            new ObterComposicaoQuery(emp, produtoId, lojaId), ct);

        return DataOk(result);
    }

    [SwaggerOperation(
        Summary = "Substituir composicao (replace-all transacional)",
        Description = "Substitui rendimento e todas as linhas. Validacoes: ciclo, tenant do insumo, qty minima 0.0001. Grava auditoria diff.")]
    [HttpPut]
    public async Task<IActionResult> Substituir(
        [FromRoute] Guid produtoId,
        [FromBody] SubstituirComposicaoRequest body,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, body.EmpresaId, out var emp, out var error)) return error!;

        if (!Enum.TryParse<UnidadeMedida>(body.RendimentoUnidade, true, out var rendimentoUnidade))
            return DataBadRequest($"Unidade de rendimento invalida: {body.RendimentoUnidade}.");
        if (!Enum.TryParse<UnidadeMedida>(body.UnidadeMedidaBaseProdutoFinal, true, out var unidadeBase))
            return DataBadRequest($"Unidade base do produto invalida: {body.UnidadeMedidaBaseProdutoFinal}.");

        var linhas = new List<ComposicaoLinhaInput>(body.Linhas.Count);
        for (int i = 0; i < body.Linhas.Count; i++)
        {
            var l = body.Linhas[i];
            if (!Enum.TryParse<UnidadeMedida>(l.Unidade, true, out var unidadeLinha))
                return DataBadRequest($"Unidade invalida na linha {i}: {l.Unidade}.");
            linhas.Add(new ComposicaoLinhaInput(
                l.InsumoId, l.Quantidade, unidadeLinha, l.Observacao, l.OrdemExibicao));
        }

        var command = new SubstituirComposicaoCommand(
            EmpresaId: emp,
            ProdutoFinalId: produtoId,
            LojaId: body.LojaId,
            UsuarioId: currentUser.UsuarioId,
            RendimentoBase: body.RendimentoBase,
            RendimentoUnidade: rendimentoUnidade,
            UnidadeMedidaBaseProdutoFinal: unidadeBase,
            Linhas: linhas,
            Observacao: body.Observacao);

        await gerenciarUseCase.SubstituirAsync(command, ct);
        return NoContent();
    }

    [SwaggerOperation(
        Summary = "Listar receitas que usam este produto como insumo",
        Description = "Util pra alertar impacto antes de editar/inativar um insumo.")]
    [HttpGet("onde-usado")]
    public async Task<IActionResult> OndeUsado(
        [FromRoute] Guid produtoId,
        [FromQuery] Guid? empresaId,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var error)) return error!;

        var lista = await composicaoRepository.GetOndeInsumoAsync(emp, produtoId, ct);
        var resultado = lista.Select(c => new
        {
            produtoFinalId = c.ProdutoFinalId,
            produtoFinalNome = c.ProdutoFinal?.Nome,
            lojaId = c.LojaId,
            quantidade = c.Quantidade,
            unidade = c.Unidade.ToString()
        });
        return DataOk(resultado);
    }
}

public sealed record SubstituirComposicaoRequest(
    Guid EmpresaId,
    Guid? LojaId,
    decimal RendimentoBase,
    string RendimentoUnidade,
    string UnidadeMedidaBaseProdutoFinal,
    List<ComposicaoLinhaRequest> Linhas,
    string? Observacao);

public sealed record ComposicaoLinhaRequest(
    Guid InsumoId,
    decimal Quantidade,
    string Unidade,
    string? Observacao,
    int OrdemExibicao);
