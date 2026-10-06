using System.Collections.Concurrent;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Checkout;

public class CheckoutSiteUseCaseTests
{
    private static readonly EnderecoCheckout Endereco = new("01310100", "Avenida Paulista", "120", "Bela Vista", "São Paulo", "SP", "apto 3");

    private sealed class Cenario
    {
        public CheckoutCoreServiceTests.Cenario Core { get; } = new();
        public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
        public IConversaRepository Conversas { get; } = Substitute.For<IConversaRepository>();
        public IClienteStorefrontRepository Clientes { get; } = Substitute.For<IClienteStorefrontRepository>();
        public IMercadoPagoClient Mp { get; } = Substitute.For<IMercadoPagoClient>();
        public List<Conversa> ListaConversas { get; } = [];
        public List<Mensagem> Mensagens { get; } = [];
        public List<CobrancaPedido> Cobrancas { get; } = [];
        public ConcurrentDictionary<Guid, CheckoutIdempotency> Registros { get; } = new();
        public SessaoChatSite Sessao { get; }
        public string Token { get; } = AcessoChatSite.NovoToken();
        public CheckoutSiteUseCase UseCase { get; }
        public AcessoChatSite Acesso { get; }
        public ISessaoChatSiteRepository Sessoes { get; } = Substitute.For<ISessaoChatSiteRepository>();
        public ITenantFeatureFlagRepository Flags { get; } = Substitute.For<ITenantFeatureFlagRepository>();
        public Guid Empresa => Core.Storefront.EmpresaId;

