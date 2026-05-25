using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Data;

public static partial class SeedData
{
    // ──────────────────────────────────────────────────────────────────────────
    // Clientes
    // ──────────────────────────────────────────────────────────────────────────

    internal static async Task<Cliente> EnsureClienteAsync(
        EasyStockDbContext context,
        Guid empresaId,
        string nome,
        string? documento = null,
        string? telefone = null,
        string? email = null,
        string? endereco = null,
        string? observacoes = null,
        DateTime? criadoEm = null,
        int orderCount = 0,
        DateTime? lastOrderAt = null)
    {
        var cliente = await context.Clientes.FirstOrDefaultAsync(c =>
            c.EmpresaId == empresaId &&
            c.Nome == nome &&
            (c.Documento ?? string.Empty) == (documento ?? string.Empty));

        if (cliente is null)
        {
            cliente = Cliente.Criar(empresaId, nome);
            if (criadoEm.HasValue)
            {
                cliente.CriadoEm = criadoEm.Value;
                cliente.AlteradoEm = criadoEm.Value;
            }
            context.Clientes.Add(cliente);
        }

        cliente.AtualizarCadastro(nome, apt: null, endereco, telefone, email, documento, observacoes);
        cliente.OrderCount = orderCount;
        cliente.LastOrderAt = lastOrderAt;
        return cliente;
    }

