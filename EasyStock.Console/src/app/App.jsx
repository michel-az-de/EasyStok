import { useCallback, useEffect, useRef, useState } from 'react'
import {
  closestCenter, DndContext, DragOverlay, PointerSensor, TouchSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import { AtendimentoProvider } from '../aplicacao/AtendimentoProvider'
import { criarMemoriaDoLaco } from '../aplicacao/cicloDeSincronizacao'
import { ContextoRascunhosDosAjustes, useGravacoesDosAjustes } from '../aplicacao/rascunhosDosAjustes'
import { useAcoes, useAtendimento, useCatalogo } from '../aplicacao/contextos'
import { useReacaoClienteSimulado } from '../aplicacao/useReacaoClienteSimulado'
import { useDigitacaoAutomatica } from '../aplicacao/useDigitacaoAutomatica'
import { CHAVE_CARDAPIO, usePreferenciaDePainel } from '../aplicacao/preferenciaDosPaineis'
import { CARDAPIO, larguraDoCardapio, larguraMaxima, normalizarCardapio } from '../dominio/paineis'
import { useHash } from '../hooks/useHash'
import { useLarguras } from '../hooks/useLarguras'
import { useRelogio } from '../hooks/useRelogio'
import { DESKTOP, useTamanhoTela } from '../hooks/useTamanhoTela'
import { INSTANTE_INICIAL, LINHAS_PRODUTO } from '../infra/catalogo'
import { FONTE_API } from '../infra/fonteDados'
import { identidadeDaSessao } from '../infra/api/sessao'
import { gravarVisita, lerVisita } from '../infra/api/visitaDaSessao'
import { useSessaoApi } from '../aplicacao/useSessaoApi'
import { useNotificacoesDaSessao } from '../aplicacao/useNotificacoesDaSessao'
import { useAvisosNoAparelho } from '../aplicacao/useAvisosNoAparelho'
import { SinoDaSessao } from '../features/notificacoes/SinoDaSessao'
import { ContextoAcessoModulos, useAcessoModulos, useModulosDaSessao } from '../aplicacao/acessoModulos'
import { moduloDaRota, permiteModulo, permiteRota } from '../dominio/acessoModulos'
import { Vazio } from '../componentes/Vazio'
import { Botao } from '../componentes/Botao'
import { Modal } from '../componentes/Modal'
import { TelaLogin } from '../features/login/TelaLogin'
import { TelaConvite } from '../features/login/TelaConvite'
import { ehRotaDeConvite, tokenDoConvite } from '../dominio/usuarios'
import { AvisoDaAcaoApi, FaixaApi, LimparAvisoAoNavegar } from './FaixaApi'
import { contarPrecisaDeVoce, precisaDeVoce } from '../dominio/automatico'
import { useTituloDaAba } from '../aplicacao/useTituloDaAba'
import { TITULO_BASE, tituloDaAba } from '../dominio/tituloDaAba'
import { pedidoAcabouDeNascer } from '../dominio/pedido'
import { canalDaConversa } from '../dominio/canal'
import { envioBloqueado } from '../dominio/janela'
import {
  HASH_HALL, ROTA_CARDAPIO_LINK, ROTA_COZINHA, ROTA_ENTREGAS, ROTA_HALL, ROTA_PRINCIPAL, rotaDaHash,
} from '../dominio/rota'
import { EditorItemCardapio } from '../features/cardapio/EditorItemCardapio'
import { CartaoArrasto, PainelCardapio } from '../features/cardapio/PainelCardapio'
import { ModalNota } from '../features/notas/ModalNota'
import { PainelGaleria } from '../features/anexos/PainelGaleria'
import { ModalAutomacoes } from '../features/automacoes/ModalAutomacoes'
import { ModalHorariosDaSemana } from '../features/automacoes/ModalHorariosDaSemana'
import { ModalGestao, PainelDeAjuste } from '../features/gestao/ModalGestao'
import { HallDeModulos } from '../features/hall/HallDeModulos'
import { PainelAgora } from '../features/agora/PainelAgora'
import { avisoDeResponder, filasDoAgora } from '../dominio/agora'
import { hashDoModulo } from '../dominio/modulos'
import { MolduraDoModulo } from '../features/hall/MolduraDoModulo'
import { GavetaEntregas } from '../features/entregas/GavetaEntregas'
import { TelaEntregas } from '../features/entregas/TelaEntregas'
import { GavetaEntregasApi } from '../features/entregas/GavetaEntregasApi'
import { TelaEntregasApi } from '../features/entregas/TelaEntregasApi'
import { CadastroEntregaApi } from '../features/entregas/CadastroEntregaApi'
import { TelaCozinha } from '../features/cozinha/TelaCozinha'
import { TelaCozinhaApi } from '../features/cozinha/TelaCozinhaApi'
import { AvisoCardapioDoSite, TelaCardapioLink } from '../features/cardapio-link/TelaCardapioLink'
import { ModalEncerrar } from '../features/encerramento/ModalEncerrar'
import { FilaCanhotos } from '../features/ficha-cliente/FilaCanhotos'
import { GestoLoja } from '../features/loja/GestoLoja'
import { ModalLoteDePapel } from '../features/lote-papel/ModalLoteDePapel'
import { PainelSimulacoes } from '../features/simulacoes/PainelSimulacoes'
import { useRoteiro } from '../features/simulacoes/useRoteiro'
import { PainelAgente } from '../features/agente/PainelAgente'
import { BalaoAssistente } from '../features/assistente/BalaoAssistente'
import { Moldura } from './Moldura'
import { PilhaDeAvisos } from './PilhaDeAvisos'
import { AjudaDoConsole } from '../features/ajuda/AjudaDoConsole'
import { NavegacaoDoConsole } from './NavegacaoDoConsole'
import css from './moldura.module.css'

// Só o topo conhece todas as features. Feature nenhuma importa outra feature.
function Composicao({ rota }) {
  const {
    selecionada, regras, encerrando, encerrandoNumero, agente, modoAgente, agora, aberta, horarioDaLoja,
    conversas, automaticoPausado,
  } = useAtendimento()
  const {
    salvarNota, adicionarItem, ajustarQuantidade, alternarRegra, fecharEncerramento,
    consultarAgente, limparAgente, trocarModoAgente, definirRascunho,
  } = useAcoes()
  const { canais, janelas } = useCatalogo()

  // Item 8 (rodada 10): título da aba com a contagem de "Precisa de você",
  // para quem trocou de janela no mesmo computador (US-010, saber sem olhar).
  useTituloDaAba(tituloDaAba(rota, contarPrecisaDeVoce(conversas, agora, automaticoPausado, aberta, janelas, horarioDaLoja)))
  const [modal, setModal] = useState(null)
  // #1847: o rascunho pertence ao cliente escolhido ao abrir, mesmo ao navegar ou salvar.
  const [nota, setNota] = useState(null)
  // #1761 (L3): gerada a cobrança, o cardápio virava "Consulta do cardápio" com tudo desligado,
  // cobrindo a lista e apertando a conversa. Fecha sozinho; "Ver cardápio" na Ficha reabre.
  const pedidoNaTela = { conversaId: selecionada?.id ?? null, pedidoId: selecionada?.pedido?.pedidoId ?? null }
  const pedidoVisto = useRef(pedidoNaTela)
  useEffect(() => {
    const nasceu = pedidoAcabouDeNascer(pedidoVisto.current, { conversaId: pedidoNaTela.conversaId, pedidoId: pedidoNaTela.pedidoId })
    pedidoVisto.current = { conversaId: pedidoNaTela.conversaId, pedidoId: pedidoNaTela.pedidoId }
    if (nasceu) setModal((atual) => (atual === 'cardapio' ? null : atual))
  }, [pedidoNaTela.conversaId, pedidoNaTela.pedidoId])
  const [focoBiblioteca, setFocoBiblioteca] = useState(null)
  const [abaGestao, setAbaGestao] = useState(null)
  const [pratoArrastando, setPratoArrastando] = useState(null)
  // Rodada 10 (registro 79): o cliente simulado que reage às ações da
  // Thatiane, sempre ativo (não só quando a gaveta Simulações está aberta).
  // Modo API (F01): nada de cliente simulado nem resposta automática inventada
  // em conversa de verdade. O automático real é o agente do EasyStok (S06).
  const conversasSimuladas = FONTE_API ? SEM_CONVERSAS : conversas
  useReacaoClienteSimulado({ conversas: conversasSimuladas, agora })
  // Rodada 11 (issue #8, registro 92): a resposta automática "digitando" sai
  // por aqui, com a gaveta aberta ou fechada.
  useDigitacaoAutomatica({ conversas: conversasSimuladas, agora })
  // Rodada 11 (registro 92): o roteiro do cenário mora aqui em cima, não
  // dentro da gaveta. Antes ele morria ao fechar a gaveta, e a gaveta aberta
  // cobria a conversa: a automação rodava sem ninguém ver. Agora a gaveta
  // fecha ao iniciar o cenário e o roteiro segue.
  const roteiro = useRoteiro()
  // Painel de simulações (seção 7): NÃO é modal, o Balcão continua clicável
  // ao lado, então tem estado próprio em vez de dividir o `modal` acima com
  // nota/cardápio/automáticas/entregas (esses sim se excluem entre si).
  const [simulando, setSimulando] = useState(false)
  const tamanho = useTamanhoTela()
  // Larguras redimensionáveis por alça (seção 9): um hook só, aqui em cima,
  // porque Moldura e o painel do cardápio leem a mesma chave de localStorage
  // e duas instâncias do hook se pisariam na hora de gravar.
  const { larguras, padrao: largurasPadrao, definir: aoRedimensionar, tela } = useLarguras()
  // #1806: lado (Balcão ou Resumo) e modo (compacto ou amplo) do cardápio, lembrados por
  // aparelho. A máxima deixa a conversa com 320 px à vista (`dominio/paineis.js`); amplo usa a
  // máxima. A largura arrastada continua em `larguras.cardapio`.
  const [prefCardapio, definirPrefCardapio] = usePreferenciaDePainel(CHAVE_CARDAPIO, normalizarCardapio)
  const areaDoBalcao = { tela, balcao: larguras.balcao, ficha: larguras.ficha, colunas: tamanho === DESKTOP }
  const maximaCardapio = larguraMaxima({
    ...areaDoBalcao, lado: prefCardapio.lado, empurra: true, faixa: CARDAPIO,
  })
  const larguraCardapio = larguraDoCardapio({ modo: prefCardapio.modo, guardada: larguras.cardapio, maxima: maximaCardapio })

  // Arrasto de prato (cardápio → comanda) e reordenar item da comanda dividem
  // o mesmo DndContext (seção 9, C3): os dois precisam de um ancestral comum
  // para o dnd-kit ver o alvo do drop, e PainelCardapio/BlocoPedido moram em
  // ramos separados da árvore. Distância de 6 px com mouse, toque longo de
  // 250 ms no tablet, para não brigar com a rolagem.
  const sensores = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 5 } }),
  )

  const fechar = () => setModal(null)

  const aoComecarArrasto = (evento) => {
    if (evento.active.data.current?.type === 'prato') setPratoArrastando(evento.active.data.current.item)
  }

  // Arrasto de prato (cardápio → comanda), único uso deste DndContext desde
  // que reordenar item dentro da comanda saiu (corte #64, sem pedido atrás).
  const aoTerminarArrasto = ({ active, over }) => {
    setPratoArrastando(null)
    if (!over || !selecionada) return
    if (active.data.current?.type === 'prato' && ['comanda-solta', 'comanda-resumo'].includes(over.id)) {
      adicionarItem(selecionada.id, active.data.current.item)
    }
  }

  // O antigo painel "Sugerir" (US-004/D2) saiu do fio da conversa e passou a
  // morar no balão flutuante (rodada 7, fala do dono 24/09/2026 04h12):
  // montado aqui, não em `features/assistente/`, porque feature nenhuma
  // importa outra feature (`ferramentas/verificar-camadas.mjs`); o balão só
  // recebe o resultado pronto pelo slot `sugestaoAgente`.
  const sugestaoAgente = selecionada ? (
    <PainelAgente
      key={selecionada.id}
      passouParaVoce={precisaDeVoce(selecionada, agora, true, aberta, janelas, horarioDaLoja)}
      bloqueada={envioBloqueado(selecionada, agora, canalDaConversa(canais, selecionada))}
      modo={FONTE_API ? 'api' : modoAgente}
      estado={agente.conversaId === selecionada.id ? agente.estado : 'ocioso'}
      sugestao={agente.conversaId === selecionada.id ? agente.sugestao : null}
      erro={agente.conversaId === selecionada.id ? agente.erro : null}
      esperado={selecionada.esperado ?? null}
      aoConsultar={() => consultarAgente(selecionada.id)}
      aoTrocarModo={trocarModoAgente}
      aoUsar={(texto) => { definirRascunho(selecionada.id, texto); limparAgente() }}
      aoDescartar={limparAgente}
    />
  ) : null

  // #1445: "abrir o cardápio/comanda" pedido ao assistente, só depois do clique no cartão.
  // A comanda mora na Ficha (`#comanda-pedido`); sem ela na tela (sem pedido ainda, ou Ficha
  // em gaveta fechada), abre o cardápio, que é por onde a comanda começa.
  const abrirTelaDoAssistente = (tela) => {
    const comanda = tela === 'comanda' ? document.getElementById('comanda-pedido') : null
    if (comanda) {
      comanda.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' })
      return
    }
    setModal('cardapio')
  }

  return (
    <DndContext
      sensors={sensores}
      collisionDetection={closestCenter}
      onDragStart={aoComecarArrasto}
      onDragEnd={aoTerminarArrasto}
      onDragCancel={() => setPratoArrastando(null)}
    >
      <div className={css.areaAtendimento} data-conteudo-console>
      <Moldura
        tamanho={tamanho}
        aoAbrirNota={() => {
          if (selecionada) setNota((atual) => atual ?? { id: selecionada.id, nome: selecionada.nome })
          setModal('nota')
        }}
        aoAbrirGaleria={() => setModal('galeria')}
        aoAbrirBiblioteca={(foco) => { setFocoBiblioteca(foco ?? null); setModal('respostas') }}
        aoAbrirCardapio={() => setModal('cardapio')}
        aoAbrirAutomacoes={() => setModal('automacoes')}
        aoAbrirGestao={() => setModal('gestao')}
        aoAbrirEntregas={() => setModal('entregas')}
        aoAbrirAjuda={() => setModal('ajuda')}
        simulando={simulando}
        // Modo API (F06): sem Simular. Cenário simulado em conversa de verdade some em 5 s
        // e desloca o relógio; sem o gatilho, nem o botão nem o F2 aparecem.
        aoAlternarSimulacoes={FONTE_API ? null : () => setSimulando((v) => !v)}
        larguras={{ ...larguras, cardapio: larguraCardapio }}
        largurasPadrao={largurasPadrao}
        aoRedimensionar={aoRedimensionar}
        // #1806 e #1828: o cardápio alarga a coluna do lado em que abre (Balcão ou Resumo).
        cardapioAberto={modal === 'cardapio' && selecionada ? prefCardapio.lado : null}
      />

      {nota && (
        <ModalNota
          visivel={modal === 'nota'}
          nomeCliente={nota.nome}
          outraConversa={selecionada?.id !== nota.id}
          aoFechar={() => { setNota(null); setModal((atual) => atual === 'nota' ? null : atual) }}
          aoSalvar={(texto) => salvarNota(nota.id, texto)}
        />
      )}

      {/* Rodada 7 · frente Anexos: substitui o antigo "Prato" do composer
          (registro em auditoria/decisoes/61-anexos.md). */}
      {modal === 'galeria' && selecionada && <PainelGaleria aoFechar={fechar} />}
      {modal === 'cardapio' && selecionada && (
        <PainelCardapio
          pedido={selecionada.pedido}
          largura={larguraCardapio}
          larguraMaxima={maximaCardapio}
          // #1697: no computador abre do lado do Balcão. #1806: a pessoa pode mandar para o lado
          // do Resumo. Na gaveta e no celular não há colunas fixas: o painel vai para a borda.
          ladoDoBalcao={tamanho === DESKTOP && prefCardapio.lado === 'esquerda'}
          amplo={prefCardapio.modo === 'amplo'}
          aoAlternarAmplo={() => definirPrefCardapio({ modo: prefCardapio.modo === 'amplo' ? 'compacto' : 'amplo' })}
          aoTrocarLado={tamanho === DESKTOP
            ? () => definirPrefCardapio({ lado: prefCardapio.lado === 'esquerda' ? 'direita' : 'esquerda' })
            : null}
          // Arrastar a alça volta ao compacto com a largura nova.
          aoRedimensionar={(valor) => { aoRedimensionar('cardapio', valor); definirPrefCardapio({ modo: 'compacto' }) }}
          aoFechar={fechar}
          aoEscolher={(item) => adicionarItem(selecionada.id, item)}
          // "−" ao lado do "+" no cartão do prato já na comanda (seção 3.1):
          // desfazer um toque errado sem sair do cardápio.
          aoAjustar={(sku, delta) => ajustarQuantidade(selecionada.id, sku, delta)}
        />
      )}

      {modal === 'automacoes' && (
        <ModalAutomacoes regras={regras} aoAlternar={alternarRegra} aoFechar={fechar} horariosApi={<AjustarHorariosAtendimento />} />
      )}

      {modal === 'ajuda' && <AjudaDoConsole rota={rota} fonteApi={FONTE_API} aoFechar={fechar} />}

      {modal === 'gestao' && (
        <ModalGestao
          renderizarEditorCardapio={(props) => <EditorItemCardapio {...props} />}
          horariosAtendimento={<AjustarHorariosAtendimento />}
          abaInicial={abaGestao}
          aoTrocarAba={setAbaGestao}
          janelasApi={FONTE_API ? <CadastroEntregaApi /> : null}
          avisosApi={<AvisoDaAcaoApi />}
          aoFechar={fechar}
        />
      )}

      {modal === 'respostas' && (
        <Modal
          titulo="Respostas e automáticas"
          largura="min(880px, calc(100vw - 24px))"
          aoFechar={fechar}
          rodape={<><AvisoDaAcaoApi /><Botao onClick={fechar}>Fechar</Botao></>}
        >
          <PainelDeAjuste aba="respostas" focoInicial={focoBiblioteca} />
        </Modal>
      )}

      {/* Rodada 5, seção 6 (passo zero): gaveta vazia, a F6 desenha a tela de
          verdade. `PainelEntregas.jsx` fica no lugar até a F6 apagar. */}
      {modal === 'entregas' && (FONTE_API ? <GavetaEntregasApi aoFechar={fechar} /> : <GavetaEntregas aoFechar={fechar} />)}

      {/* Rodada 5, seção 5 (passo zero): abre de qualquer lugar por
          `ui.encerrando`, hoje vazia até a F5 construir o resumo. */}
      {encerrando && (
        <ModalEncerrar conversaId={encerrando} numeroAtendimento={encerrandoNumero} aoFechar={fecharEncerramento} />
      )}

      {/* Rodada 5, seção 7 (passo zero): painel vazio, a F7 constrói os
          cenários. Só existe para o botão "Simular" ter o que abrir. */}
      {simulando && !FONTE_API && <PainelSimulacoes roteiro={roteiro} aoFechar={() => setSimulando(false)} />}

      {/* #1443: abrir a loja abre o caixa; fechar no horário pede justificativa. Só o modo API pede. */}
      {FONTE_API && <GestoLoja />}

      {/* RN-27: fila do canhoto automático no pagamento, global porque o
          pedido pago pode não ser o da ficha aberta agora. */}
      <FilaCanhotos />

      {/* US-042, D6, UC-04 E1: banner de queda + lançamento em lote ao
          voltar. Global pelo mesmo motivo de FilaCanhotos, um degrau acima. */}
      <ModalLoteDePapel />

      <BalaoAssistente sugestaoAgente={sugestaoAgente} aoAbrirTela={abrirTelaDoAssistente} areaDoBalcao={areaDoBalcao} />

      <DragOverlay>
        {pratoArrastando && <CartaoArrasto item={pratoArrastando} />}
      </DragOverlay>
      </div>
      <FaixaApi />
    </DndContext>
  )
}

