using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Acervo;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.UseCases.Acervo;

/// <summary>
/// #1808 itens 3 e 5: a dica do prato e a sugestão de imagem para o prato. Com IA o texto e os pares
/// vêm do LLM, sempre conferidos contra os dados da empresa; sem IA (ou com falha) a dica é a
/// estática e a sugestão é por nome. Nada é gravado: a publicação segue o fluxo confirmado.
/// </summary>
public class AssistenteDoCardapioUseCaseTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private readonly IAcervoRepository _repo = Substitute.For<IAcervoRepository>();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly CardapioItem _lasanha = CardapioItem.CriarAvulso(Guid.NewGuid(), "Lasanha à bolonhesa", 30m, null);
    private readonly CardapioItem _ravioli = CardapioItem.CriarAvulso(Guid.NewGuid(), "Ravioli de queijo", 30m, "massas");
    private readonly AcervoImagem _fotoLasanha = Imagem("Lasanha bolonhesa na travessa");
    private readonly AcervoImagem _fotoRavioli = Imagem("Ravioli com queijo");
    private readonly AcervoImagem _naoAprovada = Imagem("Lasanha crua", aprovada: false);
    private readonly AcervoImagem _peca = Imagem("Lasanha promoção", natureza: "Comunicacao");

    public AssistenteDoCardapioUseCaseTests()
    {
        _repo.ItemAsync(Empresa, _lasanha.Id, Arg.Any<CancellationToken>()).Returns(_lasanha);
        _repo.ItensAsync(Empresa, Arg.Any<CancellationToken>()).Returns([_lasanha, _ravioli]);
        _repo.MapaImagensAsync(Empresa, Arg.Any<CancellationToken>()).Returns([_fotoLasanha, _fotoRavioli, _naoAprovada, _peca]);
    }

    private static AcervoImagem Imagem(string titulo, bool aprovada = true, string natureza = "Produto", params Guid[] itens) => new()
    {
        EmpresaId = Empresa, Titulo = titulo, Natureza = natureza, ItensJson = JsonSerializer.Serialize(itens),
        AprovadoEm = aprovada ? DateTime.UtcNow : null,
    };

    private AssistenteDoCardapioUseCase Sut() => new(_repo, _llm);

    private void ResponderFerramenta(string nome, string json) =>
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(
            RespostaLlm.StopToolUse, [new BlocoUsoFerramentaLlm("t1", nome, JsonDocument.Parse(json).RootElement)], 10, 10));

    [Fact]
    public async Task Dica_sem_ia_e_a_estatica_com_as_pendencias()
    {
        _llm.Disponivel.Returns(false);

        var dica = await Sut().DicaAsync(Empresa, _lasanha.Id, default);

        dica.PorIa.Should().BeFalse();
        dica.Pendencias.Should().Equal(CardapioItem.PendenciaSemFoto, CardapioItem.PendenciaSemCategoria, CardapioItem.PendenciaSemDescricao);
        dica.Texto.Should().Contain("foto").And.Contain("categoria").And.Contain("descrição");
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task Dica_com_ia_usa_o_texto_do_llm_e_manda_o_prato_como_dado()
    {
        _llm.Disponivel.Returns(true);
        ResponderFerramenta("dar_dica", """{"texto":"Fotografe a lasanha cortada e cite o ragu na descrição."}""");

        var dica = await Sut().DicaAsync(Empresa, _lasanha.Id, default);

        dica.PorIa.Should().BeTrue();
        dica.Texto.Should().Be("Fotografe a lasanha cortada e cite o ragu na descrição.");
        await _llm.Received(1).EnviarAsync(Arg.Is<RequisicaoLlm>(r =>
            r.Mensagens.Single().Conteudo.OfType<BlocoTextoLlm>().Single().Texto.Contains("lasanha à bolonhesa")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dica_com_resposta_invalida_ou_falha_cai_na_estatica()
    {
        _llm.Disponivel.Returns(true);
        ResponderFerramenta("dar_dica", """{"texto":""}""");
        (await Sut().DicaAsync(Empresa, _lasanha.Id, default)).PorIa.Should().BeFalse();

        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns<RespostaLlm>(_ => throw new HttpRequestException("fora"));
        (await Sut().DicaAsync(Empresa, _lasanha.Id, default)).PorIa.Should().BeFalse();
    }

    [Fact]
    public async Task Dica_de_prato_de_outra_empresa_nao_e_encontrada()
    {
        var acao = () => Sut().DicaAsync(Empresa, Guid.NewGuid(), default);
        await acao.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Sugestao_sem_ia_casa_pelo_nome_so_imagens_aprovadas_de_produto()
    {
        _llm.Disponivel.Returns(false);

        var resultado = await Sut().SugerirAssociacoesAsync(Empresa, default);

        resultado.PorIa.Should().BeFalse();
        resultado.Sugestoes.Select(s => (s.ImagemId, s.ItemId)).Should().BeEquivalentTo(new[]
        {
            (_fotoLasanha.Id, _lasanha.Id), (_fotoRavioli.Id, _ravioli.Id),
        });
        resultado.Sugestoes.Should().OnlyContain(s => s.Motivo.Length > 0);
    }

    [Fact]
    public async Task Sugestao_sem_ia_ignora_o_peso_no_nome_do_prato()
    {
        _llm.Disponivel.Returns(false);
        var pesado = CardapioItem.CriarAvulso(Guid.NewGuid(), "Nhoque artesanal 500 g", 30m, "massas");
        _repo.ItensAsync(Empresa, Arg.Any<CancellationToken>()).Returns([pesado]);
        _repo.MapaImagensAsync(Empresa, Arg.Any<CancellationToken>()).Returns([Imagem("Nhoque no prato fundo")]);

        (await Sut().SugerirAssociacoesAsync(Empresa, default)).Sugestoes.Should().ContainSingle()
            .Which.Motivo.Should().Be("O título tem 1 de 2 palavras do nome do prato.");
    }

    [Fact]
    public async Task Sugestao_nao_repete_imagem_ja_vinculada_ao_prato()
    {
        _llm.Disponivel.Returns(false);
        var jaLigada = Imagem("Lasanha bolonhesa", itens: _lasanha.Id);
        _repo.MapaImagensAsync(Empresa, Arg.Any<CancellationToken>()).Returns([jaLigada]);

        (await Sut().SugerirAssociacoesAsync(Empresa, default)).Sugestoes.Should().BeEmpty();
    }

    [Fact]
    public async Task Sugestao_com_ia_descarta_ids_inventados_e_imagens_nao_aprovadas()
    {
        _llm.Disponivel.Returns(true);
        ResponderFerramenta("sugerir_associacoes", JsonSerializer.Serialize(new
        {
            associacoes = new object[]
            {
                new { imagemId = _fotoLasanha.Id, itemId = _lasanha.Id, motivo = "Mesmo prato." },
                new { imagemId = _naoAprovada.Id, itemId = _ravioli.Id, motivo = "Não aprovada." },
                new { imagemId = Guid.NewGuid(), itemId = _ravioli.Id, motivo = "Inventada." },
                new { imagemId = _fotoRavioli.Id, itemId = Guid.NewGuid(), motivo = "Prato inventado." },
            },
        }));

        var resultado = await Sut().SugerirAssociacoesAsync(Empresa, default);

        resultado.PorIa.Should().BeTrue();
        resultado.Sugestoes.Should().ContainSingle().Which.Should().Match<SugestaoAssociacao>(s =>
            s.ImagemId == _fotoLasanha.Id && s.ItemId == _lasanha.Id && s.Prato == "lasanha à bolonhesa" && s.Titulo == "Lasanha bolonhesa na travessa");
        await _llm.Received(1).EnviarAsync(Arg.Is<RequisicaoLlm>(r =>
            !r.Mensagens.Single().Conteudo.OfType<BlocoTextoLlm>().Single().Texto.Contains(_naoAprovada.Id.ToString())), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sugestao_com_falha_da_ia_cai_no_casamento_por_nome()
    {
        _llm.Disponivel.Returns(true);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns<RespostaLlm>(_ => throw new HttpRequestException("fora"));

        var resultado = await Sut().SugerirAssociacoesAsync(Empresa, default);

        resultado.PorIa.Should().BeFalse();
        resultado.Sugestoes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Sem_imagem_aprovada_nao_chama_a_ia()
    {
        _llm.Disponivel.Returns(true);
        _repo.MapaImagensAsync(Empresa, Arg.Any<CancellationToken>()).Returns([_naoAprovada]);

        (await Sut().SugerirAssociacoesAsync(Empresa, default)).Sugestoes.Should().BeEmpty();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }
}
