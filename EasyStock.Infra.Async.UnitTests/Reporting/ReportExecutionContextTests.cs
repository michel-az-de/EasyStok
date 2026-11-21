using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Reporting;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Reporting;

/// <summary>
/// #2664: o contexto da run alimenta o filtro de tenant do EF no Worker (ADR-R06). Fora de um Begin, o acesso falha
/// em vez de cair num tenant vazio.
/// </summary>
public class ReportExecutionContextTests
{
    [Fact]
    public async Task Begin_expoe_a_run_e_o_dispose_limpa()
    {
        var contexto = new ReportExecutionContext();
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();

        await Task.Run(() =>
        {
            var escopo = contexto.Begin(empresa, usuario, ReportContexto.Tenant);

            contexto.IsSet.Should().BeTrue();
            contexto.EmpresaId.Should().Be(empresa);
            contexto.UsuarioSolicitanteId.Should().Be(usuario);
            contexto.Contexto.Should().Be(ReportContexto.Tenant);

            escopo.Dispose();
            escopo.Dispose();

            contexto.IsSet.Should().BeFalse();
            contexto.EmpresaId.Should().BeNull();
        });
    }

    [Fact]
    public async Task Fora_de_um_begin_usuario_e_contexto_falham()
    {
        var contexto = new ReportExecutionContext();

        await Task.Run(() =>
        {
            contexto.IsSet.Should().BeFalse();
            FluentActions.Invoking(() => contexto.UsuarioSolicitanteId).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => contexto.Contexto).Should().Throw<InvalidOperationException>();
        });
    }

    [Fact]
    public async Task Begin_aninhado_e_recusado()
    {
        var contexto = new ReportExecutionContext();

        await Task.Run(() =>
        {
            using var escopo = contexto.Begin(null, Guid.NewGuid(), ReportContexto.AdminSaaS);

            FluentActions.Invoking(() => contexto.Begin(Guid.NewGuid(), Guid.NewGuid(), ReportContexto.Tenant))
                .Should().Throw<InvalidOperationException>();
            contexto.EmpresaId.Should().BeNull("o AdminSaaS roda sem empresa");
        });
    }
}
