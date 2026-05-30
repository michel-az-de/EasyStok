using EasyStock.Api.Http;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Http;

public class RequestGuardsTests
{
    // ── TryValidarMotivo ─────────────────────────────────────────────────────

    [Fact]
    public void TryValidarMotivo_DeveAceitar_QuandoMotivoTemTamanhoMinimo()
    {
        var ok = RequestGuards.TryValidarMotivo("auditoria SOX Q2", out var motivo, out var erro);

        ok.Should().BeTrue();
        motivo.Should().Be("auditoria SOX Q2");
        erro.Should().BeNull();
    }

    [Fact]
    public void TryValidarMotivo_DeveRejeitar_QuandoMotivoNulo()
    {
        var ok = RequestGuards.TryValidarMotivo(null, out var motivo, out var erro);

        ok.Should().BeFalse();
        motivo.Should().BeEmpty();
        erro.Should().Contain("Justificativa obrigatória");
        erro.Should().Contain("mínimo 10 caracteres");
    }

    [Fact]
    public void TryValidarMotivo_DeveRejeitar_QuandoMotivoVazio()
    {
        var ok = RequestGuards.TryValidarMotivo("   ", out var motivo, out var erro);

        ok.Should().BeFalse();
        motivo.Should().BeEmpty();
        erro.Should().NotBeNull();
    }

    [Fact]
    public void TryValidarMotivo_DeveRejeitar_QuandoMotivoMenorQueMinimo()
    {
        var ok = RequestGuards.TryValidarMotivo("curto", out _, out var erro);

        ok.Should().BeFalse();
        erro.Should().Contain("mínimo 10 caracteres");
    }

    [Fact]
    public void TryValidarMotivo_DeveRejeitar_QuandoMotivoMaiorQueMaximo()
    {
        var motivoLongo = new string('a', 1001);

        var ok = RequestGuards.TryValidarMotivo(motivoLongo, out _, out var erro);

        ok.Should().BeFalse();
        erro.Should().Contain("muito longa");
        erro.Should().Contain("máx 1000 caracteres");
    }

    [Fact]
    public void TryValidarMotivo_DeveNormalizar_TrimDeEspacosNasBordas()
    {
        var ok = RequestGuards.TryValidarMotivo("  ajuste de cadastro  ", out var motivo, out _);

        ok.Should().BeTrue();
        motivo.Should().Be("ajuste de cadastro");
    }

    [Fact]
    public void TryValidarMotivo_DeveRespeitar_MinimoCustomizado()
    {
        // Caller pode passar minLen=20 para operacoes irreversiveis (ex: anonimizacao LGPD)
        var ok = RequestGuards.TryValidarMotivo("apenas 18 chars--", out _, out var erro, minLen: 20);

        ok.Should().BeFalse();
        erro.Should().Contain("mínimo 20 caracteres");
    }

    [Fact]
    public void TryValidarMotivo_DeveAceitar_NoLimiteMaximoExato()
    {
        var motivo = new string('a', 1000);

        var ok = RequestGuards.TryValidarMotivo(motivo, out var normalizado, out var erro);

        ok.Should().BeTrue();
        normalizado.Should().HaveLength(1000);
        erro.Should().BeNull();
    }
}
