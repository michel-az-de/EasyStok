using System.Linq.Expressions;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
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
                .Where(RegraDeCompra.Pedido)
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
            var (query, compras) = Filtrar(empresaId, ativo, search, relacionamento);

            var total = await query.CountAsync();
            var ordenada = Ordenar(query, compras, sort, order);

            var clientes = await ordenada.ThenByDescending(c => c.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new
                {
                    Cliente = c,
                    Quantidade = compras.Count(p => p.ClienteId == c.Id),
                    UltimoEm = compras.Where(p => p.ClienteId == c.Id).Max(p => (DateTime?)p.CriadoEm)
                }).ToListAsync();
            // Apenas o retrato sem tracking recebe as métricas; o contador legado não inclui todos os canais.
            foreach (var item in clientes)
                (item.Cliente.OrderCount, item.Cliente.LastOrderAt) = (item.Quantidade, item.UltimoEm);
            return (clientes.Select(item => item.Cliente), total);
        }

        // #3244: filtro e ordenação da lista, comuns à lista simples e à lista com relação.
        private (IQueryable<Cliente> Query, IQueryable<Pedido> Compras) Filtrar(
            Guid empresaId, bool? ativo, string? search, string? relacionamento)
        {
            var query = db.Clientes.AsNoTracking()
                .Where(c => c.EmpresaId == empresaId);
            // #2777: a mesma regra de compra do dossiê na contagem, no filtro e na ordenação.
            var compras = db.Pedidos.Where(p => p.EmpresaId == empresaId).Where(RegraDeCompra.Pedido);

            if (ativo.HasValue) query = query.Where(c => c.Ativo == ativo.Value);
            if (relacionamento == "lead") query = query.Where(c => !compras.Any(p => p.ClienteId == c.Id));
            if (relacionamento == "cliente") query = query.Where(c => compras.Any(p => p.ClienteId == c.Id));
            if (relacionamento == RegraDeRecompra.Relacionamento)
                // #2946: bloqueado e inativo ficam fora mesmo sem o filtro de situação, porque a lista é de quem chamar.
                query = query.Where(PredicadoDeRecompra(compras));

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
            return (query, compras);
        }

        private static IOrderedQueryable<Cliente> Ordenar(
            IQueryable<Cliente> query, IQueryable<Pedido> compras, string? sort, string? order)
        {
            var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

            return sort?.ToLowerInvariant() switch
            {
                "criadoem"    => desc ? query.OrderByDescending(c => c.CriadoEm)    : query.OrderBy(c => c.CriadoEm),
                "lastorderat" => desc
                    ? query.OrderByDescending(c => compras.Where(p => p.ClienteId == c.Id).Max(p => (DateTime?)p.CriadoEm))
                    : query.OrderBy(c => compras.Where(p => p.ClienteId == c.Id).Max(p => (DateTime?)p.CriadoEm)),
                "ordercount" => desc
                    ? query.OrderByDescending(c => compras.Count(p => p.ClienteId == c.Id))
                    : query.OrderBy(c => compras.Count(p => p.ClienteId == c.Id)),
                "gasto" => desc
                    ? query.OrderByDescending(c => compras.Where(p => p.ClienteId == c.Id).Sum(p => (decimal?)p.Total) ?? 0m)
                    : query.OrderBy(c => compras.Where(p => p.ClienteId == c.Id).Sum(p => (decimal?)p.Total) ?? 0m),
                "ticketmedio" => desc
                    ? query.OrderByDescending(c => compras.Where(p => p.ClienteId == c.Id).Average(p => (decimal?)p.Total) ?? 0m)
                    : query.OrderBy(c => compras.Where(p => p.ClienteId == c.Id).Average(p => (decimal?)p.Total) ?? 0m),
                _             => desc ? query.OrderByDescending(c => c.Nome)        : query.OrderBy(c => c.Nome),
            };
        }

        /// <summary>
        /// #3244: o predicado de <c>relacionamento=recompra</c> (<see cref="RegraDeRecompra"/>), uma expressão só para o filtro
        /// e para o campo <c>EmRisco</c>: cadastro ativo e não bloqueado, 2 compras ou mais, nenhuma nos últimos 30 dias e
        /// sem a tag de convite nesse prazo.
        /// </summary>
        private Expression<Func<Cliente, bool>> PredicadoDeRecompra(IQueryable<Pedido> compras)
        {
            var corte = DateTime.UtcNow.AddDays(-RegraDeRecompra.DiasSemCompra);
            return c => c.Ativo && !c.Bloqueado
                && compras.Count(p => p.ClienteId == c.Id) >= RegraDeRecompra.MinimoDeCompras
                && !compras.Any(p => p.ClienteId == c.Id && p.CriadoEm > corte)
                && !db.ClienteTags.Any(t => t.EmpresaId == c.EmpresaId && t.ClienteId == c.Id
                    && t.Tag == RegraDeRecompra.TagConvidada && t.CriadoEm > corte);
        }

        public async Task<ListaDeClientes> ListarComRelacaoAsync(
            Guid empresaId, int page, int pageSize, bool? ativo = null, string? search = null,
            string? sort = "nome", string? order = "asc", string? relacionamento = null, CancellationToken ct = default)
        {
            var (query, compras) = Filtrar(empresaId, ativo, search, relacionamento);
            var total = await query.CountAsync(ct);
            var clientes = await Ordenar(query, compras, sort, order).ThenByDescending(c => c.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            var ids = clientes.Select(c => c.Id).ToList();

            // Cada bloco abaixo é UMA consulta para a página inteira (sem consulta por linha) e filtra EmpresaId.
            var carteira = await compras.Where(p => p.ClienteId != null).SumAsync(p => (decimal?)p.Total, ct) ?? 0m;
            var metricas = (await compras.Where(p => ids.Contains(p.ClienteId!.Value))
                .GroupBy(p => p.ClienteId!.Value)
                .Select(g => new
                {
                    Id = g.Key, Compras = g.Count(), Gasto = g.Sum(p => (decimal)p.Total),
                    Primeira = (DateTime?)g.Min(p => p.CriadoEm), Ultima = (DateTime?)g.Max(p => p.CriadoEm)
                }).ToListAsync(ct)).ToDictionary(m => m.Id);
            var ocorrencias = (await db.Ocorrencias.Where(o => o.EmpresaId == empresaId && ids.Contains(o.ClienteId))
                .GroupBy(o => o.ClienteId)
                .Select(g => new { Id = g.Key, Total = g.Count(), Abertas = g.Count(o => o.Status == StatusOcorrencia.Aberta) })
                .ToListAsync(ct)).ToDictionary(o => o.Id);
            var notas = (await db.ClienteNotas.Where(n => n.EmpresaId == empresaId && ids.Contains(n.ClienteId))
                .GroupBy(n => n.ClienteId)
                .Select(g => new
                {
                    Id = g.Key, Total = g.Count(),
                    Ultima = g.OrderByDescending(n => n.CriadoEm).Select(n => n.Texto).FirstOrDefault()
                }).ToListAsync(ct)).ToDictionary(n => n.Id);
            var tags = (await db.ClienteTags.Where(t => t.EmpresaId == empresaId && ids.Contains(t.ClienteId))
                .OrderByDescending(t => t.CriadoEm).ThenBy(t => t.Tag)
                .Select(t => new { t.ClienteId, t.Tag }).ToListAsync(ct))
                .GroupBy(t => t.ClienteId).ToDictionary(g => g.Key, g => g.Take(ClienteRelacao.MaximoDeTags).Select(t => t.Tag).ToList());
            var emRisco = (await db.Clientes.Where(c => c.EmpresaId == empresaId && ids.Contains(c.Id))
                .Where(PredicadoDeRecompra(compras)).Select(c => c.Id).ToListAsync(ct)).ToHashSet();

            var itens = clientes.Select(c =>
            {
                metricas.TryGetValue(c.Id, out var m);
                ocorrencias.TryGetValue(c.Id, out var o);
                notas.TryGetValue(c.Id, out var n);
                // O retrato sem tracking leva as métricas, como em GetByEmpresaAsync (o contador legado não inclui todos os canais).
                (c.OrderCount, c.LastOrderAt) = (m?.Compras ?? 0, m?.Ultima);
                var ultima = n?.Ultima;
                return new ClienteRelacao(
                    c, m?.Compras ?? 0, m?.Gasto ?? 0m, m?.Primeira, m?.Ultima,
                    o?.Abertas ?? 0, o?.Total ?? 0, n?.Total ?? 0,
                    ultima is { Length: > ClienteRelacao.TamanhoDaNotaCurta } ? ultima[..ClienteRelacao.TamanhoDaNotaCurta] : ultima,
                    tags.TryGetValue(c.Id, out var t) ? t : [], emRisco.Contains(c.Id));
            }).ToList();
            return new ListaDeClientes(itens, total, carteira);
        }

        public async Task<IEnumerable<Cliente>> SearchAsync(Guid empresaId, string termo, int maxResults = 20, bool incluirInativos = false)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = PadraoILike.Contendo(BuscaTextual.Normalizar(termo));
            var digitos = BuscaTextual.DigitosTelefone(termo);
            var buscaId = Guid.TryParse(termo, out var id);
            return await db.Clientes.AsNoTracking()
                .Where(c => c.EmpresaId == empresaId && (incluirInativos || c.Ativo) &&
                    ((buscaId && c.Id == id) || EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.Nome.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), pattern, PadraoILike.Escape) ||
                     (c.NomeFantasia != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.NomeFantasia.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), pattern, PadraoILike.Escape)) ||
                     (c.Telefone != null && EF.Functions.ILike(c.Telefone, pattern, PadraoILike.Escape)) ||
                     (digitos.Length >= 3 && c.Telefone != null && EasyStockDbContext.TraduzirTexto(c.Telefone, BuscaTextual.MascaraTelefone, "").Contains(digitos)) ||
                     (digitos.Length >= 3 && c.Telefones.Any(t => EasyStockDbContext.TraduzirTexto(t.Numero, BuscaTextual.MascaraTelefone, "").Contains(digitos))) ||
                     (c.Documento != null && EF.Functions.ILike(c.Documento, pattern, PadraoILike.Escape)) ||
                     (c.Email != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(c.Email.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), pattern, PadraoILike.Escape))))
                .OrderByDescending(c => c.Ativo)
                .ThenBy(c => c.Nome)
                .Take(maxResults)
                .ToListAsync();
        }

        // #1290: o número pode estar só na lista de telefones do cadastro (ex.: WhatsApp como segundo número).
        public Task<Cliente?> FindByTelefoneAsync(Guid empresaId, string telefone)
        {
            // #3341: o cadastro pode ter o número em outra grafia; casa pelo DDD e os 8 últimos dígitos.
            // Replace encadeado (e não TraduzirTexto) para o predicado também rodar no provedor em memória.
            var formas = TelefoneE164.FormasDeBusca(telefone);
            return db.Clientes
                .Where(c => c.EmpresaId == empresaId
                            && (c.Telefone == telefone || c.Telefones.Any(t => t.Numero == telefone)
                                || (formas.Count > 0
                                    && ((c.Telefone != null && formas.Contains(c.Telefone.Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Replace(" ", "")))
                                        || c.Telefones.Any(t => formas.Contains(t.Numero.Replace("(", "").Replace(")", "").Replace("+", "").Replace("-", "").Replace(".", "").Replace(" ", "")))))))
                // Número idêntico vence a grafia só de dígitos, que vence a mascarada; entre iguais, o cadastro mais
                // antigo (sem depender da ordem do banco).
                .OrderBy(c => c.Telefone == telefone ? 0 : c.Telefones.Any(t => t.Numero == telefone) ? 1
                    : (c.Telefone != null && formas.Contains(c.Telefone)) || c.Telefones.Any(t => formas.Contains(t.Numero)) ? 2 : 3)
                .ThenBy(c => c.CriadoEm).ThenBy(c => c.Id)
                .FirstOrDefaultAsync();
        }

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
            var telefone = await db.Set<ClienteTelefone>().Include(t => t.Cliente).ThenInclude(c => c!.Telefones)
                .Where(t => t.Id == telefoneId
                            && t.ClienteId == clienteId
                            && db.Clientes.Any(c => c.Id == clienteId && c.EmpresaId == empresaId))
                .SingleOrDefaultAsync();
            if (telefone is null) return false;
            telefone.Cliente!.AlteradoEm = DateTime.UtcNow;
            db.Entry(telefone.Cliente!).Property(c => c.AlteradoEm).IsModified = true;
            telefone.Cliente!.PromoverPrincipalSemOTelefone(telefone.Id);
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
