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
    public void ConflitoConcorrenciaException_sem_detalhe_usa_mensagem_padrao()
    {
        var ex = new ConflitoConcorrenciaException("Pedido");

        ex.Message.Should().Contain("Pedido").And.Contain("Conflito").And.Contain("Recarregue");
    }

    [Fact]
    public void ConflitoConcorrenciaException_com_detalhe_anexa_explicacao()
    {
        var ex = new ConflitoConcorrenciaException("Fatura", "Concorrencia entre webhook e job de cobranca");

        ex.Message.Should().Contain("Fatura").And.Contain("webhook");
    }

    [Fact]
    public void MovimentacaoJaEstornadaException_inclui_id_no_texto()
    {
        var id = Guid.NewGuid();
        var ex = new MovimentacaoJaEstornadaException(id);

        ex.Message.Should().Contain(id.ToString()).And.Contain("ja foi estornada");
    }
}
