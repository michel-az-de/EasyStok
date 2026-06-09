using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="CardapioItem"/>.
///
/// CardapioItem é a ponte entre <see cref="EasyStock.Domain.Entities.Storefront.Storefront"/>
/// e <see cref="Produto"/> (ERP). Cada item adiciona metadata pública
/// (foto, descrição, tag, filtros) e pode sobrescrever o preço do produto.
///
/// Cobertura: factory + invariantes (tag enum + FiltrosJson) + transições
/// (visível/disponível) + preço efetivo (override OR fallback) + ordem fracionária.
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class CardapioItemTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static Produto NovoProdutoValido(decimal? precoReferencia = 25m)
    {
        return new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            CategoriaId = Guid.NewGuid(),
            Nome = "Capeletti de Frango",
            Tipo = TipoProduto.Alimento,
            PrecoReferencia = precoReferencia is null ? null : Dinheiro.FromDecimal(precoReferencia.Value),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        };
    }

    private static CardapioItem NovoItemValido(
        Guid? storefrontId = null,
        Produto? produto = null)
    {
        var prod = produto ?? NovoProdutoValido();
        var item = CardapioItem.CriarAPartirDeProduto(
            storefrontId: storefrontId ?? Guid.NewGuid(),
            produto: prod);
        // PrecoEfetivo()/Nome leem a navegacao Produto (carregada via Include em producao);
        // popular a nav no teste reflete producao e exercita o fallback PrecoReferencia.
        item.Produto = prod;
        return item;
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void CriarAPartirDeProduto_define_estado_inicial_seguro()
    {
        var storefrontId = Guid.NewGuid();
        var produto = NovoProdutoValido(precoReferencia: 25m);

        var item = CardapioItem.CriarAPartirDeProduto(storefrontId, produto);

        item.Id.Should().NotBeEmpty();
        item.StorefrontId.Should().Be(storefrontId);
        item.ProdutoId.Should().Be(produto.Id);

        item.Visivel.Should().BeFalse(
            "Babá aprova manualmente cada item depois de importar — default oculto");
        item.Disponivel.Should().BeTrue("disponível por default — fica esgotado só via MarcarEsgotado()");

        item.PrecoStorefront.Should().BeNull("sem override inicial — usa PrecoReferencia do Produto");
        item.Tag.Should().BeNull();
        item.FiltrosJson.Should().Be("[]", "default vazio mas JSON array válido");

        item.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        item.AlteradoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void CriarAPartirDeProduto_rejeita_storefront_id_vazio()
    {
        var produto = NovoProdutoValido();

        var act = () => CardapioItem.CriarAPartirDeProduto(Guid.Empty, produto);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Storefront*");
    }

    [Fact]
    public void CriarAPartirDeProduto_rejeita_produto_null()
    {
        var act = () => CardapioItem.CriarAPartirDeProduto(Guid.NewGuid(), produto: null!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Produto*");
    }

    [Fact]
    public void CriarAPartirDeProduto_rejeita_produto_sem_id()
    {
        var produto = NovoProdutoValido();
        produto.Id = Guid.Empty;

        var act = () => CardapioItem.CriarAPartirDeProduto(Guid.NewGuid(), produto);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Produto*");
    }

    // ── PrecoEfetivo (override OR fallback Produto OR 0) ───────────────

    [Fact]
    public void PrecoEfetivo_usa_PrecoStorefront_quando_override_definido()
    {
        var produto = NovoProdutoValido(precoReferencia: 25m);
        var item = NovoItemValido(produto: produto);
        item.AtualizarMetadata(precoStorefront: 19.90m);

        item.PrecoEfetivo().Should().Be(19.90m,
            "override do storefront tem prioridade sobre preço de referência do produto");
    }

    [Fact]
    public void PrecoEfetivo_usa_PrecoReferencia_quando_sem_override()
    {
        var produto = NovoProdutoValido(precoReferencia: 25m);
        var item = NovoItemValido(produto: produto);

        item.PrecoEfetivo().Should().Be(25m);
    }

    [Fact]
    public void PrecoEfetivo_retorna_zero_quando_sem_override_e_produto_sem_preco_referencia()
    {
        var produto = NovoProdutoValido(precoReferencia: null);
        var item = NovoItemValido(produto: produto);

        item.PrecoEfetivo().Should().Be(0m, "produto sem preço base é caso de borda, mas não pode crashar");
    }

    // ── Visibilidade ──────────────────────────────────────────────────

    [Fact]
    public void TornarVisivel_altera_estado_e_atualiza_data()
    {
        var item = NovoItemValido();
        var antes = item.AlteradoEm;
        Thread.Sleep(10);

        item.TornarVisivel();

        item.Visivel.Should().BeTrue();
        item.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void Ocultar_eh_idempotente()
    {
        var item = NovoItemValido();
        // já está oculto por default
        var antes = item.AlteradoEm;
        Thread.Sleep(10);

        item.Ocultar();

        item.Visivel.Should().BeFalse();
        item.AlteradoEm.Should().Be(antes, "ocultar item já oculto não modifica timestamps");
    }

    // ── Disponibilidade ───────────────────────────────────────────────

    [Fact]
    public void MarcarEsgotado_torna_indisponivel_e_atualiza_data()
    {
        var item = NovoItemValido();
        var antes = item.AlteradoEm;
        Thread.Sleep(10);

        item.MarcarEsgotado();

        item.Disponivel.Should().BeFalse();
        item.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void MarcarDisponivel_apos_esgotado_volta_disponivel()
    {
        var item = NovoItemValido();
        item.MarcarEsgotado();
        item.Disponivel.Should().BeFalse();

        Thread.Sleep(10);
        item.MarcarDisponivel();

        item.Disponivel.Should().BeTrue();
    }

    // ── Tag (enum string) ─────────────────────────────────────────────

    [Theory]
    [InlineData("assinatura")]
    [InlineData("novo")]
    [InlineData("vegetariano")]
    public void AtualizarMetadata_aceita_tags_permitidas(string tag)
    {
        var item = NovoItemValido();

        var act = () => item.AtualizarMetadata(tag: tag);

        act.Should().NotThrow();
        item.Tag.Should().Be(tag);
    }

    [Theory]
    [InlineData("destaque")] // não está na lista
    [InlineData("vegano")]   // typo proibido — só vegetariano
    [InlineData("foo bar")]  // qualquer outro lixo
    public void AtualizarMetadata_rejeita_tag_fora_da_lista_permitida(string tag)
    {
        var item = NovoItemValido();

        var act = () => item.AtualizarMetadata(tag: tag);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Tag*");
    }

    [Fact]
    public void AtualizarMetadata_normaliza_tag_para_lowercase()
    {
        var item = NovoItemValido();

        item.AtualizarMetadata(tag: "ASSINATURA");

        item.Tag.Should().Be("assinatura", "entity normaliza tag para lowercase antes de validar");
    }

    [Fact]
    public void LimparTag_remove_tag_e_atualiza_data()
    {
        var item = NovoItemValido();
        item.AtualizarMetadata(tag: "assinatura");
        item.Tag.Should().Be("assinatura");
        var antes = item.AlteradoEm;
        Thread.Sleep(10);

        item.LimparTag();

        item.Tag.Should().BeNull();
        item.AlteradoEm.Should().BeAfter(antes);
    }

    // ── FiltrosJson ───────────────────────────────────────────────────

    [Fact]
    public void AtualizarMetadata_aceita_filtros_json_array_vazio()
    {
        var item = NovoItemValido();

        var act = () => item.AtualizarMetadata(filtrosJson: "[]");

        act.Should().NotThrow();
        item.FiltrosJson.Should().Be("[]");
    }

    [Fact]
    public void AtualizarMetadata_aceita_filtros_json_array_de_strings()
    {
        var item = NovoItemValido();

        item.AtualizarMetadata(filtrosJson: "[\"sem-gluten\",\"vegano\"]");

        item.FiltrosJson.Should().Be("[\"sem-gluten\",\"vegano\"]");
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"key\":\"value\"}")]          // objeto, não array
    [InlineData("[1, 2, 3]")]                    // array, mas de números
    [InlineData("[\"ok\", 42]")]                 // array misto
    public void AtualizarMetadata_rejeita_filtros_json_invalido(string filtros)
    {
        var item = NovoItemValido();

        var act = () => item.AtualizarMetadata(filtrosJson: filtros);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Filtros*");
    }

    // ── DefinirOrdem (double pra inserir entre 2 sem renumerar) ────────

    [Fact]
    public void DefinirOrdem_aceita_valor_fracionario()
    {
        var item = NovoItemValido();

        item.DefinirOrdem(1.5);

        item.OrdemExibicao.Should().Be(1.5);
    }

    [Fact]
    public void DefinirOrdem_permite_inserir_entre_dois_sem_renumerar()
    {
        // Cenário: tenho ordens 1.0 e 2.0 e quero inserir no meio. Posso usar 1.5
        // sem mexer nos outros. Esse é o motivo de OrdemExibicao ser double.
        var item = NovoItemValido();
        item.DefinirOrdem(1.0);
        var antes = item.AlteradoEm;
        Thread.Sleep(10);

        item.DefinirOrdem(1.5);

        item.OrdemExibicao.Should().Be(1.5);
        item.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void DefinirOrdem_rejeita_valor_negativo()
    {
        var item = NovoItemValido();

        var act = () => item.DefinirOrdem(-0.1);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ── AtualizarMetadata bulk ────────────────────────────────────────

    [Fact]
    public void AtualizarMetadata_atualiza_todos_campos_opcionais_e_marca_data()
    {
        var item = NovoItemValido();
        var antes = item.AlteradoEm;
        Thread.Sleep(10);

        item.AtualizarMetadata(
            descricaoPublica: "Massa fresca recheada com frango caipira desfiado.",
            ingredientes: "Farinha 00, ovo caipira, frango caipira, sal.",
            alergenos: "Glúten, ovo.",
            sugestaoMolho: "Molho de tomate fresco com manjericão.",
            tempoPreparo: "3 minutos",
            fotoUrl: "https://cdn.example.com/capeletti.jpg",
            precoStorefront: 22.50m,
            tag: "assinatura",
            filtrosJson: "[\"recheado\",\"caipira\"]",
            pesoExibicao: "500g");

        item.DescricaoPublica.Should().Be("Massa fresca recheada com frango caipira desfiado.");
        item.Ingredientes.Should().Be("Farinha 00, ovo caipira, frango caipira, sal.");
        item.Alergenos.Should().Be("Glúten, ovo.");
        item.SugestaoMolho.Should().Be("Molho de tomate fresco com manjericão.");
        item.TempoPreparo.Should().Be("3 minutos");
        item.FotoUrl.Should().Be("https://cdn.example.com/capeletti.jpg");
        item.PrecoStorefront.Should().Be(22.50m);
        item.Tag.Should().Be("assinatura");
        item.FiltrosJson.Should().Be("[\"recheado\",\"caipira\"]");
        item.PesoExibicao.Should().Be("500g");
        item.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void AtualizarMetadata_rejeita_descricao_publica_maior_que_240()
    {
        var item = NovoItemValido();

        var act = () => item.AtualizarMetadata(descricaoPublica: new string('a', 241));

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AtualizarMetadata_rejeita_preco_storefront_negativo()
    {
        var item = NovoItemValido();

        var act = () => item.AtualizarMetadata(precoStorefront: -1m);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ── CriarAvulso: item sem vínculo com ERP (ADR-0031) ──────────────

    [Fact]
    public void CriarAvulso_sucesso_ProdutoId_nulo()
    {
        var item = CardapioItem.CriarAvulso(
            storefrontId: Guid.NewGuid(),
            nome: "Lasanha Bolonhesa",
            precoEmReais: 35.00m,
            categoria: "Lasanhas");

        item.ProdutoId.Should().BeNull("item avulso não tem vínculo com produto do ERP");
        item.NomePublico.Should().Be("lasanha bolonhesa", "armazenado em lowercase");
        item.CategoriaTexto.Should().Be("lasanhas", "armazenado em lowercase");
        item.PrecoStorefront.Should().Be(35.00m, "preço é obrigatório para avulso");
        item.Visivel.Should().BeFalse("safe default — tenant publica manualmente");
        item.Disponivel.Should().BeTrue("disponível por padrão");
        item.FiltrosJson.Should().Be("[]");
    }

    [Fact]
    public void CriarAvulso_nome_nulo_throws()
    {
        var act = () => CardapioItem.CriarAvulso(
            storefrontId: Guid.NewGuid(),
            nome: null!,
            precoEmReais: 35.00m);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Nome*");
    }

    [Fact]
    public void CriarAvulso_nome_whitespace_throws()
    {
        var act = () => CardapioItem.CriarAvulso(
            storefrontId: Guid.NewGuid(),
            nome: "   ",
            precoEmReais: 35.00m);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Nome*");
    }

    [Fact]
    public void CriarAvulso_preco_zero_throws()
    {
        var act = () => CardapioItem.CriarAvulso(
            storefrontId: Guid.NewGuid(),
            nome: "Lasanha",
            precoEmReais: 0m);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Preço*positivo*");
    }

    [Fact]
    public void CriarAvulso_preco_negativo_throws()
    {
        var act = () => CardapioItem.CriarAvulso(
            storefrontId: Guid.NewGuid(),
            nome: "Lasanha",
            precoEmReais: -1m);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Preço*positivo*");
    }

    [Fact]
    public void CriarAvulso_sem_categoria_categoria_texto_null()
    {
        var item = CardapioItem.CriarAvulso(
            storefrontId: Guid.NewGuid(),
            nome: "Capeletti",
            precoEmReais: 28.00m);

        item.CategoriaTexto.Should().BeNull();
        item.NomePublico.Should().Be("capeletti");
    }

    [Fact]
    public void CriarAPartirDeProduto_backward_compat_com_produto_existente()
    {
        // Garante que a factory original (modo vinculado) não regrediu
        var produto = NovoProdutoValido();
        var item = CardapioItem.CriarAPartirDeProduto(Guid.NewGuid(), produto);
        item.Produto = produto;

        item.ProdutoId.Should().Be(produto.Id);
        item.NomePublico.Should().BeNull("vinculado não preenche NomePublico por padrão");
        item.CategoriaTexto.Should().BeNull("vinculado não preenche CategoriaTexto por padrão");
        item.PrecoEfetivo().Should().Be(produto.PrecoReferencia!.Valor);
    }

    /// <summary>
    /// Unit-pinning: fixa a conversão R$→centavos na camada de domínio.
    /// PrecoEfetivo() retorna decimal em R$, não centavos.
    /// A conversão ×100 acontece na projeção do DTO (ListarCardapioPublicoUseCase).
    /// </summary>
    [Fact]
    public void PrecoEfetivo_avulso_retorna_decimal_em_reais_nao_centavos()
    {
        var item = CardapioItem.CriarAvulso(
            storefrontId: Guid.NewGuid(),
            nome: "Lasanha",
            precoEmReais: 35.00m);

        // PrecoEfetivo = 35.00m (R$), NÃO 3500 (centavos)
        item.PrecoEfetivo().Should().Be(35.00m,
            "domínio armazena e retorna em R$; centavos são responsabilidade do DTO");
    }
}