    internal static async Task EnsureClienteEnderecoAsync(
        EasyStockDbContext context,
        Cliente cliente,
        string tipo,
        string logradouro,
        string? numero,
        string? complemento,
        string? bairro,
        string cidade,
        string estado,
        string? cep,
        bool padrao,
        DateTime agora)
    {
        var existente = await context.Set<ClienteEndereco>()
            .FirstOrDefaultAsync(e => e.ClienteId == cliente.Id && e.Logradouro == logradouro && e.Tipo == tipo);
        if (existente is not null) return;

        context.Set<ClienteEndereco>().Add(new ClienteEndereco
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Tipo = tipo,
            Logradouro = logradouro,
            Numero = numero,
            Complemento = complemento,
            Bairro = bairro,
            Cidade = cidade,
            Estado = estado,
            Cep = cep,
            Pais = "Brasil",
            Padrao = padrao,
            CriadoEm = agora,
            AlteradoEm = agora
        });
    }

    internal static async Task EnsureClienteTelefoneAsync(
        EasyStockDbContext context,
        Cliente cliente,
        string tipo,
        string numero,
        bool whatsapp,
        bool principal,
        DateTime agora)
    {
        var existente = await context.Set<ClienteTelefone>()
            .FirstOrDefaultAsync(t => t.ClienteId == cliente.Id && t.Numero == numero);
        if (existente is not null) return;

        context.Set<ClienteTelefone>().Add(new ClienteTelefone
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Tipo = tipo,
            Numero = numero,
            Whatsapp = whatsapp,
            Principal = principal,
            CriadoEm = agora,
            AlteradoEm = agora
        });
    }

    internal static async Task EnsureClienteDocumentoAsync(
        EasyStockDbContext context,
        Cliente cliente,
        string tipo,
        string valor,
        bool principal,
        DateTime agora)
    {
        var existente = await context.Set<ClienteDocumento>()
            .FirstOrDefaultAsync(d => d.ClienteId == cliente.Id && d.Tipo == tipo && d.Valor == valor);
        if (existente is not null) return;

        context.Set<ClienteDocumento>().Add(new ClienteDocumento
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Tipo = tipo,
            Valor = valor,
            Principal = principal,
            CriadoEm = agora,
            AlteradoEm = agora
        });
    }

    internal static async Task EnsureClienteAlteracaoAsync(
        EasyStockDbContext context,
        Cliente cliente,
        Usuario usuario,
        string campo,
        string? valorAntigo,
        string? valorNovo,
        DateTime alteradoEm,
        string? origem = "web")
    {
        var existente = await context.Set<ClienteAlteracao>()
            .FirstOrDefaultAsync(a => a.ClienteId == cliente.Id && a.Campo == campo && a.AlteradoEm == alteradoEm);
        if (existente is not null) return;

        context.Set<ClienteAlteracao>().Add(new ClienteAlteracao
        {
            Id = Guid.NewGuid(),
            EmpresaId = cliente.EmpresaId,
            ClienteId = cliente.Id,
            AlteradoPorUserId = usuario.Id,
            AlteradoPorNome = usuario.Nome,
            Campo = campo,
            ValorAntigo = valorAntigo,
            ValorNovo = valorNovo,
            AlteradoEm = alteradoEm,
            Origem = origem
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Pedido (encomenda) com lifecycle completo
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed record PedidoLinhaSeed(
        Produto Produto,
        decimal Quantidade,
        decimal PrecoUnitario,
        ItemEstoque? ItemEstoqueParaVenda = null,
        string? Observacao = null,
        string? Emoji = null,
        string? Unidade = "un");

    internal static string MarcadorSeedPedido(string seedKey) => $"[seed:PED:{seedKey}]";

    internal static async Task<Pedido?> EnsurePedidoAsync(
        EasyStockDbContext context,
        Guid empresaId,
        string seedKey,
        Cliente? cliente,
        Guid? lojaId,
        DateTime criadoEm,
        string status,
        string observacoes,
        bool gerarVendaSeEntregue,
        DateTime agora,
        params PedidoLinhaSeed[] linhas)
    {
        var marcador = MarcadorSeedPedido(seedKey);
        var pedidoExistente = await context.Pedidos.FirstOrDefaultAsync(p =>
            p.EmpresaId == empresaId &&
            p.Observacoes != null &&
            p.Observacoes.Contains(marcador));

        if (pedidoExistente is not null) return pedidoExistente;

        var pedido = Pedido.Criar(empresaId, cliente, lojaId, "web");
        pedido.CriadoEm = criadoEm;
        pedido.AlteradoEm = criadoEm;
        pedido.Observacoes = string.IsNullOrWhiteSpace(observacoes) ? marcador : $"{observacoes} {marcador}";

        foreach (var linha in linhas)
        {
            var subtotal = linha.Quantidade * linha.PrecoUnitario;
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                ProdutoId = linha.Produto.Id,
                Nome = linha.Produto.Nome,
                Emoji = linha.Emoji,
                Unidade = linha.Unidade,
                Quantidade = linha.Quantidade,
                PrecoUnitario = linha.PrecoUnitario,
                Subtotal = subtotal,
                Observacao = linha.Observacao,
                CriadoEm = criadoEm
            });
        }

        pedido.RecalcularTotal();
        pedido.AlteradoEm = criadoEm;

        // Atribuição manual de status — não usar mutators para preservar timestamps históricos
        pedido.Status = status;
        if (status == "entregue") pedido.EntreguEm = criadoEm.AddMinutes(45);
        else if (status == "cancelado") pedido.CanceladoEm = criadoEm.AddMinutes(20);

        // Evento de criação
        pedido.Eventos.Add(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "criado",
            StatusNovo = "aguardando",
            Detalhes = $"Pedido criado via seed ({seedKey})",
            UsuarioNome = "Sistema (seed)",
            Origem = "web",
            OcorridoEm = criadoEm
        });

        // Evento(s) de status_changed para chegar ao status final
        var sequencia = ProgressaoStatus(status);
        var passoEm = criadoEm;
        var statusAnterior = "aguardando";
        foreach (var statusAtual in sequencia.Skip(1))
        {
            passoEm = passoEm.AddMinutes(15);
            pedido.Eventos.Add(new PedidoEvento
            {
                Id = Guid.NewGuid(),
                PedidoId = pedido.Id,
                Tipo = "status_changed",
                StatusAntigo = statusAnterior,
                StatusNovo = statusAtual,
                Detalhes = $"Status alterado para {statusAtual}",
                UsuarioNome = "Sistema (seed)",
                Origem = "web",
                OcorridoEm = passoEm
            });
            statusAnterior = statusAtual;
        }

        if (cliente is not null)
            cliente.RegistrarPedido(criadoEm);

        context.Pedidos.Add(pedido);

        // Se entregue + gerar venda: cria Venda linkada
        if (status == "entregue" && gerarVendaSeEntregue && lojaId.HasValue)
        {
            var nf = $"NF-{seedKey}";
            var vendaLinhas = new List<VendaLinha>();
            foreach (var linha in linhas)
            {
                var itemEstoque = linha.ItemEstoqueParaVenda
                    ?? await ResolverItemEstoqueAsync(context, empresaId, lojaId.Value, linha.Produto.Id);
                if (itemEstoque is null) continue;
                vendaLinhas.Add(new VendaLinha(itemEstoque, null,
                    Quantidade.From((int)linha.Quantidade),
                    Dinheiro.FromDecimal(linha.PrecoUnitario)));
            }

            if (vendaLinhas.Count > 0)
            {
                var venda = await EnsureVendaAsync(context, empresaId, lojaId.Value, nf, criadoEm.AddMinutes(45),
                    $"Venda gerada do pedido {seedKey}", vendaLinhas.ToArray());
                if (venda is not null)
                    pedido.VendaId = venda.Id;
            }
        }

        return pedido;
    }

    private static IReadOnlyList<string> ProgressaoStatus(string statusFinal) => statusFinal switch
    {
        "aguardando" => new[] { "aguardando" },
        "preparando" => new[] { "aguardando", "preparando" },
        "pronto" => new[] { "aguardando", "preparando", "pronto" },
        "entregue" => new[] { "aguardando", "preparando", "pronto", "entregue" },
        "cancelado" => new[] { "aguardando", "cancelado" },
        _ => new[] { statusFinal }
    };

    internal static Task EnsurePedidoPagamentoAsync(
        Pedido pedido,
        string metodo,
        decimal valor,
        DateTime pagoEm,
        Guid? registradoPorUserId,
        string? registradoPorNome,
        string? referencia = null,
        string? observacao = null)
    {
        var existente = pedido.Pagamentos.FirstOrDefault(p =>
            p.Metodo == metodo &&
            (p.Referencia ?? string.Empty) == (referencia ?? string.Empty) &&
            p.Valor == valor);
        if (existente is not null) return Task.CompletedTask;

        pedido.Pagamentos.Add(new PedidoPagamento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Metodo = metodo,
            Valor = valor,
            Referencia = referencia,
            Observacao = observacao,
            PagoEm = pagoEm,
            RegistradoPorUserId = registradoPorUserId,
            RegistradoPorNome = registradoPorNome
        });

        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PedidoFornecedor (compras)
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed record PedidoFornecedorItemSeed(
        Produto? Produto,
        string Nome,
        string Unidade,
        decimal Quantidade,
        decimal QuantidadeRecebida,
        decimal CustoUnitario,
        string? Observacao = null);

    internal static string MarcadorSeedPedidoFornecedor(string seedKey) => $"[seed:PFOR:{seedKey}]";

    internal static async Task<PedidoFornecedor> EnsurePedidoFornecedorAsync(
        EasyStockDbContext context,
        Guid empresaId,
        string seedKey,
        Fornecedor fornecedor,
        Guid? lojaId,
        DateTime dataPedido,
        DateTime? previsaoEntrega,
        DateTime? dataRecebimento,
        StatusPedidoFornecedor status,
        string canal,
        string? tracking,
        string observacoes,
        DateTime agora,
        params PedidoFornecedorItemSeed[] itens)
    {
        var marcador = MarcadorSeedPedidoFornecedor(seedKey);
        var existente = await context.PedidosFornecedor
            .Include(p => p.Itens)
            .FirstOrDefaultAsync(p =>
                p.EmpresaId == empresaId &&
                p.Observacoes != null &&
                p.Observacoes.Contains(marcador));
        if (existente is not null) return existente;

        var pedido = new PedidoFornecedor
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            FornecedorId = fornecedor.Id,
            LojaId = lojaId,
            DataPedido = dataPedido,
            PrevisaoEntrega = previsaoEntrega,
            DataRecebimento = dataRecebimento,
            Status = status,
            Canal = canal,
            Tracking = tracking,
            Observacoes = string.IsNullOrWhiteSpace(observacoes) ? marcador : $"{observacoes} {marcador}",
            ValorEstimado = itens.Sum(i => i.Quantidade * i.CustoUnitario),
            CriadoEm = dataPedido,
            AlteradoEm = agora
        };

        foreach (var itemSeed in itens)
        {
            pedido.Itens.Add(new PedidoFornecedorItem
            {
                Id = Guid.NewGuid(),
                PedidoFornecedorId = pedido.Id,
                ProdutoId = itemSeed.Produto?.Id,
                Nome = itemSeed.Nome,
                Unidade = itemSeed.Unidade,
                Quantidade = itemSeed.Quantidade,
                QuantidadeRecebida = itemSeed.QuantidadeRecebida,
                CustoUnitario = itemSeed.CustoUnitario,
                Observacao = itemSeed.Observacao,
                CriadoEm = dataPedido
            });
        }

        context.PedidosFornecedor.Add(pedido);
        return pedido;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Caixa
    // ──────────────────────────────────────────────────────────────────────────

    internal static async Task<MovimentoCaixa> EnsureMovimentoCaixaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid lojaId,
        string seedReferencia,
        string tipo,
        decimal valor,
        DateTime dataMovimento,
        string descricao,
        string? metodo = null,
        string? categoria = null,
        Usuario? registradoPor = null)
    {
        var existente = await context.MovimentosCaixa.FirstOrDefaultAsync(m =>
            m.EmpresaId == empresaId &&
            m.Referencia == seedReferencia);
        if (existente is not null) return existente;

        var movimento = MovimentoCaixa.Criar(empresaId, tipo, valor, dataMovimento, lojaId);
        movimento.Descricao = descricao;
        movimento.Metodo = metodo;
        movimento.Categoria = categoria;
        movimento.Referencia = seedReferencia;
        movimento.Origem = "web";
        movimento.RegistradoPorUserId = registradoPor?.Id;
        movimento.RegistradoPorNome = registradoPor?.Nome;

        context.MovimentosCaixa.Add(movimento);
        return movimento;
    }

    internal static async Task EnsureFechamentoCaixaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid lojaId,
        DateOnly data,
        decimal saldoInicial,
        decimal totalVendas,
        decimal totalPagamentosPedidos,
        decimal totalEntradasExtras,
        decimal totalSaidasExtras,
        Usuario? fechadoPor,
        string? observacoes,
        DateTime fechadoEm)
    {
        var existente = await context.FechamentosCaixa.FirstOrDefaultAsync(f =>
            f.EmpresaId == empresaId && f.LojaId == lojaId && f.Data == data);
        if (existente is not null) return;

        var fechamento = FechamentoCaixa.Criar(empresaId, data, saldoInicial, totalVendas,
            totalPagamentosPedidos, totalEntradasExtras, totalSaidasExtras, lojaId);
        fechamento.FechadoPorUserId = fechadoPor?.Id;
        fechamento.FechadoPorNome = fechadoPor?.Nome;
        fechamento.Observacoes = observacoes;
        fechamento.FechadoEm = fechadoEm;

        context.FechamentosCaixa.Add(fechamento);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lote de produção
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed record LoteItemSeed(
        Produto? Produto,
        string Nome,
        string? Emoji,
        string Unidade,
        int Quantidade,
        int? PesoG = null,
        int? ValidadeDias = null);

    internal static async Task<Lote> EnsureLoteAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid? lojaId,
        string codigo,
        DateTime dataProducao,
        string statusFinal,
        Usuario? operador,
        string? observacoes,
        DateTime agora,
        params LoteItemSeed[] itens)
    {
        var existente = await context.Lotes
            .Include(l => l.Itens)
            .Include(l => l.Etiquetas)
            .FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Codigo == codigo);
        if (existente is not null) return existente;

        var lote = Lote.Criar(empresaId, codigo, dataProducao, lojaId);
        lote.OperadorUserId = operador?.Id;
        lote.OperadorNome = operador?.Nome;
        lote.Observacoes = observacoes;
        lote.Origem = "web";
        lote.CriadoEm = dataProducao;
        lote.AlteradoEm = agora;

        var sequencialEtiqueta = 1;
        foreach (var itemSeed in itens)
        {
            var loteItem = new LoteItem
            {
                Id = Guid.NewGuid(),
                LoteId = lote.Id,
                ProdutoId = itemSeed.Produto?.Id,
                Nome = itemSeed.Nome,
                Emoji = itemSeed.Emoji,
                Unidade = itemSeed.Unidade,
                Quantidade = itemSeed.Quantidade,
                PesoG = itemSeed.PesoG,
                ValidadeDias = itemSeed.ValidadeDias,
                ExpiraEm = itemSeed.ValidadeDias.HasValue ? dataProducao.AddDays(itemSeed.ValidadeDias.Value) : null,
                CriadoEm = dataProducao
            };
            lote.Itens.Add(loteItem);

            for (int i = 0; i < itemSeed.Quantidade; i++)
            {
                lote.Etiquetas.Add(new LoteEtiqueta
                {
                    Id = Guid.NewGuid(),
                    LoteId = lote.Id,
                    LoteItemId = loteItem.Id,
                    Sequencial = sequencialEtiqueta++,
                    Codigo = $"{codigo}-{(sequencialEtiqueta - 1):D4}",
                    Status = "pendente",
                    CriadoEm = dataProducao
                });
            }
        }

        if (statusFinal == "finalizado")
        {
            lote.Status = "finalizado";
            lote.FinalizadoEm = dataProducao.AddHours(4);
        }

        context.Lotes.Add(lote);
        return lote;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Audit logs (login/logout/etc)
    // ──────────────────────────────────────────────────────────────────────────

    internal static async Task EnsureAuditLogAsync(
        EasyStockDbContext context,
        Usuario usuario,
        string acao,
        bool sucesso,
        DateTime dataHora,
        string? detalhes = null,
        string? ip = null,
        string? userAgent = null)
    {
        // Arredonda ao segundo para idempotência
        var dataNormalizada = new DateTime(dataHora.Year, dataHora.Month, dataHora.Day,
            dataHora.Hour, dataHora.Minute, dataHora.Second, dataHora.Kind);

        var existente = await context.AuditLogs.FirstOrDefaultAsync(a =>
            a.UsuarioId == usuario.Id && a.Acao == acao && a.DataHora == dataNormalizada);
        if (existente is not null) return;

        context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            Acao = acao,
            DataHora = dataNormalizada,
            Sucesso = sucesso,
            Detalhes = detalhes,
            Ip = ip ?? "127.0.0.1",
            UserAgent = userAgent ?? "Mozilla/5.0 (seed)"
        });
    }

    internal static async Task EnsureAdminAuditLogAsync(
        EasyStockDbContext context,
        string adminEmail,
        string acao,
        DateTime criadoEm,
        string? detalhes = null,
        Guid? tenantId = null,
        string? ip = null)
    {
        var dataNormalizada = new DateTime(criadoEm.Year, criadoEm.Month, criadoEm.Day,
            criadoEm.Hour, criadoEm.Minute, criadoEm.Second, criadoEm.Kind);

        var existente = await context.AdminAuditLogs.FirstOrDefaultAsync(a =>
            a.AdminEmail == adminEmail && a.Acao == acao && a.CriadoEm == dataNormalizada);
        if (existente is not null) return;

        var entry = AdminAuditLog.Criar(adminEmail, acao, detalhes, tenantId, ip);
        // O factory carimba CriadoEm com UtcNow — substituir via reflection-free trick:
        // o property é private set, mas Entity Framework preencherá via constructor.
        // Aqui usamos um proxy: como `Criar` retorna `new AdminAuditLog`, vamos usar EF para
        // anexar e atualizar diretamente no DbContext.
        context.AdminAuditLogs.Add(entry);
        // Atualiza CriadoEm via SetValues após o Add (EF aceita atualização pré-save).
        var entry_entity = context.Entry(entry);
        entry_entity.Property("CriadoEm").CurrentValue = dataNormalizada;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Auditorias finas (Produto/Cliente/MovEstoque alteracoes)
    // ──────────────────────────────────────────────────────────────────────────

    internal static async Task EnsureProdutoAlteracaoAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Produto produto,
        Usuario usuario,
        string acao,
        DateTime alteradoEm,
        string? motivo = null,
        string? alteracoesJson = null,
        string? observacao = null)
    {
        var dataNormalizada = new DateTime(alteradoEm.Year, alteradoEm.Month, alteradoEm.Day,
            alteradoEm.Hour, alteradoEm.Minute, alteradoEm.Second, alteradoEm.Kind);

        var existente = await context.ProdutoAlteracoes.FirstOrDefaultAsync(a =>
            a.ProdutoId == produto.Id && a.AlteradoEm == dataNormalizada && a.Acao == acao);
        if (existente is not null) return;

        context.ProdutoAlteracoes.Add(new ProdutoAlteracao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            UsuarioId = usuario.Id,
            Acao = acao,
            AlteracoesJson = alteracoesJson,
            Motivo = motivo,
            Observacao = observacao,
            AlteradoEm = dataNormalizada
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Auxiliar: resolver ItemEstoque para mapear Pedido → Venda
    // ──────────────────────────────────────────────────────────────────────────

    internal static async Task<ItemEstoque?> ResolverItemEstoqueAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid lojaId,
        Guid produtoId)
    {
        return await context.ItensEstoque
            .Where(i => i.EmpresaId == empresaId && i.LojaId == lojaId && i.ProdutoId == produtoId)
            .OrderByDescending(i => i.EntradaEm)
            .FirstOrDefaultAsync();
    }
}
