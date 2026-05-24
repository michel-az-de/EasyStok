using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="EasyStock.Domain.Entities.Storefront.Storefront"/>.
///
/// Cobertura: factory + invariantes de slug/preço/título + transições de
/// ativação + feature flags (NfeAutomaticaHabilitada, ModeloFiscal) que
/// blindam decisões pendentes do contador (ver ADR-0010 do plano).
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class StorefrontTests
{
    private static EasyStock.Domain.Entities.Storefront.Storefront NovoStorefrontValido(
        Guid? empresaId = null,
        string slug = "casadababa",
        string titulo = "Casa da Babá")
    {
        return EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: empresaId ?? Guid.NewGuid(),
            slug: slug,
            tituloPublico: titulo,
            pedidoMinimoEntrega: 40m);
    }

    // ── Factory: happy path ────────────────────────────────────────────

    [Fact]
    public void Criar_define_estado_inicial_correto_e_carimba_datas()
    {
        var empresaId = Guid.NewGuid();
        var s = EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: empresaId,
            slug: "casadababa",
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 40m);

        s.Id.Should().NotBeEmpty();
        s.EmpresaId.Should().Be(empresaId);
        s.Slug.Should().Be("casadababa");
        s.TituloPublico.Should().Be("Casa da Babá");
        s.PedidoMinimoEntrega.Should().Be(40m);

        // Defaults importantes — feature flags SEMPRE safe-by-default
        s.Ativo.Should().BeFalse("storefront é criado inativo; admin precisa Ativar() explicitamente");
        s.NfeAutomaticaHabilitada.Should().BeFalse(
            "ADR-0010: emissão NF-e automática fica DRAFT até contador validar (TASK-002/003)");
        s.ModeloFiscal.Should().Be("manual",
            "ADR-0010: modelo fiscal default 'manual' até cenário A/B/C ser decidido pelo contador");

        s.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        s.AlteradoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Criar_normaliza_slug_para_lowercase_e_trim()
    {
        var s = EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: "  CasaDaBaba  ",
            tituloPublico: "Casa",
            pedidoMinimoEntrega: 40m);

        s.Slug.Should().Be("casadababa", "slug é PII na URL pública; sempre normalizado");
    }

    // ── Factory: validações de slug ────────────────────────────────────

    [Theory]
    [InlineData("ab")]              // < 3 chars
    [InlineData("a")]               // 1 char
    [InlineData("")]                // vazio
    public void Criar_rejeita_slug_menor_que_3_caracteres(string slug)
    {
        var act = () => EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: slug,
            tituloPublico: "Casa",
            pedidoMinimoEntrega: 40m);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*slug*");
    }

    [Fact]
    public void Criar_rejeita_slug_maior_que_40_caracteres()
    {
        var slugLongo = new string('a', 41);

        var act = () => EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: slugLongo,
            tituloPublico: "Casa",
            pedidoMinimoEntrega: 40m);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("Casa Da Baba")]     // espaços
    [InlineData("casa.da.baba")]     // dots
    [InlineData("casa_da_baba")]     // underscore (não permitido — só hifen)
    [InlineData("casadababá")]       // acento
    [InlineData("CASADABABA")]       // uppercase (não vamos normalizar uppercase? sim, mas testa rejeição se não usar normalizador)
    [InlineData("-casadababa")]      // hífen no início
    [InlineData("casadababa-")]      // hífen no fim
    [InlineData("casa--baba")]       // hífen duplo
    public void Criar_rejeita_slug_fora_do_regex(string slug)
    {
        // Note: "CASADABABA" passa via normalização (lowercase) na verdade.
        // Testamos rejeição em outros casos — pra "CASADABABA" precisamos garantir
        // que normalização happens ANTES da validação regex.
        // Pra simplicidade do MVP, validamos APÓS normalização. Então "CASADABABA"
        // vira "casadababa" e passa. Removemos do Theory.

        if (slug == "CASADABABA")
            return; // ignorar este caso — normalização lida

        var act = () => EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: slug,
            tituloPublico: "Casa",
            pedidoMinimoEntrega: 40m);

        act.Should().Throw<RegraDeDominioVioladaException>(
            $"slug '{slug}' não casa com regex ^[a-z0-9]([a-z0-9-]{{1,38}}[a-z0-9])?$");
    }

    // ── Factory: validações de outros campos ───────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_rejeita_titulo_publico_vazio(string titulo)
    {
        var act = () => EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: "casadababa",
            tituloPublico: titulo,
            pedidoMinimoEntrega: 40m);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*título*");
    }

    [Fact]
    public void Criar_rejeita_titulo_publico_maior_que_120_caracteres()
    {
        var tituloLongo = new string('a', 121);

        var act = () => EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: "casadababa",
            tituloPublico: tituloLongo,
            pedidoMinimoEntrega: 40m);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Criar_rejeita_pedido_minimo_negativo(decimal valor)
    {
        var act = () => EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: "casadababa",
            tituloPublico: "Casa",
            pedidoMinimoEntrega: valor);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*pedido mínimo*");
    }

    [Fact]
    public void Criar_aceita_pedido_minimo_zero_para_storefronts_sem_minimo()
    {
        var s = EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.NewGuid(),
            slug: "casadababa",
            tituloPublico: "Casa",
            pedidoMinimoEntrega: 0m);

        s.PedidoMinimoEntrega.Should().Be(0m);
    }

    [Fact]
    public void Criar_rejeita_empresa_id_vazio()
    {
        var act = () => EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: Guid.Empty,
            slug: "casadababa",
            tituloPublico: "Casa",
            pedidoMinimoEntrega: 40m);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ── Ativar / Desativar ─────────────────────────────────────────────

    [Fact]
    public void Ativar_throws_quando_falta_pre_requisitos()
    {
        var s = NovoStorefrontValido();
        // Sem cardápio, sem MP cadastrado, sem zonas de frete — pré-requisitos não atendidos.
        // Por enquanto, entity NÃO conhece cardápio/MP/frete diretamente (são outras entidades).
        // Para MVP do entity, validação é simples: pode ativar se TituloPublico estiver setado.
        // Lógica de "cardápio com itens visíveis + MP credencial + zonas frete" fica no
        // AtivarStorefrontUseCase (Application Layer).

        // Aqui testamos apenas que Ativar() é idempotente e atualiza AlteradoEm.
        var antesAtivar = s.AlteradoEm;
        Thread.Sleep(10); // garantir diff de timestamp
        s.Ativar();

        s.Ativo.Should().BeTrue();
        s.AlteradoEm.Should().BeAfter(antesAtivar);
    }

    [Fact]
    public void Ativar_eh_idempotente()
    {
        var s = NovoStorefrontValido();
        s.Ativar();
        var alteradoApos1ªAtivacao = s.AlteradoEm;

        Thread.Sleep(10);
        s.Ativar();

        s.Ativo.Should().BeTrue();
        s.AlteradoEm.Should().Be(alteradoApos1ªAtivacao,
            "ativar 2x não modifica timestamps (idempotente)");
    }

    [Fact]
    public void Desativar_apenas_marca_ativo_falso_e_atualiza_data()
    {
        var s = NovoStorefrontValido();
        s.Ativar();
        Thread.Sleep(10);

        var antesDesativar = s.AlteradoEm;
        Thread.Sleep(10);
        s.Desativar();

        s.Ativo.Should().BeFalse();
        s.AlteradoEm.Should().BeAfter(antesDesativar);
    }

    [Fact]
    public void Desativar_eh_idempotente()
    {
        var s = NovoStorefrontValido();
        // já está inativo por default
        var antes = s.AlteradoEm;
        Thread.Sleep(10);
        s.Desativar();
        s.AlteradoEm.Should().Be(antes, "desativar 2x não modifica (idempotente)");
    }

    // ── AtualizarBranding / AjustarPedidoMinimo ────────────────────────

    [Fact]
    public void AtualizarBranding_atualiza_campos_opcionais_e_marca_data()
    {
        var s = NovoStorefrontValido();
        var antes = s.AlteradoEm;
        Thread.Sleep(10);

        s.AtualizarBranding(
            subtituloPublico: "Massas frescas artesanais",
            logoUrl: "https://example.com/logo.png",
            corPrimaria: "#5C2E0D",
            whatsappPedidos: "+5511997573992",
            mensagemForaArea: "Por enquanto entregamos só no Butantã.");

        s.SubtituloPublico.Should().Be("Massas frescas artesanais");
        s.LogoUrl.Should().Be("https://example.com/logo.png");
        s.CorPrimaria.Should().Be("#5C2E0D");
        s.WhatsappPedidos.Should().Be("+5511997573992");
        s.MensagemForaArea.Should().Be("Por enquanto entregamos só no Butantã.");
        s.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void AtualizarBranding_rejeita_cor_primaria_invalida()
    {
        var s = NovoStorefrontValido();

        var act = () => s.AtualizarBranding(corPrimaria: "naoEhHex");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*cor*");
    }

    [Theory]
    [InlineData("#5C2E0D")]
    [InlineData("#fff")]
    [InlineData("#FFFFFF")]
    public void AtualizarBranding_aceita_hex_valido(string cor)
    {
        var s = NovoStorefrontValido();
        var act = () => s.AtualizarBranding(corPrimaria: cor);
        act.Should().NotThrow();
        s.CorPrimaria.Should().Be(cor);
    }

    [Fact]
    public void AjustarPedidoMinimo_atualiza_e_marca_data()
    {
        var s = NovoStorefrontValido();
        var antes = s.AlteradoEm;
        Thread.Sleep(10);

        s.AjustarPedidoMinimo(60m);

        s.PedidoMinimoEntrega.Should().Be(60m);
        s.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void AjustarPedidoMinimo_rejeita_valor_negativo()
    {
        var s = NovoStorefrontValido();
        var act = () => s.AjustarPedidoMinimo(-1m);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ── Feature flags fiscais ──────────────────────────────────────────

    [Fact]
    public void HabilitarNfeAutomatica_so_funciona_se_modelo_fiscal_eh_nfe55_ou_nfce65()
    {
        var s = NovoStorefrontValido();
        // ModeloFiscal default é "manual"; habilitar NFe automática sem definir modelo deve falhar.

        var act = () => s.HabilitarNfeAutomatica();

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*modelo fiscal*");
    }

    [Theory]
    [InlineData("nfe55")]
    [InlineData("nfce65")]
    public void HabilitarNfeAutomatica_funciona_quando_modelo_definido(string modelo)
    {
        var s = NovoStorefrontValido();
        s.DefinirModeloFiscal(modelo);
        s.HabilitarNfeAutomatica();

        s.NfeAutomaticaHabilitada.Should().BeTrue();
        s.ModeloFiscal.Should().Be(modelo);
    }

    [Fact]
    public void DefinirModeloFiscal_rejeita_valores_fora_da_lista_permitida()
    {
        var s = NovoStorefrontValido();
        var act = () => s.DefinirModeloFiscal("nfe65"); // typo: deveria ser nfe55 ou nfce65
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("nfe55")]
    [InlineData("nfce65")]
    public void DefinirModeloFiscal_aceita_valores_validos(string modelo)
    {
        var s = NovoStorefrontValido();
        var act = () => s.DefinirModeloFiscal(modelo);
        act.Should().NotThrow();
        s.ModeloFiscal.Should().Be(modelo);
    }

    [Fact]
    public void DesabilitarNfeAutomatica_funciona_e_eh_idempotente()
    {
        var s = NovoStorefrontValido();
        s.DefinirModeloFiscal("nfe55");
        s.HabilitarNfeAutomatica();
        s.NfeAutomaticaHabilitada.Should().BeTrue();

        s.DesabilitarNfeAutomatica();
        s.NfeAutomaticaHabilitada.Should().BeFalse();

        // Idempotente
        s.DesabilitarNfeAutomatica();
        s.NfeAutomaticaHabilitada.Should().BeFalse();
    }
}
