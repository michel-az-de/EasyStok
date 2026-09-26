// Atendimento por WhatsApp (ADR-0050) — configuração por tenant (S08), status da integração
// (S01) e agente/conversa (S02, S06, S07 adicionam mais).

using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registra os UseCases do módulo de atendimento por WhatsApp.</summary>
    public static IServiceCollection AddEasyStockAtendimentoUseCases(this IServiceCollection services)
    {
        services.AddScoped<ObterConfiguracaoAtendimentoUseCase>();
        services.AddScoped<AtualizarConfiguracaoAtendimentoUseCase>();
        services.AddScoped<ObterStatusIntegracaoWhatsAppUseCase>();
        services.AddScoped<ProcessarEventoWhatsAppUseCase>();
        services.AddScoped<ProcessarMidiaWhatsAppJobUseCase>();
        services.AddScoped<ArmazenadorMidiaWhatsApp>();
        services.AddScoped<ResolvedorCanal>();
        services.AddScoped<IdentificarClientePorTelefoneUseCase>();
        services.AddScoped<SaudacaoAtendimento>();

        // S06: agente de atendimento (LLM com ferramentas) e roteador de botões sem LLM.
        services.AddScoped<AgenteAtendimentoService>();
        services.AddScoped<ProcessarTurnoAgenteUseCase>();
        services.AddScoped<IEscaladorConversa, EscalarConversaUseCase>(); // S07: Assumir + nota + Push + SSE
        services.AddScoped<RoteadorAcoesBotao>();
        services.AddScoped<IAcaoBotaoHandler, ConfirmarEnderecoAcaoBotao>();
        // TODO(S16): acao:escolher_janela; TODO(S26): acao:avaliacao.

        // Ferramentas do agente. As demais da tabela da S06 dependem da onda 2 e entram aqui quando
        // existirem: TODO(S14) validar_endereco e confirmar_endereco; TODO(S16) listar_janelas;
        // TODO(S10, S11, S32) criar_pedido; TODO(S24, S31) registrar_restricao, registrar_interesse
        // e registrar_nota. Até lá o prompt manda fechar pedido pelo link do cardápio ou pela dona.
        services.AddScoped<IFerramentaAgente, ConsultarCardapioFerramenta>();
        services.AddScoped<IFerramentaAgente, EnviarCardapioImagemFerramenta>();
        services.AddScoped<IFerramentaAgente, ConsultarPedidoFerramenta>();
        services.AddScoped<IFerramentaAgente, EscalarParaDonaFerramenta>();
        services.AddScoped<IFerramentaAgente, EncerrarConversaFerramenta>();

        // S07: handoff pelo console (inbox, envio da dona, assumir, liberar, encerrar, marcar lida).
        services.AddScoped<ListarConversasAtendimentoUseCase>();
        services.AddScoped<ListarMensagensConversaUseCase>();
        services.AddScoped<EnviarMensagemConsoleUseCase>();
        services.AddScoped<GerenciarConversaAtendimentoUseCase>();

        return services;
    }
}
