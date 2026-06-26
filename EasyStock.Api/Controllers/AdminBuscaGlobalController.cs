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
    IAdminBuscaGlobalQueries buscaQueries,
    AdminAuditService audit) : EasyStockControllerBase
{
    // Mínimo de dígitos para acionar a busca por documento (CNPJ). Evita que 1-2 dígitos
    // avulsos (ex: extraídos de um payload com caracteres especiais) casem com quase todos
    // os CNPJs via ILIKE (QA ADM-008, issue 698).
    private const int MinDigitosBuscaDocumento = 3;

    [HttpGet]
    public async Task<IActionResult> Buscar([FromQuery] string q, [FromQuery] int limit = 8, CancellationToken ct = default)
    {
        var termo = (q ?? string.Empty).Trim();
        if (termo.Length < 2)
            return DataOk(new { clientes = Array.Empty<object>(), lojas = Array.Empty<object>(), usuarios = Array.Empty<object>(), q = termo });

        // Clamp pra não pegar busca abusiva.
        var lim = Math.Clamp(limit, 1, 20);

        // Pattern ILIKE — cobre prefixo/sufixo/middle do termo. Postgres faz seq scan
        // até as colunas mais usadas terem índice; em produção, considerar pg_trgm.
        var padrao = $"%{termo}%";
        // CNPJ sem pontuação: também tenta match com o termo só-dígitos. Em produção
        // adicionaria coluna desnormalizada `documento_limpo` indexada.
        var termoSoDigitos = new string(termo.Where(char.IsDigit).ToArray());
        // Só busca por documento quando há dígitos suficientes. Antes, um único dígito
        // (ex: o "1" de "<img ...alert(1)>") virava ILIKE %1% e casava qualquer CNPJ com "1".
        var padraoDigitos = termoSoDigitos.Length >= MinDigitosBuscaDocumento ? $"%{termoSoDigitos}%" : null;

        var resultado = await buscaQueries.BuscarAsync(padrao, padraoDigitos, lim, ct);

        var clientes = resultado.Clientes.Select(c => new
        {
            id = c.Id,
            nome = c.Nome,
            documento = MascararDoc(c.Documento),
            tipo = "cliente",
            url = "/Tenants/Detail/" + c.Id
        }).ToList();

        var lojas = resultado.Lojas.Select(l => new
        {
            id = l.Id,
            nome = l.Nome,
            empresaId = l.EmpresaId,
            empresaNome = l.EmpresaNome,
            ativa = l.Ativa,
            tipo = "loja",
            // Loja detail page ainda é P2 follow-up — por ora vai pra Cliente 360 tab Lojas.
            url = "/Tenants/Detail/" + l.EmpresaId + "?tab=lojas"
        }).ToList();

        var usuarios = resultado.Usuarios.Select(u => new
        {
            id = u.Id,
            nome = u.Nome,
            emailMascarado = MascararEmail(u.Email),
            empresaId = u.EmpresaId,
            empresaNome = u.EmpresaNome ?? "(sem cliente)",
            ativo = u.Ativo,
            tipo = "usuario",
            url = u.EmpresaId.HasValue
                ? $"/Tenants/Detail/{u.EmpresaId.Value}?tab=usuarios"
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
