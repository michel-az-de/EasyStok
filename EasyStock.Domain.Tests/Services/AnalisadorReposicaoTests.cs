using EasyStock.Domain.Defaults;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Reposicao;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

public class AnalisadorReposicaoTests
{
    private readonly AnalisadorReposicao _sut = new();
    private static readonly OpcoesReposicao Opcoes = new(DiasCoberturaAlvo: 7, DiasHistoricoMinimoConfianca: 14);

    [Fact] // R1: níveis derivados na ordem produto -> categoria -> default global.
    public void U1_resolve_limiar_na_hierarquia_produto_categoria_default()
    {
        // Produto define mínima; crítica cai na categoria; nada na config -> default não usado aqui.
        var item = _sut.Avaliar(Snap(vigente: 1, prodMin: 100, catCrit: 20), Opcoes);

        item!.NivelMinimo.Should().Be(100);   // do produto
        item.NivelCritico.Should().Be(20);     // da categoria

        // Só config define -> usa config (produto/categoria nulos).
        var soConfig = _sut.Avaliar(Snap(vigente: 1, cfgMin: 30, cfgCrit: 8), Opcoes);
        soConfig!.NivelMinimo.Should().Be(30);
        soConfig.NivelCritico.Should().Be(8);

        // Nada definido -> defaults globais 5/2.
        var defaults = _sut.Avaliar(Snap(vigente: 1), Opcoes);
        defaults!.NivelMinimo.Should().Be(OperacionalDefaults.QuantidadeMinima);
        defaults.NivelCritico.Should().Be(OperacionalDefaults.QuantidadeCritica);
    }

    [Fact] // R1: a função opera sobre QuantidadeVigente (a infra exclui vencidos ao montar o snapshot).
    public void U2_vigente_zero_representa_sem_lotes_validos_e_vira_esgotado()
    {
        var item = _sut.Avaliar(Snap(vigente: 0, prodMin: 5, prodCrit: 2), Opcoes);

        item!.Estado.Should().Be(EstadoReposicao.Esgotado);
        item.QuantidadeVigente.Should().Be(0);
    }

    [Fact] // R2: item ESGOTADO (vigente 0) SEMPRE entra, sejam quais forem os limiares.
    public void U3_esgotado_sempre_entra()
    {
        var item = _sut.Avaliar(Snap(vigente: 0, prodMin: 1, prodCrit: 0), Opcoes);

        item.Should().NotBeNull();
        item!.Estado.Should().Be(EstadoReposicao.Esgotado);
    }

    [Fact] // R1/R6: config inválida (crítico >= mínimo) é normalizada e não gera estado contraditório.
    public void U4_config_invalida_e_normalizada()
    {
        // produtoCritica(8) >= produtoMinima(5) -> resolver rebaixa crítica para 4.
        var item = _sut.Avaliar(Snap(vigente: 6, prodMin: 5, prodCrit: 8), Opcoes);

        // vigente 6 > mínimo 5 -> não elegível (nem ATENCAO), e a crítica normalizou < mínima.
        item.Should().BeNull();

        var critico = _sut.Avaliar(Snap(vigente: 4, prodMin: 5, prodCrit: 8), Opcoes)!;
        critico.NivelCritico.Should().BeLessThan(critico.NivelMinimo);
        critico.NivelCritico.Should().Be(4);
        critico.Estado.Should().Be(EstadoReposicao.Critico); // 4 <= 4
    }

    [Fact] // R3: quantidade sugerida cobre lead_time + cobertura, descontando o vigente.
    public void U5_sugestao_cobre_leadtime_mais_cobertura()
    {
        // velocidade 2/dia, lead 3, cobertura 7, vigente 4, lote 1
        // necessário = 2*3 + 2*7 - 4 = 6 + 14 - 4 = 16
        var item = _sut.Avaliar(
            Snap(vigente: 4, prodMin: 20, prodCrit: 5, velocidade: 2m, diasHistorico: 30, leadTime: 3),
            Opcoes)!;

        item.Confianca.Should().Be(ConfiancaReposicao.Alta);
        item.QuantidadeSugerida.Should().Be(16m);
    }

