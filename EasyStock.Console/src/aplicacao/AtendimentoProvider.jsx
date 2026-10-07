import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { estadoInicial, reducer } from './reducer'
import * as acao from './acoes'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from './contextos'
import { carregarConversas, proximoId } from '../infra/repositorioConversas'
import { carregarCatalogo } from '../infra/repositorioCatalogo'
import { carregarHistorico } from '../infra/historicoPedidos'
import { pedirSugestao } from '../infra/conexaoAgente'
import { REGRAS_PADRAO } from '../dominio/automacao'
import { FUNCIONAMENTO_PADRAO, estaAberta } from '../dominio/funcionamento'
import {
  ACOES, acaoSugerida, classificarIntencao, confiancaDe, itensSemSaldoCitados,
  montarPrompt, rascunhoSugerido,
} from '../dominio/agente'
import { montarPromptLivre } from '../dominio/assistente'
import { historicoComPedidoVivo } from '../dominio/cliente'
import {
  conversasBloqueadasDoBalcao, conversasDoBalcao, conversasEncerradasDoBalcao, ordenarBalcao,
} from '../dominio/conversa'
import { conversasParaEncerrarPorJanela } from '../dominio/janela'
import { ocupacaoDeHoje } from '../dominio/entrega'
import { EVENTOS_DE_SOM, motivoDaPassagem } from '../dominio/automatico'
import { precisaEscalarPorArea } from '../dominio/areaEntrega'
import { entregaPassouDoPrazo, ocorrenciaAberta, ORIGENS_OCORRENCIA } from '../dominio/ocorrencia'
import { pedirPermissaoDeNotificacao, permissaoDeNotificacao } from '../infra/notificacaoNavegador'
import { lembretesAutomaticos, listaDeLembretes } from '../dominio/lembrete'
import { useAvisoSonoro } from './useAvisoSonoro'
import { audioDestravado, destravarAudio } from '../infra/som'
import { gravarPreferenciaSom, lerPreferenciaSom } from '../infra/preferenciaSom'
import { conectarPrincipal } from '../infra/canalEntreJanelas'
import { FONTE_API } from '../infra/fonteDados'
import { comApi } from './acoesApi'
import { criarAcoes } from './criarAcoes'
import { useSincronizacaoApi } from './useSincronizacaoApi'
import { gravarRascunhos, lerRascunhos } from '../infra/api/rascunhosDaSessao'

const carregar = () => ({
  conversas: carregarConversas(),
  catalogo: carregarCatalogo(),
  regras: REGRAS_PADRAO,
  funcionamento: FUNCIONAMENTO_PADRAO,
  // Integração 48, decisão do gerente: a massa nasce com a loja ABERTA pela
  // Thatiane, no modo manual, coerente com os pedidos em preparo do dia. Sem
  // isso, relógio do Simular fora das 8h-22h fechava a loja sozinho e as
  // conversas saíam de "Precisa de você". Fechar é um toque no topo.
  // No modo API (F02) quem manda é o expediente da API; até ele chegar, segue o horário.
  lojaAberta: FONTE_API ? null : true,
  modoAgente: 'simulado',
  // Chave "Som da cozinha" (US-010): lembra a escolha entre uma visita e
  // outra, guardada em infra/preferenciaSom.js.
  som: lerPreferenciaSom(),
})