// A janela de Entregas (rodada 5, seção 6), a janela de Cozinha (US-037,
// US-038, o tablet da parede) e o Cardápio por link (rodada 7, US-021) são
// abertos por `window.open` com hash própria e NÃO carregam
// `AtendimentoProvider`: as três são espelho do estado do Balcão pelo
// `infra/canalEntreJanelas.js`, nunca uma segunda árvore de estado.
//
// Issue #40 (rodada 13): antes a hash só era lida na carga da página, com
// `window.location.hash` direto aqui embaixo. `useHash` (fora de qualquer
// `if`, para não chamar hook condicionalmente) reage a `hashchange`, e
// `rotaDaHash` (domínio puro) decide a tela a cada troca, sem recarregar.

const SEM_CONVERSAS = []

// Modo demonstração parte do instante fixo da massa; modo API usa o relógio real.
const INICIO_DO_RELOGIO = FONTE_API ? Date.now() : INSTANTE_INICIAL

// #1447 (homologação de 07/10): a janela principal abre no hall de módulos. O
// Balcão (`#/m/atendimento`) segue sendo a `Composicao` de sempre (cockpit, D5);
// as telas de ajuste (as abas da antiga Gestão) abrem na moldura do módulo. As
// telas de ajuste compartilham o `AtendimentoProvider`. Hall, Cozinha e Entregas usam
// composição própria; a visita conserva a seleção e as comandas ainda locais ao voltar.
function TelaDaRota({ rota }) {
  const { sessao, agora } = useAtendimento()
  if (rota.tipo === ROTA_PRINCIPAL) return <Composicao rota={rota} />
  // Na demonstração, navegar no mesmo tab mantém a origem do espelho viva.
  // Os apelidos avulsos continuam sem outro provider, como as janelas do tablet.
  if (!FONTE_API && rota.tipo === ROTA_COZINHA) return <NoModulo rota={rota}><TelaCozinha /></NoModulo>
  if (!FONTE_API && rota.tipo === ROTA_ENTREGAS) return <NoModulo rota={rota}><TelaEntregas /></NoModulo>
  return (
    <>
      {rota.tipo === ROTA_HALL
        ? <HallDeModulos fonteApi={FONTE_API} sessao={sessao} agora={agora} ajuda={<ControleAjuda rota={rota} />} painel={<AgoraDoHall />} />
        : (
          <MolduraDoModulo moduloId={rota.modulo} telaId={rota.tela} fonteApi={FONTE_API} ajuda={<ControleAjuda rota={rota} />}>
            {/* #1440: no modo API, Entregas › Janelas de entrega é o cadastro da S45 (o mesmo de
                Entregas › Janelas e frete). Feature não importa feature; quem compõe é o App. */}
            {rota.aba && (
              <PainelDeAjuste renderizarEditorCardapio={(props) => <EditorItemCardapio {...props} />} horariosAtendimento={<AjustarHorariosAtendimento />} key={rota.aba} aba={rota.aba} janelasApi={FONTE_API ? <CadastroEntregaApi /> : null} />
            )}
          </MolduraDoModulo>
        )}
      {/* #1582: Horários e mensagens também abre e fecha a loja; sem isto o gesto só aparecia
          depois, sozinho, ao voltar para o Balcão. */}
      {FONTE_API && <GestoLoja />}
      <FaixaApi />
    </>
  )
}

