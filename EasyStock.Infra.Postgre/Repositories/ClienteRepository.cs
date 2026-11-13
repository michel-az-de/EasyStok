using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ClienteRepository(EasyStockDbContext db) : IClienteRepository
    {
        public Task<Cliente?> GetByIdAsync(Guid id) =>
            db.Clientes.FirstOrDefaultAsync(c => c.Id == id);

        public Task<Cliente?> GetByIdAsync(Guid empresaId, Guid id) =>
            db.Clientes.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id);

        public async Task<IReadOnlyList<Cliente>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            await db.Clientes.AsNoTracking().Where(c => c.EmpresaId == empresaId && ids.Contains(c.Id)).ToListAsync(ct);

        public Task<Cliente?> GetByIdWithDetailsAsync(Guid empresaId, Guid id) =>
            db.Clientes
                .Include(c => c.Enderecos)
                .Include(c => c.Telefones)
                .Include(c => c.Documentos)
                .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id);

        public async Task<(int OrderCount, DateTime? LastOrderAt)> ObterMetricasPedidosAsync(Guid empresaId, Guid clienteId)
        {
            var metricas = await db.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.ClienteId == clienteId)
                .GroupBy(p => p.ClienteId)
                .Select(g => new { Quantidade = g.Count(), UltimoEm = (DateTime?)g.Max(p => p.CriadoEm) })
                .SingleOrDefaultAsync();
            return (metricas?.Quantidade ?? 0, metricas?.UltimoEm);
        }

        public async Task<(IEnumerable<Cliente> items, int total)> GetByEmpresaAsync(
            Guid empresaId, int page, int pageSize,
            bool? ativo = null, string? search = null,
            string? sort = "nome", string? order = "asc", string? relacionamento = null)
        {
            var query = db.Clientes.AsNoTracking()
                .Where(c => c.EmpresaId == empresaId);

            if (ativo.HasValue) query = query.Where(c => c.Ativo == ativo.Value);
            if (relacionamento == "lead") query = query.Where(c => !db.Pedidos.Any(p => p.EmpresaId == empresaId && p.ClienteId == c.Id));
            if (relacionamento == "cliente") query = query.Where(c => db.Pedidos.Any(p => p.EmpresaId == empresaId && p.ClienteId == c.Id));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var termo = search.Trim();
                var padrao = PadraoILike.Contendo(BuscaTextual.Normalizar(termo));
                var digitos = BuscaTextual.DigitosTelefone(termo);
                var buscaId = Guid.TryParse(termo, out var id);
                // NomeFantasia entra na busca porque, para PJ, e por ele que o operador
                // conhece o cliente — o Nome guarda a razao social, que raramente e o que
                // se digita.
                query = query.Where(c =>
                    (buscaId && c.Id == id) ||
                    EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.Nome.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), padrao, PadraoILike.Escape) ||
                    (c.NomeFantasia != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.NomeFantasia.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), padrao, PadraoILike.Escape)) ||
                    (c.Documento != null && EF.Functions.ILike(c.Documento, padrao, PadraoILike.Escape)) ||
                    (c.Telefone != null && EF.Functions.ILike(c.Telefone, padrao, PadraoILike.Escape)) ||
                    (digitos.Length >= 3 && c.Telefone != null && EasyStockDbContext.TraduzirTexto(c.Telefone, BuscaTextual.MascaraTelefone, "").Contains(digitos)) ||
                    (digitos.Length >= 3 && c.Telefones.Any(t => EasyStockDbContext.TraduzirTexto(t.Numero, BuscaTextual.MascaraTelefone, "").Contains(digitos))) ||
                    (c.Email != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.Email.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), padrao, PadraoILike.Escape)) ||
                    (c.Apt != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.Apt.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), padrao, PadraoILike.Escape)));
            }

            var total = await query.CountAsync();
            var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

            var ordenada = sort?.ToLowerInvariant() switch
            {
                "criadoem"    => desc ? query.OrderByDescending(c => c.CriadoEm)    : query.OrderBy(c => c.CriadoEm),
                "lastorderat" => desc
                    ? query.OrderByDescending(c => db.Pedidos.Where(p => p.EmpresaId == empresaId && p.ClienteId == c.Id).Max(p => (DateTime?)p.CriadoEm))
                    : query.OrderBy(c => db.Pedidos.Where(p => p.EmpresaId == empresaId && p.ClienteId == c.Id).Max(p => (DateTime?)p.CriadoEm)),
                "ordercount" => desc
                    ? query.OrderByDescending(c => db.Pedidos.Count(p => p.EmpresaId == empresaId && p.ClienteId == c.Id))
                    : query.OrderBy(c => db.Pedidos.Count(p => p.EmpresaId == empresaId && p.ClienteId == c.Id)),
                _             => desc ? query.OrderByDescending(c => c.Nome)        : query.OrderBy(c => c.Nome),
            };

            var clientes = await ordenada.ThenByDescending(c => c.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new
                {
                    Cliente = c,
                    Quantidade = db.Pedidos.Count(p => p.EmpresaId == empresaId && p.ClienteId == c.Id),
                    UltimoEm = db.Pedidos.Where(p => p.EmpresaId == empresaId && p.ClienteId == c.Id).Max(p => (DateTime?)p.CriadoEm)
                }).ToListAsync();
            // Apenas o retrato sem tracking recebe as métricas; o contador legado não inclui todos os canais.
            foreach (var item in clientes)
                (item.Cliente.OrderCount, item.Cliente.LastOrderAt) = (item.Quantidade, item.UltimoEm);
            return (clientes.Select(item => item.Cliente), total);
        }

        public async Task<IEnumerable<Cliente>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = PadraoILike.Contendo(BuscaTextual.Normalizar(termo));
            var digitos = BuscaTextual.DigitosTelefone(termo);
            var buscaId = Guid.TryParse(termo, out var id);
            return await db.Clientes.AsNoTracking()
                .Where(c => c.EmpresaId == empresaId && c.Ativo &&
                    ((buscaId && c.Id == id) || EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.Nome.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), pattern, PadraoILike.Escape) ||
                     (c.NomeFantasia != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.NomeFantasia.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), pattern, PadraoILike.Escape)) ||
                     (c.Telefone != null && EF.Functions.ILike(c.Telefone, pattern, PadraoILike.Escape)) ||
                     (digitos.Length >= 3 && c.Telefone != null && EasyStockDbContext.TraduzirTexto(c.Telefone, BuscaTextual.MascaraTelefone, "").Contains(digitos)) ||
                     (digitos.Length >= 3 && c.Telefones.Any(t => EasyStockDbContext.TraduzirTexto(t.Numero, BuscaTextual.MascaraTelefone, "").Contains(digitos))) ||
                     (c.Documento != null && EF.Functions.ILike(c.Documento, pattern, PadraoILike.Escape))))
                .OrderBy(c => c.Nome)
                .Take(maxResults)
                .ToListAsync();
        }

        // #1290: o número pode estar só na lista de telefones do cadastro (ex.: WhatsApp como segundo número).
        public Task<Cliente?> FindByTelefoneAsync(Guid empresaId, string telefone) =>
            db.Clientes
                .Where(c => c.EmpresaId == empresaId
                            && (c.Telefone == telefone || c.Telefones.Any(t => t.Numero == telefone)))
                .OrderBy(c => c.Telefone == telefone ? 0 : 1) // o telefone principal vence a lista
                .FirstOrDefaultAsync();

        public async Task<bool> TemTelefoneCompartilhadoAsync(Guid empresaId, IReadOnlyList<string> telefones,
            IReadOnlyList<string> hashes, CancellationToken ct = default) =>
            await db.Clientes.AsNoTracking().Where(c => c.EmpresaId == empresaId
                && ((c.TelefoneHash != null && hashes.Contains(c.TelefoneHash))
                    || (c.Telefone != null && telefones.Contains(c.Telefone))
                    || c.Telefones.Any(t => telefones.Contains(t.Numero))))
                .Select(c => c.Id).Take(2).CountAsync(ct) > 1;

        public Task AddAsync(Cliente cliente) { db.Clientes.Add(cliente); return Task.CompletedTask; }
        public Task UpdateAsync(Cliente cliente) { db.Clientes.Update(cliente); return Task.CompletedTask; }

        // ── Sub-recursos ──────────────────────────────────────────
        public Task AddEnderecoAsync(ClienteEndereco e) { db.Set<ClienteEndereco>().Add(e); return Task.CompletedTask; }
        public async Task<bool> RemoveEnderecoAsync(Guid empresaId, Guid clienteId, Guid enderecoId)
        {
            // A revisão da raiz e a exclusão participam do mesmo SaveChanges.
            var endereco = await db.Set<ClienteEndereco>().Include(e => e.Cliente)
                .Where(e => e.Id == enderecoId
                            && e.ClienteId == clienteId
                            && db.Clientes.Any(c => c.Id == clienteId && c.EmpresaId == empresaId))
                .SingleOrDefaultAsync();
            if (endereco is null) return false;
            endereco.Cliente!.AlteradoEm = DateTime.UtcNow;
            db.Entry(endereco.Cliente!).Property(c => c.AlteradoEm).IsModified = true;
            db.Remove(endereco);
            return true;
        }

        public Task AddTelefoneAsync(ClienteTelefone t) { db.Set<ClienteTelefone>().Add(t); return Task.CompletedTask; }
        public async Task<bool> RemoveTelefoneAsync(Guid empresaId, Guid clienteId, Guid telefoneId)
        {
            var telefone = await db.Set<ClienteTelefone>().Include(t => t.Cliente)
                .Where(t => t.Id == telefoneId
                            && t.ClienteId == clienteId
                            && db.Clientes.Any(c => c.Id == clienteId && c.EmpresaId == empresaId))
                .SingleOrDefaultAsync();
            if (telefone is null) return false;
            telefone.Cliente!.AlteradoEm = DateTime.UtcNow;
            db.Entry(telefone.Cliente!).Property(c => c.AlteradoEm).IsModified = true;
            db.Remove(telefone);
            return true;
        }

        public Task AddDocumentoAsync(ClienteDocumento d) { db.Set<ClienteDocumento>().Add(d); return Task.CompletedTask; }
        public async Task<bool> RemoveDocumentoAsync(Guid empresaId, Guid clienteId, Guid documentoId)
        {
            var rows = await db.Set<ClienteDocumento>()
                .Where(d => d.Id == documentoId
                            && d.ClienteId == clienteId
                            && db.Clientes.Any(c => c.Id == clienteId && c.EmpresaId == empresaId))
                .ExecuteDeleteAsync();
            return rows > 0;
        }

        public Task AddAlteracaoAsync(ClienteAlteracao a) { db.Set<ClienteAlteracao>().Add(a); return Task.CompletedTask; }

        public async Task<IEnumerable<ClienteAlteracao>> GetAlteracoesAsync(Guid clienteId, int max = 100) =>
            await db.Set<ClienteAlteracao>().AsNoTracking()
                .Where(a => a.ClienteId == clienteId)
                .OrderByDescending(a => a.AlteradoEm)
                .Take(max).ToListAsync();
    }
}
