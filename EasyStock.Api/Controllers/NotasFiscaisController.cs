using EasyStock.Api.Dtos.Fiscal;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Fiscal.CancelarNotaFiscal;
using EasyStock.Application.UseCases.Fiscal.ConsultarNotaFiscal;
using EasyStock.Application.UseCases.Fiscal.EmitirNotaFiscalConsumidor;
using EasyStock.Application.UseCases.Fiscal.InutilizarNumeracao;
using EasyStock.Domain.Enums.Fiscal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Notas fiscais (NFC-e modelo 65 / NFe modelo 55)")]
[ApiController]
[Route("api/notas-fiscais")]
[Authorize]
[ValidateEmpresaId]
[EnableRateLimiting("nfce")]
public sealed class NotasFiscaisController(
    EmitirNotaFiscalConsumidorUseCase emitirUseCase,
    CancelarNotaFiscalUseCase cancelarUseCase,
    InutilizarNumeracaoUseCase inutilizarUseCase,
    ConsultarNotaFiscalUseCase consultarUseCase,
    INotaFiscalRepository repo,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Emite NFC-e a partir de um pedido", OperationId = "EmitirNFCe")]
    [HttpPost("nfce/emitir")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Emitir([FromBody] EmitirNFCeRequest body)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var emp, out var err)) return err!;

        var cmd = new EmitirNotaFiscalConsumidorCommand(
            EmpresaId: emp,
            PedidoId: body.PedidoId,
            LojaId: body.LojaId,
            ClienteCpfCnpj: body.ClienteCpfCnpj,
            Pagamentos: body.Pagamentos.Select(p => new EmitirNotaFiscalPagamentoInput(
                p.FormaPagamento, p.Valor, p.BandeiraCartao, p.CnpjCredenciadora, p.Nsu)).ToList(),
            Origem: body.Origem ?? "api",
            UsuarioId: currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null);

        var result = await emitirUseCase.ExecuteAsync(cmd);
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Obtem nota fiscal por ID")]
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Obter(Guid id)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var emp, out var err)) return err!;

        var nota = await repo.ObterPorIdComItensAsync(emp, id, HttpContext.RequestAborted);
        if (nota is null) return DataNotFound("Nota fiscal não encontrada.");

        return DataOk(NotaFiscalResponse.From(nota));
    }

    [SwaggerOperation(Summary = "Lista notas fiscais com filtros")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? lojaId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? ate,
        [FromQuery] StatusNotaFiscal? status,
        [FromQuery] string? chave,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var emp, out var err)) return err!;

        var (p, sz) = NormalisePage(page, pageSize);
        var query = new ConsultarNotaFiscalQuery(emp, lojaId, desde, ate, status, chave, p, sz);
        var r = await consultarUseCase.ExecuteAsync(query);

        return DataPaged(r.Items, r.TotalItens, r.Pagina, r.TamanhoPagina);
    }

    [SwaggerOperation(Summary = "Download do XML autorizado")]
    [HttpGet("{id:guid}/xml")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> DownloadXml(Guid id)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var emp, out var err)) return err!;

        var nota = await repo.ObterPorIdAsync(emp, id, HttpContext.RequestAborted);
        if (nota is null) return DataNotFound();
        if (string.IsNullOrEmpty(nota.XmlAutorizado))
            return DataBadRequest("XML autorizado nao disponivel para esta nota.");

        var bytes = System.Text.Encoding.UTF8.GetBytes(nota.XmlAutorizado);
        return File(bytes, "application/xml", $"NFCe-{nota.ChaveAcesso.Valor}.xml");
    }

    [SwaggerOperation(Summary = "Cancela NFC-e (prazo 30 minutos)")]
    [HttpPost("{id:guid}/cancelar")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarNotaRequest body)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var emp, out var err)) return err!;

        var cmd = new CancelarNotaFiscalCommand(
            EmpresaId: emp,
            NotaFiscalId: id,
            Justificativa: body.Justificativa,
            UsuarioId: currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null);

        var r = await cancelarUseCase.ExecuteAsync(cmd);
        return DataOk(r);
    }

    [SwaggerOperation(Summary = "Inutiliza faixa de numeracao fiscal")]
    [HttpPost("inutilizacao")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Inutilizar([FromBody] InutilizarRequest body)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var emp, out var err)) return err!;

        var cmd = new InutilizarNumeracaoCommand(
            EmpresaId: emp,
            LojaId: body.LojaId,
            Serie: body.Serie,
            NumeroInicial: body.NumeroInicial,
            NumeroFinal: body.NumeroFinal,
            Ano: body.Ano,
            Justificativa: body.Justificativa,
            UsuarioId: currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null);

        var r = await inutilizarUseCase.ExecuteAsync(cmd);
        return DataOk(r);
    }

    [SwaggerOperation(Summary = "Lista inutilizacoes")]
    [HttpGet("inutilizacoes")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ListarInutilizacoes(
        [FromQuery] Guid? lojaId,
        [FromQuery] int? ano)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var emp, out var err)) return err!;
        var lista = await repo.ListarInutilizacoesAsync(emp, lojaId, ano, HttpContext.RequestAborted);
        return DataOk(lista.Select(InutilizacaoResponse.From).ToList());
    }
}
