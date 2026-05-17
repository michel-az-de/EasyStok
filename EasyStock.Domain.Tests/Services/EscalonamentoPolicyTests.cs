using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

public class EscalonamentoPolicyTests
{
    private static AdminTicket TicketAtivoSemSla(NivelAtendimento nivel = NivelAtendimento.N1)
    {
        return AdminTicket.Criar(
            Guid.NewGuid(), "Titulo", "Descricao",
            TicketCategoria.Bug, TicketPrioridade.Alta, nivel);
    }

    [Fact]
    public void Nao_escala_quando_ticket_ja_n3()
    {
        var policy = new EscalonamentoPolicy();
        var t = TicketAtivoSemSla(NivelAtendimento.N3);

        var d = policy.Decidir(t, DateTime.UtcNow);

        d.DeveEscalar.Should().BeFalse();
        d.Motivo.Should().Contain("nivel maximo");
    }

    [Fact]
    public void Nao_escala_quando_ticket_resolvido()
    {
        var policy = new EscalonamentoPolicy();
        var t = TicketAtivoSemSla();
        t.Status = TicketStatus.Resolvido;

        var d = policy.Decidir(t, DateTime.UtcNow);

        d.DeveEscalar.Should().BeFalse();
    }

    [Fact]
    public void Nao_escala_quando_nenhum_sla_violado()
    {
        var policy = new EscalonamentoPolicy();
        var t = TicketAtivoSemSla();
        t.PrazoResposta = DateTime.UtcNow.AddHours(1);
        t.PrazoResolucao = DateTime.UtcNow.AddHours(4);

        var d = policy.Decidir(t, DateTime.UtcNow);

        d.DeveEscalar.Should().BeFalse();
    }

    [Fact]
    public void Escala_de_n1_para_n2_quando_sla_resolucao_estourado_e_fora_do_cooldown()
    {
        var policy = new EscalonamentoPolicy();
        var agora = DateTime.UtcNow;
        var t = TicketAtivoSemSla(NivelAtendimento.N1);
        t.PrazoResolucao = agora.AddHours(-2);
        // simula que ticket nao foi tocado ha 7h
        t.AlteradoEm = agora.AddHours(-7);

        var d = policy.Decidir(t, agora);

        d.DeveEscalar.Should().BeTrue();
        d.ProximoNivel.Should().Be(NivelAtendimento.N2);
    }

    [Fact]
    public void Nao_escala_se_em_cooldown_apos_alteracao_recente()
    {
        var policy = new EscalonamentoPolicy();
        var agora = DateTime.UtcNow;
        var t = TicketAtivoSemSla(NivelAtendimento.N1);
        t.PrazoResolucao = agora.AddHours(-2);
        // alterado ha 1h — dentro do cooldown padrao (6h)
        t.AlteradoEm = agora.AddHours(-1);

        var d = policy.Decidir(t, agora);

        d.DeveEscalar.Should().BeFalse();
        d.Motivo.Should().Contain("cooldown");
    }

    [Fact]
    public void Escala_de_n2_para_n3_quando_sla_resposta_estourado()
    {
        var policy = new EscalonamentoPolicy();
        var agora = DateTime.UtcNow;
        var t = TicketAtivoSemSla(NivelAtendimento.N2);
        t.PrazoResposta = agora.AddHours(-3);
        t.AlteradoEm = agora.AddHours(-8);

        var d = policy.Decidir(t, agora);

        d.DeveEscalar.Should().BeTrue();
        d.ProximoNivel.Should().Be(NivelAtendimento.N3);
    }
}
