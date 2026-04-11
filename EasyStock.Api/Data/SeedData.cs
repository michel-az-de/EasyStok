using BCrypt.Net;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Data;

public static class SeedData
{
    public static async Task ExecutarAsync(IServiceProvider services, ILogger logger)
    {
        var context = services.GetRequiredService<EasyStockDbContext>();

        if (await context.Lojas.AnyAsync())
        {
            logger.LogInformation("Banco ja possui dados. Seed ignorado.");
            return;
        }

        logger.LogInformation("Executando seed de dados iniciais...");

        var agora = DateTime.UtcNow;

        // ─── Plano ───────────────────────────────────────────────────────────
        var plano = new Plano
        {
            Id = Guid.NewGuid(),
            Nome = "Profissional",
            Descricao = "Plano completo para pequenas e medias empresas",
            LimiteLojas = Plano.SemLimite,
            LimiteUsuarios = Plano.SemLimite,
            LimiteProdutos = Plano.SemLimite,
            LimiteGeracoesIaMensais = 100,
            PrecoMensal = 99.90m,
            Ativo = true,
            CriadoEm = agora
        };
        context.Planos.Add(plano);

        // ─── Empresa ─────────────────────────────────────────────────────────
        var empresa = Empresa.Criar("Felipe Azevedo ME", "12.345.678/0001-90");
        context.Empresas.Add(empresa);

        // ─── Assinatura ──────────────────────────────────────────────────────
        var assinatura = new AssinaturaEmpresa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            PlanoId = plano.Id,
            DataInicio = agora,
            DataFim = agora.AddYears(1),
            Status = StatusAssinatura.Ativa,
            CriadoEm = agora,
            AlteradoEm = agora
        };
        context.AssinaturasEmpresa.Add(assinatura);

        // ─── Lojas ───────────────────────────────────────────────────────────
        var lojaFone = Loja.Criar(empresa.Id, "Clube do Fone");
        lojaFone.Descricao = "Revenda especializada em IEM e fones KZ";
        lojaFone.Telefone = "(41) 99999-1111";
        lojaFone.Endereco = "Rua das Flores, 123 - Curitiba/PR";
        context.Lojas.Add(lojaFone);

        var lojaCantina = Loja.Criar(empresa.Id, "Cantina da Thati");
        lojaCantina.Descricao = "Massas artesanais e molhos frescos";
        lojaCantina.Telefone = "(41) 99999-2222";
        lojaCantina.Endereco = "Av. das Massas, 456 - Curitiba/PR";
        context.Lojas.Add(lojaCantina);

        // ─── Configuracoes das Lojas ─────────────────────────────────────────
        context.ConfiguracoesLoja.Add(ConfiguracaoLoja.CriarPadrao(lojaFone.Id));
        context.ConfiguracoesLoja.Add(ConfiguracaoLoja.CriarPadrao(lojaCantina.Id));

