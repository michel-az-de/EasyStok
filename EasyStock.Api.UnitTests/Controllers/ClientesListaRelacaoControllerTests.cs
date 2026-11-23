using System.Text.Json;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ListarClientes;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#3244 (S1): GET api/clientes devolve a relação por linha e o gasto da carteira no meta.</summary>
public class ClientesListaRelacaoControllerTests
{
    [Fact]
    public async Task Lista_devolve_campos_da_relacao_e_gastoTotalDaCarteira_no_meta()
    {
        var empresaId = Guid.NewGuid();
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.Nivel.Returns(EasyStock.Domain.Enums.NivelAcesso.Gerente);
        usuario.EmpresaId.Returns(empresaId);
        var cliente = Cliente.Criar(empresaId, "Maria Ficticia");
        var repo = Substitute.For<IClienteRepository>();
        repo.ListarComRelacaoAsync(empresaId, 1, 20, null, null, "gasto", "desc", null, Arg.Any<CancellationToken>())
            .Returns(new ListaDeClientes(
                [new ClienteRelacao(cliente, 2, 80m, DateTime.UtcNow.AddDays(-9), DateTime.UtcNow, 0, 1, 1, "Nota", ["vip"], false)],
                1, 999m));
        var controller = new ClientesController(
            null!, null!, new ListarClientesUseCase(repo), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, usuario)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var ok = (await controller.GetAll(null, sort: "gasto", order: "desc")).Should().BeOfType<OkObjectResult>().Subject;

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.RootElement.GetProperty("meta").GetProperty("gastoTotalDaCarteira").GetDecimal().Should().Be(999m);
        json.RootElement.GetProperty("meta").GetProperty("total").GetInt32().Should().Be(1);
        var linha = json.RootElement.GetProperty("data")[0];
        linha.GetProperty("nome").GetString().Should().Be("Maria Ficticia");
        linha.GetProperty("gastoTotal").GetDecimal().Should().Be(80m);
        linha.GetProperty("ticketMedio").GetDecimal().Should().Be(40m);
        linha.GetProperty("estagio").GetString().Should().Be("recorrente");
        linha.GetProperty("emRisco").GetBoolean().Should().BeFalse();
        linha.GetProperty("tags")[0].GetString().Should().Be("vip");
    }
}
