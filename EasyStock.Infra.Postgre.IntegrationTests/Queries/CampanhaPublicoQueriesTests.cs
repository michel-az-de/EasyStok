using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Queries;

/// <summary>
/// S29 em Postgres real: os fatos de cada cliente que o cálculo do público usa (tags, consentimento
/// por canal, última compra do item entregue, última campanha recebida) e a lista de destinatários,
/// sempre só da empresa.
/// </summary>
public class CampanhaPublicoQueriesTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Campanha NovaCampanha(Guid empresaId, string nome) =>
        Campanha.Criar(empresaId, Guid.NewGuid(),
            new DadosCampanha(nome, "Oi {{nome}}", null, null, FiltroCampanha.ParaTodos, [], null, false, null), Agora);

    [SkippableFact]
    public async Task CandidatosTrazemOsFatosDoPublicoSoDaEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresa = Empresa.Criar("Casa da Baba Publico", "55555555000191");
        var outraEmpresa = Empresa.Criar("Outra Publico", "66666666000191");
        var item = Guid.NewGuid();

        var ana = Cliente.Criar(empresa.Id, "Ana");
        ana.Telefone = "5511999990001";
        ana.DefinirConsentimentoMarketing(true, Agora);
        ana.AdicionarTag("vegano", OrigemClienteTag.Dona, Agora);
        var bia = Cliente.Criar(empresa.Id, "Bia");
        bia.DefinirConsentimentoMarketing(true, Agora); // o booleano diz sim, mas a linha do canal revogou
        bia.Bloquear("calote", Agora);
        var cris = Cliente.Criar(empresa.Id, "Cris"); // sem booleano, com consentimento no WhatsApp
        var inativa = Cliente.Criar(empresa.Id, "Inativa");
        inativa.Ativo = false;
        var deFora = Cliente.Criar(outraEmpresa.Id, "De fora");

        var entregue = Pedido.Criar(empresa.Id, ana);
        entregue.Status = StatusPedidoMapper.Entregue;
        entregue.EntreguEm = Agora.AddDays(-3);
        entregue.Itens.Add(NovoItem(entregue, cardapioItemId: item));
        var antigo = Pedido.Criar(empresa.Id, ana);
        antigo.Status = StatusPedidoMapper.Entregue;
        antigo.EntreguEm = Agora.AddDays(-20);
        antigo.Itens.Add(NovoItem(antigo, cardapioItemId: item)); // ProdutoId tem FK para produtos: fica no OR da query
        var naoEntregue = Pedido.Criar(empresa.Id, cris);
        naoEntregue.Itens.Add(NovoItem(naoEntregue, cardapioItemId: item));

        var campanha = NovaCampanha(empresa.Id, "Esta");
        var anterior = NovaCampanha(empresa.Id, "Anterior");
        var recebeuAnterior = CampanhaDestinatario.Criar(anterior, ana.Id);
        recebeuAnterior.Enfileirar(1, Guid.NewGuid());
        recebeuAnterior.MarcarEnviado(Agora.AddDays(-5));
        var recebeuEsta = CampanhaDestinatario.Criar(campanha, cris.Id);
        recebeuEsta.Enfileirar(1, Guid.NewGuid());
        recebeuEsta.MarcarEnviado(Agora.AddDays(-1));

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.AddRange(empresa, outraEmpresa);
            db.Clientes.AddRange(ana, bia, cris, inativa, deFora);
            db.Pedidos.AddRange(entregue, antigo, naoEntregue);
            db.ConsentimentosContato.AddRange(
                ConsentimentoContato.Registrar(empresa.Id, bia.Id, CanalConversa.WhatsApp, FinalidadeContato.Marketing,
                    SituacaoConsentimento.Revogado, "palavra_sair", Agora),
                ConsentimentoContato.Registrar(empresa.Id, cris.Id, CanalConversa.WhatsApp, FinalidadeContato.Marketing,
                    SituacaoConsentimento.Concedido, "console", Agora),
                ConsentimentoContato.Registrar(empresa.Id, ana.Id, CanalConversa.Email, FinalidadeContato.Marketing,
                    SituacaoConsentimento.Revogado, "console", Agora));
            db.Campanhas.AddRange(campanha, anterior);
            db.CampanhaDestinatarios.AddRange(recebeuAnterior, recebeuEsta);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var queries = new CampanhaPublicoQueries(db);

            var candidatos = await queries.ListarCandidatosAsync(empresa.Id, campanha.Id, item);

            candidatos.Select(c => c.Nome).Should().Equal("Ana", "Bia", "Cris");
            candidatos[0].Should().BeEquivalentTo(new
            {
                TemTelefone = true,
                Bloqueado = false,
                ConsentiuMarketing = true, // revogação no e-mail não vale para o WhatsApp
                Tags = new[] { "vegano" },
                UltimaCompraItemEm = Agora.AddDays(-3),
                UltimaCampanhaRecebidaEm = Agora.AddDays(-5),
            });
            candidatos[1].Should().BeEquivalentTo(new { TemTelefone = false, Bloqueado = true, ConsentiuMarketing = false });
            candidatos[2].ConsentiuMarketing.Should().BeTrue();
            candidatos[2].UltimaCompraItemEm.Should().BeNull("pedido não entregue não conta");
            candidatos[2].UltimaCampanhaRecebidaEm.Should().BeNull("a própria campanha não conta no limite semanal");

            (await queries.ListarCandidatosAsync(empresa.Id, campanha.Id, null))[0].UltimaCompraItemEm
                .Should().BeNull("sem item no filtro não há compra a procurar");
            (await queries.ListarCandidatosAsync(outraEmpresa.Id, campanha.Id, item)).Should().BeEmpty("EmpresaId no WHERE e RLS");

            var destinatarios = await queries.ListarDestinatariosAsync(empresa.Id, campanha.Id, null, 100);
            destinatarios.Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                ClienteId = cris.Id,
                Nome = "Cris",
                Status = StatusCampanhaDestinatario.Enviado,
                Onda = 1,
            });
            (await queries.ListarDestinatariosAsync(empresa.Id, campanha.Id, StatusCampanhaDestinatario.Pendente, 100))
                .Should().BeEmpty();
            (await queries.ListarDestinatariosAsync(outraEmpresa.Id, campanha.Id, null, 100)).Should().BeEmpty();
        }
    }

    private static PedidoItem NovoItem(Pedido pedido, Guid cardapioItemId) => new()
    {
        Id = Guid.NewGuid(),
        PedidoId = pedido.Id,
        Nome = "Bolo de fubá",
        Quantidade = 1m,
        PrecoUnitario = 10m,
        Subtotal = 10m,
        CriadoEm = Agora,
        CardapioItemId = cardapioItemId,
    };
}
