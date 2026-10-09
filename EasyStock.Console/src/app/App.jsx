import { useState } from 'react'
import {
  closestCenter, DndContext, DragOverlay, PointerSensor, TouchSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import { AtendimentoProvider } from '../aplicacao/AtendimentoProvider'
import { useAcoes, useAtendimento, useCatalogo } from '../aplicacao/contextos'
import { useReacaoClienteSimulado } from '../aplicacao/useReacaoClienteSimulado'
import { useDigitacaoAutomatica } from '../aplicacao/useDigitacaoAutomatica'
import { useHash } from '../hooks/useHash'
import { useLarguras } from '../hooks/useLarguras'
import { useRelogio } from '../hooks/useRelogio'
import { DESKTOP, useTamanhoTela } from '../hooks/useTamanhoTela'
import { INSTANTE_INICIAL, LINHAS_PRODUTO } from '../infra/catalogo'
import { FONTE_API } from '../infra/fonteDados'
import { useSessaoApi } from '../aplicacao/useSessaoApi'
import { ContextoAcessoModulos, useAcessoModulos, useModulosDaSessao } from '../aplicacao/acessoModulos'
import { moduloDaRota, permiteModulo, permiteRota } from '../dominio/acessoModulos'
import { Vazio } from '../componentes/Vazio'
import { Botao } from '../componentes/Botao'
import { TelaLogin } from '../features/login/TelaLogin'
import { FaixaApi, LimparAvisoAoNavegar } from './FaixaApi'
import { contarPrecisaDeVoce, precisaDeVoce } from '../dominio/automatico'
import { useTituloDaAba } from '../aplicacao/useTituloDaAba'
import { canalDaConversa } from '../dominio/canal'
import { envioBloqueado } from '../dominio/janela'
import {
  HASH_HALL, ROTA_CARDAPIO_LINK, ROTA_COZINHA, ROTA_ENTREGAS, ROTA_HALL, ROTA_PRINCIPAL, rotaDaHash,
} from '../dominio/rota'
import { hashDoModulo } from '../dominio/modulos'
import { CartaoArrasto, PainelCardapio } from '../features/cardapio/PainelCardapio'
import { ModalNota } from '../features/notas/ModalNota'
import { PainelGaleria } from '../features/anexos/PainelGaleria'
import { ModalAutomacoes } from '../features/automacoes/ModalAutomacoes'
import { PainelDeAjuste } from '../features/gestao/ModalGestao'
import { HallDeModulos } from '../features/hall/HallDeModulos'
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

// Só o topo conhece todas as features. Feature nenhuma importa outra feature.
function Composicao({ aoSair }) {
  const {
    selecionada, regras, encerrando, encerrandoNumero, agente, modoAgente, agora, aberta,
    conversas, automaticoPausado,
  } = useAtendimento()
  const {
    salvarNota, adicionarItem, ajustarQuantidade, alternarRegra, fecharEncerramento,
    consultarAgente, limparAgente, trocarModoAgente, definirRascunho,
  } = useAcoes()
  const { canais, janelas } = useCatalogo()

  // Item 8 (rodada 10): título da aba com a contagem de "Precisa de você",
  // para quem trocou de janela no mesmo computador (US-010, saber sem olhar).
  useTituloDaAba(contarPrecisaDeVoce(conversas, agora, automaticoPausado, aberta, janelas))
  const [modal, setModal] = useState(null)
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
  const { larguras, definir: aoRedimensionar } = useLarguras()

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
    if (active.data.current?.type === 'prato' && over.id === 'comanda-solta') {
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
      passouParaVoce={precisaDeVoce(selecionada, agora, true, aberta, janelas)}
      bloqueada={envioBloqueado(selecionada, agora, canalDaConversa(canais, selecionada))}
      modo={modoAgente}
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
      comanda.scrollIntoView({ behavior: 'smooth', block: 'start' })
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
      <Moldura
        tamanho={tamanho}
        aoAbrirNota={() => setModal('nota')}
        aoAbrirGaleria={() => setModal('galeria')}
        // #1441: "Gerenciar respostas" do seletor e a etiqueta "automática" do balão abrem
        // a tela Respostas e automáticas do módulo Atendimento.
        aoAbrirBiblioteca={() => { window.location.hash = hashDoModulo('atendimento', 'respostas') }}
        aoAbrirCardapio={() => setModal('cardapio')}
        aoAbrirAutomacoes={() => setModal('automacoes')}
        // #1447: o antigo modal Gestão virou o hall de módulos; o botão leva para lá.
        aoAbrirGestao={() => { window.location.hash = HASH_HALL }}
        aoAbrirEntregas={() => setModal('entregas')}
        simulando={simulando}
        // Modo API (F06): sem Simular. Cenário simulado em conversa de verdade some em 5 s
        // e desloca o relógio; sem o gatilho, nem o botão nem o F2 aparecem.
        aoAlternarSimulacoes={FONTE_API ? null : () => setSimulando((v) => !v)}
        larguras={larguras}
        aoRedimensionar={aoRedimensionar}
      />

      {modal === 'nota' && selecionada && (
        <ModalNota
          nomeCliente={selecionada.nome}
          aoFechar={fechar}
          aoSalvar={(texto) => { salvarNota(selecionada.id, texto); fechar() }}
        />
      )}

      {/* Rodada 7 · frente Anexos: substitui o antigo "Prato" do composer
          (registro em auditoria/decisoes/61-anexos.md). */}
      {modal === 'galeria' && selecionada && <PainelGaleria aoFechar={fechar} />}
      {modal === 'cardapio' && selecionada && (
        <PainelCardapio
          pedido={selecionada.pedido}
          largura={larguras.cardapio}
          // "right: var(--largura-ficha)" da seção 9: só faz sentido encostado
          // na coluna Ficha quando ela existe (layout de três colunas). Na
          // gaveta e no celular a Ficha não fica fixa na tela, então o painel
          // vai para a borda mesmo.
          deslocamentoDireita={tamanho === DESKTOP ? larguras.ficha : 0}
          aoRedimensionar={(valor) => aoRedimensionar('cardapio', valor)}
          aoFechar={fechar}
          aoEscolher={(item) => adicionarItem(selecionada.id, item)}
          // "−" ao lado do "+" no cartão do prato já na comanda (seção 3.1):
          // desfazer um toque errado sem sair do cardápio.
          aoAjustar={(sku, delta) => ajustarQuantidade(selecionada.id, sku, delta)}
        />
      )}

      {modal === 'automacoes' && (
        <ModalAutomacoes regras={regras} aoAlternar={alternarRegra} aoFechar={fechar} />
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

      <BalaoAssistente sugestaoAgente={sugestaoAgente} aoAbrirTela={abrirTelaDoAssistente} />

      <FaixaApi aoSair={aoSair} />

      <DragOverlay>
        {pratoArrastando && <CartaoArrasto item={pratoArrastando} />}
      </DragOverlay>
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
// três dividem o mesmo `AtendimentoProvider`, então ir e voltar não recarrega nada.
function TelaDaRota({ rota, aoSair }) {
  const { sessao, agora } = useAtendimento()
  if (rota.tipo === ROTA_PRINCIPAL) return <Composicao aoSair={aoSair} />
  // Na demonstração, navegar no mesmo tab mantém a origem do espelho viva.
  // Os apelidos avulsos continuam sem outro provider, como as janelas do tablet.
  if (!FONTE_API && rota.tipo === ROTA_COZINHA) return <NoModulo rota={rota}><TelaCozinha /></NoModulo>
  if (!FONTE_API && rota.tipo === ROTA_ENTREGAS) return <NoModulo rota={rota}><TelaEntregas /></NoModulo>
  return (
    <>
      {rota.tipo === ROTA_HALL
        ? <HallDeModulos fonteApi={FONTE_API} sessao={sessao} agora={agora} />
        : (
          <MolduraDoModulo moduloId={rota.modulo} telaId={rota.tela} fonteApi={FONTE_API}>
            {/* #1440: no modo API, Entregas › Janelas de entrega é o cadastro da S45 (o mesmo de
                Entregas › Janelas e frete). Feature não importa feature; quem compõe é o App. */}
            {rota.aba && (
              <PainelDeAjuste key={rota.aba} aba={rota.aba} janelasApi={FONTE_API ? <CadastroEntregaApi /> : null} />
            )}
          </MolduraDoModulo>
        )}
      <FaixaApi aoSair={aoSair} />
    </>
  )
}

function AppPrincipal({ rota, sessao, aoSair }) {
  const agora = useRelogio(INICIO_DO_RELOGIO, undefined, { real: FONTE_API })
  const { permite } = useAcessoModulos()
  return (
    // `key`: trocar de usuário ou empresa recomeça o estado, sem conversa de outra empresa na tela.
    <AtendimentoProvider key={sessao?.token ?? 'demo'} agora={agora} sessao={sessao} atendimentoAtivo={permite('atendimento')}>
      <LimparAvisoAoNavegar />
      <TelaDaRota rota={rota} aoSair={aoSair} />
    </AtendimentoProvider>
  )
}

// Fila da Cozinha e painel de Entregas abertos pelo hall (`#/m/cozinha`, `#/m/entregas`)
// ganham a barra do módulo para voltar; pelo apelido antigo (`#/cozinha`, a janela
// avulsa do tablet) abrem como sempre, sem barra.
function NoModulo({ rota, children }) {
  if (!rota.modulo && !FONTE_API) return children
  return <MolduraDoModulo moduloId={moduloDaRota(rota)} telaId={rota.tela} fonteApi={FONTE_API} operacao>{children}</MolduraDoModulo>
}

// Cozinha no modo API (F05): a fila vem do KDS, não do espelho do Balcão. Sem
// sessão nesta aba, pede o login como a janela principal.
function CozinhaApi({ rota, sessao }) {
  return <NoModulo rota={rota}><TelaCozinhaApi key={sessao.token} linhas={LINHAS_PRODUTO} /></NoModulo>
}

// Entregas no modo API (F04): janela própria lê a API, sem espelho do Balcão.
function EntregasApi({ rota, sessao }) {
  return <NoModulo rota={rota}><TelaEntregasApi key={sessao.token} /></NoModulo>
}

export function App() {
  const hash = useHash()
  const { sessao, entrarNaEmpresa, google, encerrarSessao } = useSessaoApi()
  const rota = rotaDaHash(hash, { fonteApi: FONTE_API })
  if (FONTE_API && !sessao) return <TelaLogin entrarNaEmpresa={entrarNaEmpresa} google={google} />
  if (FONTE_API) return <AppComAcesso key={sessao.token} rota={rota} sessao={sessao} aoSair={encerrarSessao} />
  if (!FONTE_API && rota.modulo) return <AppPrincipal rota={rota} sessao={sessao} aoSair={encerrarSessao} />
  if (rota.tipo === ROTA_ENTREGAS) return FONTE_API ? <EntregasApi rota={rota} sessao={sessao} /> : <NoModulo rota={rota}><TelaEntregas /></NoModulo>
  if (rota.tipo === ROTA_COZINHA) return FONTE_API ? <CozinhaApi rota={rota} sessao={sessao} /> : <NoModulo rota={rota}><TelaCozinha /></NoModulo>
  if (rota.tipo === ROTA_CARDAPIO_LINK) return FONTE_API ? <AvisoCardapioDoSite /> : <TelaCardapioLink />
  return <AppPrincipal rota={rota} sessao={sessao} aoSair={encerrarSessao} />
}

function AppComAcesso({ rota, sessao, aoSair }) {
  const agora = useRelogio(INICIO_DO_RELOGIO, undefined, { real: true })
  const { dados, erro, tentarNovamente } = useModulosDaSessao(sessao)
  if (!dados) return (
    <Vazio titulo={erro ? 'Não foi possível carregar seu acesso' : 'Carregando seus módulos'}
      acao={<>{erro && <Botao onClick={tentarNovamente}>Tentar novamente</Botao>}<Botao variante="texto" onClick={aoSair}>Sair</Botao></>}>
      {erro ?? 'Aguarde um instante.'}
    </Vazio>
  )
  const acesso = { permite: (id) => permiteModulo(dados, id), acoes: dados.acoes ?? {}, aoSair }
  let tela
  if (!permiteRota(dados, rota)) tela = (
    <Vazio titulo="Seu perfil não tem acesso a este módulo" acao={<><a href={HASH_HALL}>Voltar aos módulos</a><Botao variante="texto" onClick={aoSair}>Sair</Botao></>}>
      Peça à dona para revisar seu acesso, se precisar trabalhar aqui.
    </Vazio>
  )
  else if (rota.tipo === ROTA_HALL) tela = <HallDeModulos fonteApi sessao={sessao} agora={agora} />
  else if (rota.tipo === ROTA_COZINHA) tela = <CozinhaApi rota={rota} sessao={sessao} />
  else if (rota.tipo === ROTA_ENTREGAS) tela = <EntregasApi rota={rota} sessao={sessao} />
  else if (rota.tipo === ROTA_CARDAPIO_LINK) tela = <AvisoCardapioDoSite />
  else tela = <AppPrincipal rota={rota} sessao={sessao} aoSair={aoSair} />
  return <ContextoAcessoModulos.Provider value={acesso}>{tela}</ContextoAcessoModulos.Provider>
}