// #1608: painel "Agora" da entrada. No modo API a lista de conversas chega sem o pedido
// (`traducaoConversas.js`), então Cozinhar e Despachar só aparecem na demonstração: fila que
// marcasse zero com pedido no fogo mentiria. Responder vale nos dois modos.
function AgoraDoHall() {
  const { conversas, agora, aberta, horarioDaLoja, automaticoPausado, sincronizacao } = useAtendimento()
  const { selecionar } = useAcoes()
  const { janelas } = useCatalogo()
  const { permite } = useAcessoModulos()
  const filas = filasDoAgora(conversas, agora, { automaticoPausado, aberta, janelas, expediente: horarioDaLoja })
  return (
    <PainelAgora filas={filas}
      avisoResponder={avisoDeResponder({ fonteApi: FONTE_API, estado: sincronizacao.estado, total: conversas.length })}
      podeVer={{ atendimento: permite('atendimento'), cozinha: !FONTE_API && permite('cozinha'), entregas: !FONTE_API && permite('entregas') }}
      aoAbrirConversa={(id) => { selecionar(id); window.location.hash = hashDoModulo('atendimento', 'balcao') }} />
  )
}

function RestaurarConversa({ ultimaConversa, aoLembrarConversa }) {
  const { conversas, selecionadaId, sincronizacao } = useAtendimento()
  const { selecionar } = useAcoes()
  const restaurada = useRef(false)
  useEffect(() => {
    if (!restaurada.current) {
      // Hall, Cozinha e Entregas não montam o Provider da API. Só restauramos após a lista chegar.
      if (sincronizacao.estado !== 'ok') return
      restaurada.current = true
      const anterior = ultimaConversa.current
      if (anterior && anterior !== selecionadaId && conversas.some((c) => c.id === anterior)) {
        selecionar(anterior)
        return
      }
    }
    aoLembrarConversa(selecionadaId)
  }, [conversas, selecionadaId, sincronizacao.estado, selecionar, ultimaConversa, aoLembrarConversa])
  return null
}

