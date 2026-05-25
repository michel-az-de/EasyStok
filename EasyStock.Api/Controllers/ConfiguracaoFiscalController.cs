using EasyStock.Api.Http;
using EasyStock.Api.Models.Fiscal;
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

    [SwaggerOperation(Summary = "Atualiza regime tributario, IE, IM e endereco do emitente. Cria a configuracao se nao existir.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("dados-emitente")]
    public async Task<IActionResult> AtualizarDadosEmitente(
        [FromBody] AtualizarDadosEmitenteRequest req,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        try
        {
            await uow.ExecuteInTransactionAsync(async txCt =>
            {
                var config = await db.EmpresaConfiguracoesFiscais
                    .FirstOrDefaultAsync(c => c.EmpresaId == eid, txCt);

                if (config is null)
                {
                    config = EmpresaConfiguracaoFiscal.Criar(eid, req.RegimeTributario);
                    await db.EmpresaConfiguracoesFiscais.AddAsync(config, txCt);
                }
                else if (config.RegimeTributario != req.RegimeTributario)
                {
                    // Regime e read-only pos-criacao — bloqueia mudanca silenciosa que
                    // afetaria CST/CSOSN de notas ja emitidas neste regime.
                    throw new EasyStock.Domain.Exceptions.RegraDeDominioVioladaException(
                        "Regime tributario nao pode ser alterado apos criado. Contate o suporte.");
                }

                var endereco = req.Endereco is null
                    ? null
                    : new EasyStock.Domain.ValueObjects.Endereco(
                        Logradouro: req.Endereco.Logradouro,
                        Numero: req.Endereco.Numero,
                        Complemento: req.Endereco.Complemento,
                        Bairro: req.Endereco.Bairro,
                        Cidade: req.Endereco.Cidade,
                        Uf: req.Endereco.Uf,
                        Cep: req.Endereco.Cep,
                        Pais: "BR");

                config.AtualizarDadosEmitente(req.InscricaoEstadual, req.InscricaoMunicipal, endereco);
                db.EmpresaConfiguracoesFiscais.Update(config);
            });

            logger.LogInformation("Dados emitente atualizados para empresa {Empresa}.", eid);
            return DataOk(new { mensagem = "Dados do emitente atualizados." });
        }
        catch (EasyStock.Domain.Exceptions.RegraDeDominioVioladaException ex) { return DataBadRequest(ex.Message); }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
    }

    [SwaggerOperation(Summary = "Escolhe o provedor SEFAZ do tenant (mock, focus, enotas).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("provedor")]
    public async Task<IActionResult> EscolherProvedor(
        [FromBody] EscolherProvedorRequest req,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        if (string.IsNullOrWhiteSpace(req.Provedor)) return DataBadRequest("Provedor obrigatorio.");

        try
        {
            await uow.ExecuteInTransactionAsync(async txCt =>
            {
                var config = await db.EmpresaConfiguracoesFiscais
                    .FirstOrDefaultAsync(c => c.EmpresaId == eid, txCt)
                    ?? throw new InvalidOperationException("Config fiscal nao encontrada. Salve os dados do emitente primeiro.");

                config.EscolherProvedor(req.Provedor);
                db.EmpresaConfiguracoesFiscais.Update(config);
            });

            return DataOk(new { mensagem = $"Provedor '{req.Provedor.ToLowerInvariant()}' configurado." });
        }
        catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
        catch (EasyStock.Domain.Exceptions.RegraDeDominioVioladaException ex) { return DataBadRequest(ex.Message); }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
    }

    [SwaggerOperation(Summary = "Configurar CSC (Codigo de Seguranca do Contribuinte) para NFC-e")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("csc")]
    public async Task<IActionResult> ConfigurarCsc(
        [FromBody] ConfigurarCscRequest req,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        if (string.IsNullOrWhiteSpace(req.CscId)) return DataBadRequest("CSC ID obrigatorio.");
        if (string.IsNullOrWhiteSpace(req.CscToken)) return DataBadRequest("CSC Token obrigatorio.");

        try
        {
            await uow.ExecuteInTransactionAsync(async txCt =>
            {
                var config = await db.EmpresaConfiguracoesFiscais
                    .FirstOrDefaultAsync(c => c.EmpresaId == eid, txCt)
                    ?? throw new InvalidOperationException("Config fiscal nao encontrada. Subir certificado primeiro.");

                config.ConfigurarCsc(req.CscId, req.CscToken);
                db.EmpresaConfiguracoesFiscais.Update(config);
            });

            logger.LogInformation("CSC configurado para empresa {Empresa}", eid);
            return DataOk(new { mensagem = "CSC configurado com sucesso." });
        }
        catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
    }

    [SwaggerOperation(Summary = "Alterar serie e ambiente fiscal do tenant")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("serie-ambiente")]
    public async Task<IActionResult> AlterarSerieAmbiente(
        [FromBody] AlterarSerieAmbienteRequest req,
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

                if (req.Ambiente.HasValue)
                    config.AlterarAmbiente(req.Ambiente.Value);

                if (req.SerieNfce.HasValue)
                    config.AlterarSerieNfce(req.SerieNfce.Value);

                db.EmpresaConfiguracoesFiscais.Update(config);
            });

            return DataOk(new { mensagem = "Serie e ambiente atualizados." });
        }
        catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
    }

    [SwaggerOperation(Summary = "Desabilita emissao fiscal (killswitch)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpPost("desabilitar")]
    public async Task<IActionResult> Desabilitar(
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        await uow.ExecuteInTransactionAsync(async txCt =>
        {
            var config = await db.EmpresaConfiguracoesFiscais
                .FirstOrDefaultAsync(c => c.EmpresaId == eid, txCt);
            if (config is null) return;

            config.Desabilitar();
            db.EmpresaConfiguracoesFiscais.Update(config);
        });

        logger.LogWarning("Emissao fiscal DESABILITADA para empresa {Empresa}", eid);
        return DataOk(new { habilitada = false });
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

        // Identidade do emitente (CNPJ/razão/fantasia) — usada pelo caixa NFC-e;
        // vive na Empresa, não na config fiscal.
        var empresa = await db.Empresas
            .AsNoTracking()
            .Where(e => e.Id == eid)
            .Select(e => new { e.Documento, e.Nome, e.NomeFantasia })
            .FirstOrDefaultAsync(ct);

        var cert = config.CertificadoCredencialId.HasValue
            ? await credencialRepo.GetByIdAsync(config.CertificadoCredencialId.Value, ct)
            : null;

        return DataOk(new
        {
            configurado = true,
            habilitada = config.Habilitada,
            cnpj = empresa?.Documento,
            razaoSocial = empresa?.Nome,
            nomeFantasia = empresa?.NomeFantasia,
            ambiente = config.Ambiente.ToString(),
            regimeTributario = config.RegimeTributario.ToString(),
            provedor = config.ProvedorPreferido,
            serieNfce = config.SerieNfce,
            proximoNumeroNfce = config.ProximoNumeroNfce,
            inscricaoEstadual = config.InscricaoEstadual,
            inscricaoMunicipal = config.InscricaoMunicipal,
            endereco = config.Endereco is null ? null : new
            {
                logradouro = config.Endereco.Logradouro,
                numero = config.Endereco.Numero,
                complemento = config.Endereco.Complemento,
                bairro = config.Endereco.Bairro,
                cidade = config.Endereco.Cidade,
                uf = config.Endereco.Uf,
                cep = config.Endereco.Cep,
            },
            temCsc = !string.IsNullOrWhiteSpace(config.CscId),
            cscId = config.CscId,
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
