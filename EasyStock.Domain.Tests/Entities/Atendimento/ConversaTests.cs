using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// Testes do agregado <see cref="Conversa"/> (S04, ADR-0050).
///
/// Uma conversa e o estado de atendimento de um contato de WhatsApp: nasce
/// <c>Automatica</c> (o agente responde), passa a <c>Assumida</c> quando a dona
/// escreve ou o agente escala, e termina <c>Encerrada</c>. A janela de 24 h da
/// Meta e derivada da ultima mensagem de entrada. O instante vem sempre por
/// parametro: o dominio nao le relogio ambiente.
/// </summary>
public class ConversaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
    private const string WaId = "5511999990001";

    private static Conversa Nova(DateTime? agora = null) =>
        Conversa.Abrir(Empresa, WaId, agora ?? Agora, contatoNome: "Tatiana");

    // ── Abrir ──────────────────────────────────────────────────────────

    [Fact]
    public void Abrir_NormalizaWaIdEComecaAutomatica()
    {
        var conversa = Conversa.Abrir(Empresa, "+55 (11) 99999-0001", Agora, contatoNome: "  Tatiana ");

        conversa.Id.Should().NotBeEmpty();
        conversa.EmpresaId.Should().Be(Empresa);
        conversa.ContatoWaId.Should().Be(WaId, "so digitos, sem '+', espacos ou mascara");
        conversa.ContatoNome.Should().Be("Tatiana");
        conversa.Canal.Should().Be(CanalConversa.WhatsApp);
        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        conversa.EstaAberta.Should().BeTrue();
        conversa.IniciadaEm.Should().Be(Agora);
        conversa.UltimaMensagemEm.Should().Be(Agora);
        conversa.UltimaMensagemEntradaEm.Should().BeNull();
        conversa.NaoLidas.Should().Be(0);
        conversa.ContextoJson.Should().Be("{}");
        conversa.ClienteId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("5511999990001234567")]
    public void Abrir_ComWaIdInvalido_Lanca(string waId)
    {
        var act = () => Conversa.Abrir(Empresa, waId, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Abrir_SemEmpresa_Lanca()
    {
        var act = () => Conversa.Abrir(Guid.Empty, WaId, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ── Assumir / liberar / encerrar ───────────────────────────────────

    [Fact]
    public void AssumirEmEncerradaLanca()
    {
        var conversa = Nova();
        conversa.Encerrar(Agora.AddMinutes(5));

        var act = () => conversa.Assumir(Agora.AddMinutes(6), usuarioId: Guid.NewGuid());

        act.Should().Throw<RegraDeDominioVioladaException>();
        conversa.Situacao.Should().Be(SituacaoConversa.Encerrada);
    }

    [Fact]
    public void Assumir_EhIdempotenteEGuardaUsuario()
    {
        var conversa = Nova();
        var dona = Guid.NewGuid();

        conversa.Assumir(Agora.AddMinutes(1), dona);
        conversa.Assumir(Agora.AddMinutes(2), dona);

        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().Be(dona);
    }

    [Fact]
    public void LiberarAutomatico_VoltaParaAutomaticaESolta_Usuario()
    {
        var conversa = Nova();
        conversa.Assumir(Agora.AddMinutes(1), Guid.NewGuid());

        conversa.LiberarAutomatico();

        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        conversa.AssumidaPorUsuarioId.Should().BeNull();
    }

    [Fact]
    public void LiberarAutomatico_EmEncerrada_Lanca()
    {
        var conversa = Nova();
        conversa.Encerrar(Agora.AddMinutes(1));

        var act = () => conversa.LiberarAutomatico();

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Encerrar_EhIdempotenteEPreservaPrimeiroCarimbo()
    {
        var conversa = Nova();

        conversa.Encerrar(Agora.AddMinutes(1));
        conversa.Encerrar(Agora.AddMinutes(9));

        conversa.Situacao.Should().Be(SituacaoConversa.Encerrada);
        conversa.EstaAberta.Should().BeFalse();
        conversa.EncerradaEm.Should().Be(Agora.AddMinutes(1));
    }

    // ── Janela de 24 h ─────────────────────────────────────────────────

    [Fact]
    public void Janela24h()
    {
        var conversa = Nova();
        var entrada = Agora.AddMinutes(10);

        conversa.DentroDaJanela24h(Agora).Should().BeFalse("sem mensagem de entrada nao ha janela");

        conversa.RegistrarEntrada(entrada);

        conversa.DentroDaJanela24h(entrada.AddHours(23)).Should().BeTrue();
        conversa.DentroDaJanela24h(entrada.AddHours(25)).Should().BeFalse();
    }

    // ── Mensagens ──────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEntrada_AtualizaCarimbosEContaNaoLidas()
    {
        var conversa = Nova();
        var t1 = Agora.AddMinutes(1);
        var t2 = Agora.AddMinutes(2);

        conversa.RegistrarEntrada(t1);
        conversa.RegistrarEntrada(t2);

        conversa.UltimaMensagemEm.Should().Be(t2);
        conversa.UltimaMensagemEntradaEm.Should().Be(t2);
        conversa.NaoLidas.Should().Be(2);

        conversa.MarcarLida();

        conversa.NaoLidas.Should().Be(0);
    }

    [Fact]
    public void RegistrarSaida_AtualizaUltimaMensagemMasNaoAJanela()
    {
        var conversa = Nova();
        var entrada = Agora.AddMinutes(1);
        var saida = Agora.AddMinutes(2);
        conversa.RegistrarEntrada(entrada);

        conversa.RegistrarSaida(saida);

        conversa.UltimaMensagemEm.Should().Be(saida);
        conversa.UltimaMensagemEntradaEm.Should().Be(entrada, "a janela de 24 h e contada da ultima mensagem do cliente");
        conversa.NaoLidas.Should().Be(1, "saida nao mexe no contador de nao lidas");
    }

    [Fact]
    public void RegistrarEntrada_EmEncerrada_Lanca()
    {
        var conversa = Nova();
        conversa.Encerrar(Agora.AddMinutes(1));

        var act = () => conversa.RegistrarEntrada(Agora.AddMinutes(2));

        act.Should().Throw<RegraDeDominioVioladaException>("apos encerrar, a proxima mensagem abre conversa nova");
    }

    // ── Contexto e vinculos ────────────────────────────────────────────

    [Fact]
    public void DefinirContexto_AceitaJsonEVaziaViraObjeto()
    {
        var conversa = Nova();

        conversa.DefinirContexto("{\"etapa\":\"endereco\",\"carrinho\":[]}");
        conversa.ContextoJson.Should().Contain("\"etapa\"");

        conversa.DefinirContexto(null);
        conversa.ContextoJson.Should().Be("{}");
    }

    [Fact]
    public void DefinirContexto_JsonInvalido_Lanca()
    {
        var conversa = Nova();

        var act = () => conversa.DefinirContexto("{etapa:");

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void VincularClienteEPedido_GuardamIdsERejeitamVazio()
    {
        var conversa = Nova();
        var cliente = Guid.NewGuid();
        var pedido = Guid.NewGuid();

        conversa.VincularCliente(cliente);
        conversa.DefinirPedidoEmAndamento(pedido);

        conversa.ClienteId.Should().Be(cliente);
        conversa.PedidoEmAndamentoId.Should().Be(pedido);

        var act = () => conversa.VincularCliente(Guid.Empty);
        act.Should().Throw<RegraDeDominioVioladaException>();

        conversa.DefinirPedidoEmAndamento(null);
        conversa.PedidoEmAndamentoId.Should().BeNull();
    }
}
