using BCrypt.Net;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Data;

public static class SeedData
{
    private const string EmpresaNome = "Casa da Baba";
    private const string EmpresaDocumento = "48.735.219/0001-62";
    private const long AdvisoryLockKey = 840240017320251L;

    public static async Task ExecutarAsync(IServiceProvider services, ILogger logger)
    {
        var context = services.GetRequiredService<EasyStockDbContext>();
        var agora = DateTime.UtcNow;

        await using var tx = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})");

        logger.LogInformation("Executando seed demo '{Empresa}'...", EmpresaNome);

        var plano = await UpsertPlanoAsync(context, agora);
        var empresa = await UpsertEmpresaAsync(context, agora);
        await UpsertAssinaturaAsync(context, empresa.Id, plano.Id, agora);

        var lojaCentro = await UpsertLojaAsync(context, empresa.Id, "Casa da Baba | Centro",
            "Unidade de producao e varejo de massas frescas", "(11) 3228-1101",
            "Rua Conselheiro Crispiniano, 141 - Centro, Sao Paulo/SP", agora);
        var lojaMercadao = await UpsertLojaAsync(context, empresa.Id, "Casa da Baba | Mercadao",
            "Ponto de distribuicao local com alto giro diario", "(11) 3326-4510",
            "Rua da Cantareira, 306 - Box 18, Centro Historico, Sao Paulo/SP", agora);

        await EnsureConfiguracaoLojaAsync(context, lojaCentro.Id);
        await EnsureConfiguracaoLojaAsync(context, lojaMercadao.Id);

        var perfilAdmin = await UpsertPerfilAsync(context, empresa.Id, "Admin", "Administrador com acesso total", NivelAcesso.Admin, agora);
        var perfilGerente = await UpsertPerfilAsync(context, empresa.Id, "Gerente", "Gestao operacional e analytics", NivelAcesso.Gerente, agora);
        var perfilOperador = await UpsertPerfilAsync(context, empresa.Id, "Operador", "Operacao diaria de estoque e vendas", NivelAcesso.Operador, agora);

        var usuarioAdmin = await UpsertUsuarioAsync(context, "Admin Casa da Baba", "admin@casadababa.demo", "admin123");
        var usuarioGerente = await UpsertUsuarioAsync(context, "Gerente Casa da Baba", "gerente@casadababa.demo", "gerente123");
        var usuarioOperadorCentro = await UpsertUsuarioAsync(context, "Operador Centro", "operador.centro@casadababa.demo", "operador123");
        var usuarioOperadorMercadao = await UpsertUsuarioAsync(context, "Operador Mercadao", "operador.mercadao@casadababa.demo", "operador123");

        await EnsureUsuarioEmpresaAsync(context, usuarioAdmin.Id, empresa.Id, agora);
        await EnsureUsuarioEmpresaAsync(context, usuarioGerente.Id, empresa.Id, agora);
        await EnsureUsuarioEmpresaAsync(context, usuarioOperadorCentro.Id, empresa.Id, agora);
        await EnsureUsuarioEmpresaAsync(context, usuarioOperadorMercadao.Id, empresa.Id, agora);

        await EnsureUsuarioPerfilAsync(context, usuarioAdmin.Id, empresa.Id, perfilAdmin.Id, null, agora);
        await EnsureUsuarioPerfilAsync(context, usuarioGerente.Id, empresa.Id, perfilGerente.Id, lojaCentro.Id, agora);
        await EnsureUsuarioPerfilAsync(context, usuarioGerente.Id, empresa.Id, perfilGerente.Id, lojaMercadao.Id, agora);
        await EnsureUsuarioPerfilAsync(context, usuarioOperadorCentro.Id, empresa.Id, perfilOperador.Id, lojaCentro.Id, agora);
        await EnsureUsuarioPerfilAsync(context, usuarioOperadorMercadao.Id, empresa.Id, perfilOperador.Id, lojaMercadao.Id, agora);

        var catMassas = await UpsertCategoriaAsync(context, empresa.Id, "Massas Frescas", "Massas artesanais refrigeradas de alta rotatividade", null, agora);
        var catMolhos = await UpsertCategoriaAsync(context, empresa.Id, "Molhos", "Molhos artesanais para acompanhar massas", null, agora);
        var catRecheiosBases = await UpsertCategoriaAsync(context, empresa.Id, "Recheios e Bases", "Bases frescas para producao interna", null, agora);
        var catEmbalagens = await UpsertCategoriaAsync(context, empresa.Id, "Embalagens", "Materiais para fracionamento e venda", null, agora);
        var catIngredientes = await UpsertCategoriaAsync(context, empresa.Id, "Ingredientes", "Insumos para producao diaria", null, agora);

        var subTalharim = await UpsertCategoriaAsync(context, empresa.Id, "Talharim", "Linha de talharim fresco", catMassas.Id, agora);
        var subRavioli = await UpsertCategoriaAsync(context, empresa.Id, "Ravioli", "Massas recheadas artesanais", catMassas.Id, agora);
        var subNhoque = await UpsertCategoriaAsync(context, empresa.Id, "Nhoque", "Nhoque fresco de preparo rapido", catMassas.Id, agora);
        var subLasanha = await UpsertCategoriaAsync(context, empresa.Id, "Lasanha", "Folhas frescas para montagem de lasanha", catMassas.Id, agora);
        var subVermelho = await UpsertCategoriaAsync(context, empresa.Id, "Vermelho", "Molhos a base de tomate", catMolhos.Id, agora);
        var subBranco = await UpsertCategoriaAsync(context, empresa.Id, "Branco", "Molhos cremosos", catMolhos.Id, agora);
        var subPesto = await UpsertCategoriaAsync(context, empresa.Id, "Pesto", "Molhos verdes frescos", catMolhos.Id, agora);

        var fornFarinha = await UpsertFornecedorAsync(context, empresa.Id, "Moinho Santa Italia",
            "22.418.901/0001-06", "vendas@moinhosantaitalia.com.br", "(11) 3504-8801", "Marcos Silveira",
            "Farinha de trigo especial e semola", "Nacional", 5, "https://moinhosantaitalia.com.br",
            "R$ 800,00", "CIF", "Fornecedor principal de farinha tipo 00 para massas frescas");
        var fornOvos = await UpsertFornecedorAsync(context, empresa.Id, "Granja Ouro Caipira",
            "31.209.665/0001-51", "comercial@ourocaipira.com.br", "(11) 4962-2210", "Elisa Pacheco",
            "Ovos frescos", "Nacional", 2, "https://ourocaipira.com.br",
            "R$ 300,00", "CIF", "Entrega diaria para manter qualidade da massa fresca");
        var fornLaticinios = await UpsertFornecedorAsync(context, empresa.Id, "Laticinios Serra Alta",
            "07.843.117/0001-90", "pedidos@serraalta.com.br", "(11) 3891-6120", "Ricardo Moreira",
            "Queijos e derivados", "Nacional", 3, "https://serraalta.com.br",
            "R$ 400,00", "FOB", "Ricota, creme de leite fresco e parmesao ralado");
        var fornCarnes = await UpsertFornecedorAsync(context, empresa.Id, "Frigorifico Bela Arte",
            "12.776.100/0001-32", "atendimento@belaartefrigorifico.com.br", "(11) 4227-9000", "Fernanda Lopes",
            "Carnes e recheios", "Nacional", 4, "https://belaartefrigorifico.com.br",
            "R$ 700,00", "CIF", "Cortes bovinos e suinos para recheios e molho bolonhesa");
        var fornEmbalagens = await UpsertFornecedorAsync(context, empresa.Id, "PackFresh Embalagens",
            "58.190.334/0001-18", "vendas@packfresh.com.br", "(11) 4125-4477", "Guilherme Neri",
            "Embalagens alimenticias", "Nacional", 6, "https://packfresh.com.br",
            "R$ 500,00", "CIF", "Bandejas PET, selos termicos e potes para molhos");
        var fornHortifruti = await UpsertFornecedorAsync(context, empresa.Id, "HortiVerde Premium",
            "44.920.701/0001-73", "pedidos@hortiverde.com.br", "(11) 3822-9191", "Patricia Campos",
            "Hortifruti", "Nacional", 1, "https://hortiverde.com.br",
            "R$ 250,00", "FOB", "Espinafre, manjericao, tomate italiano e ervas frescas");

        var produtos = new Dictionary<string, Produto>
        {
            ["talharim500"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMassas.Id, subTalharim.Id, "Talharim fresco 500g",
                "Talharim artesanal refrigerado, produzido diariamente com farinha tipo 00 e ovos frescos.",
                "Casa da Baba", TipoProduto.Alimento, true, 6.20m, 14.90m, "7891000100011", "CDB-TALH-500", 58.35m, agora)),
            ["talharim1kg"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMassas.Id, subTalharim.Id, "Talharim fresco 1kg",
                "Versao familiar do talharim fresco, ideal para restaurantes e consumo de fim de semana.",
                "Casa da Baba", TipoProduto.Alimento, true, 11.80m, 26.90m, "7891000100012", "CDB-TALH-1KG", 56.07m, agora)),
            ["ravioliCarne"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMassas.Id, subRavioli.Id, "Ravioli de carne 400g",
                "Ravioli fresco recheado com carne bovina e temperos artesanais.",
                "Casa da Baba", TipoProduto.Alimento, true, 9.10m, 22.50m, "7891000100013", "CDB-RAVI-CAR-400", 59.56m, agora)),
            ["ravioliRicota"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMassas.Id, subRavioli.Id, "Ravioli de ricota e espinafre 400g",
                "Ravioli artesanal com recheio leve de ricota fresca e espinafre selecionado.",
                "Casa da Baba", TipoProduto.Alimento, true, 8.70m, 21.90m, "7891000100014", "CDB-RAVI-RIC-400", 60.27m, agora)),
            ["nhoque500"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMassas.Id, subNhoque.Id, "Nhoque de batata 500g",
                "Nhoque fresco com batata asterix e farinha de trigo premium.",
                "Casa da Baba", TipoProduto.Alimento, true, 6.00m, 15.50m, "7891000100015", "CDB-NHOQ-500", 61.29m, agora)),
            ["lasanha500"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMassas.Id, subLasanha.Id, "Massa para lasanha fresca 500g",
                "Folhas de massa fresca para montagem de lasanhas artesanais.",
                "Casa da Baba", TipoProduto.Alimento, true, 5.40m, 13.90m, "7891000100016", "CDB-LASA-500", 61.15m, agora)),
            ["bolonhesa300"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMolhos.Id, subVermelho.Id, "Molho bolonhesa 300ml",
                "Molho bolonhesa com tomate italiano, carne bovina e cozimento lento.",
                "Casa da Baba", TipoProduto.Alimento, true, 4.90m, 12.90m, "7891000100017", "CDB-MOL-BOL-300", 62.02m, agora)),
            ["branco300"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMolhos.Id, subBranco.Id, "Molho branco 300ml",
                "Molho branco cremoso com leite fresco e noz-moscada.",
                "Casa da Baba", TipoProduto.Alimento, true, 4.40m, 11.90m, "7891000100018", "CDB-MOL-BRA-300", 63.03m, agora)),
            ["pesto200"] = await UpsertProdutoAsync(context, new ProdutoSeed(
                empresa.Id, catMolhos.Id, subPesto.Id, "Molho pesto 200ml",
                "Pesto fresco com manjericao, azeite extra virgem e castanhas.",
                "Casa da Baba", TipoProduto.Alimento, true, 5.20m, 14.90m, "7891000100019", "CDB-MOL-PES-200", 65.10m, agora))
        };

        var variacaoTalharimIntegral = await UpsertVariacaoAsync(context, empresa.Id, produtos["talharim500"].Id,
            "Integral 500g", null, "500g", "Talharim integral de fermentacao lenta", "CDB-TALH-500-INT", "7891000101011", agora);
        var variacaoRavioliFamilia = await UpsertVariacaoAsync(context, empresa.Id, produtos["ravioliCarne"].Id,
            "Porcao familia 1kg", null, "1kg", "Ravioli de carne para refeicoes em familia", "CDB-RAVI-CAR-1KG", "7891000102013", agora);
        var variacaoMolhoPicante = await UpsertVariacaoAsync(context, empresa.Id, produtos["bolonhesa300"].Id,
            "Picante 300ml", null, "300ml", "Molho bolonhesa com pimenta calabresa", "CDB-MOL-BOL-300-PIC", "7891000103017", agora);

        foreach (var produto in produtos.Values)
        {
            await UpsertCaracteristicaAsync(context, empresa.Id, produto.Id, "Conservacao", "Manter refrigerado entre 1°C e 5°C", null, 1, agora);
            await UpsertCaracteristicaAsync(context, empresa.Id, produto.Id, "Origem", "Producao artesanal local - Casa da Baba", null, 2, agora);
        }

        await UpsertCaracteristicaAsync(context, empresa.Id, produtos["talharim500"].Id, "Tipo de massa", "Farinha tipo 00 + ovos caipira", variacaoTalharimIntegral.Id, 3, agora);
        await UpsertCaracteristicaAsync(context, empresa.Id, produtos["ravioliCarne"].Id, "Porcionamento", "Versao padrao 400g ou familiar 1kg", variacaoRavioliFamilia.Id, 3, agora);
        await UpsertCaracteristicaAsync(context, empresa.Id, produtos["bolonhesa300"].Id, "Perfil de sabor", "Tradicional ou picante", variacaoMolhoPicante.Id, 3, agora);

        foreach (var produto in produtos.Values)
        {
            await UpsertEmbalagemAsync(context, empresa.Id, produto.Id, "Bandeja PET selada", "Embalagem primaria com filme termoencolhivel", true, agora);
            await UpsertEmbalagemAsync(context, empresa.Id, produto.Id, "Caixa master transporte", "Caixa de papelao ondulado para distribuicao", false, agora);
        }

        await context.SaveChangesAsync();

        var itens = new Dictionary<string, ItemSeedContext>
        {
            ["centro-talharim-l1"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornFarinha.Id, fornFarinha.Nome, produtos["talharim500"],
                null, "CDB-CEN-TALH-001", "LOT-TALH-20260409-A", 42, 6.20m, 14.90m, agora.AddDays(-1), agora.AddDays(7),
                "Lote de alto giro para varejo diario", 2.8m),
            ["centro-talharim-l2"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornFarinha.Id, fornFarinha.Nome, produtos["talharim1kg"],
                null, "CDB-CEN-TALH-002", "LOT-TALH-20260407-B", 18, 11.80m, 26.90m, agora.AddDays(-3), agora.AddDays(8),
                "Pacotes 1kg para pedidos de restaurante", 1.4m),
            ["centro-ravioli-carne"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornCarnes.Id, fornCarnes.Nome, produtos["ravioliCarne"],
                null, "CDB-CEN-RAVI-001", "LOT-RAVI-20260408-A", 22, 9.10m, 22.50m, agora.AddDays(-2), agora.AddDays(5),
                "Lote regular com reposicao semanal", 1.7m),
            ["centro-ravioli-ricota"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornLaticinios.Id, fornLaticinios.Nome, produtos["ravioliRicota"],
                null, "CDB-CEN-RAVI-002", "LOT-RAVI-20260406-C", 6, 8.70m, 21.90m, agora.AddDays(-4), agora.AddDays(2),
                "Lote proximo da validade para campanha de giro", 0.9m),
            ["centro-nhoque"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornFarinha.Id, fornFarinha.Nome, produtos["nhoque500"],
                null, "CDB-CEN-NHOQ-001", "LOT-NHOQ-20260409-A", 30, 6.00m, 15.50m, agora.AddDays(-1), agora.AddDays(6),
                "Produto com giro elevado nas sextas e sabados", 3.2m),
            ["centro-bolonhesa"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaCentro.Id, fornCarnes.Id, fornCarnes.Nome, produtos["bolonhesa300"],
                variacaoMolhoPicante, "CDB-CEN-MBOL-001", "LOT-MBOL-20260405-A", 15, 4.90m, 12.90m, agora.AddDays(-5), agora.AddDays(20),
                "Molho para kits promocionais", 1.2m),
            ["merc-talharim"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornFarinha.Id, fornFarinha.Nome, produtos["talharim500"],
                variacaoTalharimIntegral, "CDB-MER-TALH-001", "LOT-TALH-20260408-M1", 14, 6.40m, 15.40m, agora.AddDays(-2), agora.AddDays(7),
                "Lote de talharim integral para publico fitness", 1.8m),
            ["merc-nhoque"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornFarinha.Id, fornFarinha.Nome, produtos["nhoque500"],
                null, "CDB-MER-NHOQ-001", "LOT-NHOQ-20260407-M1", 10, 6.10m, 16.20m, agora.AddDays(-3), agora.AddDays(5),
                "Giro alto no horario de almoco", 2.4m),
            ["merc-lasanha"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornFarinha.Id, fornFarinha.Nome, produtos["lasanha500"],
                null, "CDB-MER-LASA-001", "LOT-LASA-20260225-M1", 20, 5.40m, 13.90m, agora.AddDays(-45), agora.AddDays(10),
                "Item parado para acao de desconto", 0.2m),
            ["merc-branco"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornLaticinios.Id, fornLaticinios.Nome, produtos["branco300"],
                null, "CDB-MER-MBRA-001", "LOT-MBRA-20260404-M1", 11, 4.40m, 11.90m, agora.AddDays(-6), agora.AddDays(18),
                "Molho branco com giro estavel", 1.0m),
            ["merc-pesto"] = await EnsureItemEstoqueAsync(context, empresa.Id, lojaMercadao.Id, fornHortifruti.Id, fornHortifruti.Nome, produtos["pesto200"],
                null, "CDB-MER-MPES-001", "LOT-MPES-20260402-M1", 2, 5.20m, 14.90m, agora.AddDays(-8), agora.AddDays(4),
                "Estoque critico aguardando reposicao", 0.8m)
        };

        await context.SaveChangesAsync();

        foreach (var itemContext in itens.Values.Where(x => x.CriadoAgora))
        {
            var natureza = itemContext.Item.FornecedorNome == "Producao Propria"
                ? NaturezaMovimentacaoEstoque.Producao
                : NaturezaMovimentacaoEstoque.Compra;

            await EnsureEntradaMovimentacaoAsync(
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

        await EnsureReposicaoAsync(context, empresa.Id, itens["centro-talharim-l1"].Item, Quantidade.From(16),
            Dinheiro.FromDecimal(6.25m), agora.AddDays(-6), "Reposicao para semana de alto fluxo", "SEED-CDB-REP-001");
        await EnsureReposicaoAsync(context, empresa.Id, itens["centro-nhoque"].Item, Quantidade.From(12),
            Dinheiro.FromDecimal(6.10m), agora.AddDays(-5), "Reposicao extra para promocao de nhoque", "SEED-CDB-REP-002");
        await EnsureReposicaoAsync(context, empresa.Id, itens["merc-pesto"].Item, Quantidade.From(8),
            Dinheiro.FromDecimal(5.30m), agora.AddDays(-3), "Reposicao emergencial de pesto", "SEED-CDB-REP-003");

        await EnsureVendaAsync(context, empresa.Id, lojaCentro.Id, "NF-CDB-2026-0101", agora.AddDays(-13), "Venda balcão - combo massa + molho",
            new VendaLinha(itens["centro-talharim-l1"].Item, null, Quantidade.From(6), Dinheiro.FromDecimal(14.90m)),
            new VendaLinha(itens["centro-bolonhesa"].Item, variacaoMolhoPicante.Id, Quantidade.From(4), Dinheiro.FromDecimal(12.90m)));
        await EnsureVendaAsync(context, empresa.Id, lojaMercadao.Id, "NF-CDB-2026-0102", agora.AddDays(-11), "Venda loja Mercadao",
            new VendaLinha(itens["merc-nhoque"].Item, null, Quantidade.From(5), Dinheiro.FromDecimal(16.20m)),
            new VendaLinha(itens["merc-branco"].Item, null, Quantidade.From(3), Dinheiro.FromDecimal(11.90m)));
        await EnsureVendaAsync(context, empresa.Id, lojaCentro.Id, "NF-CDB-2026-0103", agora.AddDays(-8), "Venda rapida de hora do almoco",
            new VendaLinha(itens["centro-ravioli-carne"].Item, null, Quantidade.From(7), Dinheiro.FromDecimal(22.50m)));
        await EnsureVendaAsync(context, empresa.Id, lojaMercadao.Id, "NF-CDB-2026-0104", agora.AddDays(-6), "Venda promocional de sexta",
            new VendaLinha(itens["merc-talharim"].Item, variacaoTalharimIntegral.Id, Quantidade.From(6), Dinheiro.FromDecimal(15.40m)),
            new VendaLinha(itens["merc-pesto"].Item, null, Quantidade.From(4), Dinheiro.FromDecimal(14.90m)));
        await EnsureVendaAsync(context, empresa.Id, lojaCentro.Id, "NF-CDB-2026-0105", agora.AddDays(-4), "Venda com foco em kits familia",
            new VendaLinha(itens["centro-talharim-l2"].Item, null, Quantidade.From(5), Dinheiro.FromDecimal(26.90m)),
            new VendaLinha(itens["centro-ravioli-ricota"].Item, null, Quantidade.From(2), Dinheiro.FromDecimal(21.90m)));
        await EnsureVendaAsync(context, empresa.Id, lojaMercadao.Id, "NF-CDB-2026-0106", agora.AddDays(-2), "Venda de alto giro no Mercadao",
            new VendaLinha(itens["merc-nhoque"].Item, null, Quantidade.From(4), Dinheiro.FromDecimal(16.20m)),
            new VendaLinha(itens["merc-branco"].Item, null, Quantidade.From(2), Dinheiro.FromDecimal(11.90m)));

        await EnsureSaidaAvulsaAsync(context, empresa.Id, itens["centro-ravioli-ricota"].Item, Quantidade.From(1),
            Dinheiro.FromDecimal(8.70m), NaturezaMovimentacaoEstoque.Perda, agora.AddDays(-1),
            "Perda por dano de embalagem no refrigerador", "SEED-CDB-PER-001");
        await EnsureSaidaAvulsaAsync(context, empresa.Id, itens["merc-lasanha"].Item, Quantidade.From(2),
            Dinheiro.FromDecimal(5.40m), NaturezaMovimentacaoEstoque.Prejuizo, agora.AddDays(-1),
            "Prejuizo por quebra de cadeia fria na madrugada", "SEED-CDB-PREJ-001");

        foreach (var item in itens.Values.Select(x => x.Item))
            item.RecalcularIndicadores(agora);

        await context.SaveChangesAsync();

        await EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.EstoqueCritico,
            "Molho pesto 200ml no Mercadao esta com estoque critico e precisa de reposicao imediata.", itens["merc-pesto"].Item.Id);
        await EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.ValidadeProxima,
            "Ravioli de ricota e espinafre 400g no Centro vence em 2 dias. Priorizar giro.", itens["centro-ravioli-ricota"].Item.Id);
        await EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.ProdutoParado,
            "Massa para lasanha fresca 500g no Mercadao esta com baixo giro ha mais de 30 dias.", itens["merc-lasanha"].Item.Id);
        await EnsureNotificacaoAsync(context, empresa.Id, TipoAlertaEstoque.ReposicaoSugerida,
            "Nhoque de batata 500g apresenta giro alto no Centro. Sugestao de reposicao para proximos 3 dias.", itens["centro-nhoque"].Item.Id);

        await context.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation(
            "Seed demo '{Empresa}' concluido: {Lojas} lojas, {Produtos} produtos, {Fornecedores} fornecedores e credenciais demo prontas.",
            empresa.Nome,
            2,
            produtos.Count,
            6);
    }

    private static async Task<Plano> UpsertPlanoAsync(EasyStockDbContext context, DateTime agora)
    {
        var plano = await context.Planos.FirstOrDefaultAsync(p => p.Nome == "Demo Comercial Casa da Baba");
        if (plano is null)
        {
            plano = new Plano
            {
                Id = Guid.NewGuid(),
                Nome = "Demo Comercial Casa da Baba",
                CriadoEm = agora
            };
            context.Planos.Add(plano);
        }

        plano.Descricao = "Plano de demonstracao para operacao de massas frescas artesanais";
        plano.LimiteLojas = Plano.SemLimite;
        plano.LimiteUsuarios = Plano.SemLimite;
        plano.LimiteProdutos = Plano.SemLimite;
        plano.LimiteGeracoesIaMensais = Plano.SemLimite;
        plano.PrecoMensal = 149.90m;
        plano.Ativo = true;

        return plano;
    }

    private static async Task<Empresa> UpsertEmpresaAsync(EasyStockDbContext context, DateTime agora)
    {
        var empresa = await context.Empresas.FirstOrDefaultAsync(e => e.Documento == EmpresaDocumento)
                     ?? await context.Empresas.FirstOrDefaultAsync(e => e.Nome == EmpresaNome);

        if (empresa is null)
        {
            empresa = Empresa.Criar(EmpresaNome, EmpresaDocumento);
            context.Empresas.Add(empresa);
        }

        empresa.Nome = EmpresaNome;
        empresa.Documento = EmpresaDocumento;
        empresa.AlteradoEm = agora;

        return empresa;
    }

    private static async Task UpsertAssinaturaAsync(EasyStockDbContext context, Guid empresaId, Guid planoId, DateTime agora)
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
        assinatura.DataInicio = agora.Date.AddDays(-90);
        assinatura.DataFim = agora.Date.AddYears(1);
        assinatura.Status = StatusAssinatura.Ativa;
        assinatura.AlteradoEm = agora;
    }

    private static async Task<Loja> UpsertLojaAsync(
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

    private static async Task EnsureConfiguracaoLojaAsync(EasyStockDbContext context, Guid lojaId)
    {
        var configuracaoExiste = await context.ConfiguracoesLoja.AnyAsync(c => c.LojaId == lojaId);
        if (!configuracaoExiste)
            context.ConfiguracoesLoja.Add(ConfiguracaoLoja.CriarPadrao(lojaId));
    }

    private static async Task<Perfil> UpsertPerfilAsync(
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

    private static async Task<Usuario> UpsertUsuarioAsync(EasyStockDbContext context, string nome, string email, string senhaPlana)
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
            usuario.AlteradoEm = DateTime.UtcNow;
        }

        return usuario;
    }

    private static async Task EnsureUsuarioEmpresaAsync(EasyStockDbContext context, Guid usuarioId, Guid empresaId, DateTime agora)
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

    private static async Task EnsureUsuarioPerfilAsync(
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

    private static async Task<Categoria> UpsertCategoriaAsync(
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

    private static async Task<Fornecedor> UpsertFornecedorAsync(
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

    private static async Task<Produto> UpsertProdutoAsync(EasyStockDbContext context, ProdutoSeed seed)
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

    private static async Task<ProdutoVariacao> UpsertVariacaoAsync(
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

    private static async Task UpsertCaracteristicaAsync(
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

    private static async Task UpsertEmbalagemAsync(
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

    private static async Task<ItemSeedContext> EnsureItemEstoqueAsync(
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
        decimal velocidadeSaidaDiaria)
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
        item.AtualizarVelocidadeSaida(velocidadeSaidaDiaria, DateTime.UtcNow);
        item.RecalcularIndicadores(DateTime.UtcNow);

        return new ItemSeedContext(item, criadoAgora);
    }

    private static async Task EnsureEntradaMovimentacaoAsync(
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

    private static async Task EnsureReposicaoAsync(
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

    private static async Task EnsureVendaAsync(
        EasyStockDbContext context,
        Guid empresaId,
        Guid lojaId,
        string numeroNotaFiscal,
        DateTime dataVenda,
        string observacao,
        params VendaLinha[] linhas)
    {
        var vendaExiste = await context.Vendas.AnyAsync(v => v.EmpresaId == empresaId && v.NumeroNotaFiscal == numeroNotaFiscal);
        if (vendaExiste)
            return;

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
    }

    private static async Task EnsureSaidaAvulsaAsync(
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

    private static async Task EnsureNotificacaoAsync(
        EasyStockDbContext context,
        Guid empresaId,
        TipoAlertaEstoque tipo,
        string mensagem,
        Guid? referenciaId)
    {
        var notificacao = await context.Notificacoes.FirstOrDefaultAsync(n =>
            n.EmpresaId == empresaId &&
            n.TipoAlerta == tipo &&
            n.ReferenciaId == referenciaId);

        if (notificacao is null)
        {
            context.Notificacoes.Add(Notificacao.Criar(empresaId, tipo, mensagem, referenciaId));
            return;
        }

        notificacao.AtualizarMensagem(mensagem);
        notificacao.Lida = false;
        notificacao.LidaEm = null;
    }

    private sealed record ProdutoSeed(
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

    private sealed record ItemSeedContext(ItemEstoque Item, bool CriadoAgora);
    private sealed record VendaLinha(ItemEstoque ItemEstoque, Guid? ProdutoVariacaoId, Quantidade Quantidade, Dinheiro PrecoUnitario);
}
