using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #3068: a varredura acha, em todos os tenants, a conversa que está com a equipe e em que o cliente espera sem resposta
/// humana. Respondida pela equipe, atendida pelo robô antes de a equipe assumir, já decidida, de loja com o Assistente
/// desligado, automática ou fora da faixa de espera fica de fora.
/// </summary>
public class RetomadasPendentesIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task AchaSoAConversaComAEquipeEmQueOClienteEsperaSemRespostaHumana()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = new DateTime(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
        var casa = Empresa.Criar("Retomada por tempo", null);
        var desligada = Empresa.Criar("Retomada por tempo desligada", null);
        var mensagens = new List<Mensagem>();

        Conversa ComEntrada(Empresa empresa, string waId, int minutosAtras, out Mensagem entrada)
        {
            var conversa = Conversa.Abrir(empresa.Id, waId, agora.AddMinutes(-minutosAtras - 1));
            conversa.RegistrarEntrada(agora.AddMinutes(-minutosAtras));
            entrada = Mensagem.Entrada(empresa.Id, conversa.Id, agora.AddMinutes(-minutosAtras), TipoConteudoMensagem.Texto, "alguém aí?");
            mensagens.Add(entrada);
            return conversa;
        }

        void Escalar(Conversa conversa, int minutosAtras)
        {
            var quando = agora.AddMinutes(-minutosAtras);
            conversa.Escalar(quando, "cliente pediu uma pessoa");
            mensagens.Add(Mensagem.Saida(conversa.EmpresaId, conversa.Id, AutorMensagem.Sistema, quando, TipoConteudoMensagem.Texto,
                EscalarConversaUseCase.PrefixoMotivo + "cliente pediu uma pessoa"));
            mensagens.Add(Mensagem.Saida(conversa.EmpresaId, conversa.Id, AutorMensagem.Agente, quando, TipoConteudoMensagem.Texto,
                "Vou encaminhar sua mensagem para a equipe.", $"encaminhamento-{conversa.Id:N}"));
        }

        // Entram: escalada pelo robô e a que o cliente escreveu com a equipe sem ninguém responder.
        var escalada = ComEntrada(casa, "5511999993068", 5, out _);
        Escalar(escalada, 4);
        var semResposta = ComEntrada(casa, "5511999993069", 3, out _);
        semResposta.Assumir(agora.AddMinutes(-10), Guid.NewGuid());

        // Ficam de fora.
        var respondidaPelaEquipe = ComEntrada(casa, "5511999993070", 5, out _);
        Escalar(respondidaPelaEquipe, 4);
        mensagens.Add(Mensagem.Saida(casa.Id, respondidaPelaEquipe.Id, AutorMensagem.Atendente, agora.AddMinutes(-3),
            TipoConteudoMensagem.Texto, "Oi, já vejo.", "resposta-equipe-3068"));
        var atendidaPeloRobo = ComEntrada(casa, "5511999993071", 5, out _);
        mensagens.Add(Mensagem.Saida(casa.Id, atendidaPeloRobo.Id, AutorMensagem.Agente, agora.AddMinutes(-4),
            TipoConteudoMensagem.Texto, "Abrimos sim.", "resposta-robo-3068"));
        atendidaPeloRobo.Assumir(agora.AddMinutes(-3), Guid.NewGuid());
        var jaDecidida = ComEntrada(casa, "5511999993072", 5, out var entradaDecidida);
        Escalar(jaDecidida, 4);
        jaDecidida.DispensarRetomadaPorTempo(entradaDecidida.Id);
        var automatica = ComEntrada(casa, "5511999993073", 5, out _);
        var dentroDoPrazo = ComEntrada(casa, "5511999993074", 0, out _);
        dentroDoPrazo.Assumir(agora, Guid.NewGuid());
        var antiga = ComEntrada(casa, "5511999993075", 120, out _);
        Escalar(antiga, 119);
        var deLojaDesligada = ComEntrada(desligada, "5511999993076", 5, out _);
        Escalar(deLojaDesligada, 4);
        // A equipe respondeu e o cliente só reagiu: reação não é pergunta esperando resposta.
        var soReagiu = ComEntrada(casa, "5511999993077", 5, out _);
        soReagiu.Assumir(agora.AddMinutes(-10), Guid.NewGuid());
        mensagens.Add(Mensagem.Saida(casa.Id, soReagiu.Id, AutorMensagem.Atendente, agora.AddMinutes(-4),
            TipoConteudoMensagem.Texto, "Sai às 18h.", "resposta-equipe-3077"));
        soReagiu.RegistrarEntrada(agora.AddMinutes(-2));
        mensagens.Add(Mensagem.Entrada(casa.Id, soReagiu.Id, agora.AddMinutes(-2), TipoConteudoMensagem.Outro, null, "reacao-3077"));
        // A atendente acabou de assumir: o prazo dela começa ali.
        var recemAssumida = ComEntrada(casa, "5511999993078", 3, out _);
        recemAssumida.Assumir(agora.AddSeconds(-20), Guid.NewGuid());

        // A equipe respondeu e o cliente só tocou um botão de ação, que o fluxo normal resolve sem o robô.
        var tocouBotao = ComEntrada(casa, "5511999993079", 5, out _);
        tocouBotao.Assumir(agora.AddMinutes(-10), Guid.NewGuid());
        mensagens.Add(Mensagem.Saida(casa.Id, tocouBotao.Id, AutorMensagem.Atendente, agora.AddMinutes(-4),
            TipoConteudoMensagem.Texto, "Sai às 18h.", "resposta-equipe-3079"));
        tocouBotao.RegistrarEntrada(agora.AddMinutes(-2));
        mensagens.Add(Mensagem.Entrada(casa.Id, tocouBotao.Id, agora.AddMinutes(-2), TipoConteudoMensagem.Botao, "Avaliar",
            "botao-3079", "acao:avaliar:5"));
        // Pergunta de duas horas atrás e uma reação agora: a reação não traz a pergunta velha de volta para a faixa.
        var reacaoEmPerguntaVelha = ComEntrada(casa, "5511999993080", 120, out _);
        reacaoEmPerguntaVelha.Assumir(agora.AddMinutes(-119), Guid.NewGuid());
        reacaoEmPerguntaVelha.RegistrarEntrada(agora.AddMinutes(-2));
        mensagens.Add(Mensagem.Entrada(casa.Id, reacaoEmPerguntaVelha.Id, agora.AddMinutes(-2), TipoConteudoMensagem.Outro, null,
            "reacao-3080"));

        // #2833: a equipe respondeu e apagou a resposta para todos: o cliente não a vê, continua esperando.
        var respostaApagada = ComEntrada(casa, "5511999993081", 5, out _);
        respostaApagada.Assumir(agora.AddMinutes(-10), Guid.NewGuid());
        var apagada = Mensagem.Saida(casa.Id, respostaApagada.Id, AutorMensagem.Atendente, agora.AddMinutes(-4),
            TipoConteudoMensagem.Texto, "Fica R$ 5.", "resposta-apagada-3081");
        apagada.ApagarParaTodos(Guid.NewGuid(), agora.AddMinutes(-3));
        mensagens.Add(apagada);

        var ligada = ConfiguracaoAtendimento.CriarPadrao(casa.Id);
        ligada.Ativo = true;

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(casa, desligada);
            db.ConfiguracoesAtendimento.AddRange(ligada, ConfiguracaoAtendimento.CriarPadrao(desligada.Id));
            db.AtendimentoConversas.AddRange(escalada, semResposta, respondidaPelaEquipe, atendidaPeloRobo, jaDecidida,
                automatica, dentroDoPrazo, antiga, deLojaDesligada, soReagiu, recemAssumida, tocouBotao, reacaoEmPerguntaVelha,
                respostaApagada);
            db.AtendimentoMensagens.AddRange(mensagens);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var candidatas = await new RetomadasPendentesQuery(db).ListarAsync(agora.AddMinutes(-30), agora.AddSeconds(-60));

            candidatas.Where(p => p.EmpresaId == casa.Id || p.EmpresaId == desligada.Id)
                .Should().BeEquivalentTo([
                    new { EmpresaId = casa.Id, ConversaId = escalada.Id },
                    new { EmpresaId = casa.Id, ConversaId = semResposta.Id },
                    new { EmpresaId = casa.Id, ConversaId = respostaApagada.Id },
                ]);
        }
    }
}