        // ─── Perfis (Roles) ──────────────────────────────────────────────────
        var perfilAdmin = new Perfil
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Admin",
            Descricao = "Administrador com acesso total",
            Nivel = NivelAcesso.Admin,
            CriadoEm = agora
        };
        var perfilGerente = new Perfil
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Gerente",
            Descricao = "Gerente com acesso a relatorios e gestao",
            Nivel = NivelAcesso.Gerente,
            CriadoEm = agora
        };
        var perfilOperador = new Perfil
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Operador",
            Descricao = "Operador de loja com acesso basico",
            Nivel = NivelAcesso.Operador,
            CriadoEm = agora
        };
        context.Perfis.AddRange(perfilAdmin, perfilGerente, perfilOperador);

        // ─── Usuarios ────────────────────────────────────────────────────────
        var felipe = Usuario.Criar(
            "Felipe Azevedo",
            "felipe@easystock.com",
            BCrypt.Net.BCrypt.HashPassword("Admin@2026!Secure"));

        var thatiane = Usuario.Criar(
            "Thatiane Preschlak",
            "thatiane@easystock.com",
            BCrypt.Net.BCrypt.HashPassword("Thati@2026!Gerente"));

        var operadorFone = Usuario.Criar(
            "Operador Clube do Fone",
            "operador.fone@easystock.com",
            BCrypt.Net.BCrypt.HashPassword("OpFone@2026!Access"));

        var operadorCantina = Usuario.Criar(
            "Operador Cantina da Thati",
            "operador.cantina@easystock.com",
            BCrypt.Net.BCrypt.HashPassword("OpCantina@2026!Access"));

        context.Usuarios.AddRange(felipe, thatiane, operadorFone, operadorCantina);

        // ─── UsuarioEmpresa (vinculo usuario <-> empresa) ─────────────────────
        context.UsuariosEmpresas.AddRange(
            new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = felipe.Id, EmpresaId = empresa.Id, Ativo = true, CriadoEm = agora },
            new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = thatiane.Id, EmpresaId = empresa.Id, Ativo = true, CriadoEm = agora },
            new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = operadorFone.Id, EmpresaId = empresa.Id, Ativo = true, CriadoEm = agora },
            new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = operadorCantina.Id, EmpresaId = empresa.Id, Ativo = true, CriadoEm = agora }
        );

        // ─── UsuarioPerfil (role por empresa / loja) ──────────────────────────
        context.UsuariosPerfis.AddRange(
            // Felipe: Admin (sem restricao de loja)
            new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = felipe.Id, EmpresaId = empresa.Id, PerfilId = perfilAdmin.Id, LojaId = null, AtribuidoEm = agora },
            // Thatiane: Gerente (acesso a ambas as lojas)
            new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = thatiane.Id, EmpresaId = empresa.Id, PerfilId = perfilGerente.Id, LojaId = lojaFone.Id, AtribuidoEm = agora },
            new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = thatiane.Id, EmpresaId = empresa.Id, PerfilId = perfilGerente.Id, LojaId = lojaCantina.Id, AtribuidoEm = agora },
            // Operadores: cada um na sua loja
            new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = operadorFone.Id, EmpresaId = empresa.Id, PerfilId = perfilOperador.Id, LojaId = lojaFone.Id, AtribuidoEm = agora },
            new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = operadorCantina.Id, EmpresaId = empresa.Id, PerfilId = perfilOperador.Id, LojaId = lojaCantina.Id, AtribuidoEm = agora }
        );

        // ─── Categorias — Clube do Fone ───────────────────────────────────────
        var catIem = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "IEM / In-Ear Monitor",
            Descricao = "Fones intra-auriculares de alta fidelidade",
            CriadoEm = agora,
            AlteradoEm = agora
        };
        var catCabos = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Cabos e Acessorios",
            Descricao = "Cabos de upgrade e acessorios para IEM",
            CriadoEm = agora,
            AlteradoEm = agora
        };

        // ─── Categorias — Cantina da Thati ────────────────────────────────────
        var catMassas = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Massas Frescas",
            Descricao = "Massas artesanais produzidas diariamente",
            CriadoEm = agora,
            AlteradoEm = agora
        };
        var catMolhos = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            Nome = "Molhos",
            Descricao = "Molhos caseiros e industrializados",
            CriadoEm = agora,
            AlteradoEm = agora
        };
        context.Categorias.AddRange(catIem, catCabos, catMassas, catMolhos);

        // ─── Fornecedores ─────────────────────────────────────────────────────
        var fornKz = Fornecedor.Criar(empresa.Id, "KZ Acoustics Trading Co.");
        fornKz.AtualizarCadastro(
            "KZ Acoustics Trading Co.",
            null, "kz@kz-acoustics.com", "+86 755-1234-5678",
            "James Li", "Eletronica / Audio", "Internacional",
            45, "https://www.kz-acoustics.com",
            "USD 200,00", "FOB Shenzhen", "Fabricante de IEM em Shenzhen, China");
        context.Fornecedores.Add(fornKz);

        var fornMoinho = Fornecedor.Criar(empresa.Id, "Moinho Sao Paulo Ltda");
        fornMoinho.AtualizarCadastro(
            "Moinho Sao Paulo Ltda",
            "60.123.456/0001-78", "vendas@moinhosp.com.br", "(11) 3456-7890",
            "Ana Paula", "Alimentos", "Nacional",
            7, "https://www.moinhosp.com.br",
            "R$ 500,00", "CIF", "Fornecedor de farinha e massas para o mercado paulista");
        context.Fornecedores.Add(fornMoinho);

        // ─── Produtos — Clube do Fone ─────────────────────────────────────────
        var prodZsnPro = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = catIem.Id,
            Nome = "KZ ZSN Pro X",
            DescricaoBase = "IEM hibrido com driver dinamico + balanced armature",
            Marca = "KZ",
            Tipo = TipoProduto.Fisico,
            SkuBase = null,
            CodigoBarras = "6971569432011",
            ControlaValidade = false,
            CustoReferencia = Dinheiro.FromDecimal(42.00m),
            PrecoReferencia = Dinheiro.FromDecimal(89.90m),
            MargemEstimada = 53.9m,
            Status = StatusProduto.Ativo,
            CriadoEm = agora,
            AlteradoEm = agora
        };
        var prodZs10 = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = catIem.Id,
            Nome = "KZ ZS10 Pro",
            DescricaoBase = "IEM com 4 balanced armatures + 1 driver dinamico",
            Marca = "KZ",
            Tipo = TipoProduto.Fisico,
            CodigoBarras = "6971569430109",
            ControlaValidade = false,
            CustoReferencia = Dinheiro.FromDecimal(68.00m),
            PrecoReferencia = Dinheiro.FromDecimal(129.90m),
            MargemEstimada = 47.9m,
            Status = StatusProduto.Ativo,
            CriadoEm = agora,
            AlteradoEm = agora
        };
        var prodCaboPrata = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = catCabos.Id,
            Nome = "Cabo KZ Prata",
            DescricaoBase = "Cabo de upgrade prata 8 cores com conector 2-pin 0.75mm",
            Marca = "KZ",
            Tipo = TipoProduto.Fisico,
            CodigoBarras = "6971569435001",
            ControlaValidade = false,
            CustoReferencia = Dinheiro.FromDecimal(14.00m),
            PrecoReferencia = Dinheiro.FromDecimal(34.90m),
            MargemEstimada = 59.9m,
            Status = StatusProduto.Ativo,
            CriadoEm = agora,
            AlteradoEm = agora
        };

        // ─── Produtos — Cantina da Thati ──────────────────────────────────────
        var prodTalharim = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = catMassas.Id,
            Nome = "Talharim Fresco 500g",
            DescricaoBase = "Talharim fresco artesanal produzido no dia",
            Marca = "Cantina da Thati",
            Tipo = TipoProduto.Alimento,
            ControlaValidade = true,
            CustoReferencia = Dinheiro.FromDecimal(5.50m),
            PrecoReferencia = Dinheiro.FromDecimal(12.90m),
            MargemEstimada = 57.4m,
            Status = StatusProduto.Ativo,
            CriadoEm = agora,
            AlteradoEm = agora
        };
        var prodRavioli = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = catMassas.Id,
            Nome = "Ravioli de Carne 400g",
            DescricaoBase = "Ravioli recheado com carne bovina temperada",
            Marca = "Cantina da Thati",
            Tipo = TipoProduto.Alimento,
            ControlaValidade = true,
            CustoReferencia = Dinheiro.FromDecimal(8.00m),
            PrecoReferencia = Dinheiro.FromDecimal(18.90m),
            MargemEstimada = 57.7m,
            Status = StatusProduto.Ativo,
            CriadoEm = agora,
            AlteradoEm = agora
        };
        var prodMolho = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            CategoriaId = catMolhos.Id,
            Nome = "Molho Bolonhesa 300ml",
            DescricaoBase = "Molho bolonhesa caseiro com carne moida e tomate",
            Marca = "Cantina da Thati",
            Tipo = TipoProduto.Alimento,
            ControlaValidade = true,
            CustoReferencia = Dinheiro.FromDecimal(4.50m),
            PrecoReferencia = Dinheiro.FromDecimal(9.90m),
            MargemEstimada = 54.5m,
            Status = StatusProduto.Ativo,
            CriadoEm = agora,
            AlteradoEm = agora
        };
        context.Produtos.AddRange(prodZsnPro, prodZs10, prodCaboPrata, prodTalharim, prodRavioli, prodMolho);

        // ─── Itens de Estoque — Clube do Fone ────────────────────────────────
        var itemZsnPro = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresa.Id, prodZsnPro, null,
            Quantidade.From(30), Dinheiro.FromDecimal(42.00m), Dinheiro.FromDecimal(89.90m),
            agora.AddDays(-30), "ZSN-PRO-001", null, null,
            null, null, null, null, null,
            "KZ Acoustics Trading Co.", null, "Lote inicial importado", agora.AddDays(-30));
        itemZsnPro.LojaId = lojaFone.Id;
        itemZsnPro.FornecedorId = fornKz.Id;

        var itemZs10 = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresa.Id, prodZs10, null,
            Quantidade.From(20), Dinheiro.FromDecimal(68.00m), Dinheiro.FromDecimal(129.90m),
            agora.AddDays(-30), "ZS10-PRO-001", null, null,
            null, null, null, null, null,
            "KZ Acoustics Trading Co.", null, "Lote inicial importado", agora.AddDays(-30));
        itemZs10.LojaId = lojaFone.Id;
        itemZs10.FornecedorId = fornKz.Id;

        var itemCaboPrata = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresa.Id, prodCaboPrata, null,
            Quantidade.From(50), Dinheiro.FromDecimal(14.00m), Dinheiro.FromDecimal(34.90m),
            agora.AddDays(-30), "CABO-PRATA-001", null, null,
            null, null, null, null, null,
            "KZ Acoustics Trading Co.", null, "Lote inicial importado", agora.AddDays(-30));
        itemCaboPrata.LojaId = lojaFone.Id;
        itemCaboPrata.FornecedorId = fornKz.Id;

        // ─── Itens de Estoque — Cantina da Thati ──────────────────────────────
        var validadeMassa = Validade.From(agora.AddDays(3));
        var itemTalharim = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresa.Id, prodTalharim, null,
            Quantidade.From(40), Dinheiro.FromDecimal(5.50m), Dinheiro.FromDecimal(12.90m),
            agora, "TALH-001", null, null,
            null, null, null, null, null,
            "Producao Propria", validadeMassa, "Producao do dia", agora);
        itemTalharim.LojaId = lojaCantina.Id;

        var itemRavioli = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresa.Id, prodRavioli, null,
            Quantidade.From(25), Dinheiro.FromDecimal(8.00m), Dinheiro.FromDecimal(18.90m),
            agora, "RAVL-001", null, null,
            null, null, null, null, null,
            "Producao Propria", validadeMassa, "Producao do dia", agora);
        itemRavioli.LojaId = lojaCantina.Id;

        var itemMolho = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), empresa.Id, prodMolho, null,
            Quantidade.From(30), Dinheiro.FromDecimal(4.50m), Dinheiro.FromDecimal(9.90m),
            agora, "MOLHO-001", null, null,
            null, null, null, null, null,
            "Producao Propria", Validade.From(agora.AddDays(7)), "Producao semanal", agora);
        itemMolho.LojaId = lojaCantina.Id;

        context.ItensEstoque.AddRange(itemZsnPro, itemZs10, itemCaboPrata, itemTalharim, itemRavioli, itemMolho);

        // Salvar tudo antes das movimentacoes (precisamos dos IDs)
        await context.SaveChangesAsync();

        // ─── Movimentacoes de Entrada ─────────────────────────────────────────
        var movEntradaZsn = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(), empresa.Id, itemZsnPro,
            NaturezaMovimentacaoEstoque.Compra,
            Quantidade.From(30), Dinheiro.FromDecimal(42.00m),
            agora.AddDays(-30), "Pedido de importacao KZ - lote 01",
            "PO-2026-001", agora.AddDays(-30));

        var movEntradaZs10 = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(), empresa.Id, itemZs10,
            NaturezaMovimentacaoEstoque.Compra,
            Quantidade.From(20), Dinheiro.FromDecimal(68.00m),
            agora.AddDays(-30), "Pedido de importacao KZ - lote 01",
            "PO-2026-001", agora.AddDays(-30));

        var movEntradaTalharim = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(), empresa.Id, itemTalharim,
            NaturezaMovimentacaoEstoque.Producao,
            Quantidade.From(40), Dinheiro.FromDecimal(5.50m),
            agora, "Producao diaria de talharim",
            null, agora);

        context.MovimentacoesEstoque.AddRange(movEntradaZsn, movEntradaZs10, movEntradaTalharim);

        // ─── Pedido de Compra ─────────────────────────────────────────────────
        var pedidoKz = new PedidoFornecedor
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            FornecedorId = fornKz.Id,
            DataPedido = agora.AddDays(-35),
            PrevisaoEntrega = agora.AddDays(-5),
            DataRecebimento = agora.AddDays(-30),
            ValorEstimado = 3960.00m,
            Status = StatusPedidoFornecedor.Recebido,
            Canal = "Email",
            Tracking = "KZ-TRK-20260301",
            Observacoes = "Pedido inaugural - lote 01 KZ ZSN Pro X e ZS10 Pro",
            CriadoEm = agora.AddDays(-35),
            AlteradoEm = agora.AddDays(-30)
        };
        context.PedidosFornecedor.Add(pedidoKz);

        // ─── Venda — Clube do Fone ────────────────────────────────────────────
        var vendaId = Guid.NewGuid();
        var venda = Venda.Criar(vendaId, empresa.Id,
            CanalVenda.LojaPropria, NaturezaMovimentacaoEstoque.Venda,
            agora.AddDays(-10), agora.AddDays(-10),
            "NF-2026-0001", "Venda presencial", agora.AddDays(-10));
        venda.LojaId = lojaFone.Id;

        var itemVenda1 = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = vendaId,
            ItemEstoqueId = itemZsnPro.Id,
            ProdutoId = prodZsnPro.Id,
            DescricaoSnapshot = prodZsnPro.Nome,
            Quantidade = Quantidade.From(2),
            PrecoUnitario = Dinheiro.FromDecimal(89.90m),
            PrecoTotal = Dinheiro.FromDecimal(179.80m),
            CriadoEm = agora.AddDays(-10)
        };
        var itemVenda2 = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = vendaId,
            ItemEstoqueId = itemCaboPrata.Id,
            ProdutoId = prodCaboPrata.Id,
            DescricaoSnapshot = prodCaboPrata.Nome,
            Quantidade = Quantidade.From(2),
            PrecoUnitario = Dinheiro.FromDecimal(34.90m),
            PrecoTotal = Dinheiro.FromDecimal(69.80m),
            CriadoEm = agora.AddDays(-10)
        };
        venda.AdicionarItem(itemVenda1);
        venda.AdicionarItem(itemVenda2);
        context.Vendas.Add(venda);
        context.ItensVenda.AddRange(itemVenda1, itemVenda2);

        // Registrar saida no estoque
        itemZsnPro.RegistrarSaida(Quantidade.From(2), agora.AddDays(-10), agora.AddDays(-10));
        itemCaboPrata.RegistrarSaida(Quantidade.From(2), agora.AddDays(-10), agora.AddDays(-10));

        var movSaidaZsn = MovimentacaoEstoque.CriarSaida(
            Guid.NewGuid(), empresa.Id, itemZsnPro, vendaId,
            NaturezaMovimentacaoEstoque.Venda, Quantidade.From(2),
            Dinheiro.FromDecimal(89.90m), agora.AddDays(-10),
            "Venda NF-2026-0001", "NF-2026-0001", agora.AddDays(-10));

        var movSaidaCabo = MovimentacaoEstoque.CriarSaida(
            Guid.NewGuid(), empresa.Id, itemCaboPrata, vendaId,
            NaturezaMovimentacaoEstoque.Venda, Quantidade.From(2),
            Dinheiro.FromDecimal(34.90m), agora.AddDays(-10),
            "Venda NF-2026-0001", "NF-2026-0001", agora.AddDays(-10));

        context.MovimentacoesEstoque.AddRange(movSaidaZsn, movSaidaCabo);

        // ─── Transferencia (movimentacao entre lojas) ─────────────────────────
        // Simula transferencia de 5 cabos da loja Fone para Cantina (ajuste de estoque)
        // Usa criacao manual pois ajustes nao possuem VendaId
        itemCaboPrata.RegistrarSaida(Quantidade.From(5), agora.AddDays(-5), agora.AddDays(-5));
        var movTransferencia = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            ItemEstoqueId = itemCaboPrata.Id,
            ProdutoId = prodCaboPrata.Id,
            VendaId = null,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Ajuste,
            Quantidade = Quantidade.From(5),
            ValorUnitario = Dinheiro.FromDecimal(14.00m),
            ValorTotal = Dinheiro.FromDecimal(70.00m),
            DataMovimentacao = agora.AddDays(-5),
            Descricao = "Transferencia de 5 cabos para exposicao da Cantina",
            DocumentoReferencia = "TRANSF-2026-001",
            CriadoEm = agora.AddDays(-5)
        };
        context.MovimentacoesEstoque.Add(movTransferencia);

        // ─── Vendas adicionais — Clube do Fone ───────────────────────────────
        // Gerar vendas variadas nos últimos 30 dias para demonstrar giro real
        var vendasFone = new (int diasAtras, Guid prodId, string nomeProd, Guid itemId, int qty, decimal preco)[]
        {
            (-25, prodZsnPro.Id, "KZ ZSN Pro X", itemZsnPro.Id, 3, 89.90m),
            (-22, prodCaboPrata.Id, "Cabo KZ Prata", itemCaboPrata.Id, 4, 34.90m),
            (-18, prodZs10.Id, "KZ ZS10 Pro", itemZs10.Id, 2, 129.90m),
            (-15, prodZsnPro.Id, "KZ ZSN Pro X", itemZsnPro.Id, 1, 89.90m),
            (-12, prodCaboPrata.Id, "Cabo KZ Prata", itemCaboPrata.Id, 3, 34.90m),
            (-8, prodZs10.Id, "KZ ZS10 Pro", itemZs10.Id, 5, 129.90m),
            (-5, prodZsnPro.Id, "KZ ZSN Pro X", itemZsnPro.Id, 2, 89.90m),
            (-2, prodCaboPrata.Id, "Cabo KZ Prata", itemCaboPrata.Id, 2, 34.90m),
        };

        foreach (var (diasAtras, prodId, nomeProd, itemId, qty, preco) in vendasFone)
        {
            var vId = Guid.NewGuid();
            var nfNum = $"NF-FONE-{Math.Abs(diasAtras):D3}";
            var dataVenda = agora.AddDays(diasAtras);
            var v = Venda.Criar(vId, empresa.Id, CanalVenda.LojaPropria,
                NaturezaMovimentacaoEstoque.Venda, dataVenda, dataVenda, nfNum, "Venda presencial", dataVenda);
            v.LojaId = lojaFone.Id;

            var iv = new ItemVenda
            {
                Id = Guid.NewGuid(), VendaId = vId, ItemEstoqueId = itemId, ProdutoId = prodId,
                DescricaoSnapshot = nomeProd, Quantidade = Quantidade.From(qty),
                PrecoUnitario = Dinheiro.FromDecimal(preco), PrecoTotal = Dinheiro.FromDecimal(qty * preco),
                CriadoEm = dataVenda
            };
            v.AdicionarItem(iv);
            context.Vendas.Add(v);
            context.ItensVenda.Add(iv);

            // Registrar saída no estoque e movimentação
            var item = itemId == itemZsnPro.Id ? itemZsnPro : itemId == itemZs10.Id ? itemZs10 : itemCaboPrata;
            item.RegistrarSaida(Quantidade.From(qty), dataVenda, dataVenda);
            context.MovimentacoesEstoque.Add(MovimentacaoEstoque.CriarSaida(
                Guid.NewGuid(), empresa.Id, item, vId,
                NaturezaMovimentacaoEstoque.Venda, Quantidade.From(qty),
                Dinheiro.FromDecimal(preco), dataVenda, $"Venda {nfNum}", nfNum, dataVenda));
        }

        // ─── Vendas adicionais — Cantina da Thati ────────────────────────────
        // Alimentos vendem em volume alto, preço baixo
        var vendasCantina = new (int diasAtras, Guid prodId, string nomeProd, Guid itemId, int qty, decimal preco)[]
        {
            (-28, prodTalharim.Id, "Talharim 500g", itemTalharim.Id, 5, 12.90m),
            (-26, prodRavioli.Id, "Ravioli 400g", itemRavioli.Id, 3, 18.90m),
            (-24, prodMolho.Id, "Molho Bolonhesa", itemMolho.Id, 4, 9.90m),
            (-22, prodTalharim.Id, "Talharim 500g", itemTalharim.Id, 6, 12.90m),
            (-20, prodRavioli.Id, "Ravioli 400g", itemRavioli.Id, 4, 18.90m),
            (-18, prodMolho.Id, "Molho Bolonhesa", itemMolho.Id, 3, 9.90m),
            (-15, prodTalharim.Id, "Talharim 500g", itemTalharim.Id, 8, 12.90m),
            (-13, prodRavioli.Id, "Ravioli 400g", itemRavioli.Id, 5, 18.90m),
            (-10, prodMolho.Id, "Molho Bolonhesa", itemMolho.Id, 6, 9.90m),
            (-8, prodTalharim.Id, "Talharim 500g", itemTalharim.Id, 4, 12.90m),
            (-5, prodRavioli.Id, "Ravioli 400g", itemRavioli.Id, 3, 18.90m),
            (-3, prodMolho.Id, "Molho Bolonhesa", itemMolho.Id, 5, 9.90m),
            (-1, prodTalharim.Id, "Talharim 500g", itemTalharim.Id, 3, 12.90m),
        };

        foreach (var (diasAtras, prodId, nomeProd, itemId, qty, preco) in vendasCantina)
        {
            var vId = Guid.NewGuid();
            var nfNum = $"NF-CANT-{Math.Abs(diasAtras):D3}";
            var dataVenda = agora.AddDays(diasAtras);
            var v = Venda.Criar(vId, empresa.Id, CanalVenda.LojaPropria,
                NaturezaMovimentacaoEstoque.Venda, dataVenda, dataVenda, nfNum, "Venda balcao", dataVenda);
            v.LojaId = lojaCantina.Id;

            var iv = new ItemVenda
            {
                Id = Guid.NewGuid(), VendaId = vId, ItemEstoqueId = itemId, ProdutoId = prodId,
                DescricaoSnapshot = nomeProd, Quantidade = Quantidade.From(qty),
                PrecoUnitario = Dinheiro.FromDecimal(preco), PrecoTotal = Dinheiro.FromDecimal(qty * preco),
                CriadoEm = dataVenda
            };
            v.AdicionarItem(iv);
            context.Vendas.Add(v);
            context.ItensVenda.Add(iv);

            var item = itemId == itemTalharim.Id ? itemTalharim : itemId == itemRavioli.Id ? itemRavioli : itemMolho;
            item.RegistrarSaida(Quantidade.From(qty), dataVenda, dataVenda);
            context.MovimentacoesEstoque.Add(MovimentacaoEstoque.CriarSaida(
                Guid.NewGuid(), empresa.Id, item, vId,
                NaturezaMovimentacaoEstoque.Venda, Quantidade.From(qty),
                Dinheiro.FromDecimal(preco), dataVenda, $"Venda {nfNum}", nfNum, dataVenda));
        }

        // ─── Produções adicionais Cantina (reposição de estoque) ─────────────
        var entradaRavioli = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(), empresa.Id, itemRavioli,
            NaturezaMovimentacaoEstoque.Producao,
            Quantidade.From(25), Dinheiro.FromDecimal(8.00m),
            agora.AddDays(-14), "Producao semanal de ravioli", null, agora.AddDays(-14));
        context.MovimentacoesEstoque.Add(entradaRavioli);
        itemRavioli.RegistrarReposicao(Quantidade.From(25), agora.AddDays(-14),
            null, null, null, "Producao semanal", null, null, null, null, agora.AddDays(-14));

        var entradaMolho = MovimentacaoEstoque.CriarEntrada(
            Guid.NewGuid(), empresa.Id, itemMolho,
            NaturezaMovimentacaoEstoque.Producao,
            Quantidade.From(30), Dinheiro.FromDecimal(4.50m),
            agora.AddDays(-14), "Producao semanal de molho", null, agora.AddDays(-14));
        context.MovimentacoesEstoque.Add(entradaMolho);
        itemMolho.RegistrarReposicao(Quantidade.From(30), agora.AddDays(-14),
            null, null, null, "Producao semanal", null, null, null, null, agora.AddDays(-14));

        // ─── Notificacoes ─────────────────────────────────────────────────────
        context.Notificacoes.AddRange(
            Notificacao.Criar(empresa.Id, TipoAlertaEstoque.EstoqueCritico,
                "KZ ZS10 Pro esta com estoque abaixo do minimo recomendado.", itemZs10.Id),
            Notificacao.Criar(empresa.Id, TipoAlertaEstoque.ValidadeProxima,
                "Talharim Fresco 500g vence em 3 dias. Verificar rotatividade.", itemTalharim.Id),
            Notificacao.Criar(empresa.Id, TipoAlertaEstoque.ReposicaoSugerida,
                "KZ ZSN Pro X: velocidade de saida sugere reposicao em 15 dias.", itemZsnPro.Id)
        );

        await context.SaveChangesAsync();
        logger.LogInformation("Seed concluido com sucesso. Empresa '{Empresa}', {Lojas} lojas, {Produtos} produtos.",
            empresa.Nome, 2, 6);
    }
}
