using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data;

public static partial class SeedData
{
    internal static async Task<Plano> UpsertPlanoAsync(EasyStockDbContext context, string nome, string descricao, decimal precoMensal, DateTime agora)
    {
        var plano = await context.Planos.FirstOrDefaultAsync(p => p.Nome == nome);
        if (plano is null)
        {
            plano = new Plano
            {
                Id = Guid.NewGuid(),
                Nome = nome,
                CriadoEm = agora
            };
            context.Planos.Add(plano);
        }

        plano.Descricao = descricao;
        plano.LimiteLojas = Plano.SemLimite;
        plano.LimiteUsuarios = Plano.SemLimite;
        plano.LimiteProdutos = Plano.SemLimite;
        plano.LimiteGeracoesIaMensais = Plano.SemLimite;
        plano.PrecoMensal = precoMensal;
        plano.Ativo = true;

        return plano;
    }

    internal static async Task<Empresa> UpsertEmpresaAsync(
        EasyStockDbContext context,
        string nome,
        string documento,
        DateTime agora,
        DateTime? criadoEmOverride = null,
        bool isSeedData = false)
    {
        var empresa = await context.Empresas.FirstOrDefaultAsync(e => e.Documento == documento)
                     ?? await context.Empresas.FirstOrDefaultAsync(e => e.Nome == nome);

        if (empresa is null)
        {
            empresa = Empresa.Criar(nome, documento);
            if (criadoEmOverride.HasValue)
            {
                empresa.CriadoEm = criadoEmOverride.Value;
                empresa.AlteradoEm = criadoEmOverride.Value;
            }
            context.Empresas.Add(empresa);
        }

        empresa.Nome = nome;
        empresa.Documento = documento;
        empresa.AlteradoEm = agora;
        if (isSeedData) empresa.IsSeedData = true;

        return empresa;
    }

    internal static async Task UpsertAssinaturaAsync(EasyStockDbContext context, Guid empresaId, Guid planoId, DateTime agora, int diasDesdeInicio = 90)
    {
        var assinatura = await context.AssinaturasEmpresa
            .OrderByDescending(a => a.CriadoEm)
            .FirstOrDefaultAsync(a => a.EmpresaId == empresaId);

        if (assinatura is null)
        {
            assinatura = new AssinaturaEmpresa
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                CriadoEm = agora
            };
            context.AssinaturasEmpresa.Add(assinatura);
        }

