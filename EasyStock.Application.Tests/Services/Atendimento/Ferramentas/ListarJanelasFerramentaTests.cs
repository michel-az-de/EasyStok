using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>listar_janelas</c> (S16): só janelas com vaga e no prazo mínimo do carrinho, as primeiras como botões
/// <c>acao:escolher_janela:&lt;id&gt;:&lt;data&gt;</c>.
/// </summary>
public class ListarJanelasFerramentaTests
{
    // Terça 02/06/2026, 11:00 em Brasília.
    private static readonly DateTimeOffset AgoraOffset = new(2026, 6, 2, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Agora = AgoraOffset.UtcDateTime;
    private static readonly DateOnly Hoje = new(2026, 6, 2);

    private sealed class Cenario
    {
        public StorefrontEntity Storefront { get; }
        public IStorefrontRepository StorefrontRepo { get; } = Substitute.For<IStorefrontRepository>();
        public ICardapioItemRepository CardapioRepo { get; } = Substitute.For<ICardapioItemRepository>();
        public IConfiguracaoAtendimentoRepository ConfiguracaoRepo { get; } = Substitute.For<IConfiguracaoAtendimentoRepository>();
        public IJanelaEntregaRepository JanelaRepo { get; } = Substitute.For<IJanelaEntregaRepository>();
        public IVagaOcupadaRepository VagaRepo { get; } = Substitute.For<IVagaOcupadaRepository>();
        public IWhatsAppCloudClient Cloud { get; } = Substitute.For<IWhatsAppCloudClient>();
        public IConversaRepository ConversaRepo { get; } = Substitute.For<IConversaRepository>();
        public Conversa Conversa { get; }
        public List<JanelaEntrega> Janelas { get; } = new();
        public List<Mensagem> Mensagens { get; } = new();
        public List<IReadOnlyList<(string Id, string Titulo)>> BotoesEnviados { get; } = new();

        public Cenario()
        {
            Storefront = StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Babá", 0m);
            Storefront.Ativar();
            StorefrontRepo.GetByEmpresaAsync(Storefront.EmpresaId, Arg.Any<CancellationToken>()).Returns(Storefront);
            StorefrontRepo.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(Storefront);

            var configuracao = ConfiguracaoAtendimento.CriarPadrao(Storefront.EmpresaId); // preparo 60 + respiro 40
            ConfiguracaoRepo.GetOrDefaultAsync(Storefront.EmpresaId).Returns(configuracao);

            JanelaRepo.GetAtivasDoStorefrontAsync(Storefront.Id, Arg.Any<CancellationToken>()).Returns(_ => Janelas.ToList());
            VagaRepo.ContarPorJanelaPeriodoAsync(
                    Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<(Guid, DateOnly), int>());

            Cloud.EnviarBotoesAsync(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Do<IReadOnlyList<(string Id, string Titulo)>>(b => BotoesEnviados.Add(b)), Arg.Any<CancellationToken>())
                .Returns(new EnvioWhatsAppResult("wamid.botoes"));
            ConversaRepo.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()))
                .Do(ci => Mensagens.Add(ci.Arg<Mensagem>()));

            Conversa = Conversa.Abrir(Storefront.EmpresaId, "5511999998888", Agora, "Maria");
        }

        public JanelaEntrega Janela(DateOnly dia, int hora, int capacidade = 5)
        {
            var janela = JanelaEntrega.Criar(
                Storefront.Id, (int)dia.DayOfWeek, new TimeOnly(hora, 0), new TimeOnly(hora + 1, 0), capacidade, $"{hora}h");
            Janelas.Add(janela);
            return janela;
        }

        public CardapioItem Item(int? tempoPreparoMinutos)
        {
            var item = CardapioItem.CriarAPartirDeProduto(Storefront.Id, new Produto
            {
                Id = Guid.NewGuid(),
                Nome = "Bolo",
                PrecoReferencia = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(10m),
            });
            item.DefinirPreparo(null, tempoPreparoMinutos, null);
            CardapioRepo.GetByIdAsync(Storefront.Id, item.Id, Arg.Any<CancellationToken>()).Returns(item);
            return item;
        }

