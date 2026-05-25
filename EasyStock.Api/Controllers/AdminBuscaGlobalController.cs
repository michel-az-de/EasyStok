using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Busca global cross-tenant pra Cmd+K do back-office. Permite o operador SuperAdmin
/// localizar Cliente / Loja / Usuário em segundos a partir de qualquer fragmento de
/// nome, email, CNPJ ou ID. Resultados retornam com PII mascarada — operador desmascara
/// individualmente via endpoint /usuario-pii (auditado).
///
/// Padrão Linear/Vercel: agrupado por seção, com debounce client-side de 200ms.
/// </summary>
[ApiController]
[Route("api/admin/buscar-global")]
[Authorize(Policy = "SuperAdmin")]
public class AdminBuscaGlobalController(
    EasyStockDbContext db,
    AdminAuditService audit) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Buscar([FromQuery] string q, [FromQuery] int limit = 8)
    {
        var termo = (q ?? string.Empty).Trim();
        if (termo.Length < 2)
            return DataOk(new { clientes = Array.Empty<object>(), lojas = Array.Empty<object>(), usuarios = Array.Empty<object>(), q = termo });

        // Clamp pra não pegar busca abusiva.
        var lim = Math.Clamp(limit, 1, 20);

        // Pattern ILIKE — cobre prefixo/sufixo/middle do termo. Postgres faz seq scan
        // até as colunas mais usadas terem índice; em produção, considerar pg_trgm.
        var padrao = $"%{termo}%";
        // CNPJ sem pontuação: também tenta match com o termo "formatado" mesmo nível.
        // Match exato sem pontuação ficaria custoso em SQL puro — solução simples por
        // ora: também aceitar padrão só-dígitos como pattern alternativo. Em produção
        // adicionaria coluna desnormalizada `documento_limpo` indexada.
        var termoSoDigitos = new string(termo.Where(char.IsDigit).ToArray());
        var padraoDigitos = string.IsNullOrEmpty(termoSoDigitos) ? null : $"%{termoSoDigitos}%";

        // ─────────────────────────── Clientes (Empresa) ───────────────────────────
        var clientesQuery = db.Empresas.AsNoTracking()
            .Where(e => EF.Functions.ILike(e.Nome, padrao)
                        || (e.Documento != null && EF.Functions.ILike(e.Documento, padrao))
                        || (padraoDigitos != null && e.Documento != null && EF.Functions.ILike(e.Documento, padraoDigitos)));
        var clientes = await clientesQuery
            .OrderBy(e => e.Nome)
            .Take(lim)
            .Select(e => new
            {
                id = e.Id,
                nome = e.Nome,
                documento = MascararDoc(e.Documento),
                tipo = "cliente",
                url = "/Tenants/Detail/" + e.Id
            })
            .ToListAsync();

        // ─────────────────────────── Lojas (cross-tenant) ───────────────────────────
        var lojasQuery = db.Lojas.AsNoTracking()
            .Where(l => EF.Functions.ILike(l.Nome, padrao));
        var lojas = await lojasQuery
            .OrderBy(l => l.Nome)
            .Take(lim)
            .Join(db.Empresas, l => l.EmpresaId, e => e.Id, (l, e) => new
            {
                id = l.Id,
                nome = l.Nome,
                empresaId = e.Id,
                empresaNome = e.Nome,
                ativa = l.Ativa,
                tipo = "loja",
                // Loja detail page ainda é P2 follow-up — por ora vai pra Cliente 360 tab Lojas.
                url = "/Tenants/Detail/" + e.Id + "?tab=lojas"
            })
            .ToListAsync();

        // ─────────────────────────── Usuários (cross-tenant, mascarado) ───────────────────────────
        var usuariosQuery = db.Usuarios.AsNoTracking()
            .Where(u => EF.Functions.ILike(u.Nome, padrao) || EF.Functions.ILike(u.Email, padrao));
        // Junta com a primeira empresa do usuário pra dar contexto (qual cliente).
        var usuariosRaw = await usuariosQuery
            .OrderBy(u => u.Nome)
            .Take(lim)
            .Select(u => new
            {
                u.Id,
                u.Nome,
                u.Email,
                u.Ativo,
                empresaId = db.UsuariosEmpresas
                    .Where(ue => ue.UsuarioId == u.Id)
                    .Select(ue => (Guid?)ue.EmpresaId)
                    .FirstOrDefault()
            })
            .ToListAsync();
        var empresaIds = usuariosRaw.Where(u => u.empresaId.HasValue).Select(u => u.empresaId!.Value).Distinct().ToList();
        var empresaNomes = empresaIds.Count > 0
            ? await db.Empresas.AsNoTracking()
                .Where(e => empresaIds.Contains(e.Id))
                .Select(e => new { e.Id, e.Nome })
                .ToDictionaryAsync(e => e.Id, e => e.Nome)
            : new();

        var usuarios = usuariosRaw.Select(u => new
        {
            id = u.Id,
            nome = u.Nome,
            emailMascarado = MascararEmail(u.Email),
            empresaId = u.empresaId,
            empresaNome = u.empresaId.HasValue && empresaNomes.TryGetValue(u.empresaId.Value, out var en) ? en : "(sem cliente)",
            ativo = u.Ativo,
            tipo = "usuario",
            url = u.empresaId.HasValue
                ? $"/Tenants/Detail/{u.empresaId.Value}?tab=usuarios"
                : "#"
        }).ToList();

        // Audit do uso da busca — sem expor termo cru se for email/doc (evita PII no log).
        var termoLog = ContemPii(termo) ? "(redigido — PII)" : termo;
        await audit.LogAsync(
            "BuscaGlobalExecutada",
            $"Termo='{termoLog}', Resultados=clientes:{clientes.Count}+lojas:{lojas.Count}+usuarios:{usuarios.Count}");

        return DataOk(new
        {
            q = termo,
            clientes,
            lojas,
            usuarios,
            total = clientes.Count + lojas.Count + usuarios.Count
        });
    }

    private static bool ContemPii(string termo) =>
        termo.Contains('@') || termo.Where(char.IsDigit).Count() >= 6;

    private static string? MascararDoc(string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc)) return null;
        var d = new string(doc.Where(char.IsDigit).ToArray());
        if (d.Length == 11) return $"{d[..3]}.***.***-{d[^2..]}";
        if (d.Length == 14) return $"{d[..2]}.***.***/****-{d[^2..]}";
        // Formato desconhecido: mascara metade do meio.
        return doc.Length > 6 ? $"{doc[..3]}***{doc[^3..]}" : "***";
    }

    private static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "***";
        return email[0] + "***@" + email[(at + 1)..];
    }
}
