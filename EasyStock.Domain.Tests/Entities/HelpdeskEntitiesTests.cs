using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class AdminTicketHelpdeskTests
{
    [Fact]
    public void Criar_define_status_aberto_nivel_default_n1_e_carimba_datas()
    {
        var t = AdminTicket.Criar(
            empresaId: Guid.NewGuid(),
            titulo: "  Erro ao salvar venda  ",
            descricao: "  steps to reproduce  ",
            categoria: TicketCategoria.Bug,
            prioridade: TicketPrioridade.Alta);

        t.Id.Should().NotBeEmpty();
        t.Status.Should().Be(TicketStatus.Aberto);
        t.Nivel.Should().Be(NivelAtendimento.N1);
        t.Titulo.Should().Be("Erro ao salvar venda"); // trim
        t.Descricao.Should().Be("steps to reproduce");
        t.SlaRespostaViolado.Should().BeFalse();
        t.SlaResolucaoViolado.Should().BeFalse();
        t.PrimeiraRespostaEm.Should().BeNull();
        t.ResolvidoEm.Should().BeNull();
        t.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        t.AlteradoEm.Should().Be(t.CriadoEm);
    }

    [Fact]
    public void Criar_aceita_nivel_e_prazos_explicitos_e_origem_ticket()
    {
        var origem = Guid.NewGuid();
        var prazoResp = DateTime.UtcNow.AddHours(2);
        var prazoResol = DateTime.UtcNow.AddHours(8);

        var t = AdminTicket.Criar(
            empresaId: Guid.NewGuid(),
            titulo: "Bug critico",
            descricao: "descricao do bug",
            categoria: TicketCategoria.BugFixDev,
            prioridade: TicketPrioridade.Critica,
            nivel: NivelAtendimento.N4,
            prazoResposta: prazoResp,
            prazoResolucao: prazoResol,
            origemTicketId: origem,
            criadoPorId: Guid.NewGuid());

        t.Nivel.Should().Be(NivelAtendimento.N4);
        t.PrazoResposta.Should().Be(prazoResp);
        t.PrazoResolucao.Should().Be(prazoResol);
        t.OrigemTicketId.Should().Be(origem);
        t.CriadoPorId.Should().NotBeNull();
    }
}

public class AdminTicketMensagemHelpdeskTests
{
    [Fact]
    public void Criar_default_nao_eh_interno()
    {
        var m = AdminTicketMensagem.Criar(Guid.NewGuid(), Guid.NewGuid(), "resposta", isAdmin: true);
        m.Interno.Should().BeFalse();
        m.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public void Criar_marcado_como_interno_preserva_flag()
    {
        var m = AdminTicketMensagem.Criar(Guid.NewGuid(), Guid.NewGuid(), "  comentario tecnico  ", isAdmin: true, interno: true);
        m.Interno.Should().BeTrue();
        m.Conteudo.Should().Be("comentario tecnico");
        m.LidoPeloAdmin.Should().BeFalse();
    }
}

public class SlaConfiguracaoTests
{
    [Fact]
    public void Criar_global_sem_empresa_nem_plano_aceita_prioridade_e_minutos()
    {
        var sla = SlaConfiguracao.Criar(
            prioridade: TicketPrioridade.Critica,
            minutosResposta: 30,
            minutosResolucao: 240);

        sla.Id.Should().NotBeEmpty();
        sla.EmpresaId.Should().BeNull();
        sla.PlanoId.Should().BeNull();
        sla.Prioridade.Should().Be(TicketPrioridade.Critica);
        sla.MinutosResposta.Should().Be(30);
        sla.MinutosResolucao.Should().Be(240);
        sla.HorarioComercialApenas.Should().BeFalse();
    }

    [Fact]
    public void Criar_override_por_empresa_preenche_empresaId()
    {
        var empresaId = Guid.NewGuid();
        var sla = SlaConfiguracao.Criar(
            prioridade: TicketPrioridade.Alta,
            minutosResposta: 60, minutosResolucao: 360,
            empresaId: empresaId);

        sla.EmpresaId.Should().Be(empresaId);
        sla.PlanoId.Should().BeNull();
    }

    [Fact]
    public void Criar_default_por_plano_preenche_planoId()
    {
        var planoId = Guid.NewGuid();
        var sla = SlaConfiguracao.Criar(
            prioridade: TicketPrioridade.Normal,
            minutosResposta: 480, minutosResolucao: 1440,
            planoId: planoId);

        sla.EmpresaId.Should().BeNull();
        sla.PlanoId.Should().Be(planoId);
    }
}

public class TicketAnexoTests
{
    [Fact]
    public void Criar_define_metadados_e_visibilidade_default_falso()
    {
        var ticketId = Guid.NewGuid();
        var enviadoPor = Guid.NewGuid();

        var a = TicketAnexo.Criar(
            ticketId: ticketId,
            mensagemId: null,
            nomeArquivo: "  log.txt  ",
            contentType: "text/plain",
            tamanhoBytes: 1234,
            storageKey: "tickets/x/y/z.txt",
            url: "https://blob/example",
            isPublico: false,
            enviadoPorId: enviadoPor,
            isAdmin: true);

        a.TicketId.Should().Be(ticketId);
        a.MensagemId.Should().BeNull();
        a.NomeArquivo.Should().Be("log.txt");
        a.IsPublico.Should().BeFalse();
        a.IsAdmin.Should().BeTrue();
        a.TamanhoBytes.Should().Be(1234);
    }
}

public class TicketHistoricoTests
{
    [Fact]
    public void Criar_persiste_acao_autor_e_carimba_data()
    {
        var ticketId = Guid.NewGuid();
        var autorId = Guid.NewGuid();

        var h = TicketHistorico.Criar(
            ticketId, autorId,
            TicketAcaoHistorico.NivelEncaminhado,
            valorAntes: "N1", valorDepois: "N3",
            metadadosJson: "{\"motivo\":\"escalacao\"}");

        h.TicketId.Should().Be(ticketId);
        h.AutorId.Should().Be(autorId);
        h.Acao.Should().Be(TicketAcaoHistorico.NivelEncaminhado);
        h.ValorAntes.Should().Be("N1");
        h.ValorDepois.Should().Be("N3");
        h.MetadadosJson.Should().Contain("escalacao");
        h.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Criar_aceita_autor_nulo_para_acoes_do_sistema()
    {
        var h = TicketHistorico.Criar(Guid.NewGuid(), autorId: null,
            TicketAcaoHistorico.SlaViolado, valorDepois: "Resposta");

        h.AutorId.Should().BeNull();
        h.Acao.Should().Be(TicketAcaoHistorico.SlaViolado);
    }
}

public class AdminTicketTecnicoMetaTests
{
    [Fact]
    public void Criar_default_severidade_media_quando_string_vazia()
    {
        var ticketId = Guid.NewGuid();
        var m = AdminTicketTecnicoMeta.Criar(ticketId, severidadeTecnica: "  ", componenteAfetado: null, stackTrace: null);
        m.TicketId.Should().Be(ticketId);
        m.SeveridadeTecnica.Should().Be("Media");
    }

    [Fact]
    public void Criar_preserva_severidade_componente_e_stack_trim()
    {
        var m = AdminTicketTecnicoMeta.Criar(Guid.NewGuid(), "Critica", "  estoque  ", "stack...");
        m.SeveridadeTecnica.Should().Be("Critica");
        m.ComponenteAfetado.Should().Be("estoque");
        m.StackTrace.Should().Be("stack...");
        m.ResolvidoEm.Should().BeNull();
    }
}
