using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// Sessão anônima do chat do site (S36): presa à loja, vence 24 h depois do último uso e guarda só o
/// hash do token. O contato da conversa é o id da sessão.
/// </summary>
public class SessaoChatSiteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Loja = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void Abrir_ValeVinteEQuatroHoras()
    {
        var sessao = SessaoChatSite.Abrir(Empresa, Loja, Hash, Agora);

        sessao.EstaValida(Agora.AddHours(23).AddMinutes(59)).Should().BeTrue();
        sessao.EstaValida(Agora.AddHours(24)).Should().BeFalse();
        sessao.ContatoIdExterno.Should().Be(sessao.Id.ToString("N"));
        sessao.ConversaId.Should().BeNull();
    }

    [Fact]
    public void RegistrarUso_RenovaAValidade()
    {
        var sessao = SessaoChatSite.Abrir(Empresa, Loja, Hash, Agora);

        sessao.RegistrarUso(Agora.AddHours(20));

        sessao.EstaValida(Agora.AddHours(43)).Should().BeTrue();
        sessao.EstaValida(Agora.AddHours(44)).Should().BeFalse();
    }

    [Fact]
    public void RegistrarUso_Vencida_Lanca()
    {
        var sessao = SessaoChatSite.Abrir(Empresa, Loja, Hash, Agora);

        var act = () => sessao.RegistrarUso(Agora.AddHours(25));

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("curto")]
    [InlineData("")]
    public void Abrir_HashInvalido_Lanca(string hash)
    {
        var act = () => SessaoChatSite.Abrir(Empresa, Loja, hash, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Abrir_SemEmpresaOuLoja_Lanca()
    {
        ((Action)(() => SessaoChatSite.Abrir(Guid.Empty, Loja, Hash, Agora))).Should().Throw<RegraDeDominioVioladaException>();
        ((Action)(() => SessaoChatSite.Abrir(Empresa, Guid.Empty, Hash, Agora))).Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void VincularConversa_GuardaAConversa()
    {
        var sessao = SessaoChatSite.Abrir(Empresa, Loja, Hash, Agora);
        var conversa = Guid.NewGuid();

        sessao.VincularConversa(conversa);

        sessao.ConversaId.Should().Be(conversa);
        ((Action)(() => sessao.VincularConversa(Guid.Empty))).Should().Throw<RegraDeDominioVioladaException>();
    }
    [Fact]
    public void Encerrar_TrocaHashEImpedeNovoUso()
    {
        var sessao = SessaoChatSite.Abrir(Empresa, Loja, Hash, Agora);
        sessao.Encerrar(Agora.AddMinutes(1));
        sessao.TokenHash.Should().HaveLength(64).And.NotBe(Hash);
        sessao.EstaValida(Agora.AddMinutes(1)).Should().BeFalse();
        ((Action)(() => sessao.RegistrarUso(Agora.AddMinutes(2)))).Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Encerrar_VencidaTambemInvalidaHash()
    {
        var sessao = SessaoChatSite.Abrir(Empresa, Loja, Hash, Agora);
        sessao.Encerrar(Agora.AddDays(2));
        sessao.TokenHash.Should().NotBe(Hash);
        sessao.EstaValida(Agora.AddDays(2)).Should().BeFalse();
    }

}