export function AtendimentoProvider({ agora, sessao = null, children }) {
  const [estado, despachar] = useReducer(reducer, null, () => ({
    ...estadoInicial(carregar()),
    // Modo API (F01): a lista nasce vazia e chega pelo polling.
    sincronizacao: { estado: FONTE_API ? 'carregando' : 'desligada', mensagem: null, aviso: null },
    // Modo API (F02): mensagens do expediente (S40); horário e controle vivem em
    // `funcionamento` e `lojaAberta`, os mesmos do modo demonstração.
    expediente: { carregado: false, mensagemForaDoHorario: '', mensagemLojaFechada: '' },
    // F07, item 6: o rascunho de antes do 401 volta depois do novo login.
    ...(FONTE_API ? { rascunhos: lerRascunhos(sessao) } : {}),
  }))
  useEffect(() => { if (FONTE_API) gravarRascunhos(sessao, estado.rascunhos) }, [sessao, estado.rascunhos])
  useSincronizacaoApi({ ativo: FONTE_API, usuario: sessao?.usuario ?? null, despachar, selecionadaId: estado.selecionadaId })

  // Relógio da tela mais o deslocamento do painel de simulações (seção 7,
  // "+10 min"/"+30 min"/"Agora"): as ações carimbam o mesmo instante que a
  // tela exibe, e os dois andam juntos. Deslocamento zero (padrão) é o
  // relógio de sempre, sem diferença nenhuma para quem não abriu o painel.
  const agoraEfetivo = agora + (estado.relogio?.deslocamentoMs ?? 0)
  const agoraRef = useRef(agoraEfetivo)
  useEffect(() => { agoraRef.current = agoraEfetivo }, [agoraEfetivo])

  // Loja aberta agora (Frente Horário e loja, pedido do dono 24/09/2026):
  // combina o horário configurado com o estado manual do topo, uma vez só,
  // porque quem lê "Precisa de você", o Balcão e o aviso sonoro precisam da
  // MESMA resposta.
  const aberta = estaAberta(agoraEfetivo, { funcionamento: estado.funcionamento, lojaAberta: estado.lojaAberta })

  const pendentes = useRef([])
  useEffect(() => () => pendentes.current.forEach(clearTimeout), [])

  // O estado mais recente fica num ref para os criadores de ação assíncronos
  // não precisarem de dependência e não recriarem o objeto de ações.
  const estadoRef = useRef(estado)
  useEffect(() => { estadoRef.current = estado }, [estado])

  // Sincronia com a janela de Entregas (seção 6): esta janela é sempre a
  // "principal" do canal. Sem nenhuma janela de Entregas aberta, o canal só
  // fica ouvindo, sem custo nenhum. `TelaEntregas.jsx` (F6) é quem conecta o
  // lado "espelho" e manda `pedir-estado`/`acao`.
  //
  // Modo API (F06): sem canal. Entregas e Cozinha leem a API (F04, F05), e o canal
  // publicaria o estado inteiro e aplicaria ação recebida sem passar por `comApi`.
  const canalPrincipalRef = useRef(null)
  useEffect(() => {
    if (FONTE_API) return undefined
    canalPrincipalRef.current = conectarPrincipal({
      estadoAtual: () => estadoRef.current,
      aplicarAcao: (acaoRecebida) => despachar(acaoRecebida),
    })
    return () => canalPrincipalRef.current?.fechar()
  }, [])
  useEffect(() => { canalPrincipalRef.current?.mandarEstado() }, [estado])

  const consultarAgente = useCallback(async (id) => {
    const atual = estadoRef.current
    const conversa = atual.conversas.find((c) => c.id === id)
    if (!conversa) return
    despachar({ tipo: acao.AGENTE_PEDINDO, id })
    const leitura = classificarIntencao(conversa)
    // A ocupação das janelas é CONTADA dos pedidos aqui, uma vez, e entregue ao
    // agente. Sem isto ele lia o `ocupadas` fixo do catálogo e oferecia janela
    // que o painel de entregas mostrava cheia. Rodada 13 (issue #42, UC-03 E1):
    // `ocupacaoDeHoje` também esconde janela pausada/fora do dia e, com o
    // cardápio em mãos, aplica a proposta Q2 de capacidade por linha.
    const ocupacoes = ocupacaoDeHoje(
      atual.catalogo.janelas, atual.conversas, agoraRef.current, conversa.pedido?.janela ?? null,
      atual.catalogo.cardapio,
    )
    try {
      const sugestao = await pedirSugestao({
        modo: atual.modoAgente,
        prompt: montarPrompt(conversa, atual.catalogo, ocupacoes),
        rascunho: rascunhoSugerido(leitura, conversa, atual.catalogo, ocupacoes, agoraRef.current),
        acao: acaoSugerida(leitura, conversa, atual.catalogo),
        intencao: leitura.intencao,
        confianca: confiancaDe(leitura),
      })
      despachar({ tipo: acao.AGENTE_RESPONDEU, id, sugestao })
      // Devolveu para a dona: a conversa ganha a marca "Precisa de você" com o
      // motivo, e o lembrete de resposta nasce a partir dela.
      if (sugestao.acao === ACOES.PASSAR_PARA_DONA) {
        despachar({
          tipo: acao.PASSAR_PARA_DONA,
          id,
          agora: agoraRef.current,
          motivo: motivoDaPassagem(
            leitura.intencao.chave,
            itensSemSaldoCitados(conversa, atual.catalogo),
            precisaEscalarPorArea(conversa),
          ),
        })
      }
      // US-049/RN-35: abertura automática da ocorrência, não um botão. Só
      // para pedido já entregue (UC-06, pré-condição) e só uma vez por pedido.
      if (leitura.reclamacaoDeProduto && conversa.pedido?.estado === 'entregue'
        && !ocorrenciaAberta(conversa.pedido?.ocorrencia)) {
        despachar({
          tipo: acao.ABRIR_OCORRENCIA,
          id,
          agora: agoraRef.current,
          ocorrenciaId: proximoId('ocorrencia'),
          relatoCliente: leitura.trecho,
        })
      }
    } catch (erro) {
      despachar({ tipo: acao.AGENTE_FALHOU, id, erro: erro.message })
    }
  }, [])

  // Balão do assistente (rodada 7): pergunta livre da Thatiane, sempre pela IA
  // de verdade (`modo: 'cli'`, nunca o simulado do dev), com o contexto da
  // conversa no prompt. Sem `despachar`: a tela guarda a troca sozinha, não
  // faz sentido no estado global (não é rascunho, não é mensagem enviada).
  // O erro sobe cru para quem chamou; `remoto` (infra/conexaoAgente.js) já
  // devolve mensagem clara quando o servidor local não responde.
  const perguntarAssistente = useCallback(async (pergunta, conversa, historicoDoCliente) => {
    const resposta = await pedirSugestao({
      modo: 'cli',
      prompt: montarPromptLivre(pergunta, conversa, historicoDoCliente),
      intencao: null,
      confianca: null,
    })
    return resposta.texto
  }, [])

  // Item 6 (rodada 10): permissão de notificação do navegador. Estado do
  // Provider, não do reducer: `Notification.permission` não é dado do
  // atendimento, e a promessa do pedido resolve fora do fluxo de ações.
  const [permissaoNotificacao, setPermissaoNotificacao] = useState(() => permissaoDeNotificacao())
  const pedirNotificacaoDoNavegador = useCallback(async () => {
    setPermissaoNotificacao(await pedirPermissaoDeNotificacao())
  }, [])

  const acoes = useMemo(() => criarAcoes({
    despachar, agoraRef, estadoRef, pendentes, consultarAgente, perguntarAssistente, pedirNotificacaoDoNavegador,
  }), [consultarAgente, perguntarAssistente, pedirNotificacaoDoNavegador])

  // Item 7 (Sininho revê o que soou enquanto ela estava fora) e item 2 (selo
  // "novo, não visto" do pagamento, independente do prazo de 10 min do sinal
  // verde). Estado local do Provider, não do reducer: nenhuma das duas
  // precisa viajar pelo canal entre janelas (rodada 10, fronteira da 82).
  const [eventosSonoros, setEventosSonoros] = useState([])
  const [pagamentosNaoVistos, setPagamentosNaoVistos] = useState(() => new Set())

  // Som da cozinha (D6, US-010). O timbre de cada evento mora em
  // infra/som.js; aqui só o QUANDO, por diferença de estado. Rodada 10:
  // `janelas` liga o motivo "pedido atrasado" (achado 3); `aoTocar` alimenta
  // o registro de sons recentes do Sininho (achado 7, item 7) e a marca de
  // "pagamento não visto" independente do sinal verde (item 2).
  useAvisoSonoro({
    conversas: estado.conversas, som: estado.som, agora: agoraEfetivo, aberta,
    janelas: estado.catalogo.janelas,
    aoTocar: (evento, conversaId) => {
      setEventosSonoros((atual) => [
        ...atual, { id: proximoId('som'), evento, conversaId, quando: agoraRef.current },
      ].slice(-30))
      if (evento === EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO) {
        setPagamentosNaoVistos((atual) => new Set(atual).add(conversaId))
      }
    },
  })

  // "Visto" nasce de um jeito só: ela abriu a conversa. Passar o tempo ou
  // fechar a tela nunca conta como ter visto (é exatamente o que o achado 4
  // reclamava do sinal verde).
  useEffect(() => {
    if (!estado.selecionadaId) return
    setPagamentosNaoVistos((atual) => {
      if (!atual.has(estado.selecionadaId)) return atual
      const novo = new Set(atual)
      novo.delete(estado.selecionadaId)
      return novo
    })
  }, [estado.selecionadaId])

  // Lembra a escolha da chave entre uma visita e outra.
  useEffect(() => { gravarPreferenciaSom(estado.som) }, [estado.som])

  // O navegador bloqueia áudio até o primeiro gesto na página. Um clique ou
  // toque em qualquer lugar (o Provider envolve a tela inteira) tenta
  // destravar; enquanto não destravar, `audioBloqueado` liga o aviso de uma
  // linha no ModalAutomacoes, que some assim que o gesto vale.
  const [audioBloqueado, setAudioBloqueado] = useState(() => !audioDestravado())
  useEffect(() => {
    if (!audioBloqueado) return
    const aoGesto = () => {
      destravarAudio()
      if (audioDestravado()) setAudioBloqueado(false)
    }
    window.addEventListener('pointerdown', aoGesto)
    return () => window.removeEventListener('pointerdown', aoGesto)
  }, [audioBloqueado])

  // Lembrete que nasce sozinho: conversa devolvida sem resposta e pedido
  // gerado sem pagamento. O reducer devolve o mesmo estado quando nada muda,
  // então isto não repinta a tela a cada volta do relógio.
  const conversasAgora = estado.conversas
  useEffect(() => {
    despachar({
      tipo: acao.SINCRONIZAR_LEMBRETES,
      automaticos: lembretesAutomaticos(conversasAgora, agoraRef.current),
    })
  }, [conversasAgora, agoraEfetivo])

  // Encerramento automático por janela (decisão 46, "Encerrar" v2): mesmo
  // gatilho de tempo do efeito acima, para a mesma regra de 24h que já
  // bloqueia o composer. Só despacha quando existe alguma conversa para
  // fechar, para não repintar a cada volta do relógio sem motivo.
  // Modo API (F06): quem encerra é o EasyStok; fechar só aqui seria encerrar de mentira.
  useEffect(() => {
    if (FONTE_API) return
    const ids = conversasParaEncerrarPorJanela(conversasAgora, agoraRef.current, estado.catalogo.canais)
    if (ids.length > 0) despachar({ tipo: acao.ENCERRAR_POR_JANELA, ids, agora: agoraRef.current })
  }, [conversasAgora, agoraEfetivo, estado.catalogo.canais])

  // Ocorrência de entrega de verdade (rodada 10, achado 7 / item 4): parada
  // já em rota que passou do fim da janela prometida abre sozinha, do mesmo
  // jeito que a reclamação abre sozinha hoje (US-049). Mesmo guarda do
  // reducer (`ABRIR_OCORRENCIA` não duplica quando já existe uma ocorrência
  // no pedido) evita reabrir a cada volta do relógio.
  useEffect(() => {
    const paradasAtrasadas = conversasAgora.filter((c) =>
      entregaPassouDoPrazo(c, estado.catalogo.janelas, agoraRef.current) && !ocorrenciaAberta(c.pedido?.ocorrencia))
    paradasAtrasadas.forEach((c) => {
      despachar({
        tipo: acao.ABRIR_OCORRENCIA,
        id: c.id,
        agora: agoraRef.current,
        ocorrenciaId: proximoId('ocorrencia'),
        relatoCliente: `Entrega do pedido ${c.pedido.numero} passou do horário prometido`,
        origem: ORIGENS_OCORRENCIA.ATRASO_ENTREGA,
      })
    })
  }, [conversasAgora, agoraEfetivo, estado.catalogo.janelas])

  const acoesAtivas = useMemo(
    () => (FONTE_API ? comApi(acoes, { despachar, agoraRef, estadoRef }) : acoes),
    [acoes],
  )

  // Modo API (F02): o expediente vem da API uma vez ao entrar; depois, a cada gravação.
  useEffect(() => {
    if (FONTE_API) acoesAtivas.recarregarExpediente()
  }, [acoesAtivas])
  useEffect(() => () => acoesAtivas.cancelarHorarioPendente?.(), [acoesAtivas])

  const valor = useMemo(() => {
    const selecionada = estado.conversas.find((c) => c.id === estado.selecionadaId) ?? null
    return {
      conversas: estado.conversas,
      selecionadaId: estado.selecionadaId,
      filtros: estado.filtros,
      regras: estado.regras,
      funcionamento: estado.funcionamento,
      lojaAberta: estado.lojaAberta,
      aberta,
      modoAgente: estado.modoAgente,
      agente: estado.agente,
      automaticoPausado: estado.automaticoPausado,
      alertasEstoque: estado.alertasEstoque,
      rascunho: estado.rascunhos[estado.selecionadaId] ?? '',
      // Ordenado aqui, uma vez só: quem lê `visiveis` (Balcão e o "abrir
      // primeira conversa" da ficha e do atendimento) precisa enxergar a
      // mesma ordem, senão o botão de estado vazio abre uma conversa
      // diferente da que está no topo da lista (achado da auditoria QA5).
      visiveis: ordenarBalcao(
        conversasDoBalcao(
          estado.conversas, estado.filtros, agoraEfetivo, estado.automaticoPausado, aberta, estado.catalogo.janelas,
        ),
        agoraEfetivo,
        {
          ordenacao: estado.filtros.ordenacao,
          automaticoPausado: estado.automaticoPausado,
          janelas: estado.catalogo.janelas,
          aberta,
        },
      ),
      // Os dois grupos recolhidos do fim de "Todas" (seção 1): mesmo canal e
      // busca da lista de cima, sem aba, porque nenhum dos dois existe em
      // "Precisa de você".
      encerradasDoBalcao: conversasEncerradasDoBalcao(estado.conversas, estado.filtros),
      bloqueadasDoBalcao: conversasBloqueadasDoBalcao(estado.conversas, estado.filtros),
      historico: selecionada ? historicoComPedidoVivo(selecionada.cliente?.historico ?? carregarHistorico(selecionada.id), selecionada.pedido) : [],
      ultimoAvanco: estado.ultimoAvanco,
      // Lote de papel (US-042): banner de queda e a modal de lançamento leem
      // só este campo, tirado de `estado.conexao` sem transformação nenhuma.
      conexao: estado.conexao,
      selecionada,
      // Rodada 5 (seção 7, passo zero): a tela inteira lê o relógio JÁ com o
      // deslocamento do painel de simulações somado, então nenhuma feature
      // precisa somar `relogio.deslocamentoMs` por conta própria.
      agora: agoraEfetivo,
      som: estado.som,
      // Aviso "Toque para ligar o som" do ModalAutomacoes (US-010).
      audioBloqueado,
      lembretes: listaDeLembretes({ lembretes: estado.lembretes }),
      vistos: estado.vistos,
      // Rodada 10: sons das últimas horas para o Sininho rever (item 7) e
      // quem pagou e ainda não foi visto, independente do sinal verde
      // expirar (item 2).
      eventosSonoros,
      pagamentosNaoVistos,
      permissaoNotificacao,
      // `ui.encerrando` (seção 5): id da conversa com a modal de
      // encerramento aberta, ou `null`.
      encerrando: estado.ui.encerrando,
      encerrandoNumero: estado.ui.encerrandoNumero ?? null,
      // Rodada 13 (issue #43): lotes, descobertos e ajustes lidos pela aba
      // Produção e cardápio de Gestão.
      producao: estado.producao,
      // Caixa (rodada 13, issue #44): a aba de Gestão lê só isto, direto.
      caixa: estado.caixa,
      // Frente Fidelidade e cupons (rodada 13, issue #45, registro 107):
      // cupons, regra de pontos, catálogo de recompensas, sorteios e resgate
      // por cadastro. Pontos GANHOS não vêm daqui: são derivados do
      // `historico` (acima), ver dominio/fidelidade.js.
      fidelidade: estado.fidelidade,
      // Modo API (F01): estado do polling e quem está logado.
      fonteApi: FONTE_API,
      sincronizacao: estado.sincronizacao,
      expediente: estado.expediente,
      sessao,
    }
  }, [estado, sessao, agoraEfetivo, audioBloqueado, aberta, eventosSonoros, pagamentosNaoVistos, permissaoNotificacao])

  return (
    <ContextoCatalogo.Provider value={estado.catalogo}>
      <ContextoAcoes.Provider value={acoesAtivas}>
        <ContextoEstado.Provider value={valor}>{children}</ContextoEstado.Provider>
      </ContextoAcoes.Provider>
    </ContextoCatalogo.Provider>
  )
}
