using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2283: a recuperação do start acha, em todos os tenants, a conversa automática em que o cliente falou por último
/// dentro da janela. Respondida, assumida, encerrada ou antiga fica de fora: reenfileirar essa daria resposta dupla ou
/// responderia fora de hora.
/// </summary>
public class TurnosPendentesIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task AchaSoAAutomaticaComOClienteEsperandoDentroDaJanela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);
        var casa = Empresa.Criar("Recuperar turno", null);
        var outraLoja = Empresa.Criar("Recuperar turno outra", null);
        var mensagens = new List<Mensagem>();

        Conversa ComEntrada(Empresa empresa, string waId, int minutosAtras)
        {
            var conversa = Conversa.Abrir(empresa.Id, waId, agora.AddMinutes(-minutosAtras - 1));
            conversa.RegistrarEntrada(agora.AddMinutes(-minutosAtras));
            mensagens.Add(Mensagem.Entrada(empresa.Id, conversa.Id, agora.AddMinutes(-minutosAtras), TipoConteudoMensagem.Texto, "tem nhoque?"));
            return conversa;
        }

        var esperando = ComEntrada(casa, "5511999992283", 3);
        var deOutraLoja = ComEntrada(outraLoja, "5511999992284", 2);
        var respondida = ComEntrada(casa, "5511999992285", 5);
        mensagens.Add(Mensagem.Saida(casa.Id, respondida.Id, AutorMensagem.Agente, agora.AddMinutes(-4),
            TipoConteudoMensagem.Texto, "Tem sim.", "resposta-agente-2283"));
        var assumida = ComEntrada(casa, "5511999992286", 3);
        assumida.Assumir(agora.AddMinutes(-2), Guid.NewGuid());
        var encerrada = ComEntrada(casa, "5511999992287", 3);
        encerrada.Encerrar(agora.AddMinutes(-2));
        var antiga = ComEntrada(casa, "5511999992288", 120);

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(casa, outraLoja);
            db.AtendimentoConversas.AddRange(esperando, deOutraLoja, respondida, assumida, encerrada, antiga);
            db.AtendimentoMensagens.AddRange(mensagens);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var pendentes = await new TurnosPendentesQuery(db).ListarAsync(agora.AddMinutes(-30), agora);

            pendentes.Where(p => p.EmpresaId == casa.Id || p.EmpresaId == outraLoja.Id)
                .Should().BeEquivalentTo([
                    new { EmpresaId = casa.Id, ConversaId = esperando.Id },
                    new { EmpresaId = outraLoja.Id, ConversaId = deOutraLoja.Id },
                ]);
        }
    }
}
