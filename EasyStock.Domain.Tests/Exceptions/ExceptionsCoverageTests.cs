using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Exceptions;

/// <summary>
/// Smoke tests para exceptions de Domain — cobre construtores e mensagens
/// formatadas. Sem testes dedicados, esses tipos contavam 0% mesmo sendo
/// lancados em fluxos reais.
/// </summary>
public class ExceptionsCoverageTests
{
    [Fact]
    public void MovimentacaoJaEstornadaException_inclui_id_no_texto()
    {
        var id = Guid.NewGuid();
        var ex = new MovimentacaoJaEstornadaException(id);

        ex.Message.Should().Contain(id.ToString()).And.Contain("ja foi estornada");
    }
}
