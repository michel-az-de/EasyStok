// Atendimento por WhatsApp (ADR-0050) — configuração por tenant (S08), status da integração
// (S01) e agente/conversa (S02, S06, S07 adicionam mais).

using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Operacao;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Atendimento.Webhook;
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
        services.AddScoped<ObterConfiguracaoAtendimentoUseCase>();
        // S10: pedido fechado na conversa pelo núcleo do checkout (a cobrança é da S11).
        services.AddScoped<CriarPedidoAtendimentoUseCase>();
        // S48: link do cardápio com token da conversa e o pedido que volta do site por ele.
        services.AddScoped<LinkCardapioConversaService>();
        services.AddScoped<CriarPedidoPeloCardapioConversaUseCase>();
        services.AddScoped<AtualizarConfiguracaoAtendimentoUseCase>();
        services.AddScoped<ObterStatusIntegracaoWhatsAppUseCase>();
        services.AddScoped<ProcessarEventoWhatsAppUseCase>();
        services.AddScoped<ProcessarEventoMensageriaMetaUseCase>(); // S35: Instagram e Messenger
        services.AddScoped<ProcessarMidiaWhatsAppJobUseCase>();
        services.AddScoped<ArmazenadorMidiaWhatsApp>();
        services.AddScoped<ResolvedorCanal>();

        // S38: consentimento do cliente final por canal e opt-out por palavra.
        services.AddScoped<PoliticaEnvioCliente>();
        services.AddScoped<AvisoStatusPedidoCliente>();
        services.AddScoped<OptOutPorPalavra>();

        // S39: mensagem programada ao cliente (console + disparador no processo da API).
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Programadas.AgendarMensagemProgramadaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Programadas.ListarMensagensProgramadasUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Programadas.CancelarMensagemProgramadaUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Programadas.ReservarMensagensProgramadasUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Programadas.DispararMensagemProgramadaUseCase>();

        // S42: respostas prontas e mensagens automáticas por gatilho (disparadas pelos handlers do outbox).
        services.AddScoped<VariaveisAtendimento>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Automacoes.RespostasProntasUseCases>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Automacoes.AutomacoesUseCases>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Automacoes.DispararAutomacaoUseCase>();

        // S43: lembretes da dona (console + avaliador no processo da API).
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Lembretes.CriarLembreteUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Lembretes.ListarLembretesUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Lembretes.ConcluirLembreteUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Lembretes.MarcarLembretesVistosUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Lembretes.AvaliarLembretesUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Consentimento.ListarConsentimentosClienteUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Consentimento.DefinirConsentimentosClienteUseCase>();
        services.AddScoped<IdentificarClientePorTelefoneUseCase>();
        services.AddScoped<SaudacaoAtendimento>();

        // S06: agente de atendimento (LLM com ferramentas) e roteador de botões sem LLM.
        services.AddScoped<AgenteAtendimentoService>();
        services.AddScoped<ProcessarTurnoAgenteUseCase>();
        services.AddScoped<IEscaladorConversa, EscalarConversaUseCase>(); // S07: Assumir + nota + Push + SSE
        services.AddScoped<RoteadorAcoesBotao>();
        services.AddScoped<IAcaoBotaoHandler, ConfirmarEnderecoAcaoBotao>();
        services.AddScoped<IAcaoBotaoHandler, EscolherJanelaAcaoBotao>(); // S16: devolve a vez ao agente
        // TODO(S26): acao:avaliacao.

        // Ferramentas do agente. As demais da tabela da S06 dependem da onda 2 e entram aqui quando
        // existirem.
        services.AddScoped<IFerramentaAgente, CriarPedidoFerramenta>(); // S10 + S11: pedido, total e link
        services.AddScoped<IFerramentaAgente, ListarJanelasFerramenta>(); // S16: janelas no prazo, com botões
        services.AddScoped<IFerramentaAgente, RegistrarInteresseFerramenta>(); // S31
        services.AddScoped<EasyStock.Application.UseCases.Campanhas.Interesse.RegistrarInteresseItemUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Campanhas.Interesse.ListarSugestoesInteresseUseCase>();
        services.AddScoped<IFerramentaAgente, ConsultarCardapioFerramenta>();
        services.AddScoped<IFerramentaAgente, EnviarCardapioImagemFerramenta>();
        services.AddScoped<IFerramentaAgente, ConsultarPedidoFerramenta>();
        services.AddScoped<IFerramentaAgente, EscalarParaDonaFerramenta>();
        services.AddScoped<IFerramentaAgente, EncerrarConversaFerramenta>();
        services.AddScoped<IFerramentaAgente, RegistrarRestricaoFerramenta>(); // S24: tag com origem agente
        services.AddScoped<IFerramentaAgente, RegistrarNotaFerramenta>(); // S24: nota interna

        // S14: endereço em texto livre e área de entrega.
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Endereco.ValidarEnderecoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Endereco.ConfirmarEnderecoClienteUseCase>();
        services.AddScoped<ConfirmarEnderecoPendente>();
        services.AddScoped<IFerramentaAgente, ValidarEnderecoFerramenta>();
        services.AddScoped<IFerramentaAgente, ConfirmarEnderecoFerramenta>();
        services.AddScoped<IFerramentaAgente, ListarEnderecoSalvoFerramenta>();

        // S07: handoff pelo console (inbox, envio da dona, assumir, liberar, encerrar, marcar lida).
        services.AddScoped<ListarConversasAtendimentoUseCase>();
        services.AddScoped<ListarMensagensConversaUseCase>();
        services.AddScoped<EnviarMensagemConsoleUseCase>();
        services.AddScoped<GerenciarConversaAtendimentoUseCase>();
        services.AddScoped<TransferirConversaUseCase>();
        services.AddScoped<ListarAtendentesUseCase>();

        // S47: assistente da dona (somente leitura, sem ferramentas).
        services.AddScoped<AssistenteDonaUseCase>();
        // S36: chat do site (canal próprio, sem provedor externo)
        services.AddScoped<EasyStock.Application.Ports.Output.Atendimento.ICanalMensageria, CanalChatSite>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.ChatSite.AcessoChatSite>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.ChatSite.AbrirSessaoChatSiteUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.ChatSite.EnviarMensagemVisitanteUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.ChatSite.ListarMensagensChatSiteUseCase>();

        return services;
    }
}
