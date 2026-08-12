using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// issue 951 (correlacionado ao QA rodada 2, BUG-006): TentarAbrirCaixaAsync tinha um
/// try/catch que nunca capturava nada — AddMovimentoAsync so fazia db.Add (sem persistir),
/// e a unique_violation da abertura estourava no CommitAsync final, FORA do try/catch,
/// derrubando a transacao inteira e descartando o PedidoPagamento junto. Agravado por
/// DateOnly.FromDateTime(pagoEm) usar dia UTC enquanto o indice unico agrupa por dia BRT
/// (caixa_abertura_utc_day), tornando a colisao rotineira entre 21h-24h BRT.
///
/// O fix: (1) HorarioBrasil.DataOperacional alinha o dia ao indice; (2) o pagamento e
/// commitado num flush ISOLADO antes da tentativa de abertura, que por sua vez usa
/// TryAddMovimentoAsync (retorna false em vez de lancar na colisao esperada). Estes testes
/// travam o invariante central: o pagamento SEMPRE persiste, care a abertura colida (corrida
/// perdida) ou falhe de qualquer outra forma inesperada.
/// </summary>
public class RegistrarPagamentoPedidoUseCaseTests
{
    private readonly IPedidoRepository _repo = Substitute.For<IPedidoRepository>();
    private readonly ICaixaRepository _caixaRepo = Substitute.For<ICaixaRepository>();
    private readonly FakeUnitOfWork _uow = new();

    private RegistrarPagamentoPedidoUseCase UC(bool comCaixa = true) =>
        new(_repo, _uow, Substitute.For<ILogger<RegistrarPagamentoPedidoUseCase>>(),
            comCaixa ? _caixaRepo : null);

    private Pedido NovoPedidoOperacional(Guid empresaId, Guid? lojaId = null)
    {
        var pedido = Pedido.Criar(empresaId, lojaId: lojaId); // Status = "aguardando" => AceitaPagamento = true
        var item = new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Item",
            Quantidade = 1, PrecoUnitario = 100m, Subtotal = 100m, CriadoEm = DateTime.UtcNow
        };
        pedido.Itens.Add(item);
        pedido.RecalcularTotal();
        _repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        // issue 1029: modela o RELATIONSHIP FIXUP do EF Core. Em producao o agregado vem
        // rastreado (GetByIdWithDetailsAsync nao usa AsNoTracking) e, quando AddPagamentoAsync
        // faz db.Set<PedidoPagamento>().Add(pag), o EF sozinho encaixa pag em pedido.Pagamentos
        // pela FK PedidoId. O substitute nao fazia isso: a colecao ficava vazia, e os testes
        // so passavam porque o use case tinha um pedido.Pagamentos.Add(pag) explicito -- que em
        // producao dobrava o TotalPago (a List<> nao deduplica por referencia). O 4abb7bb5
        // removeu a linha de producao, corretamente, mas deixou este fake mentindo. Sem esta
        // linha aqui, o teste mede um mundo que nao existe.
        _repo.When(r => r.AddPagamentoAsync(Arg.Any<PedidoPagamento>()))
             .Do(ci => pedido.Pagamentos.Add(ci.Arg<PedidoPagamento>()));