        public Task<string> ExecutarAsync(object entrada)
        {
            var listar = new ListarJanelasDisponiveisUseCase(
                StorefrontRepo, JanelaRepo, Substitute.For<IBloqueioEntregaRepository>(), VagaRepo,
                Substitute.For<IFreteZonaRepository>(), new FakeTimeProvider(AgoraOffset));
            var ferramenta = new ListarJanelasFerramenta(
                new ListarJanelasAtendimentoUseCase(StorefrontRepo, CardapioRepo, ConfiguracaoRepo, listar),
                Cloud, ConversaRepo, NullLogger<ListarJanelasFerramenta>.Instance);
            return ferramenta.ExecutarAsync(
                new ContextoTurnoAgente(Storefront.EmpresaId, Conversa, Agora), JsonSerializer.SerializeToElement(entrada));
        }
    }

    [Fact]
    public async Task OfereceSoJanelasNoPrazoComBotoes()
    {
        // Prazo padrão 60 + 40 = 100 min: hoje só a partir de 12:40.
        var c = new Cenario();
        c.Janela(Hoje, 12);
        var hoje13 = c.Janela(Hoje, 13);
        var hoje14 = c.Janela(Hoje, 14);

        var resultado = await c.ExecutarAsync(new { data = "2026-06-02" });

        var json = JsonDocument.Parse(resultado).RootElement;
        json.GetProperty("prazoMinimoMinutos").GetInt32().Should().Be(100);
        json.GetProperty("botoesEnviados").GetInt32().Should().Be(2);
        json.GetProperty("janelas").EnumerateArray().Select(j => j.GetProperty("janelaId").GetGuid())
            .Should().Equal(hoje13.Id, hoje14.Id);

        var botoes = c.BotoesEnviados.Should().ContainSingle().Subject;
        botoes.Should().Equal(
            ($"acao:escolher_janela:{hoje13.Id}:2026-06-02", "02/06 13:00-14:00"),
            ($"acao:escolher_janela:{hoje14.Id}:2026-06-02", "02/06 14:00-15:00"));

        var saida = c.Mensagens.Should().ContainSingle().Subject;
        saida.Autor.Should().Be(AutorMensagem.Agente);
        saida.ExternoId.Should().Be("wamid.botoes");
    }

    [Fact]
    public async Task PrazoUsaMaiorPreparoDosItens()
    {
        // Item de 90 min + respiro 40 = 130: nada antes de 13:10, então 13:00 sai.
        var c = new Cenario();
        c.Janela(Hoje, 13);
        var hoje14 = c.Janela(Hoje, 14);
        var rapido = c.Item(30);
        var demorado = c.Item(90);

        var resultado = await c.ExecutarAsync(new { data = "2026-06-02", itens = new[] { rapido.Id, demorado.Id } });

        var json = JsonDocument.Parse(resultado).RootElement;
        json.GetProperty("prazoMinimoMinutos").GetInt32().Should().Be(130);
        json.GetProperty("janelas").EnumerateArray().Select(j => j.GetProperty("janelaId").GetGuid())
            .Should().Equal(hoje14.Id);
    }

    [Fact]
    public async Task NoMaximoTresBotoesELotadaFicaDeFora()
    {
        var c = new Cenario();
        var amanha = Hoje.AddDays(1);
        var lotada = c.Janela(amanha, 9, capacidade: 1);
        c.VagaRepo.ContarPorJanelaPeriodoAsync(
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), int> { [(lotada.Id, amanha)] = 1 });
        c.Janela(amanha, 10);
        c.Janela(amanha, 11);
        c.Janela(amanha, 12);
        c.Janela(amanha, 13);

        var resultado = await c.ExecutarAsync(new { data = "2026-06-03" });

        var json = JsonDocument.Parse(resultado).RootElement;
        json.GetProperty("janelas").GetArrayLength().Should().Be(4);
        json.GetProperty("botoesEnviados").GetInt32().Should().Be(ListarJanelasFerramenta.MaximoBotoes);
        c.BotoesEnviados.Should().ContainSingle().Which.Should().HaveCount(3);
    }

    [Fact]
    public async Task SemJanelaNoPrazo_OrientaOutraDataSemEnviarBotoes()
    {
        var c = new Cenario();
        c.Janela(Hoje, 12);

        var resultado = await c.ExecutarAsync(new { data = "2026-06-02" });

        var json = JsonDocument.Parse(resultado).RootElement;
        json.GetProperty("janelas").GetArrayLength().Should().Be(0);
        json.TryGetProperty("orientacao", out _).Should().BeTrue();
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarBotoesAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task FalhaNoEnvioDosBotoes_DevolveListaParaOAgente()
    {
        var c = new Cenario();
        var hoje13 = c.Janela(Hoje, 13);
        c.Cloud.EnviarBotoesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<(string, string)>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new WhatsAppCloudException(131047, "fora da janela", ehPermanente: true));

        var resultado = await c.ExecutarAsync(new { data = "2026-06-02" });

        var json = JsonDocument.Parse(resultado).RootElement;
        json.GetProperty("botoesEnviados").GetInt32().Should().Be(0);
        json.GetProperty("janelas")[0].GetProperty("janelaId").GetGuid().Should().Be(hoje13.Id);
        c.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task DataInvalida_DevolveErro()
    {
        var c = new Cenario();

        var resultado = await c.ExecutarAsync(new { data = "02/06/2026" });

        resultado.Should().Contain("data_invalida");
    }
}
