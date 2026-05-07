using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public sealed class LojaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), ILojaRepository
{
    private IMongoCollection<Loja> Collection => Context.GetCollection<Loja>(MongoCollectionNames.Lojas);

    public async Task<Loja?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<Loja?> GetByIdAsync(Guid empresaId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<Loja>> GetByEmpresaAsync(Guid empresaId) =>
        await Collection.Find(x => x.EmpresaId == empresaId).SortBy(x => x.Nome).ToListAsync();

    public async Task<int> CountByEmpresaAsync(Guid empresaId) =>
        (int)await Collection.CountDocumentsAsync(x => x.EmpresaId == empresaId && x.Ativa);

    public Task AddAsync(Loja loja)
    {
        EnqueueInsert(Collection, loja);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Loja loja)
    {
        EnqueueReplaceScoped(Collection, loja.Id, loja.EmpresaId, loja);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<Loja>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
    {
        var regex = new MongoDB.Bson.BsonRegularExpression(BuildContainsPattern(termo.Trim()), "i");
        return await Collection.Find(
            Builders<Loja>.Filter.Eq(x => x.EmpresaId, empresaId) &
            Builders<Loja>.Filter.Regex(x => x.Nome, regex))
            .Limit(maxResults).ToListAsync();
    }
}

public sealed class FornecedorRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IFornecedorRepository
{
    private IMongoCollection<Fornecedor> Collection => Context.GetCollection<Fornecedor>(MongoCollectionNames.Fornecedores);

    public async Task<Fornecedor?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<Fornecedor?> GetByIdAsync(Guid empresaId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<(IEnumerable<Fornecedor>, int total)> GetByEmpresaAsync(
        Guid empresaId, int page, int pageSize,
        bool? ativo = null, string? search = null,
        string? sort = "nome", string? order = "asc")
    {
        var filter = Builders<Fornecedor>.Filter.Eq(x => x.EmpresaId, empresaId);
        if (ativo.HasValue)
            filter &= Builders<Fornecedor>.Filter.Eq(x => x.Ativo, ativo.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var regex = new MongoDB.Bson.BsonRegularExpression(BuildContainsPattern(search.Trim()), "i");
            filter &= Builders<Fornecedor>.Filter.Or(
                Builders<Fornecedor>.Filter.Regex(x => x.Nome, regex),
                Builders<Fornecedor>.Filter.Regex(x => x.Documento, regex),
                Builders<Fornecedor>.Filter.Regex(x => x.Email, regex),
                Builders<Fornecedor>.Filter.Regex(x => x.Contato, regex));
        }
        var total = (int)await Collection.CountDocumentsAsync(filter);
        var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
        var findFluent = Collection.Find(filter);
        var sorted = sort?.ToLowerInvariant() switch
        {
            "criadoem" => desc ? findFluent.SortByDescending(x => x.CriadoEm) : findFluent.SortBy(x => x.CriadoEm),
            _          => desc ? findFluent.SortByDescending(x => x.Nome) : findFluent.SortBy(x => x.Nome),
        };
        var items = await sorted
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task AddAsync(Fornecedor fornecedor)
    {
        EnqueueInsert(Collection, fornecedor);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<Fornecedor>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
    {
        termo = termo.Trim();
        if (string.IsNullOrWhiteSpace(termo)) return [];

        var regex = new MongoDB.Bson.BsonRegularExpression(BuildContainsPattern(termo), "i");
        var filter = Builders<Fornecedor>.Filter.Eq(x => x.EmpresaId, empresaId)
            & Builders<Fornecedor>.Filter.Eq(x => x.Ativo, true)
            & Builders<Fornecedor>.Filter.Or(
                Builders<Fornecedor>.Filter.Regex(x => x.Nome, regex),
                Builders<Fornecedor>.Filter.Regex(x => x.Documento, regex),
                Builders<Fornecedor>.Filter.Regex(x => x.Email, regex),
                Builders<Fornecedor>.Filter.Regex(x => x.Contato, regex));

        return await Collection.Find(filter).Limit(maxResults).ToListAsync();
    }

    public Task UpdateAsync(Fornecedor fornecedor)
    {
        EnqueueReplaceScoped(Collection, fornecedor.Id, fornecedor.EmpresaId, fornecedor);
        return Task.CompletedTask;
    }

    // ── Audit (Onda P4) ───────────────────────────────────────
    // Mongo backend não está em produção (Postgre é o ativo). Stub
    // mantém o contrato sem custo: retorna lista vazia e ignora write.
    private IMongoCollection<FornecedorAlteracao> AlteracoesCollection =>
        Context.Database.GetCollection<FornecedorAlteracao>("fornecedor_alteracoes");

    public Task AddAlteracaoAsync(FornecedorAlteracao alteracao)
    {
        EnqueueInsert(AlteracoesCollection, alteracao);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<FornecedorAlteracao>> GetAlteracoesAsync(Guid fornecedorId, int max = 200) =>
        await AlteracoesCollection.Find(a => a.FornecedorId == fornecedorId)
            .SortByDescending(a => a.AlteradoEm)
            .Limit(max)
            .ToListAsync();
}

public sealed class PedidoFornecedorRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IPedidoFornecedorRepository
{
    private IMongoCollection<PedidoFornecedor> Collection => Context.GetCollection<PedidoFornecedor>(MongoCollectionNames.PedidosFornecedor);

    public async Task<PedidoFornecedor?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public Task AddAsync(PedidoFornecedor pedido)
    {
        EnqueueInsert(Collection, pedido);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PedidoFornecedor pedido)
    {
        EnqueueReplaceScoped(Collection, pedido.Id, pedido.EmpresaId, pedido);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetHistoricoPorFornecedorAsync(Guid empresaId, Guid fornecedorId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.FornecedorId == fornecedorId)
            .SortByDescending(x => x.DataPedido)
            .ToListAsync();

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAtrasadosAsync(Guid empresaId, DateTime referencia) =>
        await Collection.Find(x => x.EmpresaId == empresaId &&
                                   x.PrevisaoEntrega.HasValue &&
                                   x.PrevisaoEntrega.Value.Date < referencia.Date &&
                                   (x.Status == StatusPedidoFornecedor.Aberto || x.Status == StatusPedidoFornecedor.EmTransito))
            .ToListAsync();

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosRecebidosNoPeriodoAsync(Guid empresaId, DateTime de, DateTime ate) =>
        await Collection.Find(x => x.EmpresaId == empresaId &&
                                   x.Status == StatusPedidoFornecedor.Recebido &&
                                   x.DataRecebimento.HasValue &&
                                   x.DataRecebimento.Value >= de &&
                                   x.DataRecebimento.Value <= ate)
            .ToListAsync();

    public async Task<int> CountPedidosAbertosOuEmTransitoAsync(Guid empresaId, Guid fornecedorId) =>
        (int)await Collection.CountDocumentsAsync(x => x.EmpresaId == empresaId &&
                                                      x.FornecedorId == fornecedorId &&
                                                      (x.Status == StatusPedidoFornecedor.Aberto || x.Status == StatusPedidoFornecedor.EmTransito));

    public async Task<IReadOnlyCollection<PedidoFornecedor>> GetPedidosAbertosComFornecedorAsync(Guid empresaId)
    {
        var pedidos = await Collection.Find(x => x.EmpresaId == empresaId &&
                                                  (x.Status == StatusPedidoFornecedor.Aberto || x.Status == StatusPedidoFornecedor.EmTransito))
            .SortByDescending(x => x.DataPedido)
            .ToListAsync();

        if (pedidos.Count == 0)
            return pedidos;

        var fornecedorIds = pedidos.Select(p => p.FornecedorId).Distinct().ToList();
        var fornecedores = await Context.GetCollection<Fornecedor>(MongoCollectionNames.Fornecedores)
            .Find(f => fornecedorIds.Contains(f.Id))
            .ToListAsync();
        var fornecedorMap = fornecedores.ToDictionary(f => f.Id);

        foreach (var pedido in pedidos)
        {
            if (fornecedorMap.TryGetValue(pedido.FornecedorId, out var fornecedor))
                pedido.Fornecedor = fornecedor;
        }

        return pedidos;
    }

    public async Task<(int QuantidadePedidos, decimal TotalGasto, decimal? LeadTimeRealMedioDias, decimal FrequenciaPedidosPorMes)> GetEstatisticasAsync(Guid empresaId, Guid fornecedorId)
    {
        var pedidos = await Collection.Find(x => x.EmpresaId == empresaId && x.FornecedorId == fornecedorId).ToListAsync();
        if (pedidos.Count == 0)
            return (0, 0m, null, 0m);

        var totalGasto = pedidos
            .Where(x => x.Status != StatusPedidoFornecedor.Cancelado)
            .Sum(x => x.ValorEstimado ?? 0m);

        var leadTimes = pedidos
            .Where(x => x.DataRecebimento.HasValue && x.DataRecebimento.Value >= x.DataPedido)
            .Select(x => (decimal)(x.DataRecebimento!.Value.Date - x.DataPedido.Date).TotalDays)
            .ToList();

        var primeiroPedido = pedidos.Min(x => x.DataPedido.Date);
        var ultimoMarco = pedidos.Max(x => (x.DataRecebimento ?? x.DataPedido).Date);
        var meses = Math.Max(1m, (decimal)(ultimoMarco - primeiroPedido).TotalDays / 30m);
        var frequencia = decimal.Round(pedidos.Count / meses, 2);

        return (
            pedidos.Count,
            totalGasto,
            leadTimes.Count == 0 ? null : decimal.Round(leadTimes.Average(), 2),
            frequencia);
    }

    public async Task<IEnumerable<PedidoFornecedor>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
    {
        var regex = new MongoDB.Bson.BsonRegularExpression(BuildContainsPattern(termo.Trim()), "i");
        return await Collection.Find(
            Builders<PedidoFornecedor>.Filter.Eq(x => x.EmpresaId, empresaId) &
            Builders<PedidoFornecedor>.Filter.Or(
                Builders<PedidoFornecedor>.Filter.Regex(x => x.Observacoes, regex),
                Builders<PedidoFornecedor>.Filter.Regex(x => x.Tracking, regex)))
            .SortByDescending(x => x.DataPedido)
            .Limit(maxResults).ToListAsync();
    }
}

public sealed class UsuarioRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IUsuarioRepository
{
    private IMongoCollection<Usuario> Usuarios => Context.GetCollection<Usuario>(MongoCollectionNames.Usuarios);
    private IMongoCollection<UsuarioEmpresa> UsuariosEmpresas => Context.GetCollection<UsuarioEmpresa>(MongoCollectionNames.UsuariosEmpresas);
    private IMongoCollection<UsuarioPerfil> UsuariosPerfis => Context.GetCollection<UsuarioPerfil>(MongoCollectionNames.UsuariosPerfis);
    private IMongoCollection<Perfil> Perfis => Context.GetCollection<Perfil>(MongoCollectionNames.Perfis);
    private IMongoCollection<PerfilPermissao> PerfisPermissoes => Context.GetCollection<PerfilPermissao>(MongoCollectionNames.PerfisPermissoes);

    public async Task<Usuario?> GetByIdAsync(Guid id)
    {
        var usuario = await Usuarios.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (usuario is null) return null;

        await HydrateUsuarioAsync(usuario);
        return usuario;
    }

    public async Task<Usuario?> GetByEmailAsync(string email)
    {
        var usuario = await Usuarios.Find(x => x.Email == email).FirstOrDefaultAsync();
        if (usuario is null) return null;

        await HydrateUsuarioAsync(usuario);
        return usuario;
    }

    public async Task<(IEnumerable<Usuario> Usuarios, int Total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize)
    {
        var links = await UsuariosEmpresas.Find(x => x.EmpresaId == empresaId && x.Ativo).ToListAsync();
        var ids = links.Select(x => x.UsuarioId).Distinct().ToList();

        if (ids.Count == 0)
            return ([], 0);

        var usuarios = await Usuarios.Find(x => ids.Contains(x.Id))
            .SortBy(x => x.Nome)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        await HydrateUsuariosAsync(usuarios);

        return (usuarios, ids.Count);
    }

    public async Task<int> CountByEmpresaAsync(Guid empresaId) =>
        (int)await UsuariosEmpresas.CountDocumentsAsync(x => x.EmpresaId == empresaId && x.Ativo);

    public Task AddAsync(Usuario usuario)
    {
        EnqueueInsert(Usuarios, usuario);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Usuario usuario)
    {
        usuario.Empresas = null;
        usuario.Perfis = null;
        EnqueueReplace(Usuarios, usuario.Id, usuario);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<Usuario>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
    {
        var links = await UsuariosEmpresas.Find(x => x.EmpresaId == empresaId && x.Ativo).ToListAsync();
        var ids = links.Select(x => x.UsuarioId).Distinct().ToList();
        if (ids.Count == 0) return [];

        var regex = new MongoDB.Bson.BsonRegularExpression(BuildContainsPattern(termo.Trim()), "i");
        return await Usuarios.Find(
            Builders<Usuario>.Filter.In(x => x.Id, ids) &
            Builders<Usuario>.Filter.Or(
                Builders<Usuario>.Filter.Regex(x => x.Nome, regex),
                Builders<Usuario>.Filter.Regex(x => x.Email, regex)))
            .Limit(maxResults).ToListAsync();
    }

    private async Task HydrateUsuarioAsync(Usuario usuario)
    {
        var empresas = await UsuariosEmpresas.Find(x => x.UsuarioId == usuario.Id).ToListAsync();
        var perfis = await UsuariosPerfis.Find(x => x.UsuarioId == usuario.Id).ToListAsync();
        var perfilIds = perfis.Select(x => x.PerfilId).Distinct().ToList();
        var perfisBase = perfilIds.Count == 0 ? [] : await Perfis.Find(x => perfilIds.Contains(x.Id)).ToListAsync();
        var permissoes = perfilIds.Count == 0 ? [] : await PerfisPermissoes.Find(x => perfilIds.Contains(x.PerfilId)).ToListAsync();

        foreach (var perfil in perfisBase)
            perfil.Permissoes = permissoes.Where(x => x.PerfilId == perfil.Id).ToList();

        foreach (var usuarioPerfil in perfis)
            usuarioPerfil.Perfil = perfisBase.FirstOrDefault(x => x.Id == usuarioPerfil.PerfilId);

        usuario.Empresas = empresas;
        usuario.Perfis = perfis;
    }

    private async Task HydrateUsuariosAsync(List<Usuario> usuarios)
    {
        var usuarioIds = usuarios.Select(x => x.Id).ToList();
        if (usuarioIds.Count == 0)
            return;

        var empresas = await UsuariosEmpresas.Find(x => usuarioIds.Contains(x.UsuarioId)).ToListAsync();
        var perfis = await UsuariosPerfis.Find(x => usuarioIds.Contains(x.UsuarioId)).ToListAsync();
        var perfilIds = perfis.Select(x => x.PerfilId).Distinct().ToList();
        var perfisBase = perfilIds.Count == 0 ? [] : await Perfis.Find(x => perfilIds.Contains(x.Id)).ToListAsync();
        var permissoes = perfilIds.Count == 0 ? [] : await PerfisPermissoes.Find(x => perfilIds.Contains(x.PerfilId)).ToListAsync();

        foreach (var perfil in perfisBase)
            perfil.Permissoes = permissoes.Where(x => x.PerfilId == perfil.Id).ToList();

        foreach (var usuarioPerfil in perfis)
            usuarioPerfil.Perfil = perfisBase.FirstOrDefault(x => x.Id == usuarioPerfil.PerfilId);

        var empresasPorUsuario = empresas.GroupBy(x => x.UsuarioId).ToDictionary(x => x.Key, x => (ICollection<UsuarioEmpresa>)x.ToList());
        var perfisPorUsuario = perfis.GroupBy(x => x.UsuarioId).ToDictionary(x => x.Key, x => (ICollection<UsuarioPerfil>)x.ToList());

        foreach (var usuario in usuarios)
        {
            usuario.Empresas = empresasPorUsuario.GetValueOrDefault(usuario.Id, []);
            usuario.Perfis = perfisPorUsuario.GetValueOrDefault(usuario.Id, []);
        }
    }
}

public sealed class PerfilRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IPerfilRepository
{
    private IMongoCollection<Perfil> Perfis => Context.GetCollection<Perfil>(MongoCollectionNames.Perfis);
    private IMongoCollection<PerfilPermissao> Permissoes => Context.GetCollection<PerfilPermissao>(MongoCollectionNames.PerfisPermissoes);

    public async Task<Perfil?> GetByIdAsync(Guid id)
    {
        var perfil = await Perfis.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (perfil is null) return null;

        perfil.Permissoes = await Permissoes.Find(x => x.PerfilId == perfil.Id).ToListAsync();
        return perfil;
    }

    public async Task<IEnumerable<Perfil>> GetPadroesAsync()
    {
        var perfis = await Perfis.Find(x => x.EmpresaId == null).ToListAsync();
        await HydratePermissoesAsync(perfis);
        return perfis;
    }

    public async Task<IEnumerable<Perfil>> GetByEmpresaAsync(Guid empresaId)
    {
        var perfis = await Perfis.Find(x => x.EmpresaId == empresaId).ToListAsync();
        await HydratePermissoesAsync(perfis);
        return perfis;
    }

    public Task AddAsync(Perfil perfil)
    {
        EnqueueInsert(Perfis, perfil);
        return Task.CompletedTask;
    }

    private async Task HydratePermissoesAsync(List<Perfil> perfis)
    {
        var ids = perfis.Select(x => x.Id).ToList();
        if (ids.Count == 0) return;

        var permissoes = await Permissoes.Find(x => ids.Contains(x.PerfilId)).ToListAsync();
        foreach (var perfil in perfis)
            perfil.Permissoes = permissoes.Where(x => x.PerfilId == perfil.Id).ToList();
    }
}

public sealed class PlanoRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IPlanoRepository
{
    private IMongoCollection<Plano> Collection => Context.GetCollection<Plano>(MongoCollectionNames.Planos);

    public async Task<Plano?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<Plano>> GetAtivosAsync() =>
        await Collection.Find(x => x.Ativo).ToListAsync();

    public Task AddAsync(Plano plano)
    {
        EnqueueInsert(Collection, plano);
        return Task.CompletedTask;
    }
}

public sealed class AssinaturaEmpresaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IAssinaturaEmpresaRepository
{
    private IMongoCollection<AssinaturaEmpresa> Collection => Context.GetCollection<AssinaturaEmpresa>(MongoCollectionNames.AssinaturasEmpresa);
    private IMongoCollection<Plano> Planos => Context.GetCollection<Plano>(MongoCollectionNames.Planos);

    public async Task<IEnumerable<AssinaturaEmpresa>> GetByEmpresaAsync(Guid empresaId)
    {
        var assinaturas = await Collection.Find(x => x.EmpresaId == empresaId).ToListAsync();
        await HydratePlanosAsync(assinaturas);
        return assinaturas;
    }

    public async Task<AssinaturaEmpresa?> GetAtivaAsync(Guid empresaId)
    {
        var assinatura = await Collection.Find(x => x.EmpresaId == empresaId && x.Status == StatusAssinatura.Ativa).FirstOrDefaultAsync();
        if (assinatura is null) return null;

        assinatura.Plano = await Planos.Find(x => x.Id == assinatura.PlanoId).FirstOrDefaultAsync();
        return assinatura;
    }

    public async Task<AssinaturaEmpresa?> GetMaisRecenteAsync(Guid empresaId)
    {
        return await Collection.Find(x => x.EmpresaId == empresaId)
            .Sort(Builders<AssinaturaEmpresa>.Sort.Descending(a => a.DataInicio))
            .FirstOrDefaultAsync();
    }

    public async Task<AssinaturaEmpresa?> GetAtivaMaisRecenteAsync(Guid empresaId)
    {
        var assinatura = await Collection.Find(x => x.EmpresaId == empresaId && x.Status == StatusAssinatura.Ativa)
            .Sort(Builders<AssinaturaEmpresa>.Sort.Descending(a => a.DataInicio))
            .FirstOrDefaultAsync();
        if (assinatura is null) return null;

        assinatura.Plano = await Planos.Find(x => x.Id == assinatura.PlanoId).FirstOrDefaultAsync();
        return assinatura;
    }

    public Task AddAsync(AssinaturaEmpresa assinatura)
    {
        EnqueueInsert(Collection, assinatura);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AssinaturaEmpresa assinatura)
    {
        assinatura.Plano = null;
        EnqueueReplaceScoped(Collection, assinatura.Id, assinatura.EmpresaId, assinatura);
        return Task.CompletedTask;
    }

    private async Task HydratePlanosAsync(List<AssinaturaEmpresa> assinaturas)
    {
        var ids = assinaturas.Select(x => x.PlanoId).Distinct().ToList();
        if (ids.Count == 0) return;

        var planos = await Planos.Find(x => ids.Contains(x.Id)).ToListAsync();
        foreach (var assinatura in assinaturas)
            assinatura.Plano = planos.FirstOrDefault(x => x.Id == assinatura.PlanoId);
    }

    public async Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencendoEmAsync(int diasAte, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var limite = now.AddDays(diasAte);
        var assinaturas = await Collection.Find(a =>
            a.Status == StatusAssinatura.Ativa &&
            ((a.TrialFim != null && a.TrialFim >= now && a.TrialFim <= limite) ||
             (a.DataFim != null && a.DataFim >= now && a.DataFim <= limite)))
            .ToListAsync(ct);
        await HydratePlanosAsync(assinaturas);
        return assinaturas;
    }

    public async Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencidasAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var assinaturas = await Collection.Find(a =>
            a.Status == StatusAssinatura.Ativa &&
            ((a.TrialFim != null && a.TrialFim < now) ||
             (a.DataFim != null && a.DataFim < now)))
            .ToListAsync(ct);
        return assinaturas;
    }

    public async Task<IEnumerable<AssinaturaEmpresa>> GetSuspensasAntigasAsync(int diasMinimos, CancellationToken ct = default)
    {
        var limite = DateTime.UtcNow.AddDays(-diasMinimos);
        var assinaturas = await Collection.Find(a =>
            a.Status == StatusAssinatura.Suspensa &&
            ((a.SuspensaEm != null && a.SuspensaEm < limite) ||
             (a.SuspensaEm == null && a.AlteradoEm < limite)))
            .ToListAsync(ct);
        return assinaturas;
    }
}

public sealed class UsuarioEmpresaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IUsuarioEmpresaRepository
{
    private IMongoCollection<UsuarioEmpresa> UsuariosEmpresas => Context.GetCollection<UsuarioEmpresa>(MongoCollectionNames.UsuariosEmpresas);

    public Task AddAsync(UsuarioEmpresa usuarioEmpresa)
    {
        EnqueueInsert(UsuariosEmpresas, usuarioEmpresa);
        return Task.CompletedTask;
    }

    public async Task<UsuarioEmpresa?> GetByUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId) =>
        await UsuariosEmpresas.Find(x => x.UsuarioId == usuarioId && x.EmpresaId == empresaId).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<UsuarioEmpresa>> GetByUsuarioIdAsync(Guid usuarioId) =>
        await UsuariosEmpresas.Find(x => x.UsuarioId == usuarioId).ToListAsync();

    public Task UpdateAsync(UsuarioEmpresa usuarioEmpresa)
    {
        EnqueueReplaceScoped(UsuariosEmpresas, usuarioEmpresa.Id, usuarioEmpresa.EmpresaId, usuarioEmpresa);
        return Task.CompletedTask;
    }
}

public sealed class UsuarioPerfilRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IUsuarioPerfilRepository
{
    private IMongoCollection<UsuarioPerfil> UsuariosPerfis => Context.GetCollection<UsuarioPerfil>(MongoCollectionNames.UsuariosPerfis);

    public Task AddAsync(UsuarioPerfil usuarioPerfil)
    {
        EnqueueInsert(UsuariosPerfis, usuarioPerfil);
        return Task.CompletedTask;
    }

    public async Task<UsuarioPerfil?> GetByUsuarioEmpresaEPerfilAsync(Guid usuarioId, Guid empresaId, Guid perfilId) =>
        await UsuariosPerfis.Find(x =>
            x.UsuarioId == usuarioId &&
            x.EmpresaId == empresaId &&
            x.PerfilId == perfilId).FirstOrDefaultAsync();

    public Task UpdateAsync(UsuarioPerfil usuarioPerfil)
    {
        EnqueueReplaceScoped(UsuariosPerfis, usuarioPerfil.Id, usuarioPerfil.EmpresaId, usuarioPerfil);
        return Task.CompletedTask;
    }
}

public sealed class RefreshTokenRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IRefreshTokenRepository
{
    private IMongoCollection<RefreshToken> Collection => Context.GetCollection<RefreshToken>(MongoCollectionNames.RefreshTokens);

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash) =>
        await Collection.Find(x => x.TokenHash == tokenHash).FirstOrDefaultAsync();

    public async Task<IEnumerable<RefreshToken>> GetByUsuarioIdAsync(Guid usuarioId) =>
        await Collection.Find(x => x.UsuarioId == usuarioId).ToListAsync();

    public Task AddAsync(RefreshToken refreshToken)
    {
        EnqueueInsert(Collection, refreshToken);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RefreshToken refreshToken)
    {
        EnqueueReplace(Collection, refreshToken.Id, refreshToken);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        EnqueueDelete(Collection, id);
        return Task.CompletedTask;
    }

    public async Task DeleteExpiredAsync()
    {
        var filter = Builders<RefreshToken>.Filter.Lt(x => x.ExpiraEm, DateTime.UtcNow);
        await Collection.DeleteManyAsync(filter);
    }

    public async Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId)
    {
        var result = await Collection.DeleteManyAsync(x => x.UsuarioId == usuarioId);
        return (int)result.DeletedCount;
    }

    public async Task<int> RevogarSessoesAtivasAsync(Guid usuarioId, DateTime agora)
    {
        var filter = Builders<RefreshToken>.Filter.And(
            Builders<RefreshToken>.Filter.Eq(x => x.UsuarioId, usuarioId),
            Builders<RefreshToken>.Filter.Eq(x => x.Revogado, false),
            Builders<RefreshToken>.Filter.Gt(x => x.ExpiraEm, agora));
        var update = Builders<RefreshToken>.Update
            .Set(x => x.Revogado, true)
            .Set(x => x.RevogadoEm, agora);
        var result = await Collection.UpdateManyAsync(filter, update);
        return (int)result.ModifiedCount;
    }
}

public sealed class ResetTokenRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IResetTokenRepository
{
    private IMongoCollection<ResetToken> Collection => Context.GetCollection<ResetToken>(MongoCollectionNames.ResetTokens);

    public async Task<ResetToken?> GetByTokenAsync(string token)
    {
        var hash = EasyStock.Application.UseCases.Common.TokenHashHelper.ComputeSha256Hash(token);
        return await Collection.Find(x => x.TokenHash == hash).FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<ResetToken>> GetByUsuarioIdAsync(Guid usuarioId) =>
        await Collection.Find(x => x.UsuarioId == usuarioId).ToListAsync();

    public Task AddAsync(ResetToken resetToken)
    {
        EnqueueInsert(Collection, resetToken);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ResetToken resetToken)
    {
        EnqueueReplace(Collection, resetToken.Id, resetToken);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        EnqueueDelete(Collection, id);
        return Task.CompletedTask;
    }

    public async Task DeleteExpiredAsync()
    {
        var filter = Builders<ResetToken>.Filter.Lt(x => x.ExpiraEm, DateTime.UtcNow);
        await Collection.DeleteManyAsync(filter);
    }

    public async Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId)
    {
        var result = await Collection.DeleteManyAsync(x => x.UsuarioId == usuarioId);
        return (int)result.DeletedCount;
    }
}

public sealed class AuditLogRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IAuditLogRepository
{
    private IMongoCollection<AuditLog> Collection => Context.GetCollection<AuditLog>(MongoCollectionNames.AuditLogs);

    public Task AddAsync(AuditLog auditLog)
    {
        EnqueueInsert(Collection, auditLog);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<AuditLog>> GetByUsuarioIdAsync(Guid usuarioId, int page, int pageSize) =>
        await Collection.Find(x => x.UsuarioId == usuarioId)
            .SortByDescending(x => x.DataHora)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
}
