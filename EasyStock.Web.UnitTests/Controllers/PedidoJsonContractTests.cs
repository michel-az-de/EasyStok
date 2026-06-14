using EasyStock.Web.Controllers;
using EasyStock.Web.Models.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// Contrato JSON do cockpit (#591): sucesso → 200 {success,pedido}; falha → propaga
/// o HttpStatus da API (400 transição inválida / 404 cross-tenant / 409 estoque) com
/// {success:false,error}. Servidor vence: o pedido do corpo é o PedidoRowDto projetado.
/// </summary>
public class PedidoJsonContractTests
{
    private static Pedido Ped() => new()
    {
        Id = "p1", Status = "pronto", Total = 100m, TotalPago = 40m, ItensCount = 2
    };

    [Fact]
    public void Sucesso_retorna_200_com_pedido_projetado()
    {
        var result = PedidoJsonContract.From(ApiResult<Pedido>.Ok(Ped()));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().BeEquivalentTo(new
        {
            success = true,
            pedido = new { Id = "p1", Status = "pronto", Pendente = 60m, Quitado = false }
        });
    }

    [Theory]
    [InlineData("VALIDATION_ERROR", 400)]        // transição inválida / valor <= 0
    [InlineData("NOT_FOUND", 404)]               // id de outro tenant
    [InlineData("BUSINESS_RULE_VIOLATION", 409)] // estoque insuficiente no →pronto
    public void Falha_propaga_status_e_corpo_de_erro(string code, int status)
    {
        var result = PedidoJsonContract.From(
            ApiResult<Pedido>.Fail(code, "mensagem do servidor", status, "cid-123"));

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(status);
        obj.Value.Should().BeEquivalentTo(new
        {
            success = false,
            error = new { code, message = "mensagem do servidor" },
            correlationId = "cid-123"
        });
    }

    [Fact]
    public void Falha_sem_httpStatus_cai_em_400()
    {
        var result = PedidoJsonContract.From(ApiResult<Pedido>.Fail("X", "y")); // HttpStatus = 0

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public void Sucesso_com_data_nula_e_tratado_como_erro_400()
    {
        var result = PedidoJsonContract.From(new ApiResult<Pedido> { Success = true, Data = null });

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
    }
}