function AppPrincipal({ rota, sessao, ultimaConversa, aoLembrarConversa, lerComandasDaVisita, aoGuardarComandas, memoriaDoLaco }) {
  const agora = useRelogio(INICIO_DO_RELOGIO, undefined, { real: FONTE_API })
  const { permite } = useAcessoModulos()
  return (
    // `key`: trocar de usuário ou empresa recomeça o estado, sem conversa de outra empresa na tela.
    <AtendimentoProvider key={identidadeDaSessao(sessao)} agora={agora} sessao={sessao} atendimentoAtivo={permite('atendimento')} lerComandasDaVisita={lerComandasDaVisita} aoGuardarComandas={aoGuardarComandas} memoriaDoLaco={memoriaDoLaco}>
      {ultimaConversa && <RestaurarConversa ultimaConversa={ultimaConversa} aoLembrarConversa={aoLembrarConversa} />}
      <LimparAvisoAoNavegar />
      <TelaDaRota rota={rota} />
      {permite('atendimento') && <PilhaDeAvisos />}
    </AtendimentoProvider>
  )
}

// Fila da Cozinha e painel de Entregas abertos pelo hall (`#/m/cozinha`, `#/m/entregas`)
// ganham a barra do módulo para voltar; pelo apelido antigo (`#/cozinha`, a janela
// avulsa do tablet) abrem como sempre, sem barra.
function NoModulo({ rota, children }) {
  if (!rota.modulo && !FONTE_API) return children
  return <MolduraDoModulo moduloId={moduloDaRota(rota)} telaId={rota.tela} fonteApi={FONTE_API} ajuda={<ControleAjuda rota={rota} />} operacao>{children}</MolduraDoModulo>
}

