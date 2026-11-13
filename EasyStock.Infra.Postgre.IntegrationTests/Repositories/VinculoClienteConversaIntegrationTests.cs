using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public sealed class VinculoClienteConversaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly Guid _ator = Guid.NewGuid();

    private EasyStockDbContext Contexto(Guid empresa)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        return db;
    }

    private VinculoClienteConversaUseCase UseCase(EasyStockDbContext db, Guid empresa, IVinculoClienteConversaRepository? repository = null)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(empresa);
        usuario.UsuarioId.Returns(_ator);
        usuario.IsAuthenticated.Returns(true);
        usuario.Nivel.Returns(NivelAcesso.Admin);
        usuario.TemPermissao(Arg.Any<Permissao>()).Returns(true);
        return new(new ConversaRepository(db), new ClienteRepository(db), repository ?? new VinculoClienteConversaRepository(db),
            db, usuario, TimeProvider.System);
    }

    private async Task<(Guid Empresa, Conversa Conversa, LinkCardapioConversa Link, SessaoChatSite Sessao)> SemearAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var empresa = Empresa.Criar("Teste identidade", null);
        await using var db = Contexto(empresa.Id);
        db.Empresas.Add(empresa);
        var cliente = Cliente.Criar(empresa.Id, "Maria");
        cliente.Telefone = "+5511999990001";
        db.Clientes.Add(cliente);
        var loja = StorefrontEntity.Criar(empresa.Id, "r4-" + empresa.Id.ToString("N"), "Teste", 0m);
        db.Set<StorefrontEntity>().Add(loja);
        var conversa = Conversa.Abrir(empresa.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow, "Maria", cliente.Id, CanalConversa.ChatSite);
        var link = LinkCardapioConversa.Gerar(empresa.Id, conversa.Id, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        link.SalvarRascunho(0, "{\"itens\":[{\"quantidade\":2}]}", DateTime.UtcNow);
        var sessao = SessaoChatSite.Abrir(empresa.Id, loja.Id, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        sessao.VincularConversa(conversa.Id);
        db.AtendimentoConversas.Add(conversa);
        db.LinksCardapioConversa.Add(link);
        db.SessoesChatSite.Add(sessao);
        await db.SaveChangesAsync();
        return (empresa.Id, conversa, link, sessao);
    }

    [SkippableTheory]
    [InlineData("(11) 99999-0001")]
    [InlineData("+5511999990001")]
    public async Task CandidatosMostramHistoricoRealESituacaoSemMisturarEmpresas(string telefone)
    {
        var c = await SemearAsync();
        var alheia = await SemearAsync();
        var primeiroEm = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        Guid umId;
        Guid doisId;
        Guid inativoId;
        await using (var db = Contexto(c.Empresa))
        {
            var zero = await db.Clientes.SingleAsync();
            zero.Ativo = true;
            zero.OrderCount = 99; // O contador legado não decide o resumo nem a classificação.
            var um = Cliente.Criar(c.Empresa, "Um pedido");
            var dois = Cliente.Criar(c.Empresa, "Dois pedidos");
            var inativo = Cliente.Criar(c.Empresa, "Pessoa inativa");
            foreach (var pessoa in new[] { um, dois, inativo }) pessoa.Telefone = zero.Telefone;
            um.Ativo = dois.Ativo = true;
            inativo.Ativo = false;
            db.Clientes.AddRange(um, dois, inativo);
            db.Pedidos.AddRange(Historico(um, primeiroEm), Historico(dois, primeiroEm), Historico(dois, primeiroEm.AddDays(1)));
            await db.SaveChangesAsync();
            (umId, doisId, inativoId) = (um.Id, dois.Id, inativo.Id);
        }
        await using (var db = Contexto(alheia.Empresa))
        {
            var pessoa = await db.Clientes.SingleAsync();
            pessoa.Ativo = true;
            db.Pedidos.Add(Historico(pessoa, primeiroEm.AddDays(10)));
            await db.SaveChangesAsync();
        }
        await using var leitura = Contexto(c.Empresa);
        var candidatos = await UseCase(leitura, c.Empresa).CandidatosAsync(c.Empresa, c.Conversa.Id, telefone);
        candidatos.Should().HaveCount(4);
        candidatos.Should().NotContain(x => x.Id == alheia.Conversa.ClienteId);
        var zeroResumo = candidatos.Single(x => x.Id == c.Conversa.ClienteId);
        zeroResumo.QuantidadePedidos.Should().Be(0);
        zeroResumo.UltimoPedidoEm.Should().BeNull();
        zeroResumo.EhLead.Should().BeTrue();
        zeroResumo.Ativo.Should().BeTrue();
        var umResumo = candidatos.Single(x => x.Id == umId);
        umResumo.QuantidadePedidos.Should().Be(1);
        umResumo.UltimoPedidoEm.Should().Be(primeiroEm);
        umResumo.EhLead.Should().BeFalse();
        var doisResumo = candidatos.Single(x => x.Id == doisId);
        doisResumo.QuantidadePedidos.Should().Be(2);
        doisResumo.UltimoPedidoEm.Should().Be(primeiroEm.AddDays(1));
        doisResumo.EhLead.Should().BeFalse();
        candidatos.Single(x => x.Id == inativoId).Ativo.Should().BeFalse();
        (await leitura.Pedidos.CountAsync()).Should().Be(3);
        (await leitura.AtendimentoConversas.SingleAsync()).ClienteId.Should().Be(c.Conversa.ClienteId);

        static Pedido Historico(Cliente pessoa, DateTime em)
        {
            var pedido = Pedido.Criar(pessoa.EmpresaId, pessoa, origem: "fixture-r4-i01");
            pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Histórico sintético",
                Quantidade = 1, PrecoUnitario = 10m, Subtotal = 10m, CriadoEm = em });
            pedido.RecalcularTotal();
            pedido.Cancelar();
            pedido.CriadoEm = em;
            pedido.CanceladoEm = pedido.AlteradoEm = em.AddHours(1);
            return pedido;
        }
    }

    private static CorrigirVinculoClienteCommand Retirar(Guid empresa, Conversa conversa) =>
        new(empresa, conversa.Id, Guid.NewGuid(), conversa.VinculoRevisao, conversa.ClienteId, "retirar", null, "Pessoa conferida no atendimento");

    [SkippableFact]
    public async Task CorrecaoAuditoriaLinksESessoesPersistemJuntosERepeticaoNaoDuplica()
    {
        var c = await SemearAsync();
        var comando = Retirar(c.Empresa, c.Conversa);
        await using (var db = Contexto(c.Empresa)) await UseCase(db, c.Empresa).ExecuteAsync(comando);
        await using (var db = Contexto(c.Empresa)) await UseCase(db, c.Empresa).ExecuteAsync(comando);
        await using var conferir = Contexto(c.Empresa);
        var conversa = await conferir.AtendimentoConversas.SingleAsync(x => x.Id == c.Conversa.Id);
        conversa.ClienteId.Should().BeNull();
        conversa.VinculoRevisao.Should().Be(1);
        (await conferir.Set<AlteracaoVinculoConversa>().CountAsync()).Should().Be(1);
        var link = await conferir.LinksCardapioConversa.SingleAsync(x => x.Id == c.Link.Id);
        link.EstaValido(DateTime.UtcNow).Should().BeFalse();
        System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(link.RascunhoJson!),
            System.Text.Json.Nodes.JsonNode.Parse(c.Link.RascunhoJson!)).Should().BeTrue();
        var sessao = await conferir.SessoesChatSite.SingleAsync(x => x.Id == c.Sessao.Id);
        sessao.TokenHash.Should().NotBe(c.Sessao.TokenHash);
        sessao.EstaValida(DateTime.UtcNow).Should().BeFalse();
        (await conferir.Clientes.SingleAsync()).Telefone.Should().Be("+5511999990001");
    }

    [SkippableFact]
    public async Task FalhaDaAuditoriaReverteVinculoEExpiracao()
    {
        var c = await SemearAsync();
        await using (var db = Contexto(c.Empresa))
        {
            var real = new VinculoClienteConversaRepository(db);
            var falha = Substitute.For<IVinculoClienteConversaRepository>();
            falha.ListarPedidosAsync(c.Empresa, c.Conversa.Id, Arg.Any<CancellationToken>()).Returns(t => real.ListarPedidosAsync(c.Empresa, c.Conversa.Id, t.Arg<CancellationToken>()));
            falha.ExpirarLinksAsync(c.Empresa, c.Conversa.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(t => real.ExpirarLinksAsync(c.Empresa, c.Conversa.Id, t.Arg<DateTime>(), t.Arg<CancellationToken>()));
            falha.EncerrarSessoesChatAsync(c.Empresa, c.Conversa.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(t => real.EncerrarSessoesChatAsync(c.Empresa, c.Conversa.Id, t.Arg<DateTime>(), t.Arg<CancellationToken>()));
            falha.AddAsync(Arg.Any<AlteracaoVinculoConversa>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("Falha controlada")));
            await UseCase(db, c.Empresa, falha).Invoking(s => s.ExecuteAsync(Retirar(c.Empresa, c.Conversa)))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        await using var conferir = Contexto(c.Empresa);
        (await conferir.AtendimentoConversas.SingleAsync(x => x.Id == c.Conversa.Id)).ClienteId.Should().Be(c.Conversa.ClienteId);
        (await conferir.LinksCardapioConversa.SingleAsync(x => x.Id == c.Link.Id)).EstaValido(DateTime.UtcNow).Should().BeTrue();
        (await conferir.SessoesChatSite.SingleAsync(x => x.Id == c.Sessao.Id)).TokenHash.Should().Be(c.Sessao.TokenHash);
        (await conferir.Set<AlteracaoVinculoConversa>().CountAsync()).Should().Be(0);
    }

    [SkippableFact]
    public async Task DuasRevisoesConcorrentesAplicamSomenteUma()
    {
        var c = await SemearAsync();
        var iniciar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> CorrigirAsync()
        {
            await using var db = Contexto(c.Empresa);
            await iniciar.Task;
            try { await UseCase(db, c.Empresa).ExecuteAsync(Retirar(c.Empresa, c.Conversa)); return true; }
            catch (VinculoClienteConflitoException) { return false; }
        }
        var primeira = CorrigirAsync();
        var segunda = CorrigirAsync();
        iniciar.SetResult();
        (await Task.WhenAll(primeira, segunda)).Count(x => x).Should().Be(1);
        await using var conferir = Contexto(c.Empresa);
        (await conferir.Set<AlteracaoVinculoConversa>().CountAsync()).Should().Be(1);
    }

    [SkippableFact]
    public async Task TresPessoasNoTelefoneExigemEscolhaSemAtingirPessoaUnicaDaOutraEmpresa()
    {
        var c = await SemearAsync();
        var outra = await SemearAsync();
        await using var db = Contexto(c.Empresa);
        var clientes = new ClienteRepository(db);
        var identificar = new IdentificarClientePorTelefoneUseCase(clientes, new ClienteStorefrontRepository(db),
            NullLogger<IdentificarClientePorTelefoneUseCase>.Instance);
        var unico = await identificar.ExecuteAsync(new(c.Empresa, "5511999990001", null));
        unico.IdentidadePendente.Should().BeFalse();
        var clienteUnico = unico.Cliente!;
        clienteUnico.Id.Should().Be(c.Conversa.ClienteId!.Value);
        var hashPessoal = clienteUnico.TelefoneHash;
        var segundo = Cliente.Criar(c.Empresa, "Bruno");
        segundo.Telefone = "11999990001";
        var terceiro = Cliente.Criar(c.Empresa, "Carla");
        terceiro.Telefones.Add(new ClienteTelefone { Id = Guid.NewGuid(), ClienteId = terceiro.Id,
            Numero = "+5511999990001", Tipo = "celular", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        db.Clientes.AddRange(segundo, terceiro);
        await db.SaveChangesAsync();

        var pendente = await identificar.ExecuteAsync(new(c.Empresa, "5511999990001", "Pessoa alegada"));

        pendente.IdentidadePendente.Should().BeTrue();
        pendente.Cliente.Should().BeNull();
        (await UseCase(db, c.Empresa).CandidatosAsync(c.Empresa, c.Conversa.Id, "11999990001"))
            .Select(x => x.Id).Should().BeEquivalentTo([clienteUnico.Id, segundo.Id, terceiro.Id]);
        clienteUnico.TelefoneHash.Should().Be(hashPessoal);
        segundo.TelefoneHash.Should().BeNull();
        terceiro.TelefoneHash.Should().BeNull();
        (await db.Clientes.CountAsync()).Should().Be(3);

        await using var outraDb = Contexto(outra.Empresa);
        var outroResultado = await new IdentificarClientePorTelefoneUseCase(new ClienteRepository(outraDb),
            new ClienteStorefrontRepository(outraDb), NullLogger<IdentificarClientePorTelefoneUseCase>.Instance)
            .ExecuteAsync(new(outra.Empresa, "5511999990001", null));
        outroResultado.IdentidadePendente.Should().BeFalse();
        outroResultado.Cliente!.Id.Should().Be(outra.Conversa.ClienteId!.Value);
    }

    [SkippableFact]
    public async Task LegadoSemProvaBloqueiaECandidatosFicamNaEmpresa()
    {
        var c = await SemearAsync();
        var outra = await SemearAsync();
        await using var db = Contexto(c.Empresa);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE atendimento_conversas SET \"HistoricoPedidosVerificado\" = false WHERE \"EmpresaId\" = {c.Empresa} AND \"Id\" = {c.Conversa.Id}");
        var useCase = UseCase(db, c.Empresa);
        await useCase.Invoking(s => s.ExecuteAsync(Retirar(c.Empresa, c.Conversa))).Should().ThrowAsync<VinculoClienteConflitoException>();
        var candidatos = await useCase.CandidatosAsync(c.Empresa, c.Conversa.Id, "(11) 99999-0001");
        candidatos.Should().ContainSingle().Which.Id.Should().Be(c.Conversa.ClienteId!.Value);
        var repo = new VinculoClienteConversaRepository(db);
        (await repo.ListarHistoricoAsync(c.Empresa, outra.Conversa.Id, CancellationToken.None)).Should().BeEmpty();
        (await repo.ListarPedidosAsync(c.Empresa, outra.Conversa.Id, CancellationToken.None)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task RetomadaComSnapshotAnteriorNaoEmiteSessaoDepoisDaCorrecao()
    {
        var c = await SemearAsync();
        var leuLink = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var retomadaDb = Contexto(c.Empresa);
        var real = new LinkCardapioConversaRepository(retomadaDb);
        var links = Substitute.For<ILinkCardapioConversaRepository>();
        links.ObterPorTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var snapshot = await retomadaDb.LinksCardapioConversa.AsNoTracking().SingleAsync(l => l.Id == c.Link.Id);
            leuLink.SetResult();
            await continuar.Task;
            return (LinkCardapioConversa?)snapshot;
        });
        links.ObterComLockAsync(c.Empresa, c.Link.Id, Arg.Any<CancellationToken>())
            .Returns(t => real.ObterComLockAsync(c.Empresa, c.Link.Id, t.Arg<CancellationToken>()));
        var lojas = Substitute.For<IStorefrontRepository>();
        var tenant = Substitute.For<ITenantContextAccessor>();
        var sessoes = new SessaoChatSiteRepository(retomadaDb);
        var acesso = new AcessoChatSite(lojas, Substitute.For<ITenantFeatureFlagRepository>(), tenant, sessoes);
        var retomada = new RetomarChatDaComandaUseCase(new ConversaRepository(retomadaDb), lojas, acesso,
            sessoes, retomadaDb, TimeProvider.System, links, tenant).ExecuteAsync("link-testado", null);
        await leuLink.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var correcaoDb = Contexto(c.Empresa);
            await UseCase(correcaoDb, c.Empresa).ExecuteAsync(Retirar(c.Empresa, c.Conversa));
        }
        finally { continuar.SetResult(); }
        var verificar = async () => await retomada;
        await verificar.Should().ThrowAsync<LinkCardapioConversaIndisponivelException>();
        await using var conferir = Contexto(c.Empresa);
        (await conferir.SessoesChatSite.CountAsync()).Should().Be(1);
        (await conferir.SessoesChatSite.SingleAsync()).EstaValida(DateTime.UtcNow).Should().BeFalse();
    }

    [SkippableFact]
    public async Task SessaoRevogadaEmVooNaoCriaOutraConversaEOtpPessoalPermaneceNoDono()
    {
        var c = await SemearAsync();
        var clienteId = c.Conversa.ClienteId!.Value;
        var outroCliente = Cliente.Criar(c.Empresa, "Outra pessoa");
        var pessoal = EasyStock.Domain.Entities.Storefront.ClienteSession.Criar(clienteId, c.Empresa, TimeProvider.System);
        var outroChat = SessaoChatSite.Abrir(c.Empresa, c.Sessao.StorefrontId,
            Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        var outraConversa = Conversa.Abrir(c.Empresa, outroChat.ContatoIdExterno, DateTime.UtcNow, canal: CanalConversa.ChatSite);
        outroChat.VincularConversa(outraConversa.Id);
        var hashPessoal = EasyStock.Domain.Entities.Storefront.ClienteOtp.CalcularTelefoneHash("+5511999990001");
        await using (var seed = Contexto(c.Empresa))
        {
            (await seed.Clientes.SingleAsync(x => x.Id == clienteId)).TelefoneHash = hashPessoal;
            seed.Clientes.Add(outroCliente);
            seed.ClienteSessions.Add(pessoal);
            seed.AtendimentoConversas.Add(outraConversa);
            seed.SessoesChatSite.Add(outroChat);
            await seed.SaveChangesAsync();
        }
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var visitanteDb = Contexto(c.Empresa);
        var sessaoAntiga = await visitanteDb.SessoesChatSite.SingleAsync(x => x.Id == c.Sessao.Id);
        var real = new ConversaRepository(visitanteDb);
        var conversas = Substitute.For<IConversaRepository>();
        conversas.TravarContatoAsync(c.Empresa, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(t => real.TravarContatoAsync(c.Empresa, t.Arg<string>(), t.Arg<CancellationToken>()));
        conversas.ObterAbertaPorClienteNoCanalAsync(c.Empresa, clienteId, CanalConversa.ChatSite, Arg.Any<CancellationToken>())
            .Returns(async t =>
            {
                // O snapshot inicial da sessão era válido; a correção entra antes da seleção/criação.
                entrou.SetResult();
                await continuar.Task;
                return await real.ObterAbertaPorClienteNoCanalAsync(c.Empresa, clienteId, CanalConversa.ChatSite, t.Arg<CancellationToken>());
            });
        conversas.ObterPorIdAsync(c.Empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(t => real.ObterPorIdAsync(c.Empresa, t.ArgAt<Guid>(1), t.Arg<CancellationToken>()));
        conversas.ObterPorIdComLockAsync(c.Empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(t => real.ObterPorIdComLockAsync(c.Empresa, t.ArgAt<Guid>(1), t.Arg<CancellationToken>()));
        conversas.ObterAbertaPorContatoAsync(c.Empresa, CanalConversa.ChatSite, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(t => real.ObterAbertaPorContatoAsync(c.Empresa, CanalConversa.ChatSite, t.Arg<string>(), t.Arg<CancellationToken>()));
        conversas.AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>())
            .Returns(t => real.AddAsync(t.Arg<Conversa>(), t.Arg<CancellationToken>()));
        var retomar = new ConversaChatSiteService(conversas, visitanteDb, new SessaoChatSiteRepository(visitanteDb))
            .ObterOuCriarAsync(sessaoAntiga, clienteId, DateTime.UtcNow);
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var corrigirDb = Contexto(c.Empresa);
            var caso = UseCase(corrigirDb, c.Empresa);
            await caso.ExecuteAsync(Retirar(c.Empresa, c.Conversa));
            await caso.ExecuteAsync(new(c.Empresa, c.Conversa.Id, Guid.NewGuid(), 1, null,
                "vincular", outroCliente.Id, "Outra pessoa confirmada"));
            (await corrigirDb.AtendimentoConversas.SingleAsync(x => x.Id == c.Conversa.Id)).Encerrar(DateTime.UtcNow);
            await corrigirDb.SaveChangesAsync();
        }
        finally { continuar.SetResult(); }

        var validar = async () => await retomar;
        await validar.Should().ThrowAsync<EasyStock.Domain.Exceptions.RegraDeDominioVioladaException>().WithMessage("A sessão mudou*");
        await using var conferir = Contexto(c.Empresa);
        (await conferir.AtendimentoConversas.CountAsync()).Should().Be(2, "a credencial revogada não abre nova conversa");
        (await conferir.SessoesChatSite.SingleAsync(x => x.Id == c.Sessao.Id)).EstaValida(DateTime.UtcNow).Should().BeFalse();
        var preservado = await conferir.SessoesChatSite.SingleAsync(x => x.Id == outroChat.Id);
        preservado.TokenHash.Should().Be(outroChat.TokenHash);
        preservado.EstaValida(DateTime.UtcNow).Should().BeTrue();
        var otp = await conferir.ClienteSessions.SingleAsync(x => x.Id == pessoal.Id);
        otp.ClienteId.Should().Be(clienteId);
        otp.Revogada.Should().BeFalse();
        (await conferir.Clientes.SingleAsync(x => x.Id == clienteId)).TelefoneHash.Should().Be(hashPessoal);
        (await conferir.Clientes.SingleAsync(x => x.Id == outroCliente.Id)).TelefoneHash.Should().BeNull();
    }

    [SkippableFact]
    public async Task PedidoDoHistoricoNaoSomeAoLimparPedidoAtual()
    {
        var c = await SemearAsync();
        Guid pedidoId;
        await using (var db = Contexto(c.Empresa))
        {
            var cliente = await db.Clientes.SingleAsync();
            var pedido = Pedido.Criar(c.Empresa, cliente);
            pedidoId = pedido.Id;
            db.Pedidos.Add(pedido);
            var conversa = await db.AtendimentoConversas.SingleAsync();
            conversa.DefinirPedidoEmAndamento(pedido.Id);
            conversa.DefinirPedidoEmAndamento(null);
            await db.SaveChangesAsync();
        }
        await using var conferir = Contexto(c.Empresa);
        var estado = await UseCase(conferir, c.Empresa).ObterAsync(c.Empresa, c.Conversa.Id);
        estado.PodeCorrigir.Should().BeFalse();
        estado.Pedidos.Should().ContainSingle().Which.Id.Should().Be(pedidoId);
        await UseCase(conferir, c.Empresa).Invoking(s => s.ExecuteAsync(Retirar(c.Empresa, c.Conversa)))
            .Should().ThrowAsync<VinculoClienteConflitoException>();
    }

    [SkippableFact]
    public async Task RlsIsolaAuditoriaSemDependerDoFiltroEf()
    {
        var primeira = await SemearAsync();
        var segunda = await SemearAsync();
        foreach (var c in new[] { primeira, segunda })
        {
            await using var gravar = Contexto(c.Empresa);
            await UseCase(gravar, c.Empresa).ExecuteAsync(Retirar(c.Empresa, c.Conversa));
        }
        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {primeira.Empresa.ToString()}, false)");
        (await db.Set<AlteracaoVinculoConversa>().IgnoreQueryFilters().ToListAsync())
            .Should().ContainSingle().Which.EmpresaId.Should().Be(primeira.Empresa);
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.empresa_id', '', false)");
        (await db.Set<AlteracaoVinculoConversa>().IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();
    }
}