    [Fact] // R3: histórico curto -> confiança Baixa + fallback repor-até-mínimo.
    public void U6_historico_curto_baixa_confianca_e_fallback()
    {
        // diasHistorico 5 < 14 -> Baixa; mínimo 5, vigente 2 -> sugere 3.
        var item = _sut.Avaliar(
            Snap(vigente: 2, prodMin: 5, prodCrit: 2, velocidade: 9m, diasHistorico: 5, leadTime: 10),
            Opcoes)!;

        item.Confianca.Should().Be(ConfiancaReposicao.Baixa);
        item.QuantidadeSugerida.Should().Be(3m); // 5 - 2, ignora a velocidade
    }

    [Fact] // R3: guarda de validade reduz a sugestão de item de giro baixo e seta o motivo.
    public void U7_guarda_de_validade_reduz_e_marca_motivo()
    {
        // velocidade 1/dia, validade 2 dias, vigente 1, lead 3, cobertura 7
        // sugestão normal = 1*3 + 1*7 - 1 = 9; teto por validade = 1*2 - 1 = 1 -> reduz para 1.
        var item = _sut.Avaliar(
            Snap(vigente: 1, prodMin: 20, prodCrit: 5, velocidade: 1m, diasHistorico: 30, leadTime: 3, validadeDias: 2),
            Opcoes)!;

        item.QuantidadeSugerida.Should().Be(1m);
        item.Motivo.Should().Be("giro baixo / risco de validade");
    }

    [Fact] // R5: ordenação ESGOTADO > CRITICO > ATENCAO; depois por dias-até-ruptura ascendente.
    public void U8_ordena_por_severidade_e_ruptura()
    {
        var atencao = Snap(vigente: 4, prodMin: 5, prodCrit: 2, nome: "Atencao");           // 4 < 5
        var critico = Snap(vigente: 2, prodMin: 5, prodCrit: 2, nome: "Critico");           // 2 <= 2
        var esgotado = Snap(vigente: 0, prodMin: 5, prodCrit: 2, nome: "Esgotado");
        var criticoRupturaCedo = Snap(vigente: 1, prodMin: 5, prodCrit: 2, velocidade: 2m, nome: "CriticoRupturaCedo"); // ruptura 0d

        var lista = _sut.Analisar(new[] { atencao, critico, esgotado, criticoRupturaCedo }, Opcoes);

        lista.Select(i => i.Nome).Should().ContainInOrder("Esgotado", "CriticoRupturaCedo", "Critico", "Atencao");
        lista[0].Estado.Should().Be(EstadoReposicao.Esgotado);
        lista[1].Estado.Should().Be(EstadoReposicao.Critico);   // ruptura 0d antes do crítico sem velocidade
        lista[3].Estado.Should().Be(EstadoReposicao.Atencao);
    }

    [Fact] // R4: cada item tem motivo legível, nunca string técnica/erro.
    public void U9_motivo_legivel()
    {
        _sut.Avaliar(Snap(vigente: 0, prodMin: 5, prodCrit: 2), Opcoes)!
            .Motivo.Should().Be("Acabou — sem unidades vigentes");

        _sut.Avaliar(Snap(vigente: 2, prodMin: 5, prodCrit: 2), Opcoes)!
            .Motivo.Should().Be("Abaixo do mínimo (2 de 5)");

        var comGiro = _sut.Avaliar(Snap(vigente: 3, prodMin: 10, prodCrit: 2, velocidade: 3m), Opcoes)!;
        comGiro.Motivo.Should().StartWith("Vende ~3/dia");

        // Nunca termo técnico de erro.
        foreach (var motivo in new[] { "Exception", "null", "DbUpdate", "Error", "stack" })
            comGiro.Motivo.Should().NotContainEquivalentOf(motivo);
    }