function ControleAjuda({ rota }) {
  const [aberta, setAberta] = useState(false)
  return <><Botao variante="texto" icone="ajuda" data-ajuda-console aria-label="Ajuda" title="Ajuda e guia do Console" aria-haspopup="dialog" onClick={() => setAberta(true)}>Ajuda</Botao>
    {aberta && <AjudaDoConsole rota={rota} fonteApi={FONTE_API} aoFechar={() => setAberta(false)} />}</>
}

function AjustarHorariosAtendimento() {
  const [aberto, setAberto] = useState(false)
  const { acoes } = useAcessoModulos()
  if (acoes.editarAtendimento !== true) return null
  return <>
    <Botao icone="relogio" aria-haspopup="dialog" onClick={() => setAberto(true)}>Ajustar horários da semana</Botao>
    {aberto && <ModalHorariosDaSemana aoFechar={() => setAberto(false)} />}
  </>
}

// Cozinha no modo API (F05): a fila vem do KDS, não do espelho do Balcão. Sem
// sessão nesta aba, pede o login como a janela principal.
function CozinhaApi({ rota, sessao }) {
  return <NoModulo rota={rota}><TelaCozinhaApi key={identidadeDaSessao(sessao)} linhas={LINHAS_PRODUTO} /></NoModulo>
}

