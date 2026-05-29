using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Resolve um Usuario "Sistema Mobile Sync" pra entries de auditoria.
///
/// Tabelas <c>produto_alteracoes</c>, <c>movimentacao_estoque_alteracoes</c> e
/// <c>audit_logs</c> tem <c>UsuarioId NOT NULL</c>. Sync mobile nao tem JWT —
/// nao tem como usar Usuario do request. Solucao: 1 Usuario dedicado por
/// empresa, identificavel pelo email padronizado, marcado <c>Ativo=false</c>
/// pra nunca conseguir logar.
///
/// Idempotente:
/// - Procura por email padronizado (escopado por empresa).
/// - Se existe, retorna o Id.
/// - Se nao existe, cria + vincula via UsuarioEmpresa, salva.
/// </summary>
public class MobileSystemUserResolver(EasyStockDbContext db)
{
    private readonly EasyStockDbContext _db = db;

    public async Task<Guid> GetOrCreateAsync(Guid empresaId, CancellationToken ct = default)
    {
        // Email escopado por empresa pra evitar conflito entre tenants.
        // Formato: mobile-sync+<empresaId-N>@system.local
        var email = $"mobile-sync+{empresaId:N}@system.local";

        // IgnoreQueryFilters: Usuario nao tem EmpresaId direto (m:n via UsuarioEmpresa),
        // mas defensive — outros filters podem mexer.
        var existing = await _db.Set<Usuario>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (existing.HasValue) return existing.Value;

        // Cria Usuario sistema. SenhaHash placeholder invalido pra login.
        // Ativo=false: hard-block de login mesmo se alguem descobrir.
        var novo = Usuario.Criar("Sistema Mobile Sync", email, "DISABLED_NO_LOGIN");
        novo.Ativo = false;
        novo.EmailConfirmado = false;
        _db.Add(novo);

        _db.Add(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(),
            UsuarioId = novo.Id,
            EmpresaId = empresaId,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return novo.Id;
    }
}
