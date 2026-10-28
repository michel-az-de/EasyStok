using System.Text.Json;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento.Avisos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2096: as leituras da vigia da operação no Postgres real, com RLS ligada como no job (tenant fixado, sem bypass), e o
/// documento dos avisos na configuração do atendimento.
/// </summary>
public class VigiaOperacaoQueryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc);

    private async Task<EasyStockDbContext> ComTenantAsync(Guid empresaId)
    {
        var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresaId.ToString()}, false)");
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    private static Pedido NovoPedido(Guid empresaId, string status, string? endereco, DateTime? agendado = null) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, Status = status, ClienteNome = "Maria Souza",
        EnderecoEntrega = endereco, AgendadoParaEm = agendado, Total = Dinheiro.Zero,
        CriadoEm = Agora.AddHours(-2), AlteradoEm = Agora.AddHours(-2),
    };

    private static PedidoEvento Pronto(Pedido p, DateTime quando) => new()
    {
        Id = Guid.NewGuid(), PedidoId = p.Id, Tipo = "status", StatusAntigo = StatusPedidoMapper.Preparando,
        StatusNovo = StatusPedidoMapper.Pronto, OcorridoEm = quando,
    };

    [SkippableFact]
    public async Task ProntoSemEntregadorIgnoraRetiradaViagemComEntregadorEOutraEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Vigia pronto", null);
        var outra = Empresa.Criar("Vigia pronto outra", null);
        var parado = NovoPedido(casa.Id, StatusPedidoMapper.Pronto, "Rua A, 1");
        var semEntregador = NovoPedido(casa.Id, StatusPedidoMapper.Pronto, "Rua B, 2");
        var comEntregador = NovoPedido(casa.Id, StatusPedidoMapper.Pronto, "Rua C, 3");
        var retirada = NovoPedido(casa.Id, StatusPedidoMapper.Pronto, null);
        var recente = NovoPedido(casa.Id, StatusPedidoMapper.Pronto, "Rua D, 4");
        var preparando = NovoPedido(casa.Id, StatusPedidoMapper.Preparando, "Rua E, 5");
        var esquecido = NovoPedido(casa.Id, StatusPedidoMapper.Pronto, "Rua G, 7");
        var deFora = NovoPedido(outra.Id, StatusPedidoMapper.Pronto, "Rua F, 6");

        var entregador = Entregador.Criar(casa.Id, "Zé", TipoEntregador.Motoboy, EmpresaEntregador.Propria, null, null, null, Agora);
        var viagemCom = Viagem.Criar(casa.Id, Agora.AddMinutes(-30));
        viagemCom.IncluirParada(comEntregador.Id, clienteBloqueado: false);
        viagemCom.DefinirEntregador(entregador);
        var viagemSem = Viagem.Criar(casa.Id, Agora.AddMinutes(-30));
        viagemSem.IncluirParada(semEntregador.Id, clienteBloqueado: false);

        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(casa, outra);
            db.Pedidos.AddRange(parado, semEntregador, comEntregador, retirada, recente, preparando, deFora, esquecido);
            db.Set<PedidoEvento>().AddRange(
                Pronto(parado, Agora.AddMinutes(-40)), Pronto(semEntregador, Agora.AddMinutes(-25)),
                Pronto(comEntregador, Agora.AddMinutes(-40)), Pronto(retirada, Agora.AddMinutes(-40)),
                Pronto(recente, Agora.AddMinutes(-5)), Pronto(deFora, Agora.AddMinutes(-40)),
                Pronto(esquecido, Agora.AddDays(-3)));
            db.Entregadores.Add(entregador);
            db.Viagens.AddRange(viagemCom, viagemSem);
            await db.SaveChangesAsync();
        }

        await using var leitura = await ComTenantAsync(casa.Id);
        var lista = await new VigiaOperacaoQuery(leitura)
            .ListarProntosSemEntregadorAsync(casa.Id, Agora.AddHours(-24), Agora.AddMinutes(-15), 50);

        // Mais novos primeiro; o pronto de 3 dias atrás fica fora da janela.
        lista.Select(p => p.PedidoId).Should().Equal(semEntregador.Id, parado.Id);
        lista[1].ProntoDesde.Should().BeCloseTo(Agora.AddMinutes(-40), TimeSpan.FromSeconds(1));
        lista[1].ClienteNome.Should().Be("Maria Souza");
    }

    [SkippableFact]
    public async Task EncomendasLeemAConfirmacaoEIgnoramFinalizadas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Vigia encomendas", null);
        var confirmada = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, "Rua A", Agora.AddHours(3));
        confirmada.ConfirmarPeloCliente(Agora.AddHours(-1));
        var semConfirmacao = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, "Rua B", Agora.AddHours(5));
        var cancelada = NovoPedido(casa.Id, StatusPedidoMapper.Cancelado, "Rua C", Agora.AddHours(4));
        var longe = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, "Rua D", Agora.AddDays(3));
        var passada = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, "Rua E", Agora.AddHours(-1));
        var naoPaga = NovoPedido(casa.Id, StatusPedidoMapper.AguardandoPagamento, "Rua F", Agora.AddHours(2));
        var naoAprovada = NovoPedido(casa.Id, StatusPedidoMapper.AguardandoAprovacaoBaba, "Rua G", Agora.AddHours(2));
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.Add(casa);
            db.Pedidos.AddRange(confirmada, semConfirmacao, cancelada, longe, passada, naoPaga, naoAprovada);
            await db.SaveChangesAsync();
        }

        await using var leitura = await ComTenantAsync(casa.Id);
        var lista = await new VigiaOperacaoQuery(leitura).ListarEncomendasAsync(casa.Id, Agora, Agora.AddHours(24), 50);
        // Não paga e não aprovada ficam de fora.

        lista.Select(e => e.PedidoId).Should().Equal(confirmada.Id, semConfirmacao.Id);
        lista[0].ConfirmadoPeloClienteEm.Should().BeCloseTo(Agora.AddHours(-1), TimeSpan.FromSeconds(1));
        lista[1].ConfirmadoPeloClienteEm.Should().BeNull();
        lista[1].CriadoEm.Should().BeCloseTo(Agora.AddHours(-2), TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task InterruptorDaLojaLidoSemTenantNosJobsEntreEmpresas()
    {
        // AvaliadorLembretes e CaixaEsquecidoJob rodam sem tenant: o filtro global zerava a leitura e o aviso desligado saía.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Avisos sem tenant", null);
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(casa.Id);
        configuracao.DefinirAvisos(new AvisosOperacao { ClienteSemResposta = false, CaixaEsquecido = false });
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.Add(casa);
            db.ConfiguracoesAtendimento.Add(configuracao);
            await db.SaveChangesAsync();
        }

        await using var semTenant = fixture.CreateDbContext();
        using var _ = semTenant.UseRowLevelSecurityBypass();
        var repositorio = new ConfiguracaoAtendimentoRepository(semTenant);
        var avisos = await repositorio.ObterAvisosAsync(casa.Id);

        avisos.Ligado(CodigosAviso.ClienteSemResposta).Should().BeFalse();
        avisos.Ligado(CodigosAviso.CaixaEsquecido).Should().BeFalse();
        (await repositorio.ObterAvisosAsync(Guid.NewGuid())).Should().Be(AvisosOperacao.Padrao, "sem registro, o padrão");
        // A leitura completa continua presa ao tenant do contexto.
        (await repositorio.GetByEmpresaIdAsync(casa.Id)).Should().BeNull();
    }

    [SkippableFact]
    public async Task AgenteConfirmaEncomendaDoProprioClienteDentroDaTransacao()
    {
        // O turno do agente não abre transação; a leitura com trava do pedido exige uma (GetForUpdateAsync).
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Agente confirma encomenda", null);
        var cliente = Cliente.Criar(casa.Id, "Maria Souza");
        var outro = Cliente.Criar(casa.Id, "Outra pessoa");
        var minha = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, "Rua A", Agora.AddDays(1));
        minha.ClienteId = cliente.Id;
        var alheia = NovoPedido(casa.Id, StatusPedidoMapper.Aguardando, "Rua B", Agora.AddDays(1));
        alheia.ClienteId = outro.Id;
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.Add(casa);
            db.Clientes.AddRange(cliente, outro);
            db.Pedidos.AddRange(minha, alheia);
            await db.SaveChangesAsync();
        }

        var conversa = Conversa.Abrir(casa.Id, "5511999990777", Agora, "Maria");
        conversa.VincularCliente(cliente.Id);
        await using var turno = await ComTenantAsync(casa.Id);
        var ferramenta = new ConfirmarEncomendaFerramenta(new ConfirmarEncomendaPeloClienteUseCase(
            new PedidoStorefrontRepository(turno), turno, TimeProvider.System));
        var contexto = new ContextoTurnoAgente(casa.Id, conversa, Agora);

        var minhaR = await ferramenta.ExecutarAsync(contexto, JsonDocument.Parse($$"""{"pedido_id":"{{minha.Id}}"}""").RootElement);
        var alheiaR = await ferramenta.ExecutarAsync(contexto, JsonDocument.Parse($$"""{"pedido_id":"{{alheia.Id}}"}""").RootElement);

        minhaR.Should().Contain("confirmada");
        alheiaR.Should().Contain("pedido_nao_encontrado");
        await using var leitura = fixture.CreateDbContext();
        var gravados = await leitura.Pedidos.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == minha.Id || p.Id == alheia.Id).ToDictionaryAsync(p => p.Id);
        gravados[minha.Id].ConfirmadoPeloClienteEm.Should().NotBeNull();
        gravados[alheia.Id].ConfirmadoPeloClienteEm.Should().BeNull();
    }

    [SkippableFact]
    public async Task FreezerContaEtiquetasSemBaixaDoLoteFinalizado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Vigia freezer", null);
        var vencendo = LoteCom(casa.Id, "VF-1", Agora.AddDays(-3), Agora.AddDays(1), ["pendente", "conferida", "consumida"]);
        var parado = LoteCom(casa.Id, "VF-2", Agora.AddDays(-20), Agora.AddDays(90), ["impressa"]);
        var tudoConsumido = LoteCom(casa.Id, "VF-3", Agora.AddDays(-20), Agora.AddDays(1), ["consumida"]);
        var emProducao = LoteCom(casa.Id, "VF-4", Agora.AddDays(-20), Agora.AddDays(1), ["pendente"], finalizar: false);
        var novo = LoteCom(casa.Id, "VF-5", Agora.AddDays(-1), Agora.AddDays(30), ["pendente"]);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.Add(casa);
            db.Lotes.AddRange(vencendo, parado, tudoConsumido, emProducao, novo);
            await db.SaveChangesAsync();
        }

        await using var leitura = await ComTenantAsync(casa.Id);
        var lista = await new VigiaOperacaoQuery(leitura)
            .ListarFreezerParaConferirAsync(casa.Id, Agora.AddDays(2), Agora.AddDays(-10), 50);

        lista.Should().HaveCount(2);
        lista.Should().ContainSingle(i => i.CodigoLote == "VF-1" && i.Unidades == 2);
        lista.Should().ContainSingle(i => i.CodigoLote == "VF-2" && i.Unidades == 1);
    }

    [SkippableFact]
    public async Task AvisosGravamELinhaAntigaLeOPadrao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var gravada = Empresa.Criar("Avisos gravados", null);
        var antiga = Empresa.Criar("Avisos linha antiga", null);
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(gravada.Id);
        configuracao.DefinirAvisos(new AvisosOperacao { ProntoSemEntregadorMinutos = 0, ReclamacaoCliente = false });
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(gravada, antiga);
            db.ConfiguracoesAtendimento.AddRange(configuracao, ConfiguracaoAtendimento.CriarPadrao(antiga.Id));
            await db.SaveChangesAsync();
            // Linha de antes da migração: o documento ficou no padrão do banco.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE configuracoes_atendimento SET avisos_operacao = '{{}}'::jsonb WHERE \"EmpresaId\" = {antiga.Id}");
        }

        await using var leitura = fixture.CreateDbContext();
        var lidas = await leitura.ConfiguracoesAtendimento.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == gravada.Id || c.EmpresaId == antiga.Id).ToListAsync();

        lidas.Single(c => c.EmpresaId == gravada.Id).Avisos.Should().Be(
            AvisosOperacao.Padrao with { ProntoSemEntregadorMinutos = 0, ReclamacaoCliente = false });
        lidas.Single(c => c.EmpresaId == antiga.Id).Avisos.Should().Be(AvisosOperacao.Padrao);
    }

    private static Lote LoteCom(Guid empresaId, string codigo, DateTime producao, DateTime expira, string[] etiquetas,
        bool finalizar = true)
    {
        var lote = Lote.Criar(empresaId, codigo, producao);
        var item = new LoteItem
        {
            Id = Guid.NewGuid(), LoteId = lote.Id, Nome = $"Item {codigo}", Quantidade = etiquetas.Length,
            ExpiraEm = expira, CriadoEm = producao,
        };
        lote.Itens.Add(item);
        for (var i = 0; i < etiquetas.Length; i++)
            lote.Etiquetas.Add(new LoteEtiqueta
            {
                Id = Guid.NewGuid(), LoteId = lote.Id, LoteItemId = item.Id, Sequencial = i + 1,
                Codigo = $"{codigo}-{i + 1}", Status = etiquetas[i], CriadoEm = producao,
            });
        if (finalizar) lote.Finalizar();
        return lote;
    }
}