// Entregas no modo API (F04): janela própria lê a API, sem espelho do Balcão.
function EntregasApi({ rota, sessao }) {
  return <NoModulo rota={rota}><TelaEntregasApi key={`${identidadeDaSessao(sessao)}:${rota.tela}`} tela={rota.tela} /></NoModulo>
}

const CHAVE_DO_CONVITE = 'easystok.convite'

export function App() {
  const hash = useHash()
  const { sessao, entrarNaEmpresa, google, encerrarSessao, avisoSaida } = useSessaoApi()
  const rota = rotaDaHash(hash, { fonteApi: FONTE_API })
  // #1582: cada tela diz o próprio nome na aba. No Balcão quem escreve é a `Composicao`, com a contagem.
  useTituloDaAba(FONTE_API && !sessao ? TITULO_BASE : rota.tipo === ROTA_PRINCIPAL ? null : tituloDaAba(rota))
  // #1753: o token sai da barra de endereço assim que é lido, para não ficar no histórico do navegador.
  // Fica guardado só nesta aba até concluir: recarregar a página não pode virar "convite não vale".
  const [tokenGuardado, setTokenGuardado] = useState(() => { try { return sessionStorage.getItem(CHAVE_DO_CONVITE) } catch { return null } })
  const tokenNaBarra = FONTE_API ? tokenDoConvite(hash) : null
  useEffect(() => {
    if (!tokenNaBarra) return
    setTokenGuardado(tokenNaBarra)
    try { sessionStorage.setItem(CHAVE_DO_CONVITE, tokenNaBarra) } catch { /* sem armazenamento, vale a memória */ }
    window.history.replaceState(window.history.state, '', '#/convite')
  }, [tokenNaBarra])
  // O link do convite abre antes do login e vale mesmo com outra pessoa logada neste aparelho. Ao concluir, a sessão
  // dessa outra pessoa é encerrada: senão quem acabou de criar a senha clicava "Entrar" e caía no Console dela.
  if (FONTE_API && ehRotaDeConvite(hash)) {
    const token = tokenNaBarra ?? tokenGuardado
    const aoConcluir = () => {
      if (sessao) encerrarSessao()
      setTokenGuardado(null)
      try { sessionStorage.removeItem(CHAVE_DO_CONVITE) } catch { /* nada a limpar */ }
      window.location.hash = ''
    }
    return <TelaConvite key={token ?? 'sem-token'} token={token} aoConcluir={aoConcluir} />
  }
  if (FONTE_API && !sessao) return <TelaLogin entrarNaEmpresa={entrarNaEmpresa} google={google} avisoSaida={avisoSaida} />
  if (FONTE_API) return <><NavegacaoDoConsole hash={hash} /><AppComAcesso key={identidadeDaSessao(sessao)} rota={rota} sessao={sessao} aoSair={encerrarSessao} /></>
  if (!FONTE_API && rota.modulo) return <AppPrincipal rota={rota} sessao={sessao} />
  if (rota.tipo === ROTA_ENTREGAS) return FONTE_API ? <EntregasApi rota={rota} sessao={sessao} /> : <NoModulo rota={rota}><TelaEntregas /></NoModulo>
  if (rota.tipo === ROTA_COZINHA) return FONTE_API ? <CozinhaApi rota={rota} sessao={sessao} /> : <NoModulo rota={rota}><TelaCozinha /></NoModulo>
  if (rota.tipo === ROTA_CARDAPIO_LINK) return FONTE_API ? <AvisoCardapioDoSite /> : <TelaCardapioLink />
  return <AppPrincipal rota={rota} sessao={sessao} />
}

