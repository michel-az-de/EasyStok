using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints de configuracao fiscal por tenant. Subida do certificado A1,
/// habilitacao da emissao, ajuste de serie/numero, alteracao de ambiente.
///
/// <para>
/// <b>F5-min escopo:</b> apenas upload do cert A1 + habilitar/desabilitar emissao.
/// UI Web completa (wizard 6 passos, historico, etc) e F5 completo, pos-MVP.
/// </para>
/// </summary>
[SwaggerTag("Configuracao Fiscal (admin tenant)")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/configuracao-fiscal")]
public class ConfiguracaoFiscalController(
    INfeCertificadoA1Service certService,
    ICredencialIntegracaoRepository credencialRepo,
    EasyStockDbContext db,
    IUnitOfWork uow,
    ICurrentUserAccessor currentUser,
    ILogger<ConfiguracaoFiscalController> logger) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Upload do certificado digital A1 (.pfx) cifrado em rest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("certificado")]
    public async Task<IActionResult> UploadCertificadoA1(
        [FromForm] IFormFile pfx,
        [FromForm] string senha,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        if (pfx is null || pfx.Length == 0) return DataBadRequest("Arquivo .pfx vazio.");
        if (string.IsNullOrEmpty(senha)) return DataBadRequest("Senha do cert obrigatoria.");
        if (pfx.Length > 10_485_760) return DataBadRequest("Arquivo .pfx muito grande (max 10MB).");

        using var ms = new MemoryStream();
        await pfx.CopyToAsync(ms, ct);
        var pfxBytes = ms.ToArray();

        DateTime validoAte;
        byte[] payloadCifrado;
        try
        {
            validoAte = certService.ValidarUpload(pfxBytes, senha);
            payloadCifrado = await certService.CifrarParaArmazenamentoAsync(pfxBytes, senha, ct);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Upload cert A1 invalido para empresa {Empresa}: {Erro}", eid, ex.Message);
            return DataBadRequest("Cert invalido ou senha incorreta.");
        }

        var usuarioId = currentUser.UsuarioId == Guid.Empty ? Guid.NewGuid() : currentUser.UsuarioId;

        var credencial = CredencialIntegracao.Criar(
            empresaId: eid,
            categoria: CategoriaIntegracao.Fiscal,
            providerKey: "sefaz",
            ambiente: AmbienteIntegracao.Production,
            payloadCifrado: payloadCifrado,
            kekId: "v1",
            iv: new byte[] { 0 },   // IDataProtectionProvider self-contained — iv/tag inline no payload
            tag: new byte[] { 0 },
            criadoPorUsuarioId: usuarioId,
            validoAte: validoAte);

        await uow.ExecuteInTransactionAsync(async txCt =>
        {
            await credencialRepo.AddAsync(credencial, txCt);

            var config = await db.EmpresaConfiguracoesFiscais
                .FirstOrDefaultAsync(c => c.EmpresaId == eid, txCt);

            if (config is null)
            {
                config = EmpresaConfiguracaoFiscal.Criar(eid, RegimeTributario.Simples);
                await db.EmpresaConfiguracoesFiscais.AddAsync(config, txCt);
            }

            config.VincularCertificado(credencial.Id);
            db.EmpresaConfiguracoesFiscais.Update(config);
        });

        logger.LogInformation("Cert A1 atualizado para empresa {Empresa}. CredencialId={Id} ValidoAte={Validade}",
            eid, credencial.Id, validoAte);

        return DataOk(new
        {
            credencialId = credencial.Id,
            validoAte,
            mensagem = $"Certificado valido ate {validoAte:dd/MM/yyyy}.",
        });
    }

    [SwaggerOperation(Summary = "Habilita emissao real de NFC-e para o tenant")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("habilitar")]
    public async Task<IActionResult> Habilitar(
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        try
        {
            await uow.ExecuteInTransactionAsync(async txCt =>
            {
                var config = await db.EmpresaConfiguracoesFiscais
                    .FirstOrDefaultAsync(c => c.EmpresaId == eid, txCt)
                    ?? throw new InvalidOperationException("Config fiscal nao encontrada. Subir certificado primeiro.");

                config.Habilitar();
                db.EmpresaConfiguracoesFiscais.Update(config);
            });

            return DataOk(new { habilitada = true });
        }
        catch (InvalidOperationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (EasyStock.Domain.Exceptions.RegraDeDominioVioladaException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Obter status fiscal atual do tenant")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Obter(
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var config = await db.EmpresaConfiguracoesFiscais
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == eid, ct);

        if (config is null) return DataOk(new { configurado = false });

        var cert = config.CertificadoCredencialId.HasValue
            ? await credencialRepo.GetByIdAsync(config.CertificadoCredencialId.Value, ct)
            : null;

        return DataOk(new
        {
            configurado = true,
            habilitada = config.Habilitada,
            ambiente = config.Ambiente.ToString(),
            regimeTributario = config.RegimeTributario.ToString(),
            serieNfce = config.SerieNfce,
            proximoNumeroNfce = config.ProximoNumeroNfce,
            certificado = cert is null
                ? null
                : new
                {
                    credencialId = cert.Id,
                    ativo = cert.Ativo,
                    validoAte = cert.ValidoAte,
                    diasParaExpirar = cert.ValidoAte.HasValue
                        ? (int)Math.Max(0, (cert.ValidoAte.Value - DateTime.UtcNow).TotalDays)
                        : (int?)null,
                },
        });
    }
}