    [Fact] // R1/R6: paridade — o overload por valores brutos == o overload por entidades.
    public void Paridade_overload_int_vs_entidades()
    {
        var produto = new Produto { Nome = "P", QuantidadeMinima = 50, QuantidadeCritica = 15 };
        var categoria = new Categoria { Nome = "C", QuantidadeMinima = 30, QuantidadeCritica = 10 };
        var config = ConfiguracaoLoja.CriarPadrao(Guid.NewGuid());
        config.QuantidadeMinimaPadrao = 20;
        config.QuantidadeCriticaPadrao = 5;

        var porEntidades = LimiarEstoqueResolver.Resolver(produto, categoria, config);
        var porValores = LimiarEstoqueResolver.Resolver(50, 15, 30, 10, 20, 5);

        porValores.Should().Be(porEntidades);
    }

    [Fact] // R6 (property/grid): após normalização vale sempre critica <= minima, e critica < minima quando minima > 0.
    public void Normalizacao_mantem_invariante_critica_menor_que_minima()
    {
        for (var min = 0; min <= 12; min++)
        {
            for (var crit = 0; crit <= 12; crit++)
            {
                var r = LimiarEstoqueResolver.Resolver(min, crit, null, null, null, null);

                (r.QuantidadeCritica <= r.QuantidadeMinima).Should().BeTrue($"min={min}, crit={crit}");
                if (r.QuantidadeMinima > 0)
                    (r.QuantidadeCritica < r.QuantidadeMinima).Should().BeTrue($"min={min}, crit={crit}");
            }
        }
    }

    [Theory] // R6/fronteira: qty == NivelMinimo NÃO é baixo (< deliberado); qty == NivelCritico é CRITICO (<=).
    [InlineData(5, null)]                  // vigente == mínimo -> não elegível
    [InlineData(2, EstadoReposicao.Critico)] // vigente == crítico -> CRITICO
    [InlineData(4, EstadoReposicao.Atencao)] // entre crítico e mínimo -> ATENCAO
    public void Fronteira_operadores(decimal vigente, EstadoReposicao? esperado)
    {
        var item = _sut.Avaliar(Snap(vigente: vigente, prodMin: 5, prodCrit: 2), Opcoes);

        if (esperado is null) item.Should().BeNull();
        else item!.Estado.Should().Be(esperado);
    }

    [Fact] // R12: velocidade fracionária NÃO trunca para 0 (migração int->decimal da calculadora).
    public void Velocidade_fracionaria_nao_zera_sugestao()
    {
        // velocidade 0.4/dia, lead 1, cobertura 1, vigente 0 -> necessário = 0.4*2 = 0.8 -> teto lote 1 = 1.
        var item = _sut.Avaliar(
            Snap(vigente: 0, prodMin: 5, prodCrit: 2, velocidade: 0.4m, diasHistorico: 30, leadTime: 1),
            new OpcoesReposicao(DiasCoberturaAlvo: 1))!;

        item.QuantidadeSugerida.Should().BeGreaterThan(0m);
        item.QuantidadeSugerida.Should().Be(1m);
    }

    private static ProdutoReposicaoSnapshot Snap(
        decimal vigente,
        int? prodMin = null, int? prodCrit = null,
        int? catMin = null, int? catCrit = null,
        int? cfgMin = null, int? cfgCrit = null,
        decimal velocidade = 0m, int diasHistorico = 30,
        int leadTime = 0, int tamanhoLote = 1,
        int? validadeDias = null, string nome = "Produto X")
        => new(
            ProdutoId: Guid.NewGuid(), VariacaoId: null, Nome: nome,
            QuantidadeVigente: vigente,
            ProdutoMinima: prodMin, ProdutoCritica: prodCrit,
            CategoriaMinima: catMin, CategoriaCritica: catCrit,
            ConfigMinima: cfgMin, ConfigCritica: cfgCrit,
            VelocidadeMediaDia: velocidade, DiasHistoricoVelocidade: diasHistorico,
            LeadTimeDias: leadTime, TamanhoLote: tamanhoLote,
            ValidadeMediaDiasRestantes: validadeDias, FornecedorId: null);
}
