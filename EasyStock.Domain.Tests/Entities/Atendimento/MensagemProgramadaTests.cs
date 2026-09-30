using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// Mensagem programada ao cliente (S39, ADR-0051): valida no agendamento e de novo no disparo, porque
/// a janela do canal pode vencer entre um e outro. Fora da janela, só o modelo aprovado passa, e só
/// onde o canal aceita modelo (WhatsApp).
/// </summary>
public class MensagemProgramadaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ModeloMensagem Modelo = new("pedido_pronto", "pt_BR", ["#123"]);

    private static MensagemProgramada Texto(CanalConversa canal, DateTime? para = null, FinalidadeContato finalidade = FinalidadeContato.Transacional) =>
        MensagemProgramada.Agendar(Empresa, Cliente, conversaId: null, canal, finalidade,
            "Seu pedido fica pronto amanhã às 11h.", modelo: null, para ?? Agora.AddHours(2), Usuario, Agora);

    private static Conversa ConversaComEntrada(CanalConversa canal, string contato, DateTime entrada)
    {
        var conversa = Conversa.Abrir(Empresa, contato, entrada, canal: canal);
        conversa.RegistrarEntrada(entrada);
        return conversa;
    }

    [Fact]
    public void Agendar_NasceAgendadaSemTentativas()
    {
        var mensagem = Texto(CanalConversa.Email);

        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Agendada);
        mensagem.Tentativas.Should().Be(0);
        mensagem.AgendadaPara.Should().Be(Agora.AddHours(2));
    }

    [Fact]
    public void Agendar_NoPassado_Lanca()
    {
        var act = () => Texto(CanalConversa.Email, para: Agora.AddMinutes(-1));

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*passado*");
    }

    [Fact]
    public void Agendar_SemTextoNemModelo_OuComOsDois_Lanca()
    {
        var semNada = () => MensagemProgramada.Agendar(Empresa, Cliente, null, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "  ", null, Agora.AddHours(1), Usuario, Agora);
        var comOsDois = () => MensagemProgramada.Agendar(Empresa, Cliente, null, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "oi", Modelo, Agora.AddHours(1), Usuario, Agora);

        semNada.Should().Throw<RegraDeDominioVioladaException>();
        comOsDois.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Agendar_ModeloEmCanalSemModelo_Lanca()
    {
        var act = () => MensagemProgramada.Agendar(Empresa, Cliente, null, CanalConversa.Sms,
            FinalidadeContato.Transacional, null, Modelo, Agora.AddHours(1), Usuario, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*modelo*");
    }

    [Fact]
    public void WhatsAppTextoForaDaJanelaNoEnvio_Recusa_ComModeloPassa()
    {
        var conversa = ConversaComEntrada(CanalConversa.WhatsApp, "5511988887777", Agora);
        var texto = MensagemProgramada.Agendar(Empresa, Cliente, conversa.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "Lembrete", null, Agora.AddHours(25), Usuario, Agora);
        var modelo = MensagemProgramada.Agendar(Empresa, Cliente, conversa.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, null, Modelo, Agora.AddHours(25), Usuario, Agora);

        var foraDaJanela = () => texto.GarantirPodeSairPor(conversa, texto.AgendadaPara);

        foraDaJanela.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*modelo aprovado*");
        modelo.Invoking(m => m.GarantirPodeSairPor(conversa, m.AgendadaPara)).Should().NotThrow();
    }

    [Fact]
    public void WhatsAppTextoDentroDaJanelaNoEnvio_Passa()
    {
        var conversa = ConversaComEntrada(CanalConversa.WhatsApp, "5511988887777", Agora);
        var texto = MensagemProgramada.Agendar(Empresa, Cliente, conversa.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "Lembrete", null, Agora.AddHours(23), Usuario, Agora);

        texto.Invoking(m => m.GarantirPodeSairPor(conversa, m.AgendadaPara)).Should().NotThrow();
    }

    [Fact]
    public void InstagramForaDaJanelaOuSemConversa_Recusa()
    {
        var conversa = ConversaComEntrada(CanalConversa.Instagram, "17841400000000001", Agora);
        var programada = MensagemProgramada.Agendar(Empresa, Cliente, conversa.Id, CanalConversa.Instagram,
            FinalidadeContato.Transacional, "Oi", null, Agora.AddHours(30), Usuario, Agora);

        programada.Invoking(m => m.GarantirPodeSairPor(conversa, m.AgendadaPara))
            .Should().Throw<RegraDeDominioVioladaException>();
        programada.Invoking(m => m.GarantirPodeSairPor(null, m.AgendadaPara))
            .Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void EmailESmsSemConversa_Passam()
    {
        Texto(CanalConversa.Email).Invoking(m => m.GarantirPodeSairPor(null, m.AgendadaPara)).Should().NotThrow();
        Texto(CanalConversa.Sms).Invoking(m => m.GarantirPodeSairPor(null, m.AgendadaPara)).Should().NotThrow();
    }

    [Fact]
    public void Ciclo_ReservarEnviar()
    {
        var mensagem = Texto(CanalConversa.Email);

        mensagem.Reservar(Agora.AddHours(2));
        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Enviando);
        mensagem.Tentativas.Should().Be(1);

        mensagem.RegistrarEnvio("id-externo", Agora.AddHours(2));
        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Enviada);
        mensagem.IdExterno.Should().Be("id-externo");
        mensagem.EnviadaEm.Should().Be(Agora.AddHours(2));
    }

    [Fact]
    public void RegistrarFalha_GuardaMotivo()
    {
        var mensagem = Texto(CanalConversa.Email);
        mensagem.Reservar(Agora.AddHours(2));

        mensagem.RegistrarFalha("janela vencida", Agora.AddHours(2));

        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Falhou);
        mensagem.Erro.Should().Be("janela vencida");
    }

    [Fact]
    public void Cancelar_SoAgendada()
    {
        var agendada = Texto(CanalConversa.Email);
        agendada.Cancelar(Agora.AddMinutes(1));
        agendada.Situacao.Should().Be(SituacaoMensagemProgramada.Cancelada);

        var enviando = Texto(CanalConversa.Email);
        enviando.Reservar(Agora.AddHours(2));
        enviando.Invoking(m => m.Cancelar(Agora.AddHours(2))).Should().Throw<RegraDeDominioVioladaException>();

        agendada.Invoking(m => m.Reservar(Agora.AddHours(2))).Should().Throw<RegraDeDominioVioladaException>("cancelada não sai");
    }
}
