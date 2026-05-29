using EasyStock.Api.Models.Fiscal;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.UseCases.Fiscal.CancelarNfe;
// Aliases para evitar fully-qualified names no MapearExcecaoFiscal abaixo.
using EasyStock.Application.UseCases.Fiscal.ConsultarNfe;
using EasyStock.Application.UseCases.Fiscal.EmitirNfce;
using EasyStock.Application.UseCases.Fiscal.InutilizarNumeracao;
using EasyStock.Domain.Fiscal;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints fiscais da NFC-e modelo 65. Tenant resolvido automaticamente via
/// <see cref="ICurrentUserAccessor.EmpresaId"/>. Emissao + Cancelamento +
/// Inutilizacao + Consulta + Listagem.
/// </summary>
[SwaggerTag("Notas Fiscais (NFC-e)")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/notas-fiscais")]
public class NotasFiscaisController(
    EmitirNfceUseCase emitirUseCase,
    CancelarNfeUseCase cancelarUseCase,
    InutilizarNumeracaoUseCase inutilizarUseCase,
    ConsultarNfeUseCase consultarUseCase,
    INfeRepository nfeRepo,
    ICurrentUserAccessor currentUser,
    EasyStock.Infra.Postgre.Data.EasyStockDbContext db) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Emite uma NFC-e em homologação ou produção conforme config do tenant")]
    [ProducesResponseType(typeof(NfeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("emitir")]
    [EnableRateLimiting("nfe-emitir")]
    public async Task<IActionResult> Emitir(
        [FromBody] EmitirNfceRequest req,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var cmd = new EmitirNfceCommand(
            EmpresaId: eid,
            PedidoId: req.PedidoId,
            IdempotencyKey: req.IdempotencyKey,
            TotalNota: req.TotalNota,
            Emitente: new DadosEmitenteInput(
                req.Emitente.Cnpj,
                req.Emitente.RazaoSocial,
                req.Emitente.NomeFantasia,
                req.Emitente.InscricaoEstadual,
                req.Emitente.InscricaoMunicipal),
            Destinatario: req.Destinatario is null
                ? null
                : new DadosDestinatarioInput(req.Destinatario.CpfCnpj, req.Destinatario.Nome, req.Destinatario.Email),
            Itens: req.Itens
                .Select(i => new EmitirNfceItemInput(
                    NomeSnapshot: i.NomeSnapshot,
                    Quantidade: i.Quantidade,
                    PrecoUnitario: i.PrecoUnitario,
                    Unidade: i.Unidade,
                    Ncm: i.Ncm,
                    Cfop: i.Cfop,
                    ProdutoIdSnapshot: i.ProdutoIdSnapshot,
                    OrigemMercadoria: i.OrigemMercadoria,
                    CstOuCsosn: i.CstOuCsosn))
                .ToList(),
            UsuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
            UsuarioNome: ResolverNomeUsuarioAtual(),
            Origem: "api");

        try
        {
            var result = await emitirUseCase.ExecuteAsync(cmd);
            var nfe = await nfeRepo.GetByIdAsync(eid, result.NfeId, ct);
            return DataOk(MapToResponse(nfe!));
        }
        catch (GatewayFiscalCredencialException ex) { return MapearExcecaoFiscal(ex); }
        catch (GatewayFiscalDenegadaException ex) { return MapearExcecaoFiscal(ex); }
    }

    [SwaggerOperation(Summary = "Emite uma NFC-e a partir de um pedido existente — Web admin")]
    [ProducesResponseType(typeof(NfeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("emitir-de-pedido")]
    [EnableRateLimiting("nfe-emitir")]
    public async Task<IActionResult> EmitirDePedido(
        [FromBody] EmitirDePedidoRequest req,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        if (req.PedidoId == Guid.Empty) return DataBadRequest("PedidoId obrigatorio.");

        var pedido = await db.Pedidos
            .AsNoTracking()
            .Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Id == req.PedidoId && p.EmpresaId == eid, ct);

        if (pedido is null) return DataNotFound("Pedido nao encontrado.");
        if (pedido.Itens.Count == 0) return DataBadRequest("Pedido sem itens.");

        var jaEmitida = await db.NfeDocumentos
            .AsNoTracking()
            .AnyAsync(n => n.PedidoId == req.PedidoId && n.Status != StatusNfe.Rejeitada
                                                       && n.Status != StatusNfe.Inutilizada, ct);
        if (jaEmitida) return DataBadRequest("Pedido ja possui NFC-e emitida.");

        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eid, ct);
        if (empresa is null) return DataBadRequest("Empresa nao encontrada.");
        if (string.IsNullOrWhiteSpace(empresa.Documento)) return DataBadRequest("Empresa sem CNPJ. Cadastre antes de emitir.");

        var config = await db.EmpresaConfiguracoesFiscais.AsNoTracking().FirstOrDefaultAsync(c => c.EmpresaId == eid, ct);
        if (config is null) return DataBadRequest("Configuracao fiscal ausente. Acesse /configuracao-fiscal.");

        var idempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey)
            ? $"web-emitir-{pedido.Id:N}-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : req.IdempotencyKey;

        var cmd = new EmitirNfceCommand(
            EmpresaId: eid,
            PedidoId: pedido.Id,
            IdempotencyKey: idempotencyKey,
            TotalNota: pedido.Total.Valor,
            Emitente: new DadosEmitenteInput(
                Cnpj: SomenteDigitos(empresa.Documento),
                RazaoSocial: empresa.Nome,
                NomeFantasia: null,
                InscricaoEstadual: config.InscricaoEstadual,
                InscricaoMunicipal: config.InscricaoMunicipal),
            Destinatario: string.IsNullOrWhiteSpace(req.DestinatarioCpf) && string.IsNullOrWhiteSpace(req.DestinatarioNome)
                ? null
                : new DadosDestinatarioInput(
                    CpfCnpj: req.DestinatarioCpf,
                    Nome: req.DestinatarioNome,
                    Email: req.DestinatarioEmail),
            Itens: pedido.Itens
                .Select(i => new EmitirNfceItemInput(
                    NomeSnapshot: i.Nome,
                    Quantidade: i.Quantidade,
                    PrecoUnitario: i.PrecoUnitario,
                    Unidade: string.IsNullOrWhiteSpace(i.Unidade) ? "UN" : i.Unidade,
                    Ncm: null,
                    Cfop: "5102",
                    ProdutoIdSnapshot: i.ProdutoId,
                    OrigemMercadoria: 0,
                    CstOuCsosn: config.RegimeTributario == RegimeTributario.Simples ? "102" : "00"))
                .ToList(),
            UsuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
            UsuarioNome: ResolverNomeUsuarioAtual(),
            Origem: "web-admin");

        try
        {
            var result = await emitirUseCase.ExecuteAsync(cmd);
            var nfe = await nfeRepo.GetByIdAsync(eid, result.NfeId, ct);
            return DataOk(MapToResponse(nfe!));
        }
        catch (GatewayFiscalCredencialException ex) { return MapearExcecaoFiscal(ex); }
        catch (GatewayFiscalDenegadaException ex) { return MapearExcecaoFiscal(ex); }
    }

    [SwaggerOperation(Summary = "Lista pedidos elegiveis para emissao de NFC-e — Web admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("pedidos-elegiveis")]
    public async Task<IActionResult> ListarPedidosElegiveis(
        [FromQuery] Guid? empresaId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        if (limit is < 1 or > 200) limit = 50;

        var nfePedidoIds = await db.NfeDocumentos
            .AsNoTracking()
            .Where(n => n.Status != StatusNfe.Rejeitada && n.Status != StatusNfe.Inutilizada)
            .Select(n => n.PedidoId)
            .ToListAsync(ct);

        var pedidos = await db.Pedidos
            .AsNoTracking()
            .Where(p => p.EmpresaId == eid
                && (p.Status == "entregue" || p.Status == "pronto")
                && !nfePedidoIds.Contains(p.Id))
            .OrderByDescending(p => p.CriadoEm)
            .Take(limit)
            .Select(p => new
            {
                id = p.Id,
                criadoEm = p.CriadoEm,
                clienteNome = p.ClienteNome ?? "Consumidor",
                total = p.Total.Valor,
                qtdItens = p.Itens.Count,
                status = p.Status,
            })
            .ToListAsync(ct);

        return DataOk(pedidos);
    }

    [SwaggerOperation(Summary = "Cancela uma NFC-e autorizada (prazo SEFAZ 24h)")]
    [ProducesResponseType(typeof(CancelarNfeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(
        Guid id,
        [FromBody] CancelarNfeRequest req,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        try
        {
            var result = await cancelarUseCase.ExecuteAsync(new CancelarNfeCommand(
                EmpresaId: eid,
                NfeId: id,
                Motivo: req.Motivo,
                UsuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
                UsuarioNome: ResolverNomeUsuarioAtual(),
                Origem: "api"));

            return DataOk(new CancelarNfeResponse
            {
                Id = result.NfeId,
                Status = result.Status,
                ProtocoloEvento = result.ProtocoloEvento,
            });
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (GatewayFiscalRejeitadaException ex) { return MapearExcecaoFiscal(ex); }
        catch (GatewayFiscalCredencialException ex) { return MapearExcecaoFiscal(ex); }
        catch (GatewayFiscalDenegadaException ex) { return MapearExcecaoFiscal(ex); }
    }

    [SwaggerOperation(Summary = "Inutiliza uma faixa de numeracao no mesmo ano fiscal")]
    [ProducesResponseType(typeof(InutilizarNumeracaoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("inutilizar")]
    public async Task<IActionResult> Inutilizar(
        [FromBody] InutilizarNumeracaoRequest req,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        try
        {
            var result = await inutilizarUseCase.ExecuteAsync(new InutilizarNumeracaoCommand(
                EmpresaId: eid,
                Serie: req.Serie,
                NumeroInicial: req.NumeroInicial,
                NumeroFinal: req.NumeroFinal,
                Justificativa: req.Justificativa,
                UsuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
                UsuarioNome: ResolverNomeUsuarioAtual(),
                Origem: "api"));

            return DataOk(new InutilizarNumeracaoResponse
            {
                ProtocoloEvento = result.ProtocoloEvento,
                DataInutilizacao = result.DataInutilizacao,
            });
        }
        catch (GatewayFiscalRejeitadaException ex) { return MapearExcecaoFiscal(ex); }
        catch (GatewayFiscalCredencialException ex) { return MapearExcecaoFiscal(ex); }
        catch (GatewayFiscalDenegadaException ex) { return MapearExcecaoFiscal(ex); }
    }

    [SwaggerOperation(Summary = "Detalhe de uma NFC-e com itens e eventos")]
    [ProducesResponseType(typeof(NfeDetalheResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detalhe(
        Guid id,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var result = await consultarUseCase.ExecuteAsync(new ConsultarNfeQuery(eid, id));
        if (result is null) return DataNotFound("NFC-e nao encontrada.");

        return DataOk(new NfeDetalheResponse
        {
            Nfe = new NfeResponse
            {
                Id = result.Id,
                ChaveAcesso = result.ChaveAcesso,
                Status = result.Status,
                Modelo = result.Modelo,
                Serie = result.Serie,
                Numero = result.Numero,
                ProtocoloAutorizacao = result.ProtocoloAutorizacao,
                DataAutorizacao = result.DataAutorizacao,
                MotivoRejeicao = result.MotivoRejeicao,
                DanfeUrl = result.DanfeUrl,
                TotalNota = result.TotalNota,
                CriadoEm = result.CriadoEm,
                AlteradoEm = result.AlteradoEm,
            },
            Itens = result.Itens.Select(i => new NfeItemResponse
            {
                Id = i.Id,
                Ordem = i.Ordem,
                NomeSnapshot = i.NomeSnapshot,
                Quantidade = i.Quantidade,
                PrecoUnitario = i.PrecoUnitario,
                Unidade = i.Unidade,
                Ncm = i.Ncm,
                Cfop = i.Cfop,
                CstOuCsosn = i.CstOuCsosn,
            }).ToList(),
            Eventos = result.Eventos.Select(e => new NfeEventoResponse
            {
                Id = e.Id,
                Tipo = e.Tipo,
                OcorridoEm = e.OcorridoEm,
                UsuarioNome = e.UsuarioNome,
                Origem = e.Origem,
            }).ToList(),
        });
    }

    [SwaggerOperation(Summary = "Lista NFC-e paginada por tenant + filtros opcionais")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId,
        [FromQuery] StatusNfe? status,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? ate,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var (p, sz) = NormalisePage(page, pageSize, maxPageSize: 100);
        var (items, total) = await nfeRepo.GetByEmpresaAsync(eid, p, sz, status, desde, ate, search, ct);

        var dtos = items.Select(n => new NfeListItemResponse
        {
            Id = n.Id,
            ChaveAcesso = n.ChaveAcesso,
            Status = n.Status,
            Serie = n.Serie,
            Numero = n.Numero,
            TotalNota = n.TotalNota.Valor,
            CriadoEm = n.CriadoEm,
            DataAutorizacao = n.DataAutorizacao,
        });

        return DataPaged(dtos, total, p, sz);
    }

    private static NfeResponse MapToResponse(NfeDocumento nfe) => new()
    {
        Id = nfe.Id,
        ChaveAcesso = nfe.ChaveAcesso,
        Status = nfe.Status,
        Modelo = nfe.Modelo,
        Serie = nfe.Serie,
        Numero = nfe.Numero,
        ProtocoloAutorizacao = nfe.ProtocoloAutorizacao,
        DataAutorizacao = nfe.DataAutorizacao,
        MotivoRejeicao = nfe.MotivoRejeicao,
        DanfeUrl = nfe.DanfeUrl,
        TotalNota = nfe.TotalNota.Valor,
        CriadoEm = nfe.CriadoEm,
        AlteradoEm = nfe.AlteradoEm,
    };

    /// <summary>
    /// Mapeia exceções do gateway fiscal que propagam até o controller (não tratadas pelo use case)
    /// em respostas HTTP com mensagem amigável ao operador, em vez de 500 genérico.
    /// Mantém categoria técnica em log estruturado (logger do request pipeline).
    /// </summary>
    private IActionResult MapearExcecaoFiscal(EasyStock.Application.Ports.Output.Fiscal.GatewayFiscalException ex) => ex switch
    {
        EasyStock.Application.Ports.Output.Fiscal.GatewayFiscalCredencialException =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, new EasyStock.Api.Http.ApiErrorResponse(new EasyStock.Api.Http.ApiError(
                "FISCAL_CREDENCIAL_INVALIDA",
                "Configuração fiscal inválida. Avise o suporte — token ou certificado do tenant precisa ser atualizado.",
                null, null))),

        EasyStock.Application.Ports.Output.Fiscal.GatewayFiscalDenegadaException denegada =>
            StatusCode(StatusCodes.Status422UnprocessableEntity, new EasyStock.Api.Http.ApiErrorResponse(new EasyStock.Api.Http.ApiError(
                "FISCAL_DENEGADA",
                $"NFC-e denegada pela SEFAZ (situação fiscal do contribuinte impede emissão): {denegada.Motivo}",
                null, null))),

        EasyStock.Application.Ports.Output.Fiscal.GatewayFiscalRejeitadaException rejeitada =>
            StatusCode(StatusCodes.Status422UnprocessableEntity, new EasyStock.Api.Http.ApiErrorResponse(new EasyStock.Api.Http.ApiError(
                "FISCAL_REJEITADA",
                $"NFC-e rejeitada pela SEFAZ: {rejeitada.Motivo}. Verifique CPF/CNPJ do consumidor e dados dos itens.",
                rejeitada.Codigo, null))),

        _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new EasyStock.Api.Http.ApiErrorResponse(new EasyStock.Api.Http.ApiError(
            "FISCAL_INDISPONIVEL",
            "Gateway fiscal temporariamente indisponível. NFC-e em fila — tente novamente em alguns segundos.",
            null, null))),
    };

    private string? ResolverNomeUsuarioAtual()
    {
        var nome = User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(nome))
            return nome.Length > 120 ? nome[..120] : nome;
        var claimNome = User?.FindFirst("name")?.Value
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(claimNome)
            ? null
            : (claimNome.Length > 120 ? claimNome[..120] : claimNome);
    }

    private static string SomenteDigitos(string input) =>
        new(input.Where(char.IsDigit).ToArray());
}
