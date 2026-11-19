using System.Collections.Concurrent;
using EasyStock.Api.BackgroundServices;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// Testes determinísticos do <see cref="ExecutorPorChave"/>: sincronização só por <see cref="TaskCompletionSource"/>.
/// O <c>WaitAsync</c> com teto existe apenas para um teste travado falhar em vez de pendurar a suíte.
/// </summary>
public class ExecutorPorChaveTests
{
    private static readonly TimeSpan Teto = TimeSpan.FromSeconds(10);

    private static TaskCompletionSource Sinal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Conversas_diferentes_rodam_ao_mesmo_tempo()
    {
        var executor = new ExecutorPorChave(paralelismo: 4, aoFalhar: (_, _) => { });
        var chaveA = Guid.NewGuid();
        var chaveB = Guid.NewGuid();
        var iniciouA = Sinal();
        var iniciouB = Sinal();
        var liberar = Sinal();

        executor.Agendar(chaveA, async _ => { iniciouA.SetResult(); await liberar.Task; });
        executor.Agendar(chaveB, async _ => { iniciouB.SetResult(); await liberar.Task; });

        await Task.WhenAll(iniciouA.Task, iniciouB.Task).WaitAsync(Teto);

        liberar.SetResult();
        await executor.AguardarTudoAsync().WaitAsync(Teto);
    }

    [Fact]
    public async Task Mesma_conversa_nunca_roda_dois_trabalhos_juntos_e_mantem_a_ordem()
    {
        var executor = new ExecutorPorChave(paralelismo: 4, aoFalhar: (_, _) => { });
        var chave = Guid.NewGuid();
        var registro = new ConcurrentQueue<string>();
        var primeiroIniciou = Sinal();
        var liberar = Sinal();

        executor.Agendar(chave, async _ =>
        {
            registro.Enqueue("1 inicio");
            primeiroIniciou.SetResult();
            await liberar.Task;
            registro.Enqueue("1 fim");
        });
        executor.Agendar(chave, _ =>
        {
            registro.Enqueue("2 inicio");
            registro.Enqueue("2 fim");
            return Task.CompletedTask;
        });

        await primeiroIniciou.Task.WaitAsync(Teto);
        liberar.SetResult();
        await executor.AguardarTudoAsync().WaitAsync(Teto);

        registro.Should().Equal("1 inicio", "1 fim", "2 inicio", "2 fim");
    }

    [Fact]
    public async Task O_limite_de_paralelismo_faz_chaves_diferentes_rodarem_uma_por_vez()
    {
        var executor = new ExecutorPorChave(paralelismo: 1, aoFalhar: (_, _) => { });
        var registro = new ConcurrentQueue<string>();
        var primeiroIniciou = Sinal();
        var liberar = Sinal();

        executor.Agendar(Guid.NewGuid(), async _ =>
        {
            registro.Enqueue("A inicio");
            primeiroIniciou.SetResult();
            await liberar.Task;
            registro.Enqueue("A fim");
        });
        executor.Agendar(Guid.NewGuid(), _ =>
        {
            registro.Enqueue("B inicio");
            return Task.CompletedTask;
        });

        await primeiroIniciou.Task.WaitAsync(Teto);
        liberar.SetResult();
        await executor.AguardarTudoAsync().WaitAsync(Teto);

        registro.Should().Equal("A inicio", "A fim", "B inicio");
    }

    [Fact]
    public async Task Excecao_de_um_trabalho_nao_impede_o_proximo_da_mesma_conversa_e_vai_ao_callback()
    {
        var erros = new ConcurrentQueue<(Guid Chave, Exception Erro)>();
        var executor = new ExecutorPorChave(paralelismo: 2, aoFalhar: (chave, erro) => erros.Enqueue((chave, erro)));
        var chave = Guid.NewGuid();
        var segundoRodou = Sinal();

        executor.Agendar(chave, _ => Task.FromException(new InvalidOperationException("falha do turno")));
        executor.Agendar(chave, _ =>
        {
            segundoRodou.SetResult();
            return Task.CompletedTask;
        });

        await segundoRodou.Task.WaitAsync(Teto);
        await executor.AguardarTudoAsync().WaitAsync(Teto);

        var erro = erros.Should().ContainSingle().Subject;
        erro.Chave.Should().Be(chave);
        erro.Erro.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("falha do turno");
    }

    [Fact]
    public async Task Depois_que_tudo_termina_o_executor_nao_guarda_nenhuma_chave()
    {
        var executor = new ExecutorPorChave(paralelismo: 2, aoFalhar: (_, _) => { });

        for (var i = 0; i < 3; i++)
        {
            executor.Agendar(Guid.NewGuid(), _ => Task.CompletedTask);
            executor.Agendar(Guid.NewGuid(), _ => Task.FromException(new InvalidOperationException("falha")));
        }

        await executor.AguardarTudoAsync().WaitAsync(Teto);

        executor.ChavesAtivas.Should().Be(0);
        executor.TrabalhosPendentes.Should().Be(0);
    }
}