        public Cenario()
        {
            Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<Conversa>>>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<CancellationToken, Task<Conversa>>>()(ci.Arg<CancellationToken>()));
            Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<CancellationToken, Task<bool>>>()(ci.Arg<CancellationToken>()));
            Flags.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);
            var tenant = Substitute.For<ITenantContextAccessor>();
            Acesso = new AcessoChatSite(Core.StorefrontRepo, Flags, tenant, Sessoes);
            Sessao = SessaoChatSite.Abrir(Empresa, Core.Storefront.Id, AcessoChatSite.HashDoToken(Token), DateTime.UtcNow);
            Sessoes.ObterPorTokenHashAsync(Empresa, Sessao.TokenHash, Arg.Any<CancellationToken>()).Returns(Sessao);
            Conversas.When(x => x.AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>())).Do(ci => ListaConversas.Add(ci.Arg<Conversa>()));
            Conversas.When(x => x.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>())).Do(ci => Mensagens.Add(ci.Arg<Mensagem>()));
            Conversas.ObterPorIdAsync(Empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => ListaConversas.FirstOrDefault(c => c.Id == ci.ArgAt<Guid>(1)));
            Conversas.ObterAbertaPorContatoAsync(Empresa, CanalConversa.ChatSite, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => ListaConversas.FirstOrDefault(c => c.ContatoIdExterno == ci.Arg<string>() && c.EstaAberta));
            Conversas.ObterAbertaPorClienteNoCanalAsync(Empresa, Arg.Any<Guid>(), CanalConversa.ChatSite, Arg.Any<CancellationToken>())
                .Returns(ci => ListaConversas.FirstOrDefault(c => c.ClienteId == ci.ArgAt<Guid>(1) && c.EstaAberta));
            Conversas.TravarParaPedidoAsync(Empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => ListaConversas.First(c => c.Id == ci.ArgAt<Guid>(1)).PedidoEmAndamentoId);
            Core.PedidoRepo.GetByIdComItensAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                var pedido = Core.PedidosAdicionados.FirstOrDefault(p => p.Id == ci.Arg<Guid>());
                if (pedido is not null) pedido.Itens = Core.ItensAdicionados.Where(i => i.PedidoId == pedido.Id).ToList();
                return pedido;
            });
            Core.PedidoRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => Core.PedidosAdicionados.FirstOrDefault(p => p.Id == ci.Arg<Guid>()));
            var repo = Substitute.For<ICheckoutIdempotencyRepository>();
            repo.GetByKeyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => Registros.TryGetValue(ci.Arg<Guid>(), out var r) ? new List<CheckoutIdempotency> { r } : []);
            repo.GetByKeyHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Registros.TryGetValue(ci.Arg<Guid>(), out var r) && r.ContentHash == ci.Arg<string>() ? r : null);
            repo.TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                var r = ci.Arg<CheckoutIdempotency>(); var ganhou = Registros.TryAdd(r.Key, r); return (ganhou, Registros[r.Key]);
            });
            var idempotencia = new CheckoutIdempotencyService(repo, NullLogger<CheckoutIdempotencyService>.Instance);
            var cobrancas = Substitute.For<ICobrancaPedidoRepository>();
            cobrancas.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => Cobrancas.Where(c => c.EmpresaId == ci.ArgAt<Guid>(0) && c.PedidoId == ci.ArgAt<Guid>(1)).ToList());
            cobrancas.When(x => x.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>())).Do(ci => Cobrancas.Add(ci.Arg<CobrancaPedido>()));
            Mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
                .Returns(new PreferenceCriadaResult("pref", "https://mp.test/preferencia"));
            var cobrar = new GerarCobrancaPedidoUseCase(Substitute.For<IPedidoRepository>(), Core.StorefrontRepo, cobrancas,
                Mp, Core.Servico(), Uow, TimeProvider.System, NullLogger<GerarCobrancaPedidoUseCase>.Instance);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Acompanhamento:JwtSecret"] = new string('s', 40) }).Build();
            var acompanhar = new AcompanhamentoTokenService(config, TimeProvider.System);
            var guest = new IniciarCheckoutGuestUseCase(Core.StorefrontRepo, Core.Servico(), cobrar, Clientes, Core.PedidoRepo,
                Uow, acompanhar, Core.Atribuicao(), tenant, TimeProvider.System, NullLogger<IniciarCheckoutGuestUseCase>.Instance);
            var logado = new IniciarCheckoutUseCase(Core.Servico(), Core.StorefrontRepo, Clientes, idempotencia, cobrar,
                Core.Atribuicao(), tenant, Uow, NullLogger<IniciarCheckoutUseCase>.Instance);
            UseCase = new CheckoutSiteUseCase(Acesso, new AbrirSessaoChatSiteUseCase(Acesso, Sessoes, Uow),
                new ConversaChatSiteService(Conversas, Uow), Conversas, Clientes, Core.PedidoRepo, idempotencia, repo,
                logado, guest, acompanhar, Substitute.For<IOperacaoEventPublisher>(), cobrancas, Uow, TimeProvider.System, NullLogger<CheckoutSiteUseCase>.Instance);
        }

        public IniciarCheckoutGuestInput Guest() => new("casa-da-baba", "Maria", "11987654321", "01310100", "120",
            [new(Core.CardapioItemId, 2)], Core.JanelaId, Core.DataEntrega);
        public IniciarCheckoutInput Logado(Guid id) => new("casa-da-baba", id, [new(Core.CardapioItemId, 2)],
            Core.JanelaId, Core.DataEntrega, "01310100", Numero: "120");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loja_sem_chat_preserva_checkout_legado_sem_header(bool autenticado)
    {
        var c = new Cenario();
        c.Flags.ListarAtivasAsync(c.Empresa, Arg.Any<CancellationToken>()).Returns([]);
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = c.Empresa };
        c.Clientes.GetByIdAsync(cliente.Id, Arg.Any<CancellationToken>()).Returns(cliente);
        var pedidoId = autenticado
            ? (await c.UseCase.LogadoAsync(c.Logado(cliente.Id), Endereco, null, null)).PedidoId
            : (await c.UseCase.GuestAsync(c.Guest(), Endereco, null, null)).PedidoId;
        c.Core.PedidosAdicionados.Should().ContainSingle().Which.Id.Should().Be(pedidoId);
        c.Core.PedidosAdicionados.Single().Observacoes.Should().Contain("Avenida Paulista, 120");
        c.ListaConversas.Should().BeEmpty();
        await c.Sessoes.DidNotReceive().AddAsync(Arg.Any<SessaoChatSite>(), Arg.Any<CancellationToken>());
        await c.Mp.Received(1).CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_presente_nao_permite_fallback_quando_chat_desabilitado()
    {
        var c = new Cenario();
        c.Flags.ListarAtivasAsync(c.Empresa, Arg.Any<CancellationToken>()).Returns([]);
        await c.UseCase.Invoking(x => x.GuestAsync(c.Guest(), Endereco, c.Token, Guid.NewGuid()))
            .Should().ThrowAsync<ChatSiteIndisponivelException>();
        c.Core.PedidosAdicionados.Should().BeEmpty();
    }

    [Fact]
    public async Task Fallback_legado_nao_aceita_cliente_de_outra_loja()
    {
        var c = new Cenario();
        c.Flags.ListarAtivasAsync(c.Empresa, Arg.Any<CancellationToken>()).Returns([]);
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = Guid.NewGuid() };
        c.Clientes.GetByIdAsync(cliente.Id, Arg.Any<CancellationToken>()).Returns(cliente);
        await c.UseCase.Invoking(x => x.LogadoAsync(c.Logado(cliente.Id), Endereco, null, null))
            .Should().ThrowAsync<RegraDeDominioVioladaException>();
        c.Core.PedidosAdicionados.Should().BeEmpty();
    }

    [Fact]
    public async Task Guest_vincula_pedido_sem_identidade_e_preserva_endereco_antes_da_cobranca()
    {
        var c = new Cenario();
        var clienteAlheio = new Cliente { Id = Guid.NewGuid(), EmpresaId = c.Empresa, Nome = "Titular" };
        c.Clientes.GetByTelefoneHashAsync(c.Empresa, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(clienteAlheio);
        c.Mp.When(x => x.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())).Do(_ =>
        {
            c.Registros.Values.Single().FaturaId.Should().Be(c.Core.PedidosAdicionados.Single().Id);
            c.ListaConversas.Single().PedidoEmAndamentoId.Should().Be(c.Core.PedidosAdicionados.Single().Id);
        });
        var r = await c.UseCase.GuestAsync(c.Guest(), Endereco, c.Token, Guid.NewGuid());
        c.ListaConversas.Should().ContainSingle().Which.ClienteId.Should().BeNull();
        c.Core.PedidosAdicionados.Single().ClienteId.Should().NotBe(clienteAlheio.Id);
        c.Core.PedidosAdicionados.Single().Observacoes.Should().Contain("Avenida Paulista, 120, apto 3").And.Contain("São Paulo/SP");
        c.Mensagens.Should().ContainSingle().Which.ExternoId.Should().BeNull();
        c.Conversas.ListarMensagensDepoisAsync(c.Empresa, c.ListaConversas.Single().Id, null, 50, Arg.Any<CancellationToken>()).Returns(c.Mensagens);
        var publicas = await new ListarMensagensChatSiteUseCase(c.Acesso, c.Sessoes, c.Conversas).ExecuteAsync("casa-da-baba", c.Token, null);
        publicas.Should().BeEmpty("o resumo interno não revela cadastro ao visitante");
        r.PedidoId.Should().Be(c.ListaConversas.Single().PedidoEmAndamentoId!.Value);
    }

    [Fact]
    public async Task Concorrente_aguarda_e_retry_retorna_mesmo_pedido_sem_cobrar_duas_vezes()
    {
        var c = new Cenario(); var key = Guid.NewGuid();
        var pagamento = new TaskCompletionSource<PreferenceCriadaResult>();
        c.Mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(pagamento.Task);
        var primeira = c.UseCase.GuestAsync(c.Guest(), Endereco, c.Token, key);
        await c.UseCase.Invoking(x => x.GuestAsync(c.Guest(), Endereco, c.Token, key)).Should().ThrowAsync<CheckoutEmAndamentoException>();
        pagamento.SetResult(new PreferenceCriadaResult("pref", "https://mp.test/preferencia"));
        var original = await primeira;
        var repetida = await c.UseCase.GuestAsync(c.Guest(), Endereco, c.Token, key);
        repetida.PedidoId.Should().Be(original.PedidoId);
        repetida.FreteEstimado.Should().Be(original.FreteEstimado);
        c.Core.PedidosAdicionados.Should().ContainSingle(); c.Mensagens.Should().ContainSingle();
        await c.Mp.Received(1).CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resposta_perdida_apos_cobranca_gravada_e_recuperada_sem_novo_pagamento()
    {
        var c = new Cenario(); var key = Guid.NewGuid();
        var original = await c.UseCase.GuestAsync(c.Guest(), Endereco, c.Token, key);
        typeof(CheckoutIdempotency).GetProperty(nameof(CheckoutIdempotency.InitPoint))!.SetValue(c.Registros.Values.Single(), null);
        var recuperada = await c.UseCase.GuestAsync(c.Guest(), Endereco, c.Token, key);
        recuperada.PedidoId.Should().Be(original.PedidoId);
        recuperada.LinkPagamento.Should().Be(original.LinkPagamento);
        c.Core.PedidosAdicionados.Should().ContainSingle(); c.Mensagens.Should().ContainSingle();
        await c.Mp.Received(1).CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_guest_com_novo_token_recupera_o_pedido_sem_transferir_historico()
    {
        var c = new Cenario(); var key = Guid.NewGuid();
        var original = await c.UseCase.GuestAsync(c.Guest(), Endereco, c.Token, key);
        var token = AcessoChatSite.NovoToken();
        var nova = SessaoChatSite.Abrir(c.Empresa, c.Core.Storefront.Id, AcessoChatSite.HashDoToken(token), DateTime.UtcNow);
        c.Sessoes.ObterPorTokenHashAsync(c.Empresa, nova.TokenHash, Arg.Any<CancellationToken>()).Returns(nova);
        var retry = await c.UseCase.GuestAsync(c.Guest(), Endereco, token, key);
        retry.PedidoId.Should().Be(original.PedidoId);
        nova.ConversaId.Should().BeNull("a chave da tentativa autoriza recuperar o pedido, não todo o histórico do chat");
        c.Core.PedidosAdicionados.Should().ContainSingle();
        await c.Mp.Received(1).CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mesma_chave_com_endereco_alterado_recusa_sem_novo_pedido()
    {
        var c = new Cenario(); var key = Guid.NewGuid();
        await c.UseCase.GuestAsync(c.Guest(), Endereco, c.Token, key);
        await c.UseCase.Invoking(x => x.GuestAsync(c.Guest(), Endereco with { Complemento = "apto 9" }, c.Token, key))
            .Should().ThrowAsync<IdempotencyMismatchException>();
        c.Core.PedidosAdicionados.Should().ContainSingle();
    }

    [Fact]
    public async Task Sessao_de_outra_loja_recusa_antes_de_criar_pedido()
    {
        var c = new Cenario();
        c.Sessoes.ObterPorTokenHashAsync(c.Empresa, c.Sessao.TokenHash, Arg.Any<CancellationToken>())
            .Returns(SessaoChatSite.Abrir(c.Empresa, Guid.NewGuid(), c.Sessao.TokenHash, DateTime.UtcNow));
        await c.UseCase.Invoking(x => x.GuestAsync(c.Guest(), Endereco, c.Token, Guid.NewGuid()))
            .Should().ThrowAsync<SessaoChatSiteInvalidaException>();
        c.Core.PedidosAdicionados.Should().BeEmpty(); c.Registros.Should().BeEmpty();
    }

    [Fact]
    public async Task Cliente_verificado_de_outro_tenant_recusa_antes_de_reservar()
    {
        var c = new Cenario(); var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = Guid.NewGuid() };
        c.Clientes.GetByIdAsync(cliente.Id, Arg.Any<CancellationToken>()).Returns(cliente);
        await c.UseCase.Invoking(x => x.LogadoAsync(c.Logado(cliente.Id), Endereco, c.Token, Guid.NewGuid()))
            .Should().ThrowAsync<RegraDeDominioVioladaException>();
        c.Core.PedidosAdicionados.Should().BeEmpty();
    }

    [Fact]
    public async Task Cliente_otp_reutiliza_conversa_em_nova_sessao()
    {
        var c = new Cenario(); var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = c.Empresa };
        c.Clientes.GetByIdAsync(cliente.Id, Arg.Any<CancellationToken>()).Returns(cliente);
        await c.UseCase.LogadoAsync(c.Logado(cliente.Id), Endereco, c.Token, Guid.NewGuid());
        var novoToken = AcessoChatSite.NovoToken();
        var outraSessao = SessaoChatSite.Abrir(c.Empresa, c.Core.Storefront.Id, AcessoChatSite.HashDoToken(novoToken), DateTime.UtcNow);
        c.Sessoes.ObterPorTokenHashAsync(c.Empresa, outraSessao.TokenHash, Arg.Any<CancellationToken>()).Returns(outraSessao);
        await c.UseCase.LogadoAsync(c.Logado(cliente.Id), Endereco, novoToken, Guid.NewGuid());
        c.ListaConversas.Should().ContainSingle().Which.ClienteId.Should().Be(cliente.Id);
        outraSessao.ConversaId.Should().Be(c.Sessao.ConversaId);
    }

    [Fact]
    public void Chave_sem_widget_e_estavel_mas_isolada_por_loja_e_cliente()
    {
        var c = new Cenario(); var key = Guid.NewGuid();
        var outra = SessaoChatSite.Abrir(c.Empresa, c.Core.Storefront.Id, new string('b', 64), DateTime.UtcNow);
        CheckoutSiteUseCase.ChaveEscopada(c.Sessao, null, key).Should().Be(CheckoutSiteUseCase.ChaveEscopada(outra, null, key));
        var outraLoja = SessaoChatSite.Abrir(Guid.NewGuid(), Guid.NewGuid(), new string('c', 64), DateTime.UtcNow);
        CheckoutSiteUseCase.ChaveEscopada(c.Sessao, null, key).Should().NotBe(CheckoutSiteUseCase.ChaveEscopada(outraLoja, null, key));
        CheckoutSiteUseCase.ChaveEscopada(c.Sessao, Guid.NewGuid(), key).Should().NotBe(CheckoutSiteUseCase.ChaveEscopada(c.Sessao, Guid.NewGuid(), key));
    }

    [Theory]
    [InlineData("99999999", "120")]
    [InlineData("01310100", "999")]
    public async Task Endereco_diferente_da_cotacao_recusa(string cep, string numero)
    {
        var c = new Cenario();
        await c.UseCase.Invoking(x => x.GuestAsync(c.Guest(), Endereco with { Cep = cep, Numero = numero }, c.Token, Guid.NewGuid()))
            .Should().ThrowAsync<Exception>();
        c.Core.PedidosAdicionados.Should().BeEmpty(); c.Registros.Should().BeEmpty();
    }
}
