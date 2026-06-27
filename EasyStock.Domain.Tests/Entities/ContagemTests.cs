using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class ContagemTests
{
    [Fact]
    public void Criar_inicia_em_rascunho_com_campos_e_observacao_trimada()
    {
        var empresaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var c = Contagem.Criar(empresaId, EscopoContagem.Todos, null,
            ModoContagem.Cego, EstrategiaLoteContagem.Guiado, userId, "  inventario geral  ");

        c.Status.Should().Be(StatusContagem.Rascunho);
        c.EmpresaId.Should().Be(empresaId);
        c.CriadoPorUsuarioId.Should().Be(userId);
        c.Escopo.Should().Be(EscopoContagem.Todos);
        c.EscopoRefId.Should().BeNull();
        c.Observacao.Should().Be("inventario geral");
        c.IniciadoEm.Should().BeNull();
        c.EstaTerminal.Should().BeFalse();
    }

    [Fact]
    public void Criar_escopo_todos_com_ref_lanca()
    {
        Action act = () => Contagem.Criar(Guid.NewGuid(), EscopoContagem.Todos, Guid.NewGuid(),
            ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid());
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Criar_escopo_categoria_sem_ref_lanca()
    {
        Action act = () => Contagem.Criar(Guid.NewGuid(), EscopoContagem.Categoria, null,
            ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid());
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Fluxo_feliz_rascunho_ate_aplicada()
    {
        var c = CriarRascunho();
        var t0 = new DateTime(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);

        c.Iniciar(t0);
        c.Status.Should().Be(StatusContagem.EmAndamento);
        c.IniciadoEm.Should().Be(t0);

        c.Pausar();
        c.Status.Should().Be(StatusContagem.Pausada);
        c.Retomar();
        c.Status.Should().Be(StatusContagem.EmAndamento);

        c.Finalizar(t0.AddHours(1));
        c.Status.Should().Be(StatusContagem.Finalizada);
        c.FinalizadoEm.Should().Be(t0.AddHours(1));

        var userAplica = Guid.NewGuid();
        c.Aplicar(userAplica, t0.AddHours(2));
        c.Status.Should().Be(StatusContagem.Aplicada);
        c.AplicadoPorUsuarioId.Should().Be(userAplica);
        c.AplicadoEm.Should().Be(t0.AddHours(2));
        c.EstaTerminal.Should().BeTrue();
    }

    [Fact]
    public void Reabrir_volta_para_em_andamento_e_limpa_finalizado()
    {
        var c = CriarRascunho();
        c.Iniciar(DateTime.UtcNow);
        c.Finalizar(DateTime.UtcNow);

        c.Reabrir();

        c.Status.Should().Be(StatusContagem.EmAndamento);
        c.FinalizadoEm.Should().BeNull();
    }

    [Fact]
    public void Iniciar_fora_de_rascunho_lanca()
    {
        var c = CriarRascunho();
        c.Iniciar(DateTime.UtcNow);
        Action act = () => c.Iniciar(DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Aplicar_sem_finalizar_lanca()
    {
        var c = CriarRascunho();
        c.Iniciar(DateTime.UtcNow);
        Action act = () => c.Aplicar(Guid.NewGuid(), DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Cancelar_de_em_andamento_ok()
    {
        var c = CriarRascunho();
        c.Iniciar(DateTime.UtcNow);
        c.Cancelar();
        c.Status.Should().Be(StatusContagem.Cancelada);
        c.EstaTerminal.Should().BeTrue();
    }

    [Fact]
    public void Cancelar_aplicada_lanca()
    {
        var c = CriarRascunho();
        c.Iniciar(DateTime.UtcNow);
        c.Finalizar(DateTime.UtcNow);
        c.Aplicar(Guid.NewGuid(), DateTime.UtcNow);
        Action act = () => c.Cancelar();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    private static Contagem CriarRascunho() =>
        Contagem.Criar(Guid.NewGuid(), EscopoContagem.Categoria, Guid.NewGuid(),
            ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid());
}
