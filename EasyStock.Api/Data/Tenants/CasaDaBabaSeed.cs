using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data.Tenants;

/// <summary>
/// Tenant T3 — Casa da Baba. Persona: negócio em crescimento, 2 lojas, mix B2B/B2C,
/// 90 dias de operação. Mantém credenciais e estrutura existentes para preservar
/// demos e testes apoiados em <c>admin@casadababa.demo</c>.
/// </summary>
internal static partial class CasaDaBabaSeed
{
    public const string EmpresaNome = "Casa da Baba";
    public const string EmpresaDocumento = "48.735.219/0001-62";

    public static async Task ExecutarAsync(EasyStockDbContext context, DateTime agora, ILogger logger)
    {
        logger.LogInformation("Seed tenant '{Empresa}'...", EmpresaNome);

        var plano = await SeedData.UpsertPlanoAsync(context, "Demo Comercial Casa da Baba",
            "Plano de demonstração para operação de massas frescas artesanais", 149.90m, agora);
        var empresa = await SeedData.UpsertEmpresaAsync(context, EmpresaNome, EmpresaDocumento, agora);
        await SeedData.UpsertAssinaturaAsync(context, empresa.Id, plano.Id, agora);

        var lojaCentro = await SeedData.UpsertLojaAsync(context, empresa.Id, "Casa da Baba | Centro",
            "Unidade de produção e varejo de massas frescas", "(11) 3228-1101",
            "Rua Conselheiro Crispiniano, 141 - Centro, São Paulo/SP", agora);
        var lojaMercadao = await SeedData.UpsertLojaAsync(context, empresa.Id, "Casa da Baba | Mercadao",
            "Ponto de distribuição local com alto giro diário", "(11) 3326-4510",
            "Rua da Cantareira, 306 - Box 18, Centro Histórico, São Paulo/SP", agora);

        await SeedData.EnsureConfiguracaoLojaAsync(context, lojaCentro.Id);
        await SeedData.EnsureConfiguracaoLojaAsync(context, lojaMercadao.Id);

        var perfilAdmin = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Admin", "Administrador com acesso total", NivelAcesso.Admin, agora);
        var perfilGerente = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Gerente", "Gestão operacional e analytics", NivelAcesso.Gerente, agora);
        var perfilOperador = await SeedData.UpsertPerfilAsync(context, empresa.Id, "Operador", "Operação diária de estoque e vendas", NivelAcesso.Operador, agora);

        var usuarioAdmin = await SeedData.UpsertUsuarioAsync(context, "Admin Casa da Baba", "admin@casadababa.demo", "admin123", agora);
        var usuarioGerente = await SeedData.UpsertUsuarioAsync(context, "Gerente Casa da Baba", "gerente@casadababa.demo", "gerente123", agora);
        var usuarioOperadorCentro = await SeedData.UpsertUsuarioAsync(context, "Operador Centro", "operador.centro@casadababa.demo", "operador123", agora);
        var usuarioOperadorMercadao = await SeedData.UpsertUsuarioAsync(context, "Operador Mercadão", "operador.mercadao@casadababa.demo", "operador123", agora);

        await SeedData.EnsureUsuarioEmpresaAsync(context, usuarioAdmin.Id, empresa.Id, agora);
        await SeedData.EnsureUsuarioEmpresaAsync(context, usuarioGerente.Id, empresa.Id, agora);
        await SeedData.EnsureUsuarioEmpresaAsync(context, usuarioOperadorCentro.Id, empresa.Id, agora);
        await SeedData.EnsureUsuarioEmpresaAsync(context, usuarioOperadorMercadao.Id, empresa.Id, agora);

        await SeedData.EnsureUsuarioPerfilAsync(context, usuarioAdmin.Id, empresa.Id, perfilAdmin.Id, null, agora);
        await SeedData.EnsureUsuarioPerfilAsync(context, usuarioGerente.Id, empresa.Id, perfilGerente.Id, lojaCentro.Id, agora);
        await SeedData.EnsureUsuarioPerfilAsync(context, usuarioGerente.Id, empresa.Id, perfilGerente.Id, lojaMercadao.Id, agora);
        await SeedData.EnsureUsuarioPerfilAsync(context, usuarioOperadorCentro.Id, empresa.Id, perfilOperador.Id, lojaCentro.Id, agora);
        await SeedData.EnsureUsuarioPerfilAsync(context, usuarioOperadorMercadao.Id, empresa.Id, perfilOperador.Id, lojaMercadao.Id, agora);

        var catMassas = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Massas Frescas", "Massas artesanais refrigeradas de alta rotatividade", null, agora);
        var catMolhos = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Molhos", "Molhos artesanais para acompanhar massas", null, agora);
        await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Recheios e Bases", "Bases frescas para produção interna", null, agora);
        await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Embalagens", "Materiais para fracionamento e venda", null, agora);
        await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Ingredientes", "Insumos para produção diária", null, agora);

        var subTalharim = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Talharim", "Linha de talharim fresco", catMassas.Id, agora);
        var subRavioli = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Ravioli", "Massas recheadas artesanais", catMassas.Id, agora);
        var subNhoque = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Nhoque", "Nhoque fresco de preparo rápido", catMassas.Id, agora);
        var subLasanha = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Lasanha", "Folhas frescas para montagem de lasanha", catMassas.Id, agora);
        var subVermelho = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Vermelho", "Molhos a base de tomate", catMolhos.Id, agora);
        var subBranco = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Branco", "Molhos cremosos", catMolhos.Id, agora);
        var subPesto = await SeedData.UpsertCategoriaAsync(context, empresa.Id, "Pesto", "Molhos verdes frescos", catMolhos.Id, agora);

        var fornFarinha = await SeedData.UpsertFornecedorAsync(context, empresa.Id, "Moinho Santa Italia",
            "22.418.901/0001-06", "vendas@moinhosantaitalia.com.br", "(11) 3504-8801", "Marcos Silveira",
            "Farinha de trigo especial e semola", "Nacional", 5, "https://moinhosantaitalia.com.br",
            "R$ 800,00", "CIF", "Fornecedor principal de farinha tipo 00 para massas frescas");
        var fornOvos = await SeedData.UpsertFornecedorAsync(context, empresa.Id, "Granja Ouro Caipira",
            "31.209.665/0001-51", "comercial@ourocaipira.com.br", "(11) 4962-2210", "Elisa Pacheco",
            "Ovos frescos", "Nacional", 2, "https://ourocaipira.com.br",
            "R$ 300,00", "CIF", "Entrega diária para manter qualidade da massa fresca");
        var fornLaticinios = await SeedData.UpsertFornecedorAsync(context, empresa.Id, "Laticinios Serra Alta",
            "07.843.117/0001-90", "pedidos@serraalta.com.br", "(11) 3891-6120", "Ricardo Moreira",
            "Queijos e derivados", "Nacional", 3, "https://serraalta.com.br",
            "R$ 400,00", "FOB", "Ricota, creme de leite fresco e parmesão ralado");
        var fornCarnes = await SeedData.UpsertFornecedorAsync(context, empresa.Id, "Frigorifico Bela Arte",
            "12.776.100/0001-32", "atendimento@belaartefrigorifico.com.br", "(11) 4227-9000", "Fernanda Lopes",
            "Carnes e recheios", "Nacional", 4, "https://belaartefrigorifico.com.br",
            "R$ 700,00", "CIF", "Cortes bovinos e suínos para recheios e molho bolonhesa");
        await SeedData.UpsertFornecedorAsync(context, empresa.Id, "PackFresh Embalagens",
            "58.190.334/0001-18", "vendas@packfresh.com.br", "(11) 4125-4477", "Guilherme Neri",
            "Embalagens alimentícias", "Nacional", 6, "https://packfresh.com.br",
            "R$ 500,00", "CIF", "Bandejas PET, selos térmicos e potes para molhos");
        var fornHortifruti = await SeedData.UpsertFornecedorAsync(context, empresa.Id, "HortiVerde Premium",
            "44.920.701/0001-73", "pedidos@hortiverde.com.br", "(11) 3822-9191", "Patricia Campos",
            "Hortifruti", "Nacional", 1, "https://hortiverde.com.br",
            "R$ 250,00", "FOB", "Espinafre, manjericão, tomate italiano e ervas frescas");

        var produtos = new Dictionary<string, Produto>
        {
            ["talharim500"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMassas.Id, subTalharim.Id, "Talharim fresco 500g",
                "Talharim artesanal refrigerado, produzido diariamente com farinha tipo 00 e ovos frescos.",
                "Casa da Baba", TipoProduto.Alimento, true, 6.20m, 14.90m, "7891000100011", "CDB-TALH-500", 58.35m, agora)),
            ["talharim1kg"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMassas.Id, subTalharim.Id, "Talharim fresco 1kg",
                "Versão familiar do talharim fresco, ideal para restaurantes e consumo de fim de semana.",
                "Casa da Baba", TipoProduto.Alimento, true, 11.80m, 26.90m, "7891000100012", "CDB-TALH-1KG", 56.07m, agora)),
            ["ravioliCarne"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMassas.Id, subRavioli.Id, "Ravioli de carne 400g",
                "Ravioli fresco recheado com carne bovina e temperos artesanais.",
                "Casa da Baba", TipoProduto.Alimento, true, 9.10m, 22.50m, "7891000100013", "CDB-RAVI-CAR-400", 59.56m, agora)),
            ["ravioliRicota"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMassas.Id, subRavioli.Id, "Ravioli de ricota e espinafre 400g",
                "Ravioli artesanal com recheio leve de ricota fresca e espinafre selecionado.",
                "Casa da Baba", TipoProduto.Alimento, true, 8.70m, 21.90m, "7891000100014", "CDB-RAVI-RIC-400", 60.27m, agora)),
            ["nhoque500"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMassas.Id, subNhoque.Id, "Nhoque de batata 500g",
                "Nhoque fresco com batata asterix e farinha de trigo premium.",
                "Casa da Baba", TipoProduto.Alimento, true, 6.00m, 15.50m, "7891000100015", "CDB-NHOQ-500", 61.29m, agora)),
            ["lasanha500"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMassas.Id, subLasanha.Id, "Massa para lasanha fresca 500g",
                "Folhas de massa fresca para montagem de lasanhas artesanais.",
                "Casa da Baba", TipoProduto.Alimento, true, 5.40m, 13.90m, "7891000100016", "CDB-LASA-500", 61.15m, agora)),
            ["bolonhesa300"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMolhos.Id, subVermelho.Id, "Molho bolonhesa 300ml",
                "Molho bolonhesa com tomate italiano, carne bovina e cozimento lento.",
                "Casa da Baba", TipoProduto.Alimento, true, 4.90m, 12.90m, "7891000100017", "CDB-MOL-BOL-300", 62.02m, agora)),
            ["branco300"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMolhos.Id, subBranco.Id, "Molho branco 300ml",
                "Molho branco cremoso com leite fresco e noz-moscada.",
                "Casa da Baba", TipoProduto.Alimento, true, 4.40m, 11.90m, "7891000100018", "CDB-MOL-BRA-300", 63.03m, agora)),
            ["pesto200"] = await SeedData.UpsertProdutoAsync(context, new SeedData.ProdutoSeed(
                empresa.Id, catMolhos.Id, subPesto.Id, "Molho pesto 200ml",
                "Pesto fresco com manjericão, azeite extra virgem e castanhas.",
                "Casa da Baba", TipoProduto.Alimento, true, 5.20m, 14.90m, "7891000100019", "CDB-MOL-PES-200", 65.10m, agora))
        };

        var variacaoTalharimIntegral = await SeedData.UpsertVariacaoAsync(context, empresa.Id, produtos["talharim500"].Id,
            "Integral 500g", null, "500g", "Talharim integral de fermentação lenta", "CDB-TALH-500-INT", "7891000101011", agora);
        var variacaoRavioliFamilia = await SeedData.UpsertVariacaoAsync(context, empresa.Id, produtos["ravioliCarne"].Id,
            "Porcao familia 1kg", null, "1kg", "Ravioli de carne para refeições em família", "CDB-RAVI-CAR-1KG", "7891000102013", agora);
        var variacaoMolhoPicante = await SeedData.UpsertVariacaoAsync(context, empresa.Id, produtos["bolonhesa300"].Id,
            "Picante 300ml", null, "300ml", "Molho bolonhesa com pimenta calabresa", "CDB-MOL-BOL-300-PIC", "7891000103017", agora);

        foreach (var produto in produtos.Values)
        {
            await SeedData.UpsertCaracteristicaAsync(context, empresa.Id, produto.Id, "Conservacao", "Manter refrigerado entre 1°C e 5°C", null, 1, agora);
            await SeedData.UpsertCaracteristicaAsync(context, empresa.Id, produto.Id, "Origem", "Produção artesanal local - Casa da Baba", null, 2, agora);
        }

        await SeedData.UpsertCaracteristicaAsync(context, empresa.Id, produtos["talharim500"].Id, "Tipo de massa", "Farinha tipo 00 + ovos caipira", variacaoTalharimIntegral.Id, 3, agora);
        await SeedData.UpsertCaracteristicaAsync(context, empresa.Id, produtos["ravioliCarne"].Id, "Porcionamento", "Versão padrão 400g ou familiar 1kg", variacaoRavioliFamilia.Id, 3, agora);
        await SeedData.UpsertCaracteristicaAsync(context, empresa.Id, produtos["bolonhesa300"].Id, "Perfil de sabor", "Tradicional ou picante", variacaoMolhoPicante.Id, 3, agora);

        foreach (var produto in produtos.Values)
        {
            await SeedData.UpsertEmbalagemAsync(context, empresa.Id, produto.Id, "Bandeja PET selada", "Embalagem primária com filme termoencolhível", true, agora);
            await SeedData.UpsertEmbalagemAsync(context, empresa.Id, produto.Id, "Caixa master transporte", "Caixa de papelão ondulado para distribuição", false, agora);
        }

        await context.SaveChangesAsync();

        var itens = new Dictionary<string, SeedData.ItemSeedContext>
        {
            ["centro-talharim-l1"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornFarinha.Id, fornFarinha.Nome, produtos["talharim500"],
                null, "CDB-CEN-TALH-001", "LOT-TALH-20260409-A", 42, 6.20m, 14.90m, agora.AddDays(-1), agora.AddDays(7),
                "Lote de alto giro para varejo diário", 2.8m, agora),
            ["centro-talharim-l2"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornFarinha.Id, fornFarinha.Nome, produtos["talharim1kg"],
                null, "CDB-CEN-TALH-002", "LOT-TALH-20260407-B", 18, 11.80m, 26.90m, agora.AddDays(-3), agora.AddDays(8),
                "Pacotes 1kg para pedidos de restaurante", 1.4m, agora),
            ["centro-ravioli-carne"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornCarnes.Id, fornCarnes.Nome, produtos["ravioliCarne"],
                null, "CDB-CEN-RAVI-001", "LOT-RAVI-20260408-A", 22, 9.10m, 22.50m, agora.AddDays(-2), agora.AddDays(5),
                "Lote regular com reposição semanal", 1.7m, agora),
            ["centro-ravioli-ricota"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornLaticinios.Id, fornLaticinios.Nome, produtos["ravioliRicota"],
                null, "CDB-CEN-RAVI-002", "LOT-RAVI-20260406-C", 6, 8.70m, 21.90m, agora.AddDays(-4), agora.AddDays(2),
                "Lote próximo da validade para campanha de giro", 0.9m, agora),
            ["centro-nhoque"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornFarinha.Id, fornFarinha.Nome, produtos["nhoque500"],
                null, "CDB-CEN-NHOQ-001", "LOT-NHOQ-20260409-A", 30, 6.00m, 15.50m, agora.AddDays(-1), agora.AddDays(6),
                "Produto com giro elevado nas sextas e sábados", 3.2m, agora),
            ["centro-bolonhesa"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornCarnes.Id, fornCarnes.Nome, produtos["bolonhesa300"],
                variacaoMolhoPicante, "CDB-CEN-MBOL-001", "LOT-MBOL-20260405-A", 15, 4.90m, 12.90m, agora.AddDays(-5), agora.AddDays(20),
                "Molho para kits promocionais", 1.2m, agora),
            ["merc-talharim"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornFarinha.Id, fornFarinha.Nome, produtos["talharim500"],
                variacaoTalharimIntegral, "CDB-MER-TALH-001", "LOT-TALH-20260408-M1", 14, 6.40m, 15.40m, agora.AddDays(-2), agora.AddDays(7),
                "Lote de talharim integral para público fitness", 1.8m, agora),
            ["merc-nhoque"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornFarinha.Id, fornFarinha.Nome, produtos["nhoque500"],
                null, "CDB-MER-NHOQ-001", "LOT-NHOQ-20260407-M1", 10, 6.10m, 16.20m, agora.AddDays(-3), agora.AddDays(5),
                "Giro alto no horário de almoço", 2.4m, agora),
            ["merc-lasanha"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornFarinha.Id, fornFarinha.Nome, produtos["lasanha500"],
                null, "CDB-MER-LASA-001", "LOT-LASA-20260225-M1", 20, 5.40m, 13.90m, agora.AddDays(-45), agora.AddDays(10),
                "Item parado para ação de desconto", 0.2m, agora),
            ["merc-branco"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornLaticinios.Id, fornLaticinios.Nome, produtos["branco300"],
                null, "CDB-MER-MBRA-001", "LOT-MBRA-20260404-M1", 11, 4.40m, 11.90m, agora.AddDays(-6), agora.AddDays(18),
                "Molho branco com giro estável", 1.0m, agora),
            ["merc-pesto"] = await SeedData.EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornHortifruti.Id, fornHortifruti.Nome, produtos["pesto200"],
                null, "CDB-MER-MPES-001", "LOT-MPES-20260402-M1", 2, 5.20m, 14.90m, agora.AddDays(-8), agora.AddDays(4),
                "Estoque crítico aguardando reposição", 0.8m, agora)
        };

        await context.SaveChangesAsync();

        foreach (var itemContext in itens.Values.Where(x => x.CriadoAgora))
        {
            var natureza = itemContext.Item.FornecedorNome == "Producao Propria"
                ? NaturezaMovimentacaoEstoque.Producao
                : NaturezaMovimentacaoEstoque.Compra;

            await SeedData.EnsureEntradaMovimentacaoAsync(
                context,
                empresa.Id,
                itemContext.Item,
                natureza,
                itemContext.Item.QuantidadeInicial,
                itemContext.Item.CustoUnitario,
                itemContext.Item.EntradaEm,
                $"Entrada inicial seed - {itemContext.Item.CodigoInterno}",
                $"SEED-CDB-ENT-{itemContext.Item.CodigoInterno}");
        }

        await SeedData.EnsureReposicaoAsync(context, empresa.Id, itens["centro-talharim-l1"].Item, Quantidade.From(16),
            Dinheiro.FromDecimal(6.25m), agora.AddDays(-6), "Reposição para semana de alto fluxo", "SEED-CDB-REP-001");
        await SeedData.EnsureReposicaoAsync(context, empresa.Id, itens["centro-nhoque"].Item, Quantidade.From(12),
            Dinheiro.FromDecimal(6.10m), agora.AddDays(-5), "Reposição extra para promoção de nhoque", "SEED-CDB-REP-002");
        await SeedData.EnsureReposicaoAsync(context, empresa.Id, itens["merc-pesto"].Item, Quantidade.From(8),
            Dinheiro.FromDecimal(5.30m), agora.AddDays(-3), "Reposição emergencial de pesto", "SEED-CDB-REP-003");

        await SeedData.EnsureVendaAsync(context, empresa.Id, lojaCentro.Id, "NF-CDB-2026-0101", agora.AddDays(-13), "Venda balcão - combo massa + molho",
            new SeedData.VendaLinha(itens["centro-talharim-l1"].Item, null, Quantidade.From(6), Dinheiro.FromDecimal(14.90m)),
            new SeedData.VendaLinha(itens["centro-bolonhesa"].Item, variacaoMolhoPicante.Id, Quantidade.From(4), Dinheiro.FromDecimal(12.90m)));
        await SeedData.EnsureVendaAsync(context, empresa.Id, lojaMercadao.Id, "NF-CDB-2026-0102", agora.AddDays(-11), "Venda loja Mercadão",
            new SeedData.VendaLinha(itens["merc-nhoque"].Item, null, Quantidade.From(5), Dinheiro.FromDecimal(16.20m)),
            new SeedData.VendaLinha(itens["merc-branco"].Item, null, Quantidade.From(3), Dinheiro.FromDecimal(11.90m)));
        await SeedData.EnsureVendaAsync(context, empresa.Id, lojaCentro.Id, "NF-CDB-2026-0103", agora.AddDays(-8), "Venda rápida de hora do almoço",
            new SeedData.VendaLinha(itens["centro-ravioli-carne"].Item, null, Quantidade.From(7), Dinheiro.FromDecimal(22.50m)));
        await SeedData.EnsureVendaAsync(context, empresa.Id, lojaMercadao.Id, "NF-CDB-2026-0104", agora.AddDays(-6), "Venda promocional de sexta",
            new SeedData.VendaLinha(itens["merc-talharim"].Item, variacaoTalharimIntegral.Id, Quantidade.From(6), Dinheiro.FromDecimal(15.40m)),
            new SeedData.VendaLinha(itens["merc-pesto"].Item, null, Quantidade.From(4), Dinheiro.FromDecimal(14.90m)));
        await SeedData.EnsureVendaAsync(context, empresa.Id, lojaCentro.Id, "NF-CDB-2026-0105", agora.AddDays(-4), "Venda com foco em kits família",
            new SeedData.VendaLinha(itens["centro-talharim-l2"].Item, null, Quantidade.From(5), Dinheiro.FromDecimal(26.90m)),
            new SeedData.VendaLinha(itens["centro-ravioli-ricota"].Item, null, Quantidade.From(2), Dinheiro.FromDecimal(21.90m)));
        await SeedData.EnsureVendaAsync(context, empresa.Id, lojaMercadao.Id, "NF-CDB-2026-0106", agora.AddDays(-2), "Venda de alto giro no Mercadão",
            new SeedData.VendaLinha(itens["merc-nhoque"].Item, null, Quantidade.From(4), Dinheiro.FromDecimal(16.20m)),
            new SeedData.VendaLinha(itens["merc-branco"].Item, null, Quantidade.From(2), Dinheiro.FromDecimal(11.90m)));

        await SeedData.EnsureSaidaAvulsaAsync(context, empresa.Id, itens["centro-ravioli-ricota"].Item, Quantidade.From(1),
            Dinheiro.FromDecimal(8.70m), NaturezaMovimentacaoEstoque.Perda, agora.AddDays(-1),
            "Perda por dano de embalagem no refrigerador", "SEED-CDB-PER-001");
        await SeedData.EnsureSaidaAvulsaAsync(context, empresa.Id, itens["merc-lasanha"].Item, Quantidade.From(2),
            Dinheiro.FromDecimal(5.40m), NaturezaMovimentacaoEstoque.Prejuizo, agora.AddDays(-1),
            "Prejuízo por quebra de cadeia fria na madrugada", "SEED-CDB-PREJ-001");

        foreach (var item in itens.Values.Select(x => x.Item))
            item.RecalcularIndicadores(agora);

        await context.SaveChangesAsync();

        await SeedData.EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.EstoqueCritico,
            "Molho pesto 200ml no Mercadão está com estoque crítico e precisa de reposição imediata.", itens["merc-pesto"].Item.Id);
        await SeedData.EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.ValidadeProxima,
            "Ravioli de ricota e espinafre 400g no Centro vence em 2 dias. Priorizar giro.", itens["centro-ravioli-ricota"].Item.Id);
        await SeedData.EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.ProdutoParado,
            "Massa para lasanha fresca 500g no Mercadão está com baixo giro há mais de 30 dias.", itens["merc-lasanha"].Item.Id);
        await SeedData.EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.ReposicaoSugerida,
            "Nhoque de batata 500g apresenta giro alto no Centro. Sugestão de reposição para próximos 3 dias.", itens["centro-nhoque"].Item.Id);

        await context.SaveChangesAsync();

        // Enriquecimento adicional (clientes, pedidos, caixa, lotes, audit) — implementado em CasaDaBabaSeed.Enrichment.cs
        await EnriquecerAsync(context, empresa, lojaCentro, lojaMercadao,
            usuarioAdmin, usuarioGerente, usuarioOperadorCentro, usuarioOperadorMercadao,
            produtos, itens, fornFarinha, fornCarnes, fornLaticinios, fornHortifruti, agora, logger);

        logger.LogInformation("Seed tenant '{Empresa}' concluído.", EmpresaNome);
    }
}