function AppComAcesso({ rota, sessao, aoSair }) {
  const [visitaInicial] = useState(() => lerVisita(sessao))
  const ultimaConversa = useRef(visitaInicial.ultimaConversa)
  const comandasDaVisita = useRef(visitaInicial.comandas)
  const [rascunhosDosAjustes, setRascunhosDosAjustes] = useState({})
  const gravacoesDosAjustes = useGravacoesDosAjustes()
  const [confirmandoSaida, setConfirmandoSaida] = useState(false)
  const temRascunhos = Object.keys(rascunhosDosAjustes).length > 0 || Object.keys(gravacoesDosAjustes.gravando).length > 0
  const definirRascunho = useCallback((chave, valorOuFuncao) => {
    setRascunhosDosAjustes((atuais) => {
      const valor = typeof valorOuFuncao === 'function' ? valorOuFuncao(atuais[chave] ?? null) : valorOuFuncao
      if (valor === (atuais[chave] ?? null)) return atuais
      const proximos = { ...atuais }
      if (valor === null) delete proximos[chave]
      else proximos[chave] = valor
      return proximos
    })
  }, [])
  useEffect(() => {
    if (!temRascunhos) return
    const antesDeSair = (evento) => { evento.preventDefault(); evento.returnValue = '' }
    window.addEventListener('beforeunload', antesDeSair)
    return () => window.removeEventListener('beforeunload', antesDeSair)
  }, [temRascunhos])
  const sair = () => temRascunhos ? setConfirmandoSaida(true) : aoSair()
  const confirmacaoSaida = confirmandoSaida && <Modal titulo="Sair sem salvar os ajustes?"
    descricao="Há ajustes não concluídos em Horários e mensagens. Você pode continuar trabalhando e voltar a eles. Sair descarta os rascunhos; uma gravação já enviada pode ser concluída. Confira os valores ao entrar novamente."
    aoFechar={() => setConfirmandoSaida(false)}
    rodape={<><Botao onClick={() => setConfirmandoSaida(false)}>Continuar trabalhando</Botao><Botao variante="texto" onClick={aoSair}>Sair e descartar ajustes</Botao></>} />
  // #1576: mensagens e pedidos já lidos ficam aqui enquanto a dona está no Hall, na Cozinha ou nas
  // Entregas; a `key` da sessão no AppComAcesso recria tudo quando troca o usuário ou a empresa.
  const [memoriaDoLaco] = useState(criarMemoriaDoLaco)
  const lerComandasDaVisita = useCallback(() => comandasDaVisita.current, [])
  const guardarComandas = useCallback((comandas) => {
    comandasDaVisita.current = comandas
    gravarVisita(sessao, { comandas, ultimaConversa: ultimaConversa.current })
  }, [sessao])
  const lembrarConversa = useCallback((id) => {
    ultimaConversa.current = id
    gravarVisita(sessao, { comandas: comandasDaVisita.current, ultimaConversa: id })
  }, [sessao])
  const avisos = useNotificacoesDaSessao()
  const avisosNoAparelho = useAvisosNoAparelho({ sessao })
  const agora = useRelogio(INICIO_DO_RELOGIO, undefined, { real: true })
  const { dados, erro, tentarNovamente } = useModulosDaSessao(sessao)
  if (!dados) return (
    <><main className={css.estadoAcesso} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label={erro ? 'Falha ao carregar acesso' : 'Carregando módulos'} aria-busy={!erro}>
    <Vazio titulo={erro ? 'Não foi possível carregar seu acesso' : 'Carregando seus módulos'}
      acao={<>{erro && <><Botao onClick={tentarNovamente}>Tentar novamente</Botao><ControleAjuda rota={rota} /></>}<Botao variante="texto" onClick={sair}>Sair</Botao></>}>
      {erro ?? 'Aguarde um instante.'}
    </Vazio>
    </main>{confirmacaoSaida}</>
  )
  const acesso = { permite: (id) => permiteModulo(dados, id), acoes: dados.acoes ?? {}, aoSair: sair, sessaoPersistente: sessao.persistente,
    conta: { nome: sessao.usuario?.nome ?? null, casa: sessao.empresa?.nome ?? null },
    notificacoes: <SinoDaSessao avisos={avisos} aparelho={avisosNoAparelho} /> }
  let tela
  if (!permiteRota(dados, rota)) tela = (
    <main className={css.estadoAcesso} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label="Tela sem acesso para o seu perfil">
    <Vazio titulo="Seu perfil não abre esta tela" acao={<><Botao variante="primario" onClick={() => { window.location.hash = HASH_HALL }}>Voltar ao início</Botao><Botao variante="texto" onClick={sair}>Sair</Botao></>}>
      Se você precisa trabalhar aqui, peça à dona para mudar o seu perfil.
    </Vazio>
    </main>
  )
  else if (rota.tipo === ROTA_HALL) tela = <HallDeModulos fonteApi sessao={sessao} agora={agora} ajuda={<ControleAjuda rota={rota} />} />
  else if (rota.tipo === ROTA_COZINHA) tela = <CozinhaApi rota={rota} sessao={sessao} />
  else if (rota.tipo === ROTA_ENTREGAS) tela = <EntregasApi rota={rota} sessao={sessao} />
  else if (rota.tipo === ROTA_CARDAPIO_LINK) tela = <AvisoCardapioDoSite />
  else tela = <AppPrincipal rota={rota} sessao={sessao} ultimaConversa={ultimaConversa} aoLembrarConversa={lembrarConversa} lerComandasDaVisita={lerComandasDaVisita} aoGuardarComandas={guardarComandas} memoriaDoLaco={memoriaDoLaco} />
  return <ContextoAcessoModulos.Provider value={acesso}>
    <ContextoRascunhosDosAjustes.Provider value={{ rascunhos: rascunhosDosAjustes, definir: definirRascunho, ...gravacoesDosAjustes }}>
      {tela}{confirmacaoSaida}
    </ContextoRascunhosDosAjustes.Provider>
  </ContextoAcessoModulos.Provider>
}
