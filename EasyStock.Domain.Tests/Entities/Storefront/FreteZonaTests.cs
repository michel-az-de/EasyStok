using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes da entity <see cref="FreteZona"/>.
///
/// FreteZona modela uma zona de entrega baseada em CEP range OU lista de bairros
/// (NÃO em coordenadas — ver ADR-0011, ADR-0013). Cliente informa CEP no checkout
/// e o backend casa contra zonas cadastradas.
///
/// Cobertura: factories (CEP range / bairros) + invariantes (label/valor/tempo/CEP)
/// + cobertura (CobreCep/CobreBairro com normalização) + ordem para desempate.
///
/// TDD red phase: todos os cenários abaixo devem FALHAR até a entity ser
/// implementada na green phase.
/// </summary>
public class FreteZonaTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static FreteZona NovaZonaPorCep(
        Guid? storefrontId = null,
        string label = "Butantã proximidade",
        string cepInicio = "05500000",
        string cepFim = "05599999",
        decimal valor = 12.50m,
        int tempoMinutos = 45,
        int ordem = 0)
    {
        return FreteZona.CriarPorCep(
            storefrontId: storefrontId ?? Guid.NewGuid(),
            label: label,
            cepInicio: cepInicio,
            cepFim: cepFim,
            valor: valor,
            tempoEstimadoMinutos: tempoMinutos,
            ordem: ordem);
    }

    private static FreteZona NovaZonaPorBairros(
        Guid? storefrontId = null,
        string label = "Butantã + Pinheiros",
        string[]? bairros = null,
        decimal valor = 15m,
        int tempoMinutos = 60,
        int ordem = 0)
    {
        return FreteZona.CriarPorBairros(
            storefrontId: storefrontId ?? Guid.NewGuid(),
            label: label,
            bairros: bairros ?? new[] { "Butantã", "Pinheiros" },
            valor: valor,
            tempoEstimadoMinutos: tempoMinutos,
            ordem: ordem);
    }

    // ── Factory CriarPorCep: happy path ────────────────────────────────

    [Fact]
    public void CriarPorCep_define_estado_inicial_seguro()
    {
        var storefrontId = Guid.NewGuid();

        var zona = FreteZona.CriarPorCep(
            storefrontId: storefrontId,
            label: "Butantã proximidade",
            cepInicio: "05500000",
            cepFim: "05599999",
            valor: 12.50m,
            tempoEstimadoMinutos: 45,
            ordem: 1);

        zona.Id.Should().NotBeEmpty();
        zona.StorefrontId.Should().Be(storefrontId);
        zona.Label.Should().Be("Butantã proximidade");
        zona.Valor.Should().Be(12.50m);
        zona.TempoEstimadoMinutos.Should().Be(45);
        zona.Ordem.Should().Be(1);
        zona.Ativa.Should().BeTrue("default = ativa (admin desativa explicitamente)");
        zona.TipoCobertura.Should().Be("cep_range");
        zona.CepInicio.Should().Be("05500000");
        zona.CepFim.Should().Be("05599999");
        zona.BairrosJson.Should().BeNull("CEP range não usa lista de bairros");
    }

    [Fact]
    public void CriarPorCep_normaliza_cep_removendo_mascara_e_espacos()
    {
        // Aceita "05500-000" e "05500000" e " 05500-000 " — normaliza pra dígitos puros.
        var zona = FreteZona.CriarPorCep(
            storefrontId: Guid.NewGuid(),
            label: "Butantã",
            cepInicio: " 05500-000 ",
            cepFim: "05599-999",
            valor: 10m,
            tempoEstimadoMinutos: 30);

        zona.CepInicio.Should().Be("05500000");
        zona.CepFim.Should().Be("05599999");
    }

    // ── Factory CriarPorCep: invariantes ───────────────────────────────

    [Fact]
    public void CriarPorCep_rejeita_storefront_id_vazio()
    {
        var act = () => NovaZonaPorCep(storefrontId: Guid.Empty);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Storefront*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CriarPorCep_rejeita_label_vazio_ou_em_branco(string label)
    {
        var act = () => NovaZonaPorCep(label: label);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Label*");
    }

    [Fact]
    public void CriarPorCep_rejeita_label_maior_que_80()
    {
        var act = () => NovaZonaPorCep(label: new string('a', 81));

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Label*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void CriarPorCep_rejeita_valor_nao_positivo(decimal valor)
    {
        var act = () => NovaZonaPorCep(valor: valor);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Valor*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void CriarPorCep_rejeita_tempo_estimado_nao_positivo(int minutos)
    {
        var act = () => NovaZonaPorCep(tempoMinutos: minutos);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Tempo*");
    }

    [Theory]
    [InlineData("1234567")]    // 7 dígitos
    [InlineData("123456789")]  // 9 dígitos
    [InlineData("abc12345")]   // letras
    [InlineData("")]
    [InlineData("        ")]
    public void CriarPorCep_rejeita_cep_inicio_invalido(string cepInicio)
    {
        var act = () => NovaZonaPorCep(cepInicio: cepInicio);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*CEP*");
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("999999999")]
    [InlineData("not-a-cep")]
    public void CriarPorCep_rejeita_cep_fim_invalido(string cepFim)
    {
        var act = () => NovaZonaPorCep(cepFim: cepFim);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*CEP*");
    }

    [Fact]
    public void CriarPorCep_rejeita_range_invertido()
    {
        // CepInicio > CepFim → range inválido (operador confundiu fim com início)
        var act = () => NovaZonaPorCep(cepInicio: "05599999", cepFim: "05500000");

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*intervalo*");
    }

    [Fact]
    public void CriarPorCep_aceita_range_de_um_cep_so_inicio_igual_fim()
    {
        // CEP único — válido (entrega exatamente naquele CEP).
        var act = () => NovaZonaPorCep(cepInicio: "05500000", cepFim: "05500000");

        act.Should().NotThrow();
    }

    [Fact]
    public void CriarPorCep_rejeita_ordem_negativa()
    {
        var act = () => NovaZonaPorCep(ordem: -1);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Ordem*");
    }

    // ── Factory CriarPorBairros: happy path ───────────────────────────

    [Fact]
    public void CriarPorBairros_define_estado_inicial_e_normaliza_bairros()
    {
        // Bairros são salvos lowercase sem acentos pra match case/diacritic-insensitive.
        var storefrontId = Guid.NewGuid();

        var zona = FreteZona.CriarPorBairros(
            storefrontId: storefrontId,
            label: "Butantã + Pinheiros",
            bairros: new[] { "Butantã", "Pinheiros", "  Vila Madalena  " },
            valor: 15m,
            tempoEstimadoMinutos: 60,
            ordem: 2);

        zona.StorefrontId.Should().Be(storefrontId);
        zona.Label.Should().Be("Butantã + Pinheiros");
        zona.Valor.Should().Be(15m);
        zona.TempoEstimadoMinutos.Should().Be(60);
        zona.Ordem.Should().Be(2);
        zona.TipoCobertura.Should().Be("bairros_lista");
        zona.CepInicio.Should().BeNull();
        zona.CepFim.Should().BeNull();

        zona.BairrosJson.Should().NotBeNull();
        zona.BairrosJson.Should().Contain("butanta");
        zona.BairrosJson.Should().Contain("pinheiros");
        zona.BairrosJson.Should().Contain("vila madalena");
    }

    // ── Factory CriarPorBairros: invariantes ──────────────────────────

    [Fact]
    public void CriarPorBairros_rejeita_lista_vazia()
    {
        var act = () => NovaZonaPorBairros(bairros: Array.Empty<string>());

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*bairro*");
    }

    [Fact]
    public void CriarPorBairros_rejeita_lista_null()
    {
        // Chamada direta na factory — o helper NovaZonaPorBairros tem fallback
        // pra default que comeria o null, mascarando o cenário.
        var act = () => FreteZona.CriarPorBairros(
            storefrontId: Guid.NewGuid(),
            label: "Qualquer",
            bairros: null!,
            valor: 10m,
            tempoEstimadoMinutos: 30);

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*bairro*");
    }

    [Fact]
    public void CriarPorBairros_rejeita_bairro_em_branco_dentro_da_lista()
    {
        var act = () => NovaZonaPorBairros(bairros: new[] { "Butantã", "  ", "Pinheiros" });

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*bairro*");
    }

    [Fact]
    public void CriarPorBairros_remove_duplicatas_apos_normalizacao()
    {
        // "Butantã" e "BUTANTA" e "butanta" são o mesmo bairro após normalização.
        var zona = FreteZona.CriarPorBairros(
            storefrontId: Guid.NewGuid(),
            label: "Butantã",
            bairros: new[] { "Butantã", "BUTANTA", "butanta" },
            valor: 10m,
            tempoEstimadoMinutos: 30);

        zona.BairrosJson.Should().NotBeNull();
        // Espera 1 entrada só, não 3.
        var ocorrencias = zona.BairrosJson!.Split("butanta").Length - 1;
        ocorrencias.Should().Be(1, "duplicatas após normalização devem ser removidas");
    }

    // ── CobreCep ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("05500000")]
    [InlineData("05550000")]
    [InlineData("05599999")]
    [InlineData("05500-000")]   // aceita máscara
    [InlineData(" 05550-000 ")] // aceita espaços
    public void CobreCep_retorna_true_para_cep_dentro_do_range(string cep)
    {
        var zona = NovaZonaPorCep(cepInicio: "05500000", cepFim: "05599999");

        zona.CobreCep(cep).Should().BeTrue();
    }

    [Theory]
    [InlineData("05499999")]    // antes do início
    [InlineData("05600000")]    // depois do fim
    [InlineData("01310100")]    // longe
    public void CobreCep_retorna_false_para_cep_fora_do_range(string cep)
    {
        var zona = NovaZonaPorCep(cepInicio: "05500000", cepFim: "05599999");

        zona.CobreCep(cep).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cep")]
    [InlineData("123")]
    public void CobreCep_retorna_false_para_cep_invalido_em_vez_de_throw(string cep)
    {
        // Decisão: CobreCep é consulta — entrada inválida do cliente NÃO deve crashar
        // o checkout, apenas retornar "não cobre". Validação acontece em camada acima.
        var zona = NovaZonaPorCep(cepInicio: "05500000", cepFim: "05599999");

        zona.CobreCep(cep).Should().BeFalse();
    }

    [Fact]
    public void CobreCep_retorna_false_quando_zona_eh_por_bairros()
    {
        // Zona modelada por bairros não cobre nenhum CEP (não há range).
        var zona = NovaZonaPorBairros();

        zona.CobreCep("05500000").Should().BeFalse();
    }

    // ── CobreBairro ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Butantã")]      // exatamente como cadastrado
    [InlineData("butanta")]      // já normalizado
    [InlineData("BUTANTÃ")]      // upper + acento
    [InlineData("  Butantã  ")]  // com espaços extras
    [InlineData("Pinheiros")]
    public void CobreBairro_retorna_true_normalizando_acentos_e_case(string bairro)
    {
        var zona = NovaZonaPorBairros(bairros: new[] { "Butantã", "Pinheiros" });

        zona.CobreBairro(bairro).Should().BeTrue();
    }

    [Theory]
    [InlineData("Lapa")]
    [InlineData("Vila Madalena")] // não cadastrado nessa zona
    [InlineData("")]
    public void CobreBairro_retorna_false_para_bairro_nao_cadastrado(string bairro)
    {
        var zona = NovaZonaPorBairros(bairros: new[] { "Butantã", "Pinheiros" });

        zona.CobreBairro(bairro).Should().BeFalse();
    }

    [Fact]
    public void CobreBairro_retorna_false_quando_zona_eh_por_cep()
    {
        var zona = NovaZonaPorCep();

        zona.CobreBairro("Butantã").Should().BeFalse();
    }

    // ── Toggle Ativa/Desativa ──────────────────────────────────────────

    [Fact]
    public void Desativar_e_Ativar_alteram_estado_e_sao_idempotentes()
    {
        var zona = NovaZonaPorCep();
        zona.Ativa.Should().BeTrue();

        zona.Desativar();
        zona.Ativa.Should().BeFalse();

        zona.Desativar(); // idempotente, sem throw
        zona.Ativa.Should().BeFalse();

        zona.Ativar();
        zona.Ativa.Should().BeTrue();

        zona.Ativar(); // idempotente
        zona.Ativa.Should().BeTrue();
    }

    // ── Ordem (desempate quando 2 zonas cobrem o mesmo CEP) ────────────

    [Fact]
    public void DefinirOrdem_atualiza_ordem_e_rejeita_negativa()
    {
        var zona = NovaZonaPorCep(ordem: 1);

        zona.DefinirOrdem(5);
        zona.Ordem.Should().Be(5);

        var act = () => zona.DefinirOrdem(-1);
        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*Ordem*");
    }
}
