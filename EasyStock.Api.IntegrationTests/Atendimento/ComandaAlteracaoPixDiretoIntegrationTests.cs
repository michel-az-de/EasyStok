using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.ValueObjects.Pix;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #2684: pedido combinado para Pix na chave da loja (#2233) e a comanda alterada. A alteração cancelava o Pix direto
/// pendente e deixava a diferença na entrega; o copia e cola com o valor velho seguia na conversa.
/// </summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    // Chave fictícia: o teste nunca usa chave real.
    private static readonly RecebedorPix RecebedorDaLoja =
        RecebedorPix.Criar(ChavePix.Criar(TipoChavePix.Email, "loja@exemplo.test"), "Casa de Doces", "Sao Paulo");

    [SkippableFact]
    public async Task AlteracaoPixDireto_AumentoNoPedidoComumCobraODiferencaNoPixDaLoja()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, dataEntrega: EasyStock.Application.Common.HorarioBrasil.Hoje().AddDays(1));
        var pedidoId = await PedidoComPixDiretoAsync(factory, client, c, itens: 1, pago: 0m, lojaComChave: true);

        var envio = await AlterarAsync(factory, client, c, itens: 2);

        envio.Situacao.Should().Be("aguardando_pagamento");
        envio.LinkPagamento.Should().BeNull("o pedido segue combinado para o Pix da loja");
        preferencias.Should().ContainSingle("só o link do pedido original foi criado");
        var pendente = (await Pendentes(factory, pedidoId)).Should().ContainSingle().Subject;
        pendente.Provedor.Should().Be(CobrancaPedido.ProvedorPixDireto);
        pendente.Valor.Should().Be(25m);
        pendente.PixCopiaCola.Should().Be(PixDiretoDoPedido.CopiaECola(RecebedorDaLoja, pedidoId, 25m));
        var aviso = await AvisoAoClienteAsync(factory);
        aviso.Should().Contain(pendente.PixCopiaCola).And.Contain("O código Pix anterior não vale mais").And.NotContain("na entrega");
    }

    [SkippableFact]
    public async Task AlteracaoPixDireto_AumentoNaEncomendaCobraOSinalNoPixDaLoja()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var pedidoId = await PedidoComPixDiretoAsync(factory, client, c, itens: 1, pago: 0m, lojaComChave: true);

        var envio = await AlterarAsync(factory, client, c, itens: 2);

        envio.Situacao.Should().Be("aguardando_pagamento");
        var pendente = (await Pendentes(factory, pedidoId)).Should().ContainSingle().Subject;
        pendente.Provedor.Should().Be(CobrancaPedido.ProvedorPixDireto);
        pendente.Valor.Should().Be(25m, "a cobrança guarda o saldo; o Pix pede o sinal");
        pendente.PixCopiaCola.Should().Be(PixDiretoDoPedido.CopiaECola(RecebedorDaLoja, pedidoId, 12.5m),
            "encomenda sem nada pago pede o sinal de 50% do total novo");
        // O texto do sinal cita o restante na entrega, como na troca de forma; o que importa é o Pix novo do sinal.
        (await AvisoAoClienteAsync(factory)).Should().Contain(pendente.PixCopiaCola).And.Contain("Sinal de")
            .And.Contain("12,50").And.Contain("O código Pix anterior não vale mais");
    }

    [SkippableFact]
    public async Task AlteracaoPixDireto_SemMudancaDeSaldoMantemOMesmoCodigo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, dataEntrega: EasyStock.Application.Common.HorarioBrasil.Hoje().AddDays(1));
        var pedidoId = await PedidoComPixDiretoAsync(factory, client, c, itens: 1, pago: 0m, lojaComChave: true);
        var antes = (await Pendentes(factory, pedidoId)).Single();

        await AlterarAsync(factory, client, c, itens: 1, observacoes: "Sem granulado");

        var pendente = (await Pendentes(factory, pedidoId)).Should().ContainSingle().Subject;
        pendente.Id.Should().Be(antes.Id, "o saldo não mudou: o código que o cliente tem continua valendo");
        pendente.PixCopiaCola.Should().Be(antes.PixCopiaCola);
        (await AvisoAoClienteAsync(factory)).Should().NotContain("O código Pix anterior não vale mais");
    }

    [SkippableFact]
    public async Task AlteracaoPixDireto_ReducaoQueZeraOSaldoCancelaOPix()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        // A devolução da redução só a gerente confirma.
        await using var factory = CriarFactory(Provedor(new()), EasyStock.Domain.Enums.NivelAcesso.Gerente);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        // Encomenda de R$ 35 com o sinal de R$ 17,50 pago e o resto no Pix da loja; a redução para R$ 15 zera o saldo.
        var pedidoId = await PedidoComPixDiretoAsync(factory, client, c, itens: 3, pago: 17.5m, lojaComChave: true);

        await AlterarAsync(factory, client, c, itens: 1);

        (await Pendentes(factory, pedidoId)).Should().BeEmpty("o total novo já está pago: nenhum Pix com valor velho fica vivo");
    }

    [SkippableFact]
    public async Task AlteracaoPixDireto_LojaSemChaveDeixaNaEntregaEAvisaAEquipe()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, dataEntrega: EasyStock.Application.Common.HorarioBrasil.Hoje().AddDays(1));
        var pedidoId = await PedidoComPixDiretoAsync(factory, client, c, itens: 1, pago: 0m, lojaComChave: false);

        var envio = await AlterarAsync(factory, client, c, itens: 2);

        envio.LinkPagamento.Should().BeNull();
        preferencias.Should().ContainSingle();
        var pendente = (await Pendentes(factory, pedidoId)).Should().ContainSingle().Subject;
        pendente.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega, "sem chave não há Pix novo a gerar");
        pendente.Valor.Should().Be(25m);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        (await db.AtendimentoMensagens.AsNoTracking().AnyAsync(m => m.ConversaId == c.ConversaId
            && m.Texto!.Contains("sem chave Pix"))).Should().BeTrue("a equipe sabe que o Pix direto caiu");
    }

    /// <summary>
    /// Pedido pela comanda e, como a troca de forma faz, a cobrança passada para o Pix na chave da loja.
    /// </summary>
    private static async Task<Guid> PedidoComPixDiretoAsync(
        WebApplicationFactory<Program> factory, HttpClient client, Cenario c, int itens, decimal pago, bool lojaComChave)
    {
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, itens)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var pedidoId = (await Enviar(client, token, 1)).PedidoId;
        if (pago > 0m) await ConfirmarPagamento(factory, pedidoId, "2684-pago", pago);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var loja = Loja.Criar(EmpresaId, "Casa de Doces");
        if (lojaComChave) loja.DefinirRecebedorPix(RecebedorDaLoja);
        db.Lojas.Add(loja);
        var agora = DateTime.UtcNow;
        foreach (var pendente in await db.CobrancasPedido.Where(x => x.PedidoId == pedidoId && x.Status == StatusCobrancaPedido.Pendente).ToListAsync())
            pendente.Cancelar("troca_forma: pix_direto", agora);
        var pedido = await db.Pedidos.SingleAsync(p => p.Id == pedidoId);
        var valor = pedido.Total.Valor - pago;
        var valorDoPix = PixDiretoDoPedido.ValorDoPix(pedido.AgendadoParaEm, pedido.CriadoEm, pedido.Total.Valor, valor);
        db.CobrancasPedido.Add(CobrancaPedido.CriarPixDireto(EmpresaId, pedidoId, valor, RecebedorDaLoja, agora, c.ConversaId, valorDoPix));
        await db.SaveChangesAsync();
        return pedidoId;
    }

    /// <summary>Link novo com a comanda alterada; redução espera o aceite da equipe, que é dado aqui.</summary>
    private static async Task<EnvioComandaCliente> AlterarAsync(
        WebApplicationFactory<Program> factory, HttpClient client, Cenario c, int itens, string? observacoes = null)
    {
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, itens)], Observacoes = observacoes ?? estado.Rascunho.Observacoes });
        var envio = await Enviar(client, token, 1);
        if (envio.Situacao != ConfirmarAlteracaoComandaUseCase.SituacaoAguardandoConfirmacao) return envio;

        var resposta = await client.PostAsJsonAsync(
            $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return (await Estado(client, token)).Envio!;
    }

    private static async Task<List<CobrancaPedido>> Pendentes(WebApplicationFactory<Program> factory, Guid pedidoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        return await db.CobrancasPedido.AsNoTracking()
            .Where(x => x.PedidoId == pedidoId && x.Status == StatusCobrancaPedido.Pendente).ToListAsync();
    }

    /// <summary>Texto do último aviso ao cliente da alteração (jsonb: os valores são lidos fora do banco).</summary>
    private static async Task<string> AvisoAoClienteAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var payloads = await db.NotifEventos.AsNoTracking()
            .Where(e => e.Tipo == EasyStock.Domain.Enums.Notifications.TipoEventoNotificacao.ComandaAtualizada)
            .OrderBy(e => e.OcorridoEm).Select(e => e.PayloadJson).ToListAsync();
        payloads.Should().NotBeEmpty("a alteração avisa o cliente");
        return string.Join("\n", Strings(JsonDocument.Parse(payloads[^1]).RootElement));
    }

    private static IEnumerable<string> Strings(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Strings(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().SelectMany(Strings),
        _ => [],
    };
}
