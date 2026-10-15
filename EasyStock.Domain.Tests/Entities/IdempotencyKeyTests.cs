using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>
/// issue 917 Fase B: modelo INSERT-first. CriarPendente substitui Criar (que só existia
/// depois do 2xx completar) — a linha nasce Pendente e transiciona para Concluido/Falhou
/// via os métodos abaixo, testados aqui no nível do agregado puro (sem repositório/CAS,
/// que são testados via integração contra Postgres real).
/// </summary>
public class IdempotencyKeyTests
{
    [Fact]
    public void CriarPendente_define_status_pendente_e_expiracao_baseada_em_TTL()
    {
        var antes = DateTime.UtcNow;
        var entry = IdempotencyKey.CriarPendente("k1", Guid.NewGuid(), "POST /api/estoque/saida", "hash123", TimeSpan.FromHours(2));

        entry.Key.Should().Be("k1");
        entry.Status.Should().Be(StatusIdempotencyKey.Pendente);
        entry.PayloadHash.Should().Be("hash123");
        entry.HttpStatus.Should().Be(0);
        entry.RespostaJson.Should().BeNull();
        entry.CriadoEm.Should().BeOnOrAfter(antes);
        entry.LockedAtUtc.Should().BeOnOrAfter(antes);
        entry.ExpiraEm.Should().BeCloseTo(entry.CriadoEm.AddHours(2), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Expirou_retorna_true_apos_a_data_limite()
    {
        var entry = IdempotencyKey.CriarPendente("k", Guid.NewGuid(), "POST /x", null, TimeSpan.FromMinutes(5));

        entry.Expirou(entry.ExpiraEm.AddSeconds(1)).Should().BeTrue();
        entry.Expirou(entry.ExpiraEm.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void MarcarConcluido_transiciona_status_e_grava_resposta()
    {
        var entry = IdempotencyKey.CriarPendente("k", Guid.NewGuid(), "POST /x", "hash", TimeSpan.FromHours(1));
        var quando = DateTime.UtcNow.AddSeconds(5);

        entry.MarcarConcluido(200, "{\"ok\":true}", quando);

        entry.Status.Should().Be(StatusIdempotencyKey.Concluido);
        entry.HttpStatus.Should().Be(200);
        entry.RespostaJson.Should().Be("{\"ok\":true}");
        entry.LockedAtUtc.Should().Be(quando);
    }

    [Fact]
    public void MarcarFalhou_transiciona_status_sem_mexer_na_resposta()
    {
        var entry = IdempotencyKey.CriarPendente("k", Guid.NewGuid(), "POST /x", "hash", TimeSpan.FromHours(1));
        var quando = DateTime.UtcNow.AddSeconds(5);

        entry.MarcarFalhou(quando);

        entry.Status.Should().Be(StatusIdempotencyKey.Falhou);
        entry.HttpStatus.Should().Be(0);
        entry.RespostaJson.Should().BeNull();
        entry.LockedAtUtc.Should().Be(quando);
    }

    [Fact]
    public void LeaseExpirado_false_antes_da_duracao_do_lease()
    {
        var entry = IdempotencyKey.CriarPendente("k", Guid.NewGuid(), "POST /x", null, TimeSpan.FromHours(1));

        entry.LeaseExpirado(entry.LockedAtUtc.Add(IdempotencyKey.LeaseDuration).AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void LeaseExpirado_true_apos_a_duracao_do_lease()
    {
        var entry = IdempotencyKey.CriarPendente("k", Guid.NewGuid(), "POST /x", null, TimeSpan.FromHours(1));

        entry.LeaseExpirado(entry.LockedAtUtc.Add(IdempotencyKey.LeaseDuration).AddSeconds(1)).Should().BeTrue();
    }
}
