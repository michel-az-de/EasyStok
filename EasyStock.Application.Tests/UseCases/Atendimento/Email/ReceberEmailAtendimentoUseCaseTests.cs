using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.Email;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Email;

/// <summary>
/// #1432: o e-mail recebido vira mensagem na conversa do canal E-mail. Protege a deduplicação pelo Message-ID, o
/// vínculo com o cliente pelo e-mail, o assunto, a fila humana e o anexo no storage privado.
/// </summary>
public class ReceberEmailAtendimentoUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IEmailAtendimentoQuery _query = Substitute.For<IEmailAtendimentoQuery>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IOperacaoEventPublisher _eventos = Substitute.For<IOperacaoEventPublisher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<Mensagem> _gravadas = [];
    private readonly List<Conversa> _abertas = [];

    private static readonly CaixaEmailAtendimento Caixa = new(
        "contato@casadababa.com", "Casa da Baba", "imap.hostinger.com", 993, "smtp.hostinger.com", 465,
        "contato@casadababa.com", "segredo-de-teste", Agora);

    public ReceberEmailAtendimentoUseCaseTests()
    {
        _conversas.AddMensagemAsync(Arg.Do<Mensagem>(_gravadas.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _conversas.AddAsync(Arg.Do<Conversa>(_abertas.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                var r = c.Arg<FileUploadRequest>();
                return new StoredFileResult($"{r.BucketPath}/{r.FileName}", "u", r.ContentType, r.Content.Length);
            });
    }

    private ReceberEmailAtendimentoUseCase Sut() => new(
        _conversas, _query, _storage, _eventos, _uow, new FakeTimeProvider(Agora),
        NullLogger<ReceberEmailAtendimentoUseCase>.Instance);

    private static EmailRecebido Email(
        string de = "Maria@Exemplo.com", string? messageId = "abc@mail.gmail.com", string? assunto = "Re: Encomenda de bolo",
        string texto = "Quero 2 lasanhas.\n\nEm qua., 7 de out. de 2026 às 10:00, Casa da Baba escreveu:\n> oi",
        bool autoGerado = false, params AnexoEmailRecebido[] anexos) =>
        new("7", messageId, de, "Maria Souza", assunto, texto, Agora.AddMinutes(-2), autoGerado, anexos);

    [Fact]
    public async Task PrimeiroEmail_AbreConversaNaFilaHumana_LigadaAoClienteComOAssunto()
    {
        var clienteId = Guid.NewGuid();
        _query.ObterClienteIdPorEmailAsync(_empresaId, "maria@exemplo.com", Arg.Any<CancellationToken>()).Returns(clienteId);

        var r = await Sut().ExecuteAsync(_empresaId, Caixa, Email());

        r.Should().Be(DesfechoEmailRecebido.Gravado);
        var conversa = _abertas.Should().ContainSingle().Subject;
        conversa.Canal.Should().Be(CanalConversa.Email);
        conversa.ContatoIdExterno.Should().Be("maria@exemplo.com");
        conversa.ContatoNome.Should().Be("Maria Souza");
        conversa.ClienteId.Should().Be(clienteId);
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida, "no e-mail não há agente: a conversa nasce com a dona");
        conversa.AssumidaPorUsuarioId.Should().BeNull();
        conversa.Assunto.Should().Be("Encomenda de bolo");
        conversa.NaoLidas.Should().Be(1);

        var mensagem = _gravadas.Should().ContainSingle().Subject;
        mensagem.Direcao.Should().Be(DirecaoMensagem.Entrada);
        mensagem.ExternoId.Should().Be("abc@mail.gmail.com");
        mensagem.Texto.Should().Be("Quero 2 lasanhas.", "o histórico citado sai");
        mensagem.Assunto.Should().Be("Re: Encomenda de bolo");
        mensagem.EnviadaEm.Should().Be(Agora.AddMinutes(-2));
        await _uow.Received(1).CommitAsync();
        await _eventos.Received(1).PublicarAsync("conversa.mensagem_recebida", _empresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MesmoMessageId_NaoGravaDeNovo()
    {
        var conversaId = Guid.NewGuid();
        _conversas.ObterMensagemPorExternoIdAsync(_empresaId, "abc@mail.gmail.com", Arg.Any<CancellationToken>())
            .Returns(Mensagem.Entrada(_empresaId, conversaId, Agora, TipoConteudoMensagem.Texto, "oi", "abc@mail.gmail.com"));

        var r = await Sut().ExecuteAsync(_empresaId, Caixa, Email(messageId: "<abc@mail.gmail.com>"));

        r.Should().Be(DesfechoEmailRecebido.Duplicado);
        _gravadas.Should().BeEmpty();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ConversaAberta_RecebeAMensagem_SemAbrirOutra()
    {
        var existente = Conversa.Abrir(_empresaId, "maria@exemplo.com", Agora.AddDays(-1), "Maria", canal: CanalConversa.Email);
        existente.DefinirAssunto("Encomenda de bolo");
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.Email, "maria@exemplo.com", Arg.Any<CancellationToken>())
            .Returns(existente);

        await Sut().ExecuteAsync(_empresaId, Caixa, Email(assunto: null, messageId: "def@x"));

        _abertas.Should().BeEmpty();
        _gravadas.Should().ContainSingle().Which.ConversaId.Should().Be(existente.Id);
        existente.Assunto.Should().Be("Encomenda de bolo", "resposta sem assunto não apaga o do fio");
    }

    [Theory]
    [InlineData("contato@casadababa.com", false)]
    [InlineData("maria@exemplo.com", true)]
    [InlineData("sem-arroba", false)]
    public async Task RespostaAutomatica_ADaPropriaCaixa_ERemetenteInvalido_SaoIgnorados(string de, bool autoGerado)
    {
        var r = await Sut().ExecuteAsync(_empresaId, Caixa, Email(de: de, autoGerado: autoGerado));

        r.Should().Be(DesfechoEmailRecebido.Ignorado);
        _gravadas.Should().BeEmpty();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Anexos_ViramMensagensComMidiaPrivada_EOGrandeViraAviso()
    {
        var r = await Sut().ExecuteAsync(_empresaId, Caixa, Email(anexos:
        [
            new AnexoEmailRecebido("foto do bolo.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
            new AnexoEmailRecebido("orcamento.pdf", "application/pdf", null),
            new AnexoEmailRecebido("planilha.exe", "application/x-msdownload", [1, 2]),
        ]));

        r.Should().Be(DesfechoEmailRecebido.Gravado);
        _gravadas.Should().HaveCount(4);
        var foto = _gravadas[1];
        foto.TipoConteudo.Should().Be(TipoConteudoMensagem.Imagem);
        foto.Texto.Should().Be("foto do bolo.jpg");
        foto.MidiaChave.Should().StartWith($"atendimento/{_empresaId}/").And.EndWith(".jpg");
        foto.ExternoId.Should().StartWith("anexo:");
        _gravadas[2].MidiaChave.Should().BeNull();
        _gravadas[2].Texto.Should().Contain("orcamento.pdf").And.Contain("grande demais");
        _gravadas[3].Texto.Should().Contain("não aceito");
        await _storage.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(u => !u.IsPublic && u.ContentType == "image/jpeg"), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task FalhaNoCommit_DescartaELanca_ParaOEmailFicarNaoLido()
    {
        _uow.CommitAsync().Returns(Task.FromException<int>(new InvalidOperationException("banco fora")));

        var act = () => Sut().ExecuteAsync(_empresaId, Caixa, Email());

        await act.Should().ThrowAsync<InvalidOperationException>();
        _uow.Received(1).DescartarAlteracoesPendentes();
        await _eventos.DidNotReceive().PublicarAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataNoFuturo_VaiParaAHoraDaLeitura()
    {
        var email = Email() with { RecebidoEm = Agora.AddDays(2) };

        await Sut().ExecuteAsync(_empresaId, Caixa, email);

        _gravadas.Single().EnviadaEm.Should().Be(Agora);
    }
}
