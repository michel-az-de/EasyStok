using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData("escalar")]
    [InlineData("encerrar")]
    [InlineData("outro_cliente")]
    [InlineData("sem_pedido")]
    [InlineData("criar")]
    [InlineData("criar_falha")]
    public async Task Comanda_AgentePreservaAcoesReaisENuncaPromoveTextoLivre(string caso)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var llm = Substitute.For<IAgenteLlmClient>(); llm.Disponivel.Returns(true);
        var mensagens = new ConcurrentQueue<string>(); var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(c => { mensagens.Enqueue(c.ArgAt<string>(1)); return new EnvioWhatsAppResult($"wamid.guard.{Guid.NewGuid():N}"); });
        var mp = Provedor(new());
        if (caso == "criar_falha")
            mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Fronteira MP indisponível"));
        await using var factory = CriarFactory(mp).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => { s.AddSingleton(llm); s.AddSingleton(cloud); }));
        using var client = factory.CreateClient(); var cenario = await SemearAsync(factory);
        var token = await Link(factory, cenario.ConversaId); var pedidoId = Guid.Empty;
        if (caso is "escalar" or "encerrar" or "outro_cliente")
        {
            var estado = await Estado(client, token);
            await Salvar(client, token, estado, estado.Rascunho with
            { Itens = [new(cenario.CardapioItemId, 1)], JanelaId = cenario.JanelaId, DataEntrega = cenario.DataEntrega });
            pedidoId = (await Enviar(client, token, 1)).PedidoId;
        }
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
        if (caso == "outro_cliente")
        {
            var outro = new EasyStock.Domain.Entities.Cliente { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Outro cliente sintético" };
            db.Clientes.Add(outro);
            conversa = Conversa.Abrir(EmpresaId, "5511988887777", DateTime.UtcNow, "Outro cliente sintético", outro.Id);
            db.AtendimentoConversas.Add(conversa);
        }
        conversa.LiberarAutomatico(); conversa.RegistrarEntrada(DateTime.UtcNow);
        db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, DateTime.UtcNow,
            TipoConteudoMensagem.Texto, "Mensagem sintética do cliente", $"wamid.guard.in.{Guid.NewGuid():N}"));
        await db.SaveChangesAsync();
        var consultar = new BlocoUsoFerramentaLlm("consulta", "consultar_pedido", JsonSerializer.SerializeToElement(new { pedido_id = pedidoId }));
        BlocoLlm[] ferramentas = caso switch
        {
            "escalar" or "encerrar" => [consultar, new BlocoUsoFerramentaLlm("acao", caso == "escalar" ? "escalar_para_dona" : "encerrar_conversa",
                JsonSerializer.SerializeToElement(new { motivo = "Conferência solicitada" })),
                new BlocoUsoFerramentaLlm("proibida_apos_acao", "criar_pedido", JsonSerializer.SerializeToElement(new { }))],
            "criar" or "criar_falha" => [new BlocoUsoFerramentaLlm("criar", "criar_pedido", JsonSerializer.SerializeToElement(new
            {
                itens = new[] { new { cardapio_item_id = cenario.CardapioItemId, quantidade = 1, observacao = "Nota livre não pode virar afirmação" } },
                data_entrega = cenario.DataEntrega.ToString("yyyy-MM-dd"), janela_id = cenario.JanelaId
            }))],
            _ => [consultar]
        };
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            new RespostaLlm(RespostaLlm.StopToolUse, ferramentas, 1, 1),
            new RespostaLlm("end_turn", [new BlocoTextoLlm("Seu estorno já caiu na conta.")], 1, 1));
        var agente = scope.ServiceProvider.GetRequiredService<AgenteAtendimentoService>();
        var resultado = await agente.ProcessarTurnoAsync(EmpresaId, conversa.Id, DateTime.UtcNow);
        var texto = mensagens.Should().ContainSingle().Subject;
        texto.Should().NotContain("caiu na conta").And.NotContain("{").And.NotContain("Nota livre");
        resultado.Respondeu.Should().BeTrue();
        resultado.Escalou.Should().Be(caso is not ("criar" or "encerrar"));
        if (caso == "criar")
        {
            texto.Should().Contain("Pedido registrado").And.Contain("Aguardando pagamento").And.Contain("R$ 15,00").And.Contain("https://");
            (await db.CobrancasPedido.CountAsync()).Should().Be(1);
        }
        else if (caso == "encerrar")
        {
            conversa.Situacao.Should().Be(SituacaoConversa.Encerrada);
            texto.Should().StartWith("Conversa encerrada.");
        }
        else texto.Should().Contain("equipe").And.NotContain("Pedido registrado");
        if (caso is "escalar" or "encerrar")
        {
            (await db.Pedidos.CountAsync()).Should().Be(1, "nenhuma ferramenta do lote pode executar após transferência ou encerramento");
            await llm.Received(1).EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
        }
        if (caso == "criar_falha") (await db.CobrancasPedido.CountAsync()).Should().Be(0);
        (await agente.ProcessarTurnoAsync(EmpresaId, conversa.Id, DateTime.UtcNow)).ChamouLlm.Should().BeFalse();
        if (Environment.GetEnvironmentVariable("EASYSTOK_GUARD_EVIDENCIAS") is { Length: > 0 } arquivo)
            await File.WriteAllTextAsync($"{arquivo}.{caso}.json", JsonSerializer.Serialize(new
            {
                caso, resultado, texto, conversa.Situacao,
                pedidos = await db.Pedidos.Select(p => new { p.Id, p.ClienteId, p.Status }).ToArrayAsync(),
                cobrancas = await db.CobrancasPedido.Select(c => c.Id).ToArrayAsync()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [SkippableTheory]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("approved")]
    public async Task Comanda_AgenteUsaLedgerDeEstornoSemPrometerCreditoBancario(string modo)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var llm = Substitute.For<IAgenteLlmClient>(); llm.Disponivel.Returns(true);
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(c =>
            c.Arg<RequisicaoLlm>().Mensagens.Last().Conteudo.OfType<BlocoResultadoFerramentaLlm>().Any()
                ? new RespostaLlm("end_turn", [new BlocoTextoLlm("Seu estorno já caiu na conta.")], 1, 1)
                : new RespostaLlm(RespostaLlm.StopToolUse,
                    [new BlocoUsoFerramentaLlm("consulta", "consultar_pedido", JsonSerializer.SerializeToElement(new { }))], 1, 1));
        var mensagens = new ConcurrentQueue<string>();
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(c => { mensagens.Enqueue(c.ArgAt<string>(1)); return new EnvioWhatsAppResult($"wamid.guard.{Guid.NewGuid():N}"); });
        var mp = Provedor(new()); var estornos = new EstornosBrowserControlados(mp); estornos.DefinirModo(modo);
        await using var factory = CriarFactory(mp, NivelAcesso.Admin).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => { s.AddSingleton(llm); s.AddSingleton(cloud); }));
        using var client = factory.CreateClient(); var cenario = await SemearAsync(factory);
        var token = await Link(factory, cenario.ConversaId); var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        { Itens = [new(cenario.CardapioItemId, 4)], JanelaId = cenario.JanelaId, DataEntrega = cenario.DataEntrega });
        var envio = await Enviar(client, token, 1);
        mp.ConsultarPagamentoAsync("840002", Arg.Any<CancellationToken>()).Returns(new PagamentoMercadoPago(
            "840002", "approved", null, envio.PedidoId.ToString(), 45, DateTime.UtcNow, "pix", "bank_transfer"));
        await ConfirmarPagamento(factory, envio.PedidoId, "840002", 45);
        var ajuste = await Link(factory, cenario.ConversaId); estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(cenario.CardapioItemId, 2)], Acerto = "estorno" });
        await Enviar(client, ajuste, 1);
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{cenario.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar",
            new { aceitar = true })).EnsureSuccessStatusCode();
        var historico = new List<object>();
        await Conferir();
        if (modo == "rejected")
        {
            // Uma intenção nova resolve o saldo; a recusa original continua histórica.
            estornos.DefinirModo("approved");
            Guid pagamentoId;
            using (var scope = factory.Services.CreateScope())
                pagamentoId = await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().Pedidos.IgnoreQueryFilters()
                    .Where(p => p.EmpresaId == EmpresaId && p.Id == envio.PedidoId).SelectMany(p => p.Pagamentos).Select(p => p.Id).SingleAsync();
            (await client.PostAsJsonAsync($"/api/pedidos/{envio.PedidoId}/estornos-online",
                new { operacaoId = Guid.NewGuid(), pagamentoId, valor = 20, motivo = "Nova intenção após recusa" })).EnsureSuccessStatusCode();
            await Conferir(resolvido: true);
        }
        if (Environment.GetEnvironmentVariable("EASYSTOK_GUARD_EVIDENCIAS") is { Length: > 0 } arquivo)
            await File.WriteAllTextAsync($"{arquivo}.{modo}.json", JsonSerializer.Serialize(historico, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        async Task Conferir(bool resolvido = false)
        {
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
            conversa.LiberarAutomatico(); conversa.RegistrarEntrada(DateTime.UtcNow);
            db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, DateTime.UtcNow,
                TipoConteudoMensagem.Texto, "Meu estorno foi confirmado?", $"wamid.guard.in.{Guid.NewGuid():N}"));
            await db.SaveChangesAsync();
            var anterior = mensagens.Count;
            var resultado = await scope.ServiceProvider.GetRequiredService<AgenteAtendimentoService>()
                .ProcessarTurnoAsync(EmpresaId, conversa.Id, DateTime.UtcNow);
            var atual = await Estado(client, ajuste);
            var texto = mensagens.Skip(anterior).Should().ContainSingle().Subject;
            historico.Add(new { resolvido, resultado, texto, atual.Pedido, atual.Acerto, atual.Financeiro });
            resultado.Respondeu.Should().BeTrue(); resultado.Escalou.Should().BeFalse();
            texto.Should().NotContain("caiu na conta").And.NotContain("{");
            texto.Should().Contain("Total: R$ 25,00").And.Contain("Dinheiro recebido registrado: R$ 45,00");
            var confirmado = modo == "approved" || resolvido;
            atual.Financeiro!.Devolvido.Should().Be(confirmado ? 20 : 0);
            atual.Financeiro.Reservado.Should().Be(modo == "pending" ? 20 : 0);
            texto.Should().Contain($"Cobertura atual: R$ {(confirmado ? "25" : "45")},00")
                .And.Contain($"Devoluções registradas: R$ {(confirmado ? "20" : "0")},00");
            texto.Should().Contain("das 09:00 às 12:00");
            if (modo == "pending") texto.Should().Contain("Reservado para devolução: R$ 20,00");
            if (modo == "rejected") texto.Should().Contain("recusada, não confirmada pelo provedor").And.NotContain("precisa de conferência");
            if (resolvido)
            {
                atual.Acerto!.Situacao.Should().Be("recusado");
                atual.Financeiro.Alocado.Should().Be(atual.Pedido!.Total);
                texto.Should().Contain("Reservado para devolução: R$ 0,00");
            }
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comanda_AgenteNuncaEnviaConfirmacaoLivreMesmoAposConsultarPedidoReal(bool consultar)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        const string falso = "Seu estorno ja caiu na conta.";
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true);
        var final = new RespostaLlm("end_turn", [new BlocoTextoLlm(falso)], 1, 1);
        var consulta = new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("toolu.consulta", "consultar_pedido", JsonSerializer.SerializeToElement(new { }))], 1, 1);
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(consultar ? consulta : final, final);
        var mensagens = new ConcurrentQueue<string>();
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(c => { mensagens.Enqueue(c.ArgAt<string>(1)); return new EnvioWhatsAppResult("wamid.guard.controlado"); });
        await using var factory = CriarFactory(Provedor(new())).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => { s.AddSingleton(llm); s.AddSingleton(cloud); }));
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        var token = await Link(factory, cenario.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(cenario.CardapioItemId, 1)], JanelaId = cenario.JanelaId, DataEntrega = cenario.DataEntrega
        });
        var envio = await Enviar(client, token, 1);
        await ConfirmarPagamento(factory, envio.PedidoId, "840001", 15);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
        conversa.LiberarAutomatico(); conversa.RegistrarEntrada(DateTime.UtcNow);
        db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, DateTime.UtcNow,
            TipoConteudoMensagem.Texto, "Meu estorno foi confirmado?", "wamid.guard.entrada"));
        await db.SaveChangesAsync();
        var resultado = await scope.ServiceProvider.GetRequiredService<AgenteAtendimentoService>()
            .ProcessarTurnoAsync(EmpresaId, conversa.Id, DateTime.UtcNow);
        var atual = await Estado(client, token);
        if (Environment.GetEnvironmentVariable("EASYSTOK_GUARD_EVIDENCIAS") is { Length: > 0 } arquivo)
            await File.WriteAllTextAsync($"{arquivo}.{consultar}.json", JsonSerializer.Serialize(new
            {
                consultar, resultado, mensagens = mensagens.ToArray(), atual.NumeroComanda,
                atual.Pedido, atual.Financeiro, atual.Acerto,
                historicoAgente = await db.AtendimentoMensagens.Where(m => m.ConversaId == conversa.Id && m.Autor == AutorMensagem.Agente)
                    .Select(m => new { m.Texto, m.ExternoId }).ToArrayAsync()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        mensagens.Should().NotContain(m => m.Contains(falso), "o texto livre do LLM não é uma confirmação autoritativa");
        resultado.Respondeu.Should().BeTrue("consulta verificada ou aviso fixo de encaminhamento responde ao cliente");
        if (consultar)
        {
            resultado.Escalou.Should().BeFalse();
            mensagens.Should().ContainSingle().Which.Should().Contain("Comanda").And.Contain("15,00").And.NotContain("{");
            mensagens.Single().Should().Contain("das 09:00 às 12:00").And.NotContain("09:00.");
        }
        else
        {
            resultado.Escalou.Should().BeTrue();
            mensagens.Should().ContainSingle().Which.Should().Contain("equipe");
        }
        (await scope.ServiceProvider.GetRequiredService<AgenteAtendimentoService>()
            .ProcessarTurnoAsync(EmpresaId, conversa.Id, DateTime.UtcNow)).ChamouLlm.Should().BeFalse();
    }
}
