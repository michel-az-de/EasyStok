// Atendimento (ADR-0050, ADR-0051). Use case, ferramenta do agente e tratador de botão entram por
// convenção (ServiceCollectionExtensions.AtendimentoConvencao.cs, #1178): spec nova NÃO edita este
// arquivo para eles. Aqui fica só o que tem interface própria, outro ciclo de vida ou não é use case.

using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Operacao;
using EasyStock.Application.UseCases.Atendimento;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registra os UseCases do módulo de atendimento por WhatsApp.</summary>
    public static IServiceCollection AddEasyStockAtendimentoUseCases(this IServiceCollection services)
    {
        // SSE de operação (S18): no-op por padrão; a Api substitui pelo publisher sobre o broker.
        services.TryAddSingleton<IOperacaoEventPublisher, NoOpOperacaoEventPublisher>();
        // S48: link do cardápio com token da conversa e o pedido que volta do site por ele.
        services.AddScoped<LinkCardapioConversaService>();
        services.AddScoped<ArmazenadorMidiaWhatsApp>();
        services.AddScoped<ResolvedorCanal>();
        // S60: reserva por SMS, desligada por padrão; a Api liga só com chave e provedor real.
        services.TryAddSingleton(ReservaSmsOpcoes.Desligada);
        services.AddScoped<ReservaSmsAtendimento>();

        // S38: consentimento do cliente final por canal e opt-out por palavra.
        services.AddScoped<PoliticaEnvioCliente>();
        services.AddScoped<AvisoStatusPedidoCliente>(); // S13: aviso de status ao cliente
        services.AddScoped<OptOutPorPalavra>();
        services.AddScoped<SaudacaoAtendimento>();

        // S42: variáveis e CRUDs agrupados (*UseCases, fora do sufixo da convenção).
        services.AddScoped<VariaveisAtendimento>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Automacoes.RespostasProntasUseCases>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Automacoes.AutomacoesUseCases>();

        // S54: caderno da loja para o agente.
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Caderno.CadernoUseCases>();

        // S06: agente de atendimento (LLM com ferramentas) e roteador de botões sem LLM.
        services.AddScoped<AgenteAtendimentoService>();
        services.AddScoped<IEscaladorConversa, EscalarConversaUseCase>(); // S07: Assumir + nota + Push + SSE

        services.AddScoped<RoteadorAcoesBotao>();
        services.AddScoped<ConfirmarEnderecoPendente>(); // S14

        // S26: avaliação de um toque (use case em UseCases.Storefront, fora da convenção do atendimento).
        services.AddScoped<EasyStock.Application.UseCases.Storefront.Avaliacao.RegistrarAvaliacaoSimplesUseCase>();

        // S31: interesse em item (use cases em UseCases.Campanhas, fora da convenção do atendimento).
        services.AddScoped<EasyStock.Application.UseCases.Campanhas.Interesse.RegistrarInteresseItemUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Campanhas.Interesse.ListarSugestoesInteresseUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Campanhas.Interesse.MarcarInteresseAtendidoUseCase>(); // #1228
        services.AddScoped<EasyStock.Application.UseCases.Campanhas.Interesse.ListarInteressesDoClienteUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Campanhas.Interesse.FecharInteressesDoPedidoUseCase>();

        // S36: chat do site (canal próprio, sem provedor externo)
        services.AddScoped<ICanalMensageria, CanalChatSite>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.ChatSite.AcessoChatSite>();

        return services.AddAtendimentoPorConvencao();
    }
}
