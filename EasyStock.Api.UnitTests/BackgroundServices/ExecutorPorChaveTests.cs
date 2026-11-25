using System.Collections.Concurrent;
using EasyStock.Api.BackgroundServices;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// Testes determinísticos do <see cref="ExecutorPorChave"/>: sincronização só por <see cref="TaskCompletionSource"/>
/// e, no teste do limite, por uma <see cref="FilaManual"/> que leva o trabalho barrado até o semáforo.
/// O <c>WaitAsync</c> com teto existe apenas para um teste travado falhar em vez de pendurar a suíte.
/// </summary>
public class ExecutorPorChaveTests
{
    private static readonly TimeSpan Teto = TimeSpan.FromSeconds(10);

    private static TaskCompletionSource Sinal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Guarda o que o executor posta (a volta do <c>Task.Yield</c>) e só roda quando o teste esvazia a fila, na
    /// thread do teste. Quando <see cref="Esvaziar"/> volta, o trabalho já parou no semáforo ou já rodou.
    /// </summary>
    private sealed class FilaManual : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Acao, object? Estado)> _fila = new();

        public override void Post(SendOrPostCallback d, object? state) => _fila.Enqueue((d, state));

        /// <summary>Roda o que está na fila e devolve quantos callbacks rodou.</summary>
        public int Esvaziar()
        {
            var rodados = 0;
            while (_fila.TryDequeue(out var item))
            {
                item.Acao(item.Estado);
                rodados++;
            }

            return rodados;
        }
    }

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
        // #3250: B só é agendado com A já na única vaga. Agendados juntos, os dois disputavam a vaga e B às vezes
        // começava primeiro, o que o contrato permite: ele só ordena trabalhos da mesma chave.
        await primeiroIniciou.Task.WaitAsync(Teto);

        var fila = new FilaManual();
        var contexto = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(fila);
        try
        {
            executor.Agendar(Guid.NewGuid(), _ =>
            {
                registro.Enqueue("B inicio");
                return Task.CompletedTask;
            });
        }
        finally
        {
            // Volta antes de qualquer await: a continuação do próprio teste não pode ficar presa na fila.
            SynchronizationContext.SetSynchronizationContext(contexto);
        }

        // B anda até o semáforo aqui, na thread do teste, e para ali enquanto A ocupa a vaga. Se o limite deixasse
        // duas chaves rodarem juntas, B rodaria agora e "B inicio" entraria antes de "A fim".
        // Sem a volta do Task.Yield pela fila, B correria noutra thread e o Red dependeria de corrida.
        fila.Esvaziar().Should().Be(1, "a volta do Task.Yield de B passa pela fila");
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

    [Fact]
    public async Task O_teto_do_grupo_segura_a_terceira_conversa_da_loja_sem_tirar_a_vaga_de_outra_loja()
    {
        // #3291 (R3): a chave é a conversa e o grupo é a loja. A loja cheia espera no próprio teto e não ocupa vaga global.
        var executor = new ExecutorPorChave(paralelismo: 3, aoFalhar: (_, _) => { }, tetoPorGrupo: 2);
        var lojaA = Guid.NewGuid();
        var lojaB = Guid.NewGuid();
        var registro = new ConcurrentQueue<string>();
        var iniciouA1 = Sinal();
        var iniciouA2 = Sinal();
        var iniciouB = Sinal();
        var liberarA = Sinal();

        executor.Agendar(Guid.NewGuid(), lojaA, async _ =>
        {
            registro.Enqueue("A1 inicio");
            iniciouA1.SetResult();
            await liberarA.Task;
            registro.Enqueue("A1 fim");
        });
        executor.Agendar(Guid.NewGuid(), lojaA, async _ =>
        {
            registro.Enqueue("A2 inicio");
            iniciouA2.SetResult();
            await liberarA.Task;
            registro.Enqueue("A2 fim");
        });
        await Task.WhenAll(iniciouA1.Task, iniciouA2.Task).WaitAsync(Teto);

        var fila = new FilaManual();
        var contexto = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(fila);
        try
        {
            executor.Agendar(Guid.NewGuid(), lojaA, _ =>
            {
                registro.Enqueue("A3 inicio");
                return Task.CompletedTask;
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(contexto);
        }

        // A3 anda até o teto da loja na thread do teste e para ali: duas conversas da loja A já rodam.
        fila.Esvaziar().Should().Be(1, "a volta do Task.Yield de A3 passa pela fila");
        registro.Should().NotContain("A3 inicio");

        // A terceira vaga global está livre: se A3 a segurasse enquanto espera o teto da loja, B nunca começaria.
        executor.Agendar(Guid.NewGuid(), lojaB, _ =>
        {
            registro.Enqueue("B inicio");
            iniciouB.SetResult();
            return Task.CompletedTask;
        });
        await iniciouB.Task.WaitAsync(Teto);

        liberarA.SetResult();
        await executor.AguardarTudoAsync().WaitAsync(Teto);

        var ordem = registro.ToList();
        ordem.IndexOf("A3 inicio").Should().BeGreaterThan(Math.Min(ordem.IndexOf("A1 fim"), ordem.IndexOf("A2 fim")),
            "A3 só entra quando uma das duas conversas da loja libera a vaga");
    }

    [Fact]
    public async Task Conversa_na_mesma_loja_mantem_a_ordem_e_nunca_roda_junto_de_si_mesma()
    {
        var executor = new ExecutorPorChave(paralelismo: 4, aoFalhar: (_, _) => { }, tetoPorGrupo: 2);
        var loja = Guid.NewGuid();
        var conversa = Guid.NewGuid();
        var registro = new ConcurrentQueue<string>();
        var primeiroIniciou = Sinal();
        var liberar = Sinal();

        executor.Agendar(conversa, loja, async _ =>
        {
            registro.Enqueue("1 inicio");
            primeiroIniciou.SetResult();
            await liberar.Task;
            registro.Enqueue("1 fim");
        });
        executor.Agendar(conversa, loja, _ =>
        {
            registro.Enqueue("2 inicio");
            return Task.CompletedTask;
        });

        await primeiroIniciou.Task.WaitAsync(Teto);
        liberar.SetResult();
        await executor.AguardarTudoAsync().WaitAsync(Teto);

        registro.Should().Equal("1 inicio", "1 fim", "2 inicio");
    }

    [Fact]
    public async Task Falha_de_um_turno_devolve_a_vaga_da_loja_e_vai_ao_callback_com_a_conversa()
    {
        var erros = new ConcurrentQueue<Guid>();
        var executor = new ExecutorPorChave(paralelismo: 4, aoFalhar: (chave, _) => erros.Enqueue(chave), tetoPorGrupo: 1);
        var loja = Guid.NewGuid();
        var conversaQueFalha = Guid.NewGuid();
        var proximoRodou = Sinal();

        executor.Agendar(conversaQueFalha, loja, _ => Task.FromException(new InvalidOperationException("falha do turno")));
        executor.Agendar(Guid.NewGuid(), loja, _ =>
        {
            proximoRodou.SetResult();
            return Task.CompletedTask;
        });

        await proximoRodou.Task.WaitAsync(Teto);
        await executor.AguardarTudoAsync().WaitAsync(Teto);

        erros.Should().ContainSingle().Which.Should().Be(conversaQueFalha);
    }

    [Fact]
    public async Task Cancelar_quem_espera_o_teto_da_loja_nao_vaza_a_vaga()
    {
        var falhas = new ConcurrentQueue<Exception>();
        var executor = new ExecutorPorChave(paralelismo: 4, aoFalhar: (_, erro) => falhas.Enqueue(erro), tetoPorGrupo: 1);
        var loja = Guid.NewGuid();
        var primeiroIniciou = Sinal();
        var liberar = Sinal();
        var segundoRodou = false;
        var terceiroRodou = Sinal();
        using var cancelamento = new CancellationTokenSource();

        executor.Agendar(Guid.NewGuid(), loja, async _ =>
        {
            primeiroIniciou.SetResult();
            await liberar.Task;
        });
        await primeiroIniciou.Task.WaitAsync(Teto);

        // O segundo espera o teto da loja (desligamento do serviço): ao cancelar, sai sem rodar nem contar como falha.
        executor.Agendar(Guid.NewGuid(), loja, _ =>
        {
            segundoRodou = true;
            return Task.CompletedTask;
        }, cancelamento.Token);
        await cancelamento.CancelAsync();

        executor.Agendar(Guid.NewGuid(), loja, _ =>
        {
            terceiroRodou.SetResult();
            return Task.CompletedTask;
        });

        liberar.SetResult();
        await terceiroRodou.Task.WaitAsync(Teto);
        await executor.AguardarTudoAsync().WaitAsync(Teto);

        segundoRodou.Should().BeFalse();
        falhas.Should().BeEmpty("cancelar no desligamento não é falha do turno");
    }

    [Fact]
    public async Task Depois_que_tudo_termina_o_executor_nao_guarda_nenhum_grupo()
    {
        var executor = new ExecutorPorChave(paralelismo: 2, aoFalhar: (_, _) => { }, tetoPorGrupo: 1);

        for (var i = 0; i < 3; i++)
        {
            var loja = Guid.NewGuid();
            executor.Agendar(Guid.NewGuid(), loja, _ => Task.CompletedTask);
            executor.Agendar(Guid.NewGuid(), loja, _ => Task.FromException(new InvalidOperationException("falha")));
        }

        await executor.AguardarTudoAsync().WaitAsync(Teto);

        executor.GruposAtivos.Should().Be(0);
        executor.ChavesAtivas.Should().Be(0);
        executor.TrabalhosPendentes.Should().Be(0);
    }

    [Fact]
    public void Teto_do_grupo_abaixo_de_um_e_recusado()
    {
        var criar = () => new ExecutorPorChave(paralelismo: 2, aoFalhar: (_, _) => { }, tetoPorGrupo: 0);

        criar.Should().Throw<ArgumentOutOfRangeException>();
    }
}
