using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

public sealed class ObterRastreioLogisticaUseCaseTests
{
    [Fact]
    public async Task Tenant_divergente_ou_demo_nao_configurada_nao_consulta_dados()
    {
        var repo = Substitute.For<ILogisticaRepository>();
        var empresa = Guid.NewGuid();
        var semDemo = new ObterRastreioLogisticaUseCase(repo, Config(Guid.Empty));
        (await semDemo.ExecuteAsync(empresa, Guid.NewGuid())).Should().BeNull();
        var outraEmpresa = new ObterRastreioLogisticaUseCase(repo, Config(empresa));
        (await outraEmpresa.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeNull();
        await repo.DidNotReceiveWithAnyArgs().DespachoAsync(default, default, default);
    }

    [Fact]
    public async Task Despacho_inexistente_ou_de_outro_tenant_nao_tem_rastreio()
    {
        var repo = Substitute.For<ILogisticaRepository>(); var empresa = Guid.NewGuid(); var id = Guid.NewGuid();
        var sut = new ObterRastreioLogisticaUseCase(repo, Config(empresa));
        (await sut.ExecuteAsync(empresa, id)).Should().BeNull();
        repo.DespachoAsync(empresa, id, Arg.Any<CancellationToken>()).Returns(new DespachoLogistica { EmpresaId = Guid.NewGuid() });
        (await sut.ExecuteAsync(empresa, id)).Should().BeNull();
    }

    [Fact]
    public async Task Projecao_publica_omite_motivo_contatos_custo_pagamento_e_pedidos()
    {
        var repo = Substitute.For<ILogisticaRepository>(); var empresa = Guid.NewGuid(); var id = Guid.NewGuid();
        var agora = new DateTime(2026, 10, 13, 13, 0, 0, DateTimeKind.Utc);
        var despacho = new DespachoLogistica { Id = id, EmpresaId = empresa, ValorCentavos = 9876,
            PedidoIdsJson = JsonSerializer.Serialize(new[] { Guid.NewGuid() }), ReferenciaPagamento = "COMPROVANTE-PRIVADO",
            CriadoEm = agora,
            EventosJson = JsonSerializer.Serialize(new[] {
                new LogisticaEvento("agendado", "Nome telefone endereço PRIVADO", agora, "chave-secreta"),
                new LogisticaEvento("pagamento_confirmado", "R$ 98,76", agora.AddMinutes(1), "pagar"),
                new LogisticaEvento("a_caminho", "contato-privado", agora.AddMinutes(2), "sair") }) };
        despacho.RegistrarEvento("a_caminho", null);
        repo.DespachoAsync(empresa, id, Arg.Any<CancellationToken>()).Returns(despacho);
        var resultado = await new ObterRastreioLogisticaUseCase(repo, Config(empresa)).ExecuteAsync(empresa, id);
        resultado!.Demonstracao.Should().BeTrue(); resultado.Status.Should().Be("a_caminho");
        resultado.Eventos.Select(e => e.Tipo).Should().Equal("agendado", "a_caminho");
        resultado.AtualizadoEm.Should().Be(agora.AddMinutes(2));
        var json = JsonSerializer.Serialize(resultado);
        json.Should().NotContain("PRIVADO").And.NotContain("privado").And.NotContain("9876")
            .And.NotContain("PedidoIds").And.NotContain("ChaveIdempotencia").And.NotContain("pagamento");
    }

    private static ConfiguracaoLogistica Config(Guid empresa) => new(empresa, new("Demo", "Origem", "+5500000000000", 0, 0));
}
