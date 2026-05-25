using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

[ApiController]
public class AdminConfiguracoesController(
    EasyStockDbContext db,
    AdminAuditService audit) : EasyStockControllerBase
{
    private static readonly (string Chave, string Valor, string Descricao)[] _defaults =
    [
        ("manutencao_ativa",   "false",                    "Modo manutenção (bloqueia login de tenants)"),
        ("aviso_global",       "",                         "Banner de aviso exibido no topo do app (vazio = oculto)"),
        ("aviso_cor",          "gold",                     "Cor do banner: gold | red | basil"),
        ("dias_trial_padrao",  "30",                       "Dias padrão ao conceder trial para novos tenants"),
        ("email_suporte",      "suporte@easystock.com.br", "E-mail de suporte exibido no app"),
        ("versao_minima_pwa",  "1.0.0",                    "Força atualização do PWA se versão instalada for menor"),
    ];

    private static readonly HashSet<string> _publicKeys = ["manutencao_ativa", "aviso_global", "aviso_cor", "email_suporte", "versao_minima_pwa"];

    [HttpGet("api/admin/configuracoes")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetAll()
    {
        await EnsureDefaults();
        var configs = await db.ConfiguracoesSistema
            .OrderBy(c => c.Chave)
            .Select(c => new { c.Chave, c.Valor, c.Descricao, c.AlteradoEm, c.AlteradoPor })
            .ToListAsync();
        return DataOk(configs);
    }

    [HttpPatch("api/admin/configuracoes")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update([FromBody] PatchConfiguracoesRequest req)
    {
        if (req.Items is null || req.Items.Length == 0)
            return DataBadRequest("Nenhum item informado.");

        await EnsureDefaults();

        var email = HttpContext.User.FindFirst("email")?.Value
                    ?? HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? "system";

        var chaves = req.Items.Select(i => i.Chave).ToList();
        var existentes = await db.ConfiguracoesSistema.Where(c => chaves.Contains(c.Chave)).ToListAsync();

        foreach (var item in req.Items)
        {
            var config = existentes.FirstOrDefault(c => c.Chave == item.Chave);
            if (config is not null)
                config.Atualizar(item.Valor ?? "", email);
            else
                db.ConfiguracoesSistema.Add(ConfiguracaoSistema.Criar(item.Chave, item.Valor ?? "", ""));
        }

        await db.CommitAsync();
        await audit.LogAsync("ConfiguracaoAlterada", string.Join(", ", req.Items.Select(i => $"{i.Chave}={i.Valor}")));

        return DataOk(new { atualizado = req.Items.Length });
    }

    [HttpGet("api/configuracoes/publica")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublica()
    {
        var configs = await db.ConfiguracoesSistema
            .Where(c => _publicKeys.Contains(c.Chave))
            .Select(c => new { c.Chave, c.Valor })
            .ToListAsync();

        var dict = configs.ToDictionary(c => c.Chave, c => c.Valor);

        // Ensure all public keys are present with defaults
        foreach (var (chave, valor, _) in _defaults.Where(d => _publicKeys.Contains(d.Chave)))
            dict.TryAdd(chave, valor);

        return DataOk(dict);
    }

    private async Task EnsureDefaults()
    {
        var existentes = await db.ConfiguracoesSistema.Select(c => c.Chave).ToListAsync();
        var faltando = _defaults.Where(d => !existentes.Contains(d.Chave)).ToList();
        if (faltando.Count == 0) return;

        foreach (var (chave, valor, descricao) in faltando)
            db.ConfiguracoesSistema.Add(ConfiguracaoSistema.Criar(chave, valor, descricao));

        await db.CommitAsync();
    }
}

public record PatchConfigItemRequest(string Chave, string? Valor);
public record PatchConfiguracoesRequest(PatchConfigItemRequest[] Items);
