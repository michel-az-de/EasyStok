using System.Reflection;
using EasyStock.Api.Mobile.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>
/// #2828 (decisão do Felipe): as listas de revisão do caixa e dos pedidos do app trazem valor e total. Seguem a mesma
/// regra das leituras do caixa e dos pedidos do ERP, a política Operador, e não a permissão de relatórios, porque o
/// Atendimento revisa essas filas.
/// </summary>
public class LeituraGestaoMobileAutorizacaoTests
{
    [Theory]
    [InlineData(typeof(MobileCashController), nameof(MobileCashController.List))]
    [InlineData(typeof(MobileOrdersController), nameof(MobileOrdersController.List))]
    public void Lista_de_revisao_com_valor_exige_a_politica_Operador(Type controller, string acao)
    {
        controller.GetMethod(acao)!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy)
            .Should().Contain("Operador");
    }
}
