using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarLoja;
using EasyStock.Application.UseCases.CriarLoja;
using EasyStock.Application.UseCases.DesativarLoja;
using EasyStock.Application.UseCases.ReativarLoja;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints SuperAdmin de gestão de Cliente (tenant). Centraliza CRUD de Lojas
/// e demais ações por tenant que não cabem em <see cref="AdminUsuariosTenantController"/>
/// (ações por usuário) ou <see cref="AdminTenantsController"/> (assinatura/plano).
/// Toda mutação exige `motivo` (≥10 chars) auditado.
/// </summary>
[ApiController]
[Route("api/admin/clientes")]
[Authorize(Policy = "SuperAdmin")]
public class AdminClientesController(
    ILojaRepository lojaRepo,
    CriarLojaUseCase criarLojaUseCase,
    AtualizarLojaUseCase atualizarLojaUseCase,
    DesativarLojaUseCase desativarLojaUseCase,
    ReativarLojaUseCase reativarLojaUseCase,
    AdminAuditService audit,
    ILogger<AdminClientesController> logger) : EasyStockControllerBase
{
    private const int MotivoMinimo = 10;

    // ─────────────────────────── #10: Criar loja ───────────────────────────

    [HttpPost("{tenantId:guid}/lojas")]
    public async Task<IActionResult> CriarLoja(Guid tenantId, [FromBody] CriarLojaAdminRequest req)
    {
        if (tenantId == Guid.Empty) return DataBadRequest("Cliente inválido.");
        if (!ValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);
        if (string.IsNullOrWhiteSpace(req?.Nome)) return DataBadRequest("Nome da loja é obrigatório.");

        try
        {
            var resultado = await criarLojaUseCase.ExecuteAsync(new CriarLojaCommand(
                EmpresaId: tenantId,
                Nome: req!.Nome,
                Descricao: req.Descricao,
                Documento: req.Documento,
                Endereco: req.Endereco,
                Telefone: req.Telefone));

            await audit.LogAsync(
                "LojaCriada",
                $"LojaId={resultado.Id}, Nome={resultado.Nome}",
                tenantId: tenantId,
                motivo: motivo,
                entidadeAfetadaId: resultado.Id);

            return DataCreated($"/api/admin/clientes/{tenantId}/lojas/{resultado.Id}", resultado);
        }
        catch (PlanoLimiteAtingidoException ex)
        {
            return DataBadRequest($"Limite de lojas do plano atingido. Recurso: {ex.Recurso}.");
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao criar loja para tenant {TenantId}", tenantId);
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao criar loja.");
        }
    }

    // ─────────────────────────── #11: Atualizar loja (com diff) ───────────────────────────

    [HttpPatch("{tenantId:guid}/lojas/{lojaId:guid}")]
    public async Task<IActionResult> AtualizarLoja(Guid tenantId, Guid lojaId, [FromBody] AtualizarLojaAdminRequest req)
    {
        if (tenantId == Guid.Empty || lojaId == Guid.Empty) return DataBadRequest("Cliente ou loja inválidos.");
        if (!ValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);
        if (string.IsNullOrWhiteSpace(req?.Nome)) return DataBadRequest("Nome da loja é obrigatório.");

        // Carrega antes pra calcular diff before/after.
        var antes = await lojaRepo.GetByIdAsync(tenantId, lojaId);
        if (antes is null) return DataNotFound("Loja não encontrada neste cliente.");

        var alteracoes = new List<string>();
        if (!string.Equals(antes.Nome, req!.Nome.Trim(), StringComparison.Ordinal))
            alteracoes.Add($"nome: '{antes.Nome}' → '{req.Nome.Trim()}'");
        if (!string.Equals(antes.Descricao ?? "", (req.Descricao ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add($"descricao alterada");
        if (!string.Equals(antes.Documento ?? "", (req.Documento ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add($"documento: '{antes.Documento ?? "-"}' → '{req.Documento ?? "-"}'");
        if (!string.Equals(antes.Endereco ?? "", (req.Endereco ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add("endereco alterado");
        if (!string.Equals(antes.Telefone ?? "", (req.Telefone ?? "").Trim(), StringComparison.Ordinal))
            alteracoes.Add($"telefone: '{antes.Telefone ?? "-"}' → '{req.Telefone ?? "-"}'");

        try
        {
            await atualizarLojaUseCase.ExecuteAsync(new AtualizarLojaCommand(
                LojaId: lojaId,
                EmpresaId: tenantId,
                Nome: req.Nome,
                Descricao: req.Descricao,
                Documento: req.Documento,
                Endereco: req.Endereco,
                Telefone: req.Telefone));
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao atualizar loja {LojaId} do tenant {TenantId}", lojaId, tenantId);
            return Problem(detail: ex.Message, statusCode: 500);
        }

        await audit.LogAsync(
            "LojaAtualizada",
            alteracoes.Count > 0 ? string.Join("; ", alteracoes) : "sem mudanças efetivas",
            tenantId: tenantId,
            motivo: motivo,
            entidadeAfetadaId: lojaId);

        return DataOk(new { lojaId, alterado = alteracoes.Count > 0, alteracoes });
    }

    // ─────────────────────────── #12: Toggle ativa/desativa ───────────────────────────

    [HttpPost("{tenantId:guid}/lojas/{lojaId:guid}/toggle")]
    public async Task<IActionResult> ToggleLoja(Guid tenantId, Guid lojaId, [FromBody] ToggleLojaRequest req)
    {
        if (tenantId == Guid.Empty || lojaId == Guid.Empty) return DataBadRequest("Cliente ou loja inválidos.");
        if (!ValidarMotivo(req?.Motivo, out var motivo, out var erro)) return DataBadRequest(erro!);

        var loja = await lojaRepo.GetByIdAsync(tenantId, lojaId);
        if (loja is null) return DataNotFound("Loja não encontrada neste cliente.");

        try
        {
            if (req!.Ativa && !loja.Ativa)
            {
                await reativarLojaUseCase.ExecuteAsync(new ReativarLojaCommand(lojaId, tenantId));
                await audit.LogAsync("LojaReativada", $"LojaId={lojaId}, Nome={loja.Nome}",
                    tenantId, motivo: motivo, entidadeAfetadaId: lojaId);
                return DataOk(new { lojaId, ativa = true });
            }
            if (!req.Ativa && loja.Ativa)
            {
                await desativarLojaUseCase.ExecuteAsync(new DesativarLojaCommand(lojaId, tenantId));
                await audit.LogAsync("LojaDesativada", $"LojaId={lojaId}, Nome={loja.Nome}",
                    tenantId, motivo: motivo, entidadeAfetadaId: lojaId);
                return DataOk(new { lojaId, ativa = false });
            }
            // Estado já é o desejado — idempotente.
            return DataOk(new { lojaId, ativa = loja.Ativa, mensagem = "Loja já estava neste estado." });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao alternar loja {LojaId} do tenant {TenantId}", lojaId, tenantId);
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    private static bool ValidarMotivo(string? motivo, out string motivoNormalizado, out string? erro)
    {
        motivoNormalizado = (motivo ?? string.Empty).Trim();
        if (motivoNormalizado.Length < MotivoMinimo)
        {
            erro = $"Justificativa obrigatória (mínimo {MotivoMinimo} caracteres) — fica registrada no audit log.";
            return false;
        }
        if (motivoNormalizado.Length > 1000)
        {
            erro = "Justificativa muito longa (máx 1000 caracteres).";
            return false;
        }
        erro = null;
        return true;
    }
}

public record CriarLojaAdminRequest(string Motivo, string Nome, string? Descricao, string? Documento, string? Endereco, string? Telefone);
public record AtualizarLojaAdminRequest(string Motivo, string Nome, string? Descricao, string? Documento, string? Endereco, string? Telefone);
public record ToggleLojaRequest(string Motivo, bool Ativa);