        assinatura.PlanoId = planoId;
        assinatura.DataInicio = agora.Date.AddDays(-diasDesdeInicio);
        assinatura.DataFim = agora.Date.AddYears(1);
        assinatura.Status = StatusAssinatura.Ativa;
        assinatura.AlteradoEm = agora;
    }

    internal static async Task<Loja> UpsertLojaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        string nome,
        string descricao,
        string telefone,
        string endereco,
        DateTime agora)
    {
        var loja = await context.Lojas.FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Nome == nome);
        if (loja is null)
        {
            loja = Loja.Criar(empresaId, nome);
            context.Lojas.Add(loja);
        }

        loja.Descricao = descricao;
        loja.Telefone = telefone;
        loja.Endereco = endereco;
        loja.Ativa = true;
        loja.AlteradoEm = agora;

        return loja;
    }

    internal static async Task EnsureConfiguracaoLojaAsync(EasyStockDbContext context, Guid lojaId)
    {
        var configuracaoExiste = await context.ConfiguracoesLoja.AnyAsync(c => c.LojaId == lojaId);
        if (!configuracaoExiste)
            context.ConfiguracoesLoja.Add(ConfiguracaoLoja.CriarPadrao(lojaId));
    }

    internal static async Task<Perfil> UpsertPerfilAsync(
        EasyStockDbContext context,
        Guid empresaId,
        string nome,
        string descricao,
        NivelAcesso nivel,
        DateTime agora)
    {
        var perfil = await context.Perfis.FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Nome == nome);
        if (perfil is null)
        {
            perfil = new Perfil
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = nome,
                CriadoEm = agora
            };
            context.Perfis.Add(perfil);
        }

        perfil.Descricao = descricao;
        perfil.Nivel = nivel;
        return perfil;
    }

    internal static async Task<Usuario> UpsertUsuarioAsync(EasyStockDbContext context, string nome, string email, string senhaPlana, DateTime agora)
    {
        var usuario = await context.Usuarios.FirstOrDefaultAsync(u => u.Email == email);
        if (usuario is null)
        {
            usuario = Usuario.Criar(nome, email, BCrypt.Net.BCrypt.HashPassword(senhaPlana));
            context.Usuarios.Add(usuario);
        }
        else
        {
            usuario.Nome = nome;
            usuario.Ativo = true;
            if (!BCrypt.Net.BCrypt.Verify(senhaPlana, usuario.SenhaHash))
                usuario.SenhaHash = BCrypt.Net.BCrypt.HashPassword(senhaPlana);
            usuario.AlteradoEm = agora;
        }

        return usuario;
    }

    internal static async Task EnsureUsuarioEmpresaAsync(EasyStockDbContext context, Guid usuarioId, Guid empresaId, DateTime agora)
    {
        var usuarioEmpresa = await context.UsuariosEmpresas.FirstOrDefaultAsync(ue => ue.UsuarioId == usuarioId && ue.EmpresaId == empresaId);
        if (usuarioEmpresa is null)
        {
            context.UsuariosEmpresas.Add(new UsuarioEmpresa
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                EmpresaId = empresaId,
                Ativo = true,
                CriadoEm = agora
            });
            return;
        }

        usuarioEmpresa.Ativo = true;
    }

    internal static async Task EnsureUsuarioPerfilAsync(
        EasyStockDbContext context,
        Guid usuarioId,
        Guid empresaId,
        Guid perfilId,
        Guid? lojaId,
        DateTime agora)
    {
        var usuarioPerfil = await context.UsuariosPerfis.FirstOrDefaultAsync(up =>
            up.UsuarioId == usuarioId &&
            up.EmpresaId == empresaId &&
            up.PerfilId == perfilId &&
            up.LojaId == lojaId);

        if (usuarioPerfil is null)
        {
            context.UsuariosPerfis.Add(new UsuarioPerfil
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                EmpresaId = empresaId,
                PerfilId = perfilId,
                LojaId = lojaId,
                AtribuidoEm = agora
            });
        }
    }

    internal static async Task<Categoria> UpsertCategoriaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        string nome,
        string descricao,
        Guid? categoriaPaiId,
        DateTime agora)
    {
        var categoria = await context.Categorias.FirstOrDefaultAsync(c =>
            c.EmpresaId == empresaId &&
            c.Nome == nome &&
            c.CategoriaPaiId == categoriaPaiId);

        if (categoria is null)
        {
            categoria = new Categoria
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = nome,
                CriadoEm = agora
            };
            context.Categorias.Add(categoria);
        }

        categoria.CategoriaPaiId = categoriaPaiId;
        categoria.Descricao = descricao;
        categoria.AlteradoEm = agora;

        return categoria;
    }

    internal static async Task<Fornecedor> UpsertFornecedorAsync(
        EasyStockDbContext context,
        Guid empresaId,
        string nome,
        string? documento,
        string? email,
        string? telefone,
        string? contato,
        string? categoria,
        string? tipo,
        int? leadTimeEstimadoDias,
        string? siteUrl,
        string? pedidoMinimo,
        string? fretePadrao,
        string? observacoes)
    {
        var fornecedor = await context.Fornecedores.FirstOrDefaultAsync(f => f.EmpresaId == empresaId && f.Nome == nome);
        if (fornecedor is null)
        {
            fornecedor = Fornecedor.Criar(empresaId, nome);
            context.Fornecedores.Add(fornecedor);
        }

        fornecedor.AtualizarCadastro(
            nome,
            documento,
            email,
            telefone,
            contato,
            categoria,
            tipo,
            leadTimeEstimadoDias,
            siteUrl,
            pedidoMinimo,
            fretePadrao,
            observacoes);

        return fornecedor;
    }

    internal static async Task<Produto> UpsertProdutoAsync(EasyStockDbContext context, ProdutoSeed seed)
    {
        var produto = await context.Produtos.FirstOrDefaultAsync(p =>
            p.EmpresaId == seed.EmpresaId &&
            p.Nome == seed.Nome);

        if (produto is null)
        {
            produto = new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = seed.EmpresaId,
                Nome = seed.Nome,
                CriadoEm = seed.Agora
            };
            context.Produtos.Add(produto);
        }

        produto.CategoriaId = seed.CategoriaId;
        produto.SubcategoriaId = seed.SubcategoriaId;
        produto.DescricaoBase = seed.Descricao;
        produto.Marca = seed.Marca;
        produto.Tipo = seed.Tipo;
        produto.ControlaValidade = seed.ControlaValidade;
        produto.CustoReferencia = Dinheiro.FromDecimal(seed.Custo);
        produto.PrecoReferencia = Dinheiro.FromDecimal(seed.Preco);
        produto.MargemEstimada = seed.Margem;
        produto.Status = StatusProduto.Ativo;
        produto.CodigoBarras = seed.CodigoBarras;
        produto.SkuBase = CodigoSku.From(seed.SkuBase);
        produto.AlteradoEm = seed.Agora;

        return produto;
    }

    internal static async Task<ProdutoVariacao> UpsertVariacaoAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid produtoId,
        string nome,
        string? cor,
        string? tamanho,
        string? descricaoComercial,
        string? sku,
        string? codigoBarras,
        DateTime agora)
    {
        var variacao = await context.ProdutosVariacao.FirstOrDefaultAsync(v =>
            v.EmpresaId == empresaId &&
            v.ProdutoId == produtoId &&
            v.Nome == nome);

        if (variacao is null)
        {
            variacao = new ProdutoVariacao
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                Nome = nome,
                CriadoEm = agora
            };
            context.ProdutosVariacao.Add(variacao);
        }

        variacao.Cor = cor;
        variacao.Tamanho = tamanho;
        variacao.DescricaoComercial = descricaoComercial;
        variacao.CodigoBarras = codigoBarras;
        variacao.Sku = string.IsNullOrWhiteSpace(sku) ? null : CodigoSku.From(sku);
        variacao.Ativa = true;
        variacao.AlteradoEm = agora;

        return variacao;
    }

    internal static async Task UpsertCaracteristicaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid produtoId,
        string nome,
        string descricao,
        Guid? variacaoId,
        int ordem,
        DateTime agora)
    {
        var caracteristica = await context.ProdutosCaracteristica.FirstOrDefaultAsync(c =>
            c.EmpresaId == empresaId &&
            c.ProdutoId == produtoId &&
            c.Nome == nome);

        if (caracteristica is null)
        {
            caracteristica = new ProdutoCaracteristica
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                Nome = nome,
                CriadoEm = agora
            };
            context.ProdutosCaracteristica.Add(caracteristica);
        }

        caracteristica.Descricao = descricao;
        caracteristica.VariacaoId = variacaoId;
        caracteristica.OrdemExibicao = ordem;
        caracteristica.AlteradoEm = agora;
    }

    internal static async Task UpsertEmbalagemAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid produtoId,
        string nome,
        string descricao,
        bool padrao,
        DateTime agora)
    {
        var embalagem = await context.ProdutosEmbalagem.FirstOrDefaultAsync(e =>
            e.EmpresaId == empresaId &&
            e.ProdutoId == produtoId &&
            e.Nome == nome);

        if (embalagem is null)
        {
            embalagem = new ProdutoEmbalagem
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                Nome = nome,
                CriadoEm = agora
            };
            context.ProdutosEmbalagem.Add(embalagem);
        }

        embalagem.Descricao = descricao;
        embalagem.Padrao = padrao;
        embalagem.AlteradoEm = agora;
    }

    internal static async Task<ItemSeedContext> EnsureItemEstoqueAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid lojaId,
        Guid? fornecedorId,
        string? fornecedorNome,
        Produto produto,
        ProdutoVariacao? variacao,
        string codigoInterno,
        string codigoLote,
        int quantidadeInicial,
        decimal custoUnitario,
        decimal precoSugerido,
        DateTime entradaEm,
        DateTime? validadeEm,
        string observacoes,
        decimal velocidadeSaidaDiaria,
        DateTime agora)
    {
        var item = await context.ItensEstoque.FirstOrDefaultAsync(i => i.EmpresaId == empresaId && i.CodigoInterno == codigoInterno);
        var criadoAgora = false;
        if (item is null)
        {
            item = ItemEstoque.CriarParaEntrada(
                Guid.NewGuid(),
                empresaId,
                produto,
                variacao,
                Quantidade.From(quantidadeInicial),
                Dinheiro.FromDecimal(custoUnitario),
                Dinheiro.FromDecimal(precoSugerido),
                entradaEm,
                codigoInterno,
                CodigoLote.From(codigoLote),
                null,
                variacao?.DescricaoComercial,
                variacao?.Cor,
                variacao?.Tamanho,
                null,
                null,
                fornecedorId is null ? "Producao Propria" : fornecedorNome,
                validadeEm.HasValue ? Validade.From(validadeEm.Value) : null,
                observacoes,
                entradaEm);

            context.ItensEstoque.Add(item);
            criadoAgora = true;
        }

        item.LojaId = lojaId;
        item.FornecedorId = fornecedorId;
        item.AtualizarVelocidadeSaida(velocidadeSaidaDiaria, agora);
        item.RecalcularIndicadores(agora);

        return new ItemSeedContext(item, criadoAgora);
    }

    internal static async Task EnsureEntradaMovimentacaoAsync(
        EasyStockDbContext context,
        Guid empresaId,
        ItemEstoque item,
        NaturezaMovimentacaoEstoque natureza,
        Quantidade quantidade,
        Dinheiro valorUnitario,
        DateTime dataMovimentacao,
        string descricao,
        string documento)
    {
        var exists = await context.MovimentacoesEstoque.AnyAsync(m =>
            m.EmpresaId == empresaId &&
            m.ItemEstoqueId == item.Id &&
            m.DocumentoReferencia == documento);
        if (exists)
            return;

        var entrada = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(),
            empresaId,
            item,
            natureza,
            quantidade,
            valorUnitario,
            dataMovimentacao,
            descricao,
            documento,
            dataMovimentacao);

        context.MovimentacoesEstoque.Add(entrada);
    }

    internal static async Task EnsureReposicaoAsync(
        EasyStockDbContext context,
        Guid empresaId,
        ItemEstoque item,
        Quantidade quantidade,
        Dinheiro valorUnitario,
        DateTime dataReposicao,
        string descricao,
        string documento)
    {
        var exists = await context.MovimentacoesEstoque.AnyAsync(m =>
            m.EmpresaId == empresaId &&
            m.ItemEstoqueId == item.Id &&
            m.DocumentoReferencia == documento);
        if (exists)
            return;

        item.RegistrarReposicao(
            quantidade,
            dataReposicao,
            item.VariacaoDescricao,
            item.Cor,
            item.Tamanho,
            descricao,
            item.DimensoesReais,
            item.ValidadeEm,
            valorUnitario,
            item.PrecoVendaSugerido,
            dataReposicao);

        var reposicao = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(),
            empresaId,
            item,
            NaturezaMovimentacaoEstoque.Reposicao,
            quantidade,
            valorUnitario,
            dataReposicao,
            descricao,
            documento,
            dataReposicao);

        context.MovimentacoesEstoque.Add(reposicao);
    }

    internal static async Task<Venda?> EnsureVendaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid lojaId,
        string numeroNotaFiscal,
        DateTime dataVenda,
        string observacao,
        params VendaLinha[] linhas)
    {
        var vendaExistente = await context.Vendas.FirstOrDefaultAsync(v => v.EmpresaId == empresaId && v.NumeroNotaFiscal == numeroNotaFiscal);
        if (vendaExistente is not null)
            return vendaExistente;

        var vendaId = Guid.NewGuid();
        var venda = Venda.Criar(
            vendaId,
            empresaId,
            CanalVenda.LojaPropria,
            NaturezaMovimentacaoEstoque.Venda,
            dataVenda,
            dataVenda,
            numeroNotaFiscal,
            observacao,
            dataVenda);
        venda.LojaId = lojaId;

        foreach (var linha in linhas)
        {
            linha.ItemEstoque.RegistrarSaida(linha.Quantidade, dataVenda, dataVenda);

            var itemVenda = new ItemVenda
            {
                Id = Guid.NewGuid(),
                VendaId = vendaId,
                ItemEstoqueId = linha.ItemEstoque.Id,
                ProdutoId = linha.ItemEstoque.ProdutoId,
                ProdutoVariacaoId = linha.ProdutoVariacaoId,
                DescricaoSnapshot = linha.ItemEstoque.Produto?.Nome ?? linha.ItemEstoque.CodigoInterno ?? "Item",
                VariacaoSnapshot = linha.ItemEstoque.VariacaoDescricao,
                Quantidade = linha.Quantidade,
                PrecoUnitario = linha.PrecoUnitario,
                PrecoTotal = Dinheiro.FromDecimal(linha.PrecoUnitario.Valor * linha.Quantidade.Value),
                CriadoEm = dataVenda
            };

            venda.AdicionarItem(itemVenda);
            context.ItensVenda.Add(itemVenda);

            var movimentacao = MovimentacaoEstoque.CriarSaida(
                Guid.NewGuid(),
                empresaId,
                linha.ItemEstoque,
                vendaId,
                NaturezaMovimentacaoEstoque.Venda,
                linha.Quantidade,
                linha.PrecoUnitario,
                dataVenda,
                $"Venda {numeroNotaFiscal}",
                numeroNotaFiscal,
                dataVenda);
            context.MovimentacoesEstoque.Add(movimentacao);
        }

        context.Vendas.Add(venda);
        return venda;
    }

    internal static async Task EnsureSaidaAvulsaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        ItemEstoque item,
        Quantidade quantidade,
        Dinheiro valorUnitario,
        NaturezaMovimentacaoEstoque natureza,
        DateTime dataMovimentacao,
        string descricao,
        string documento)
    {
        var exists = await context.MovimentacoesEstoque.AnyAsync(m =>
            m.EmpresaId == empresaId &&
            m.ItemEstoqueId == item.Id &&
            m.DocumentoReferencia == documento);
        if (exists)
            return;

        item.RegistrarSaida(quantidade, dataMovimentacao, dataMovimentacao);

        context.MovimentacoesEstoque.Add(new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = item.Id,
            ProdutoId = item.ProdutoId,
            ProdutoVariacaoId = item.ProdutoVariacaoId,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = natureza,
            Quantidade = quantidade,
            ValorUnitario = valorUnitario,
            ValorTotal = Dinheiro.FromDecimal(valorUnitario.Valor * quantidade.Value),
            DataMovimentacao = dataMovimentacao,
            Descricao = descricao,
            DocumentoReferencia = documento,
            CriadoEm = dataMovimentacao
        });
    }

    internal static async Task EnsureNotificacaoAsync(
        EasyStockDbContext context,
        Guid empresaId,
        TipoAlertaEstoque tipo,
        string mensagem,
        Guid? referenciaId,
        SeveridadeNotificacao? severidade = null,
        string? titulo = null,
        DateTime? criadaEm = null)
    {
        var notificacao = await context.Notificacoes.FirstOrDefaultAsync(n =>
            n.EmpresaId == empresaId &&
            n.TipoAlerta == tipo &&
            n.ReferenciaId == referenciaId);

        if (notificacao is null)
        {
            var nova = severidade.HasValue || titulo is not null
                ? Notificacao.Criar(empresaId, tipo, titulo ?? TituloPadrao(tipo), mensagem, severidade ?? SeveridadePadrao(tipo), referenciaId)
                : Notificacao.Criar(empresaId, tipo, mensagem, referenciaId);

            if (criadaEm.HasValue)
                nova.CriadaEm = criadaEm.Value;

            context.Notificacoes.Add(nova);
            return;
        }

        notificacao.AtualizarMensagem(mensagem);
        notificacao.Lida = false;
        notificacao.LidaEm = null;
    }

    private static string TituloPadrao(TipoAlertaEstoque tipo) => tipo switch
    {
        TipoAlertaEstoque.EstoqueCritico => "Estoque Crítico",
        TipoAlertaEstoque.ProdutoParado => "Produto Parado",
        TipoAlertaEstoque.ValidadeProxima => "Validade Próxima",
        TipoAlertaEstoque.ReposicaoSugerida => "Reposição Sugerida",
        TipoAlertaEstoque.PedidoAtrasado => "Pedido Atrasado",
        TipoAlertaEstoque.PedidoRecebido => "Pedido Recebido",
        TipoAlertaEstoque.ProdutoVencido => "Produto Vencido",
        _ => "Notificação"
    };

    private static SeveridadeNotificacao SeveridadePadrao(TipoAlertaEstoque tipo) => tipo switch
    {
        TipoAlertaEstoque.EstoqueCritico => SeveridadeNotificacao.Alta,
        TipoAlertaEstoque.ProdutoVencido => SeveridadeNotificacao.Critica,
        TipoAlertaEstoque.ValidadeProxima => SeveridadeNotificacao.Media,
        TipoAlertaEstoque.ProdutoParado => SeveridadeNotificacao.Media,
        TipoAlertaEstoque.ReposicaoSugerida => SeveridadeNotificacao.Media,
        TipoAlertaEstoque.PedidoAtrasado => SeveridadeNotificacao.Alta,
        TipoAlertaEstoque.PedidoRecebido => SeveridadeNotificacao.Informativa,
        _ => SeveridadeNotificacao.Media
    };

    internal sealed record ProdutoSeed(
        Guid EmpresaId,
        Guid CategoriaId,
        Guid? SubcategoriaId,
        string Nome,
        string Descricao,
        string Marca,
        TipoProduto Tipo,
        bool ControlaValidade,
        decimal Custo,
        decimal Preco,
        string CodigoBarras,
        string SkuBase,
        decimal Margem,
        DateTime Agora);

    internal sealed record ItemSeedContext(ItemEstoque Item, bool CriadoAgora);

    internal sealed record VendaLinha(ItemEstoque ItemEstoque, Guid? ProdutoVariacaoId, Quantidade Quantidade, Dinheiro PrecoUnitario);
}