        return pedido;
    }

    [Fact]
    public async Task Pagamento_persiste_quando_abertura_colide_com_a_unica_ja_existente()
    {
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId);
        _caixaRepo.GetFechamentoDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId).Returns((FechamentoCaixa?)null);
        _caixaRepo.GetMovimentosDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId)
            .Returns(Array.Empty<MovimentoCaixa>());
        // Simula a corrida perdida: outra transacao ja commitou a abertura primeiro.
        _caixaRepo.TryAddMovimentoAsync(Arg.Any<MovimentoCaixa>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await UC().ExecuteAsync(new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 100m));

        result.Should().NotBeNull();
        pedido.Pagamentos.Should().ContainSingle(p => p.Valor == 100m);
        pedido.TotalPago.Should().Be(100m);
        _uow.CommitCount.Should().BeGreaterThanOrEqualTo(1, "o flush do pagamento tem que ter acontecido antes da tentativa de abertura");
    }

    [Fact]
    public async Task Pagamento_persiste_quando_abertura_falha_de_forma_inesperada()
    {
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId);
        _caixaRepo.GetFechamentoDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId).Returns((FechamentoCaixa?)null);
        // Falha inesperada ANTES mesmo de chegar no TryAddMovimentoAsync (ex.: timeout de rede).
        _caixaRepo.GetMovimentosDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId)
            .Returns<Task<IEnumerable<MovimentoCaixa>>>(_ => throw new TimeoutException("conexao instavel"));

        var result = await UC().ExecuteAsync(new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "dinheiro", 100m));

        result.Should().NotBeNull();
        pedido.Pagamentos.Should().ContainSingle(p => p.Valor == 100m);
    }

    [Fact]
    public async Task Dia_da_abertura_usa_fuso_operacional_de_Brasilia_nao_dia_civil_UTC()
    {
        // Antes do fix, TentarAbrirCaixaAsync computava o dia com
        // DateOnly.FromDateTime(pagoEm) — dia civil UTC. O indice unico
        // (ix_movimentos_caixa_abertura_unica) agrupa por caixa_abertura_utc_day, que e o
        // dia OPERACIONAL de Brasilia; os dois discordam exatamente na janela 21h-24h BRT
        // (issue 951). O use case usa DateTime.UtcNow diretamente (nao ha clock injetavel),
        // entao este teste nao consegue fixar o horario do pagamento — mas verifica
        // deterministicamente que o dia passado pro repositorio e HorarioBrasil.Hoje()
        // (o dia operacional), nao um DateOnly cru derivado de UtcNow. A reproducao exata
        // da janela 21h-24h BRT com Postgres real vive no teste de integracao (fora do CI).
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId);
        var diaOperacionalEsperado = EasyStock.Application.Common.HorarioBrasil.Hoje();

        _caixaRepo.GetFechamentoDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId).Returns((FechamentoCaixa?)null);
        _caixaRepo.GetMovimentosDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId)
            .Returns(Array.Empty<MovimentoCaixa>());
        _caixaRepo.TryAddMovimentoAsync(Arg.Any<MovimentoCaixa>(), Arg.Any<CancellationToken>()).Returns(true);

        await UC().ExecuteAsync(new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 100m));

        await _caixaRepo.Received(1).GetMovimentosDoDiaAsync(empresaId, diaOperacionalEsperado, pedido.LojaId);
    }

    [Fact]
    public async Task Abertura_bem_sucedida_ainda_funciona_no_caminho_feliz()
    {
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId);
        _caixaRepo.GetFechamentoDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId).Returns((FechamentoCaixa?)null);
        _caixaRepo.GetMovimentosDoDiaAsync(empresaId, Arg.Any<DateOnly>(), pedido.LojaId)
            .Returns(Array.Empty<MovimentoCaixa>());

        MovimentoCaixa? aberturaCriada = null;
        _caixaRepo.TryAddMovimentoAsync(Arg.Do<MovimentoCaixa>(m => aberturaCriada = m), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await UC().ExecuteAsync(new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 100m));

        result.Should().NotBeNull();
        aberturaCriada.Should().NotBeNull();
        aberturaCriada!.Tipo.Should().Be("abertura");
        pedido.Pagamentos.Should().ContainSingle();
    }

    [Fact]
    public async Task Sem_caixaRepo_injetado_pagamento_persiste_normalmente()
    {
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId);

        var result = await UC(comCaixa: false).ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 100m));

        result.Should().NotBeNull();
        pedido.Pagamentos.Should().ContainSingle();
    }

    // issue 962 (BUG-002 do QA): o guard client-side do cockpit e' sobre dado carregado --
    // uma aba stale repete o superpagamento acidental. O servidor agora rejeita overpay
    // salvo quando o CALLER sinaliza PermitirExcedente=true (Detail.cshtml, onde o aviso
    // de gorjeta/arredondamento ja existe). Preserva #607 sem reverter a permissividade.

    [Fact]
    public async Task Pagamento_acima_do_pendente_e_rejeitado_sem_a_flag()
    {
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId); // Total = 100m

        var act = () => UC(comCaixa: false).ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 150m));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        pedido.Pagamentos.Should().BeEmpty();
    }

    [Fact]
    public async Task Pagamento_acima_do_pendente_e_aceito_com_a_flag()
    {
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId); // Total = 100m

        var result = await UC(comCaixa: false).ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 150m, PermitirExcedente: true));

        result.Should().NotBeNull();
        pedido.TotalPago.Should().Be(150m);
    }

    [Fact]
    public async Task Pagamento_exatamente_igual_ao_pendente_nao_e_bloqueado_mesmo_sem_a_flag()
    {
        // Fronteira: valor == pendente nao e excedente, entao nao deveria exigir a flag.
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId); // Total = 100m

        var result = await UC(comCaixa: false).ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 100m));

        result.Should().NotBeNull();
        pedido.TotalPago.Should().Be(100m);
    }

    [Fact]
    public async Task Segundo_pagamento_em_pedido_ja_quitado_e_rejeitado_sem_a_flag()
    {
        // Reproduz o cenario exato do QA: pedido "Thatiane" (R$299,40) ja quitado recebe
        // uma segunda tentativa de pagamento pelo cockpit (sem a flag) -- deve ser
        // rejeitada, fechando o vetor de aba stale que o Include (#959) sozinho nao cobre.
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId);
        pedido.Itens.Clear();
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Item",
            Quantidade = 1, PrecoUnitario = 299.40m, Subtotal = 299.40m, CriadoEm = DateTime.UtcNow
        });
        pedido.RecalcularTotal();
        pedido.Pagamentos.Add(new PedidoPagamento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 299.40m, PagoEm = DateTime.UtcNow
        });

        var act = () => UC(comCaixa: false).ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 299.40m));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        pedido.Pagamentos.Should().ContainSingle("o segundo pagamento nao pode ter sido persistido");
    }

    [Fact]
    public async Task Nao_reexecuta_o_retry_padrao_com_guid_novo_por_tentativa()
    {
        // #952: use cases com Guid.NewGuid() fresco nao podem usar ExecuteInTransactionAsync
        // COM retry (reexecutar duplicaria). Este teste documenta que o use case usa
        // ExecuteInTransactionSemRetryAsync — FakeUnitOfWork invoca o lambda exatamente uma
        // vez para ambos os metodos, entao o teste real de nao-duplicacao sob retry vive na
        // integracao (fora do CI); aqui travamos que o pagamento so e adicionado 1x por chamada.
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId);

        await UC(comCaixa: false).ExecuteAsync(new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 100m));

        pedido.Pagamentos.Should().HaveCount(1);
    }

    // issue 1029: o PedidoResult devolvido por ExecuteAsync E o corpo da resposta HTTP —
    // Api/PedidosController.AddPagamento faz DataOk(result), e o cockpit chega ate ele via
    // Web/PedidosService.PagarAsync, devolvendo PedidoRowDto (que carrega TotalPago e deriva
    // Excedente) direto pra UI. Todo teste deste arquivo afirmava sobre o AGREGADO
    // (pedido.Pagamentos / pedido.TotalPago); nenhum sobre o TotalPago da RESPOSTA.
    //
    // Isso importa porque o modo de falhar mais provavel aqui e o DUPLO: reintroduzir um
    // pedido.Pagamentos.Add(pag) no use case "para a resposta refletir o pagamento" parece
    // inofensivo, mas soma em cima do fixup do EF e dobra o TotalPago. Foi exatamente o que
    // aconteceu ao tentar consertar esta issue: o CI mediu 160 para um pagamento de 80
    // (PedidoVendaCaixaIntegrationTests, linha 154). Essa guarda de integracao existe, mas
    // exige Postgres e so roda no CI. Os dois testes abaixo a trazem pro nivel unitario.

    [Fact]
    public async Task Resposta_devolve_TotalPago_ja_incluindo_o_pagamento_recem_registrado()
    {
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId); // Total = 100m

        var result = await UC(comCaixa: false).ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 40m));

        result.Should().NotBeNull();
        result!.TotalPago.Should().Be(40m,
            "a resposta tem que refletir o pagamento recem-registrado, nao o estado anterior a ele");
    }

    [Fact]
    public async Task Resposta_acumula_o_novo_pagamento_sobre_os_ja_existentes()
    {
        // Complementa o teste acima: garante que a resposta SOMA ao que ja havia, em vez de
        // devolver apenas o valor recem-pago (que passaria no teste anterior por coincidencia).
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedidoOperacional(empresaId); // Total = 100m
        pedido.Pagamentos.Add(new PedidoPagamento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "dinheiro", Valor = 60m, PagoEm = DateTime.UtcNow
        });

        var result = await UC(comCaixa: false).ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "pix", 40m));

        result.Should().NotBeNull();
        result!.TotalPago.Should().Be(100m,
            "60 ja pagos + 40 recem-pagos: a resposta acumula, nao substitui");
    }
}
