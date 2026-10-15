using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.UseCases.Atendimento.Email;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Email;

/// <summary>#1432: a caixa de suporte é gravada cifrada, a senha nunca volta e a senha vazia mantém a gravada.</summary>
public class CaixaEmailUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc);
    private const string SenhaGravada = "senha-gravada-de-teste";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();
    private readonly ICaixaEmailCliente _cliente = Substitute.For<ICaixaEmailCliente>();

    private static CaixaEmailEntrada Entrada(string? senha = "nova-senha-de-teste", string endereco = " Contato@CasaDaBaba.com ") =>
        new(endereco, "Casa da Baba", "imap.hostinger.com", 993, "smtp.hostinger.com", 465, "contato@casadababa.com", senha);

    private void ComCaixaGravada() =>
        _credenciais.ObterAsync<CaixaEmailAtendimento>(_empresaId, CaixaEmailAtendimento.ProviderKey,
                AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
            .Returns(new CaixaEmailAtendimento("contato@casadababa.com", "Casa da Baba", "imap.hostinger.com", 993,
                "smtp.hostinger.com", 465, "contato@casadababa.com", SenhaGravada, Agora.AddDays(-1)));

    [Fact]
    public async Task Salvar_GravaCifradoNaMensageria_EOResumoNaoTemSenha()
    {
        var resumo = await new SalvarCaixaEmailUseCase(_credenciais, new FakeTimeProvider(Agora))
            .ExecuteAsync(_empresaId, _usuarioId, Entrada());

        await _credenciais.Received(1).SalvarAsync(_empresaId, CategoriaIntegracao.Mensageria, CaixaEmailAtendimento.ProviderKey,
            AmbienteIntegracao.Production,
            Arg.Is<CaixaEmailAtendimento>(c => c.Endereco == "contato@casadababa.com" && c.Senha == "nova-senha-de-teste"
                                              && c.ImapPorta == 993 && c.SmtpPorta == 465 && c.AtualizadaEm == Agora),
            _usuarioId, null, Arg.Any<CancellationToken>());
        resumo.SenhaDefinida.Should().BeTrue();
        typeof(CaixaEmailResumo).GetProperties().Select(p => p.Name).Should().NotContain("Senha");
        resumo.ToString().Should().NotContain("nova-senha-de-teste");
    }

    [Fact]
    public async Task Salvar_SenhaVaziaMantemAGravada()
    {
        ComCaixaGravada();

        await new SalvarCaixaEmailUseCase(_credenciais, new FakeTimeProvider(Agora))
            .ExecuteAsync(_empresaId, _usuarioId, Entrada(senha: ""));

        await _credenciais.Received(1).SalvarAsync(_empresaId, Arg.Any<CategoriaIntegracao>(), Arg.Any<string>(),
            Arg.Any<AmbienteIntegracao>(), Arg.Is<CaixaEmailAtendimento>(c => c.Senha == SenhaGravada),
            Arg.Any<Guid>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("contato", "imap.hostinger.com", 993, "Endereço")]
    [InlineData("contato@casadababa.com", "imap.hostinger.com:993", 993, "Servidor IMAP")]
    [InlineData("contato@casadababa.com", "imap.hostinger.com", 0, "Porta IMAP")]
    public async Task Salvar_RecusaDadosInvalidos(string endereco, string imap, int porta, string campo)
    {
        var entrada = Entrada(endereco: endereco) with { ImapHost = imap, ImapPorta = porta };

        var act = () => new SalvarCaixaEmailUseCase(_credenciais, new FakeTimeProvider(Agora))
            .ExecuteAsync(_empresaId, _usuarioId, entrada);

        (await act.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain(campo);
    }

    [Fact]
    public async Task Salvar_PrimeiraVezSemSenha_Recusa()
    {
        var act = () => new SalvarCaixaEmailUseCase(_credenciais, new FakeTimeProvider(Agora))
            .ExecuteAsync(_empresaId, _usuarioId, Entrada(senha: null));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*senha*");
    }

    [Fact]
    public async Task Obter_NaoDevolveASenha()
    {
        ComCaixaGravada();

        var resumo = await new ObterCaixaEmailUseCase(_credenciais).ExecuteAsync(_empresaId);

        resumo!.Endereco.Should().Be("contato@casadababa.com");
        resumo.SenhaDefinida.Should().BeTrue();
        resumo.ToString().Should().NotContain(SenhaGravada);
        new CaixaEmailAtendimento("a@b.com", null, "i", 1, "s", 2, "u", SenhaGravada, Agora).ToString()
            .Should().NotContain(SenhaGravada, "a caixa interpolada num log não pode vazar a senha");
    }

    [Fact]
    public async Task Testar_ComDadosDaTelaUsaASenhaGravada_ESemNadaRecusa()
    {
        ComCaixaGravada();
        _cliente.TestarAsync(Arg.Any<CaixaEmailAtendimento>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoTesteCaixaEmail(true, null, false, "SMTP: usuário ou senha recusados"));
        var sut = new TestarCaixaEmailUseCase(_credenciais, _cliente, new FakeTimeProvider(Agora));

        var r = await sut.ExecuteAsync(_empresaId, Entrada(senha: null) with { SmtpPorta = 587 });

        r.Ok.Should().BeFalse();
        await _cliente.Received(1).TestarAsync(
            Arg.Is<CaixaEmailAtendimento>(c => c.Senha == SenhaGravada && c.SmtpPorta == 587), Arg.Any<CancellationToken>());

        var semCaixa = new TestarCaixaEmailUseCase(Substitute.For<IIntegrationCredentialResolver>(), _cliente, new FakeTimeProvider(Agora));
        await semCaixa.Invoking(s => s.ExecuteAsync(_empresaId, null)).Should().ThrowAsync<UseCaseValidationException>();
    }
}
