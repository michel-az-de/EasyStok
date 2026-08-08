using System.Text;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// ADR-0047: a pendencia do login em duas etapas carrega a senha entre os passos. Em
/// producao a sessao vive no Redis (<c>ConnectionStrings:Redis</c>) com IdleTimeout de 8h —
/// grava-la em claro a deixaria legivel na rede pelo resto do dia. Estes testes travam as
/// duas garantias: o payload e cifrado e o prazo de 5 min e criptografico, nao so uma
/// checagem que alguem precisa vir fazer.
/// </summary>
public class LoginPendenteProtecaoTests
{
    private const string SenhaSecreta = "SenhaSuperSecreta#2026";

    private static (SessionService svc, FakeSession sessao) Montar()
    {
        var sessao = new FakeSession();
        var httpCtx = new DefaultHttpContext { Session = sessao };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpCtx);

        return (new SessionService(accessor, new EphemeralDataProtectionProvider()), sessao);
    }

    private static LoginPendente Pendente(TimeSpan? validade = null) =>
        new("felipe@easystok.com", SenhaSecreta, ManterLogado: false, ReturnUrl: null,
            [new EmpresaLoginItem("e1", "Casa da Babá")],
            DateTime.UtcNow.Add(validade ?? SessionService.LoginPendenteTtl));

    [Fact]
    public void Senha_nao_aparece_em_claro_no_que_e_gravado()
    {
        var (svc, sessao) = Montar();

        svc.SetLoginPendente(Pendente());

        var gravado = sessao.Bruto("login_pendente");
        gravado.Should().NotBeNullOrEmpty();
        gravado.Should().NotContain(SenhaSecreta);
        gravado.Should().NotContain("felipe@easystok.com");
    }

    [Fact]
    public void Pendencia_cifrada_volta_intacta()
    {
        var (svc, _) = Montar();
        svc.SetLoginPendente(Pendente());

        var lido = svc.GetLoginPendente(DateTime.UtcNow);

        lido.Should().NotBeNull();
        lido!.Senha.Should().Be(SenhaSecreta);
        lido.Empresas.Should().ContainSingle().Which.Nome.Should().Be("Casa da Babá");
    }

    [Fact]
    public void Prazo_vencido_nao_decifra_nem_com_a_chave_ainda_na_sessao()
    {
        var (svc, sessao) = Montar();
        svc.SetLoginPendente(Pendente(validade: TimeSpan.FromMinutes(-1)));

        // A chave continua la (a sessao so expira em 8h) — o que caducou foi o conteudo.
        sessao.Bruto("login_pendente").Should().NotBeNullOrEmpty();
        svc.GetLoginPendente(DateTime.UtcNow).Should().BeNull();
        sessao.Bruto("login_pendente").Should().BeNull("leitura de pendencia invalida ja limpa");
    }

    [Fact]
    public void Payload_adulterado_e_descartado_sem_explodir()
    {
        var (svc, sessao) = Montar();
        svc.SetLoginPendente(Pendente());
        sessao.SetString("login_pendente", "isto-nao-e-um-payload-valido");

        svc.GetLoginPendente(DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void Chave_de_data_protection_trocada_invalida_a_pendencia()
    {
        // Redeploy troca as chaves efemeras: a pendencia antiga deixa de ser legivel — e
        // isso e desejavel, ninguem deve retomar um login pendente de outro processo.
        var sessao = new FakeSession();
        var httpCtx = new DefaultHttpContext { Session = sessao };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpCtx);

        new SessionService(accessor, new EphemeralDataProtectionProvider()).SetLoginPendente(Pendente());
        var outroProcesso = new SessionService(accessor, new EphemeralDataProtectionProvider());

        outroProcesso.GetLoginPendente(DateTime.UtcNow).Should().BeNull();
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);

        /// <summary>O que de fato iria para o Redis.</summary>
        public string? Bruto(string key) =>
            _store.TryGetValue(key, out var v) ? Encoding.UTF8.GetString(v) : null;
    }
}
