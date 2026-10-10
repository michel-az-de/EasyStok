using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>S27 (#1186): regras da ocorrência.</summary>
public class OcorrenciaTests
{
    [Fact]
    public void Apurar_preserva_primeiro_responsavel_e_nao_reabre_encerrada()
    {
        var o = Nova();
        var usuario = Guid.NewGuid();
        o.Apurar(usuario, "Gerente", Agora).Should().BeTrue();
        o.Apurar(Guid.NewGuid(), "Outra", Agora.AddMinutes(1)).Should().BeFalse();
        o.ApuradaPorUsuarioId.Should().Be(usuario);
        o.ApuradaPorNome.Should().Be("Gerente");
        o.ApuradaEm.Should().Be(Agora);
        o.Resolver("Concluída", usuario, Agora, "Gerente");
        o.Apurar(Guid.NewGuid(), "Outra", Agora.AddMinutes(2)).Should().BeFalse();
        o.EstaAberta.Should().BeFalse();
        o.ResolvidaPorNome.Should().Be("Gerente");
    }

    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private static Ocorrencia Nova() => Ocorrencia.Abrir(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        OrigemOcorrencia.Agente, CategoriaOcorrencia.Atraso, "  demorou duas horas  ", Agora);

    [Fact]
    public void AbrirNasceAbertaComRelatoLimpo()
    {
        var o = Nova();

        o.Status.Should().Be(StatusOcorrencia.Aberta);
        o.Relato.Should().Be("demorou duas horas");
        o.CriadaEm.Should().Be(Agora);
        o.ResolvidaEm.Should().BeNull();
    }

    [Fact]
    public void AbrirSemRelatoFalha()
    {
        var act = () => Ocorrencia.Abrir(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            OrigemOcorrencia.Dona, CategoriaOcorrencia.Outro, " ", Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ResolverGravaQuemEQuando()
    {
        var o = Nova();
        var usuario = Guid.NewGuid();

        o.Resolver("pedido refeito", usuario, Agora.AddHours(1));

        o.Status.Should().Be(StatusOcorrencia.Resolvida);
        o.Resolucao.Should().Be("pedido refeito");
        o.ResolvidaPorUsuarioId.Should().Be(usuario);
        o.ResolvidaEm.Should().Be(Agora.AddHours(1));
    }

    [Fact]
    public void ResolverDuasVezesFalha()
    {
        var o = Nova();
        o.Resolver("ok", Guid.NewGuid(), Agora);

        var act = () => o.Resolver("de novo", Guid.NewGuid(), Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ReembolsoGravaValorIdEInstante()
    {
        var o = Nova();

        o.RegistrarReembolso(30m, "ref-1", Agora);

        o.ReembolsoValor.Should().Be(30m);
        o.ReembolsoIdSolicitacao.Should().Be("ref-1");
        o.ReembolsoEm.Should().Be(Agora);
    }

    [Fact]
    public void ReembolsoManualGuardaSoOValor()
    {
        var o = Nova();

        o.RegistrarReembolsoManual(12m);

        o.ReembolsoValor.Should().Be(12m);
        o.ReembolsoEm.Should().BeNull();
    }

    [Fact]
    public void ReembolsoDuasVezesFalha()
    {
        var o = Nova();
        o.RegistrarReembolso(30m, "ref-1", Agora);

        var act = () => o.RegistrarReembolso(10m, "ref-2", Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
