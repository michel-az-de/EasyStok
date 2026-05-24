using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="PedidoAvaliacao"/>.
///
/// Modelo: avaliação é SOLICITADA pela Babá +24h após entrega (SolicitadoEm),
/// RESPONDIDA pelo cliente (RespondidoEm + Estrelas + Comentario),
/// opcionalmente RESPONDIDA PELA BABÁ (RespostaDaBaba + RespondidaEmPorBaba),
/// e pode ser OCULTADA por moderação (OcultadoEm).
///
/// Unique constraint a nível de DB: 1 avaliação por pedido (UNIQUE PedidoId).
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser implementada.
/// </summary>
public class PedidoAvaliacaoTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static PedidoAvaliacao NovaAvaliacaoValida(
        Guid? pedidoId = null,
        Guid? clienteId = null,
        Guid? empresaId = null,
        int estrelas = 5,
        string? comentario = "Tudo perfeito!",
        bool recomendaria = true,
        string? fotoUrl = null)
    {
        return PedidoAvaliacao.Criar(
            pedidoId: pedidoId ?? Guid.NewGuid(),
            clienteId: clienteId ?? Guid.NewGuid(),
            empresaId: empresaId ?? Guid.NewGuid(),
            estrelas: estrelas,
            comentario: comentario,
            recomendariaParaAmigos: recomendaria,
            fotoUrl: fotoUrl,
            solicitadoEm: DateTime.UtcNow.AddDays(-1));
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Criar_define_estado_inicial_correto()
    {
        var pedidoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var solicitadoEm = DateTime.UtcNow.AddDays(-1);

        var aval = PedidoAvaliacao.Criar(
            pedidoId: pedidoId,
            clienteId: clienteId,
            empresaId: empresaId,
            estrelas: 5,
            comentario: "Tudo perfeito!",
            recomendariaParaAmigos: true,
            fotoUrl: "https://cdn/foto.jpg",
            solicitadoEm: solicitadoEm);

        aval.Id.Should().NotBeEmpty();
        aval.PedidoId.Should().Be(pedidoId);
        aval.ClienteId.Should().Be(clienteId);
        aval.EmpresaId.Should().Be(empresaId);
        aval.Estrelas.Should().Be(5);
        aval.Comentario.Should().Be("Tudo perfeito!");
        aval.RecomendariaParaAmigos.Should().BeTrue();
        aval.FotoUrl.Should().Be("https://cdn/foto.jpg");
        aval.SolicitadoEm.Should().Be(solicitadoEm);
        aval.RespondidoEm.Should().NotBeNull("o cliente respondeu no momento da criação");
        aval.RespondidoEm!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        aval.RespondidoEm.Value.Kind.Should().Be(DateTimeKind.Utc, "timestamps em UTC para auditoria");
        aval.OcultadoEm.Should().BeNull("nasce visível");
        aval.RespostaDaBaba.Should().BeNull("Babá ainda não respondeu");
        aval.RespondidaEmPorBaba.Should().BeNull();
    }

    // ── Factory: validações de Estrelas ───────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(100)]
    public void Criar_rejeita_estrelas_fora_do_intervalo_1_a_5(int estrelasInvalidas)
    {
        var act = () => NovaAvaliacaoValida(estrelas: estrelasInvalidas);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*estrelas*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Criar_aceita_estrelas_no_intervalo_1_a_5(int estrelasValidas)
    {
        var aval = NovaAvaliacaoValida(estrelas: estrelasValidas);

        aval.Estrelas.Should().Be(estrelasValidas);
    }

    // ── Factory: outras validações ────────────────────────────────────

    [Fact]
    public void Criar_rejeita_pedido_id_vazio()
    {
        var act = () => NovaAvaliacaoValida(pedidoId: Guid.Empty);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Pedido*");
    }

    [Fact]
    public void Criar_rejeita_cliente_id_vazio()
    {
        var act = () => NovaAvaliacaoValida(clienteId: Guid.Empty);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Cliente*");
    }

    [Fact]
    public void Criar_rejeita_empresa_id_vazio()
    {
        var act = () => NovaAvaliacaoValida(empresaId: Guid.Empty);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Empresa*");
    }

    [Fact]
    public void Criar_rejeita_comentario_excedendo_500_caracteres()
    {
        var comentarioGigante = new string('a', 501);

        var act = () => NovaAvaliacaoValida(comentario: comentarioGigante);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Coment*");
    }

    [Fact]
    public void Criar_aceita_comentario_null()
    {
        var aval = NovaAvaliacaoValida(comentario: null);

        aval.Comentario.Should().BeNull();
    }

    [Fact]
    public void Criar_normaliza_comentario_vazio_para_null()
    {
        var aval = NovaAvaliacaoValida(comentario: "   ");

        aval.Comentario.Should().BeNull("strings em branco são tratadas como ausência de comentário");
    }

    // ── Responder (resposta da Babá ao comentário do cliente) ─────────

    [Fact]
    public void Responder_preenche_RespostaDaBaba_e_timestamp()
    {
        var aval = NovaAvaliacaoValida();

        aval.Responder("Obrigada pelo carinho!");

        aval.RespostaDaBaba.Should().Be("Obrigada pelo carinho!");
        aval.RespondidaEmPorBaba.Should().NotBeNull();
        aval.RespondidaEmPorBaba!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        aval.RespondidaEmPorBaba.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Responder_aceita_atualizar_resposta_existente()
    {
        var aval = NovaAvaliacaoValida();
        aval.Responder("Primeira versão");
        var primeira = aval.RespondidaEmPorBaba;
        Thread.Sleep(10);

        aval.Responder("Versão corrigida");

        aval.RespostaDaBaba.Should().Be("Versão corrigida");
        aval.RespondidaEmPorBaba.Should().NotBe(primeira, "edição da resposta atualiza o timestamp");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Responder_rejeita_texto_vazio(string? textoInvalido)
    {
        var aval = NovaAvaliacaoValida();

        var act = () => aval.Responder(textoInvalido!);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Resposta*");
    }

    [Fact]
    public void Responder_rejeita_resposta_excedendo_500_caracteres()
    {
        var aval = NovaAvaliacaoValida();
        var respostaGigante = new string('a', 501);

        var act = () => aval.Responder(respostaGigante);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Resposta*");
    }

    // ── Ocultar (moderação Babá) ──────────────────────────────────────

    [Fact]
    public void Ocultar_define_OcultadoEm_com_timestamp_UTC()
    {
        var aval = NovaAvaliacaoValida();

        aval.Ocultar();

        aval.OcultadoEm.Should().NotBeNull();
        aval.OcultadoEm!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        aval.OcultadoEm.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Ocultar_eh_idempotente_quando_ja_oculta()
    {
        var aval = NovaAvaliacaoValida();
        aval.Ocultar();
        var ocultadoEmOriginal = aval.OcultadoEm;
        Thread.Sleep(10);

        aval.Ocultar();

        aval.OcultadoEm.Should().Be(ocultadoEmOriginal,
            "segunda chamada é no-op para preservar o timestamp original da moderação");
    }
}
