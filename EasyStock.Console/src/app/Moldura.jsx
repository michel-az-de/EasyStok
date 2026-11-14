import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { AlcaLargura } from '../componentes/AlcaLargura'
import { MenuDaConta } from '../componentes/MenuDaConta'
import { Marca } from '../componentes/Marca'
import { InterruptorTema } from '../componentes/InterruptorTema'
import { trocarTema, useTema } from '../hooks/useTema'
import { balcaoComCardapio, fichaComCardapio } from '../hooks/larguras'
import { BarraDeAbas } from '../componentes/BarraDeAbas'
import { Botao } from '../componentes/Botao'
import { BotaoIcone } from '../componentes/BotaoIcone'
import { limitarPainelMovel } from '../hooks/estadoPainelMovel'
import { janelaDosPaineis, observarAreaDosPaineis } from '../hooks/areaDosPaineis'
import { Pilula } from '../componentes/Pilula'
import { Icone } from '../componentes/Icone'
import { Modal } from '../componentes/Modal'
import { MarcaIcone } from '../componentes/MarcaIcone'
import { useAcoes, useAtendimento, useCatalogo } from '../aplicacao/contextos'
import { useAcessoModulos } from '../aplicacao/acessoModulos'
import { contarAtivas } from '../dominio/automacao'
import { contarPrecisaDeVoce } from '../dominio/automatico'
import { apresentacaoDoNome, nomeDaConversa } from '../dominio/nomeDoContato'
import { CELULAR, DESKTOP, TABLET, useTamanhoTela, useTopoCompacto } from '../hooks/useTamanhoTela'
import { useAoAbrirConversa } from '../aplicacao/aberturaDeConversa'
import { PainelConversas } from '../features/caixa-de-entrada/PainelConversas'
import { PainelAtendimento } from '../features/atendimento/PainelAtendimento'
import { CompartilharComanda } from '../features/atendimento/CompartilharComanda'
import { ChamarClientes } from '../features/atendimento/ChamarClientes'
import { PainelFicha } from '../features/ficha-cliente/PainelFicha'
import { MesaDeNotas } from '../features/ficha-cliente/MesaDeNotas'
import { ResumoEntregas } from '../features/entregas/ResumoEntregas'
import { Sininho } from '../features/lembretes/Sininho'
import { MenuDeAcoes } from '../componentes/MenuDeAcoes'
import { Popover } from '../componentes/Popover'
import { FONTE_API } from '../infra/fonteDados'
import { HASH_HALL, HASH_MODULO_COZINHA } from '../dominio/rota'
import { NOME_INICIO, NOME_RESPOSTAS_AUTOMATICAS, moduloPorId } from '../dominio/modulos'
import { IconeDoModulo } from '../componentes/IconeDoModulo'
import { JANELA_COZINHA } from '../dominio/janelaAvulsa'
import { AvisoDaAcaoApi } from './FaixaApi'
import { CHAVES_DO_MENU, gruposDoMenuMais } from './menuMais'
import {
  abasDoCelular, estadoDasAutomaticas, fechamentoPedeConfirmacao, nomeDasAutomaticas,
} from './rotulosDoTopo'
import css from './moldura.module.css'
import { LimiteDeErro } from '../componentes/LimiteDeErro'

// Título de coluna sem ícone: quando todo título tem ícone, ícone nenhum marca.
// O ícone ficou para navegação, mídia e alerta.
const ICONE_DA_COLUNA = { entrada: 'inbox', ficha: 'ficha' }

// #1868: `semTitulo` no desktop de três colunas. O trilho já marca o Balcão como ativo e o cartão
// do Resumo se identifica pelo h3; o cabeçalho de 64 px custava 1 cartão e meio da lista em 1280.
// O nome da coluna continua no `aria-label` da seção.
function Coluna({ variante, titulo, contagem, semTitulo = false, children }) {
  return (
    <section className={`${css.coluna} ${css[variante]}`} aria-label={titulo}>
      {!semTitulo && <header className={css.tituloColuna}>
        {ICONE_DA_COLUNA[variante] && <Icone nome={ICONE_DA_COLUNA[variante]} tamanho={20} />}
        {titulo}
        {contagem != null && <span className={css.contagem}>{contagem}</span>}
      </header>}
      {children}
    </section>
  )
}

// Abaixo de 1024 px o topo não comporta todos os controles: fica Automáticas, que é o trabalho do
// dia, e o resto desce para este menu. Um gatilho, uma camada, nenhuma barra quebrada em três linhas.
// Rodada 5 (seção 1, passo zero): "Canais" saiu daqui (e do topo largo) — o canal vira chip dentro
// do próprio Balcão, e "Dois lugares para a mesma pergunta" era exatamente o problema que a direção
// resolveu.
// #2232 (M1 a M4): navegação primeiro (Módulos, Entregas, Cozinha, Gestão, Ajuda), tema com a opção
// ativa marcada, sem o resumo "Hoje" (o Início mostra) e, no celular, como folha inferior sem rolagem.
// Os grupos moram em `menuMais.js` (puro, provado em `prova-2232-topo.mjs`).
function MenuMais({
  refBotao, aoAbrirEntregas, aoAbrirGestao, aoAbrirAjuda, atendente = null, celular = false,
}) {
  const { permite, aoSair, sessaoPersistente, conta } = useAcessoModulos()
  const [tema] = useTema()

  const grupos = gruposDoMenuMais({ tema, permite, aoSair, sessaoPersistente, atendente, casa: conta?.casa, celular })

  function escolher(item) {
    if ([CHAVES_DO_MENU.entregas, CHAVES_DO_MENU.gestao, CHAVES_DO_MENU.ajuda].includes(item.chave)) refBotao.current?.focus()
    if (item.chave === CHAVES_DO_MENU.modulos) window.location.hash = HASH_HALL
    if (item.chave === CHAVES_DO_MENU.entregas) aoAbrirEntregas()
    if (item.chave === CHAVES_DO_MENU.cozinha) abrirCozinha()
    // Regressão da rodada 13 (achado da validação independente): "Gestão" tinha ganhado botão próprio
    // no topo compacto e a 390 px isso empurrava "Mais" para fora da tela. O caminho para Gestão no
    // celular/tablet estreito passa só por aqui.
    if (item.chave === CHAVES_DO_MENU.gestao) aoAbrirGestao()
    if (item.chave === CHAVES_DO_MENU.ajuda) aoAbrirAjuda()
    if (item.chave === CHAVES_DO_MENU.claro) trocarTema('light')
    if (item.chave === CHAVES_DO_MENU.escuro) trocarTema('dark')
    if (item.chave === CHAVES_DO_MENU.sair) aoSair?.()
  }

  // `portal` (rodada 9, achado 1 da banca estética) vem do MenuDeAcoes: `.topo` tem backdrop-filter,
  // que vira containing block de position:fixed e fazia o menu nascer fora da tela no celular.
  return <MenuDeAcoes refGatilho={refBotao} rotulo="Mais opções" tom="secundario" grupos={grupos} aoEscolherGrupo={escolher} />
}

// Mantém o atendimento aberto. O KDS da API continua usando a rota protegida do módulo.
function abrirCozinha() {
  const destino = FONTE_API ? HASH_MODULO_COZINHA : '#/cozinha'
  const janela = window.open(destino, JANELA_COZINHA, 'width=1280,height=900')
  // Issue #40 (rodada 13, achado da varredura): popup bloqueado devolve
  // `null` e nada acontece. Cai para a mesma aba: `App.jsx` agora reage à
  // troca de hash (useHash), então a Cozinha aparece de qualquer jeito.
  if (!janela) window.location.hash = destino
}

// Estado da loja (pedido do dono, 24/09/2026): um toque, sempre visível no topo. Mostra o efetivo
// (horário configurado combinado com o estado manual, dominio/funcionamento.js), não só o que ela
// tocou por último, porque "Estado visível sempre" é sobre o que vale agora.
//
// #2232 (L1): "Aberta" sozinho não dizia de quê. O texto é "Loja aberta"/"Loja fechada" em qualquer
// largura (no celular quebra em duas linhas curtas), com o ponto de estado; a cor nunca informa
// sozinha. Fechar pede confirmação em `ConteudoMoldura`; abrir não.
function ControleLoja({ aberta, aoAlternar }) {
  const nome = aberta ? 'Loja aberta' : 'Loja fechada'
  return (
    <button
      type="button"
      className={`${css.controleLoja} ${aberta ? css.topoAberta : css.topoFechada}`}
      aria-pressed={aberta}
      title={aberta ? 'Fechar a loja (pede confirmação)' : 'Abrir a loja'}
      onClick={aoAlternar}
    >
      <span className={css.lojaPonto} aria-hidden="true" />
      <span className={css.lojaTexto}>{nome}</span>
    </button>
  )
}

// Modo API (F06): sem regra vinda da API, sem número. A massa mostrava "Automáticas 6".
const MODULO_ATENDIMENTO = moduloPorId('atendimento')
const MODULO_COZINHA = moduloPorId('cozinha')

function Cabecalho({
  regras, aberta, aoAlternarLoja, aoAbrirAutomacoes, aoAbrirGestao, aoAbrirEntregas, aoAbrirAjuda, atendente = null,
}) {
  const { permite, notificacoes, busca, aoSair, sessaoPersistente, conta } = useAcessoModulos()
  const compacto = useTopoCompacto()
  const celular = useTamanhoTela() === CELULAR
  const botaoMaisRef = useRef(null)
  const estadoAuto = estadoDasAutomaticas(regras)

  return (
    <header className={css.topo}>
      {/* #1582: entre 1024 e 1179 px não há menu Mais nem trilho; a marca é o caminho ao hall,
          como no trilho do desktop. */}
      <a className={css.marca} href={HASH_HALL} aria-label={`Ir para o ${NOME_INICIO}`} title={NOME_INICIO}>
        <Marca compacta />
      </a>
      <h1 className="sr">Casa da Baba</h1>
      <ControleLoja aberta={aberta} aoAlternar={aoAlternarLoja} />
      <div className={css.controles}>
        {/* Sininho é o primeiro do grupo, à esquerda de Automáticas (seção 8
            da direção visual). A faixa de lembretes abaixo do topo saiu. */}
        <Sininho comRotulo={!compacto} />
        {busca}
        {notificacoes}
        {/* #2232 (L2): no celular a Cozinha era um ícone solto; ela passa a item com nome no Mais. */}
        {!celular && permite('cozinha') && <Botao variante="texto" onClick={abrirCozinha} title="Cozinha (abre em outra janela)">
          <IconeDoModulo modulo={MODULO_COZINHA} tamanho={20} />Cozinha
        </Botao>}
        {/* Achado 6, P3 (banca 10): no celular a regra `.controles > button` zera o font-size do
            botão (caber em 390 px); `.rotuloCurto` mostra a abreviação com font-size próprio.
            #2232 (L3): o número cedeu lugar ao estado escrito ("ligadas"/"desligadas"); a contagem
            segue no nome acessível. Sem regras (modo API), sem estado e sem número. */}
        <Botao className={css.automaticas} onClick={aoAbrirAutomacoes} aria-label={nomeDasAutomaticas(regras)}>
          <span className={css.autoTexto}>
            <IconeDoModulo modulo={MODULO_ATENDIMENTO} telaId="respostas" tamanho={20} />
            <span className={css.rotuloLongo}>{NOME_RESPOSTAS_AUTOMATICAS}</span>
            <span className={css.rotuloCurto} aria-hidden="true">Auto</span>
            {estadoAuto && <span className={css.autoEstado} aria-hidden="true">{estadoAuto}</span>}
          </span>
        </Botao>
        {/* Casca da rodada 13 (issue das 5 frentes): mesmo lugar de
            Automáticas, ao lado dela. Rodada 13, achado da validação
            independente: no topo COMPACTO este botão empurrava "Mais" para
            fora da tela a 390 px (issue de regressão). Só sobrevive aqui no
            topo largo (tablet 1024-1179 px, sem MenuMais); no compacto,
            Gestão mudou para dentro do Mais logo abaixo. */}
        {!compacto && (
          <Botao icone="painel" onClick={aoAbrirGestao} aria-label="Gestão" aria-haspopup="dialog">
            <span className={css.rotuloLongo}>Gestão</span>
            <span className={css.rotuloCurto} aria-hidden="true">Gestão</span>
          </Botao>
        )}
        {compacto ? (
          <MenuMais
            refBotao={botaoMaisRef}
            aoAbrirEntregas={aoAbrirEntregas}
            aoAbrirGestao={aoAbrirGestao}
            aoAbrirAjuda={aoAbrirAjuda}
            atendente={atendente}
            celular={celular}
          />
        ) : (
          // Rodada 2: a ocupação das janelas mora no topo, porque capacidade
          // estourada é a coisa que ela mais teme e não pode custar dois cliques.
          <>{permite('entregas') && <ResumoEntregas aoAbrir={aoAbrirEntregas} />}
            <Botao variante="texto" icone="ajuda" aria-haspopup="dialog" onClick={aoAbrirAjuda}>Ajuda</Botao>
            {/* #1242 (D1): no tablet largo o Sair mora no avatar, como no trilho do desktop. */}
            <MenuDaConta nome={atendente} detalhe={conta?.casa} aoSair={aoSair} lado="abaixo"
              rotuloSair={sessaoPersistente ? 'Sair deste aparelho' : 'Sair'}><InterruptorTema /></MenuDaConta></>
        )}
      </div>
    </header>
  )
}

// Trilho lateral do desktop (rodada 6c): a navegação de app com ícone e
// rótulo curto, no lugar da barra de botões de texto do topo. Os mesmos
// controles, os mesmos handlers; Balcão é a tela atual, não um botão.
function DisposicaoConversas({ larguras, padrao, aoRedimensionar }) {
  const [aberto, setAberto] = useState(false)
  return <span className={css.disposicao}>
    <Botao variante="texto" className={css.itemTrilho} aria-expanded={aberto} aria-haspopup="dialog" onClick={() => setAberto(!aberto)}>
      <Icone nome="painel" tamanho={24} /><span>Disposição</span>
    </Botao>
    {aberto && <Popover rotulo="Disposição das conversas" papel="dialog" portal posicao="acima" aoFechar={() => setAberto(false)}>
      <div className={css.ajusteDisposicao}>
        <strong>Seu espaço de trabalho</strong>
        <label>Conversas <output>{larguras.balcao} px</output>
          <input aria-label="Largura de Conversas" type="range" min="244" max="480" step="1" value={larguras.balcao} onChange={e => aoRedimensionar('balcao', Number(e.target.value))} />
        </label>
        <label>Ficha <output>{larguras.ficha} px</output>
          <input aria-label="Largura da ficha" type="range" min="292" max="520" step="1" value={larguras.ficha} onChange={e => aoRedimensionar('ficha', Number(e.target.value))} />
        </label>
        <Botao variante="texto" onClick={() => { aoRedimensionar('balcao', padrao.balcao); aoRedimensionar('ficha', padrao.ficha) }}>Restaurar disposição</Botao>
        <small>Guardado neste aparelho. Você também pode arrastar as divisórias.</small>
      </div>
    </Popover>}
  </span>
}

function Trilho({
  regras, aberta, aoAlternarLoja, aoAbrirAutomacoes, aoAbrirGestao, aoAbrirEntregas, simulando, aoAlternarSimulacoes,
  atendente, aoAbrirAjuda, disposicao,
}) {
  const { permite, notificacoes, busca, aoSair, sessaoPersistente, conta } = useAcessoModulos()
  return (
    <nav className={css.trilho} aria-label="Navegação">
      <a className={css.inicioTrilho} href={HASH_HALL} aria-label={`Ir para o ${NOME_INICIO}`} title={NOME_INICIO}>
        <Marca compacta /><span>{NOME_INICIO}</span>
      </a>
      <h1 className="sr">Casa da Baba</h1>
      {/* Estado da loja logo abaixo da marca: é o que vale para tudo embaixo.
          Mesmo texto e mesmo nome do controle do topo da main ("Loja aberta"
          ou "Loja fechada"), que as provas procuram pelo nome exato. */}
      <button
        type="button"
        className={`${css.itemTrilho} ${css.lojaTrilho} ${aberta ? css.lojaAberta : css.lojaFechada}`}
        aria-pressed={aberta}
        onClick={aoAlternarLoja}
        title={aberta ? 'Fechar a loja (pede confirmação)' : 'Abrir a loja'}
      >
        <Icone nome={aberta ? 'circle-check' : 'lock'} tamanho={24} />
        <span>{aberta ? 'Loja aberta' : 'Loja fechada'}</span>
      </button>
      {/* #1870: o mesmo nome e o mesmo ícone do trilho dos módulos; a tela continua sendo o Balcão. */}
      <span className={`${css.itemTrilho} ${css.itemAtual}`} aria-current="page" title="Atendimento">
        <IconeDoModulo modulo={MODULO_ATENDIMENTO} />
        <span>{MODULO_ATENDIMENTO.nome}</span>
      </span>
      {/* #2559 (Q11): Cozinha antes de Entregas, como ORDEM_DOS_MODULOS no trilho da gestão. */}
      {permite('cozinha') && <Botao variante="texto" className={css.itemTrilho} onClick={abrirCozinha} title="Cozinha (abre em outra janela)">
        <IconeDoModulo modulo={MODULO_COZINHA} tamanho={24} />
        <span>Cozinha</span>
      </Botao>}
      {permite('entregas') && <ResumoEntregas
        aoAbrir={aoAbrirEntregas}
        noTrilho classeItem={css.itemTrilho} classeSelo={css.seloTrilho}
      />}
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirAutomacoes} aria-label={nomeDasAutomaticas(regras)} title={NOME_RESPOSTAS_AUTOMATICAS}>
        <IconeDoModulo modulo={MODULO_ATENDIMENTO} telaId="respostas" tamanho={24} />
        <span>{NOME_RESPOSTAS_AUTOMATICAS}</span>
        {regras && <Pilula contagem={contarAtivas(regras)} className={css.contaTrilho} aria-hidden="true" />}
      </Botao>
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirGestao} title="Gestão" aria-haspopup="dialog">
        <Icone nome="painel" tamanho={24} />
        <span>Gestão</span>
      </Botao>
      {/* #1807: Avisos (sino) e Lembretes (relógio) com ícone e rótulo próprios, cada rótulo
          dentro do próprio botão. Antes o "Lembretes" vinha de CSS e ficava colado ao sino de
          Avisos logo abaixo, e quem clicava em Avisos abria Lembretes. */}
      <div className={css.avisosTrilho}>{busca}{notificacoes}</div>
      <div className={css.sinoTrilho}><Sininho comRotulo /></div>
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirAjuda} aria-haspopup="dialog" title="Ajuda">
        <MarcaIcone nome="informacao" tamanho={24} /><span>Ajuda</span>
      </Botao>
      {disposicao}
      <span className={css.espacoTrilho} />
      {aoAlternarSimulacoes && !simularEscondido() && (
        <Botao variante="texto" className={css.itemTrilho} aria-pressed={simulando} onClick={aoAlternarSimulacoes} title="Simular (F2)">
          <Icone nome="flask-conical" tamanho={24} />
          <span>Simular</span>
        </Botao>
      )}
      {/* #1870: o Sair saiu do rodapé fixo e mora aqui, atrás do avatar, como nos módulos. O tema
          vem junto: em 1280x720 o trilho tinha 861 px e tema e avatar ficavam abaixo da dobra. */}
      <div className={css.atendente}>
        <MenuDaConta nome={atendente} detalhe={conta?.casa} aoSair={aoSair}
          rotuloSair={sessaoPersistente ? 'Sair deste aparelho' : 'Sair'}>
          <InterruptorTema />
        </MenuDaConta>
      </div>
    </nav>
  )
}

function Gaveta({ titulo, aoFechar, children }) {
  const painel = useRef(null)
  const medir = () => limitarPainelMovel({ x: window.innerWidth - 488, y: 72 },
    { largura: 480, altura: window.innerHeight }, janelaDosPaineis())
  const [posicao, setPosicao] = useState(medir)
  useLayoutEffect(() => {
    const ajustar = () => setPosicao(antes => {
      const p = medir()
      return ['x', 'y', 'largura', 'altura'].every(c => p[c] === antes[c]) ? antes : p
    })
    ajustar()
    return observarAreaDosPaineis(ajustar, painel.current)
  }, [])
  useEffect(() => { painel.current?.querySelector('[data-fechar-ficha]')?.focus({ preventScroll: true }) }, [])
  return (
    <dialog ref={painel} open aria-modal="false" aria-label={titulo} className={css.gaveta}
      style={{ left: posicao.x, top: posicao.y, width: posicao.largura, height: posicao.altura }}
      onKeyDown={e => { if (e.key === 'Escape' && !e.defaultPrevented && e.target.closest('dialog') === e.currentTarget) {
        e.preventDefault(); e.stopPropagation(); aoFechar()
      } }}>
      <div className={css.conteudoGaveta}>{children}</div>
      <AvisoDaAcaoApi />
    </dialog>
  )
}

// Botão "Simular" (seção 7 da direção visual, passo zero): fixo no canto,
// F2 abre e fecha de qualquer lugar da tela. O corpo do painel é vazio de
// propósito (F7 constrói); aqui só o gatilho e a persistência de "esconder
// botão" (o painel ainda não tem esse controle, então só a leitura existe).
const CHAVE_SIMULAR_ESCONDIDO = 'casa-da-baba:simular-escondido'

function simularEscondido() {
  try {
    return window.localStorage.getItem(CHAVE_SIMULAR_ESCONDIDO) === '1'
  } catch {
    return false
  }
}

function BotaoSimular({ ativo, aoAlternar }) {
  if (!aoAlternar || simularEscondido()) return null
  return (
    <Botao
      variante="texto"
      icone="flask-conical"
      className={css.botaoSimular}
      aria-pressed={ativo}
      onClick={aoAlternar}
    >
      Simular
    </Botao>
  )
}

// Três arranjos para o mesmo conteúdo. Os painéis não sabem em qual estão.
export function Moldura(props) {
  const { selecionada } = useAtendimento()
  const selecionadaId = selecionada?.id ?? null
  const solicitacaoPainel = props.solicitacaoPainel ?? null
  const [aba, setAba] = useState('balcao')
  const [selecaoDaAba, setSelecaoDaAba] = useState(selecionadaId)
  const [pedidoDaAba, setPedidoDaAba] = useState(solicitacaoPainel)
  if (selecionadaId !== selecaoDaAba) {
    setSelecaoDaAba(selecionadaId)
    if (selecionadaId?.startsWith('sim-')) setAba('atendimento')
  }
  if (solicitacaoPainel !== pedidoDaAba) {
    setPedidoDaAba(solicitacaoPainel)
    if (solicitacaoPainel) setAba(solicitacaoPainel.aba)
  }
  return <MesaDeNotas visivel={props.tamanho !== CELULAR || aba !== 'balcao'}>
    <ConteudoMoldura {...props} aba={aba} setAba={setAba} />
  </MesaDeNotas>
}

function ConteudoMoldura({
  tamanho, aba, setAba, aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca, aoAbrirCardapio, aoAbrirAutomacoes, aoAbrirGestao,
  aoAbrirEntregas, aoAbrirAjuda, aoRascunho, simulando, aoAlternarSimulacoes, larguras, largurasPadrao = larguras, aoRedimensionar, cardapioAberto = null,
  slotEditorNota, editorNotaVisivel = false, notaEmRascunho = false, solicitacaoPainel = null,
}) {
  const {
    selecionada, conversas, regras: regrasDaMassa, agora, aberta, fonteApi, sessao, marcarPainelDaConversa,
    funcionamento, automaticoPausado, horarioDaLoja,
  } = useAtendimento()
  const { janelas } = useCatalogo()
  // Modo API (F06): contadores e avatar vêm da sessão e da API; da massa, nada.
  const regras = fonteApi ? null : regrasDaMassa
  const atendente = fonteApi ? (sessao?.usuario?.nome ?? null) : 'Thatiane'
  const { alternarLoja } = useAcoes()
  // #2232 (L1): fechar a loja pede confirmação; abrir não. No modo API, dentro do horário, o gesto de
  // fechar já é um diálogo com justificativa (aplicacao/api/expediente.js) e não se pergunta duas vezes.
  const [confirmandoFechar, setConfirmandoFechar] = useState(false)
  const alternarComConfirmacao = () => {
    if (fechamentoPedeConfirmacao({ aberta, fonteApi, agora, funcionamento })) setConfirmandoFechar(true)
    else alternarLoja(agora)
  }
  const confirmarFechamento = () => {
    setConfirmandoFechar(false)
    alternarLoja(agora)
  }
  const confirmacaoDeFechar = confirmandoFechar && (
    <Modal
      titulo="Fechar a loja?"
      descricao="Os clientes passam a receber a mensagem de loja fechada até você abrir de novo."
      aoFechar={() => setConfirmandoFechar(false)}
      rodape={(
        <>
          <Botao variante="texto" onClick={() => setConfirmandoFechar(false)}>Cancelar</Botao>
          <Botao variante="perigo" onClick={confirmarFechamento}>Fechar loja</Botao>
        </>
      )}
    />
  )
  // L11: a aba Conversas conta só o que pede ação, o mesmo critério de "Precisa de você" do Balcão.
  const precisamDeVoce = contarPrecisaDeVoce(conversas, agora, automaticoPausado, aberta, janelas, horarioDaLoja)
  const [pedidoPainelVisto, setPedidoPainelVisto] = useState(solicitacaoPainel)
  const [fichaSolicitada, setFichaSolicitada] = useState(null)
  const [gavetaAberta, setGavetaAberta] = useState(false)
  const botaoFichaRef = useRef(null)

  const irParaAtendimento = useCallback(() => setAba('atendimento'), [setAba])
  // #2185 (F8): sino, aviso na tela e "Não entregues" abrem a conversa por fora da lista; no celular a tela
  // da conversa é uma aba, e sem este passo a atendente seguia na lista com a conversa só marcada.
  useAoAbrirConversa(() => { if (tamanho === CELULAR) setAba('atendimento') })
  // #2185 (F24): pagamento só conta como visto com o painel da conversa na tela.
  const painelDaConversaNaTela = tamanho !== CELULAR || aba === 'atendimento'
  useEffect(() => {
    marcarPainelDaConversa?.(painelDaConversaNaTela)
    return () => marcarPainelDaConversa?.(false)
  }, [painelDaConversaNaTela, marcarPainelDaConversa])

  // Rodada 11 (issue #8, registro 92): o cenário do Simular abre a conversa
  // nova (SIMULAR_CONVERSA seleciona a `sim-…`), mas no celular a aba ficava
  // no Balcão e a dona não via o automático responder. Quando a seleção muda
  // para uma conversa simulada, a Moldura muda a aba e este conteúdo limpa a ficha anterior.
  const selecionadaId = selecionada?.id ?? null
  const acaoCompartilhar = selecionada && <CompartilharComanda key={'compartilhar-' + selecionada.id} conversa={selecionada} />
  const [selecaoVista, setSelecaoVista] = useState(selecionadaId)
  if (selecionadaId !== selecaoVista) {
    setSelecaoVista(selecionadaId)
    setFichaSolicitada(null)
  }
  if (solicitacaoPainel !== pedidoPainelVisto) {
    setPedidoPainelVisto(solicitacaoPainel)
    if (solicitacaoPainel) {
      setFichaSolicitada(solicitacaoPainel.aba === 'ficha' ? solicitacaoPainel : null)
      if (solicitacaoPainel.aba === 'ficha' && tamanho === TABLET) setGavetaAberta(true)
    }
  }
  const solicitacaoDaFicha = { solicitacaoFicha: fichaSolicitada, aoConsumirSolicitacaoFicha: () => setFichaSolicitada(null) }

  const fecharGaveta = useCallback(() => {
    setGavetaAberta(false)
    botaoFichaRef.current?.focus()
  }, [])

  // F2 abre e fecha o painel de simulações de qualquer lugar da tela (seção
  // 7): Ctrl+Shift+S é captura de tela no Edge, F2 não tem dono em navegador
  // nenhum.
  // #2185 (E29): no modo API não há Simular; o F2 só é capturado onde existe o que abrir, senão o atalho
  // do navegador (renomear, editar) ficava mudo.
  useEffect(() => {
    if (!aoAlternarSimulacoes) return undefined
    function aoTeclar(evento) {
      if (evento.key !== 'F2') return
      evento.preventDefault()
      aoAlternarSimulacoes()
    }
    window.addEventListener('keydown', aoTeclar)
    return () => window.removeEventListener('keydown', aoTeclar)
  }, [aoAlternarSimulacoes])

  // O antigo painel do agente ("Sugerir", dentro do fio) saiu daqui e passou
  // a morar no balão flutuante (rodada 7, fala do dono 24/09/2026 04h12):
  // `app/App.jsx` monta `<PainelAgente>` e `<BalaoAssistente>` junto, porque
  // feature nenhuma importa outra feature (`ferramentas/verificar-camadas.mjs`).
  // Frente C da #1889: cada coluna do Balcão dentro do seu limite de erro (LimiteDeErro).
  const atendimento = (
    <LimiteDeErro parte="Conversa">
      <PainelAtendimento
        aoAbrirNota={aoAbrirNota}
        aoAbrirGaleria={aoAbrirGaleria}
        aoAbrirBiblioteca={aoAbrirBiblioteca}
        slotEditorNota={slotEditorNota}
        editorNotaVisivel={editorNotaVisivel}
        notaEmRascunho={notaEmRascunho}
        visivel={tamanho === DESKTOP || tamanho === TABLET || aba === 'atendimento'}
        focarAoTrocar={tamanho !== DESKTOP && tamanho !== TABLET}
      />
    </LimiteDeErro>
  )
  const cabecalho = (
    <Cabecalho
      regras={regras}
      aberta={aberta}
      aoAlternarLoja={alternarComConfirmacao}
      aoAbrirAutomacoes={aoAbrirAutomacoes}
      aoAbrirGestao={aoAbrirGestao}
      aoAbrirEntregas={aoAbrirEntregas}
      aoAbrirAjuda={aoAbrirAjuda}
      atendente={atendente}
    />
  )

  if (tamanho === DESKTOP) {
    // Largura de Balcão e Ficha por variável CSS própria da moldura (seção 9):
    // só aqui elas saem do padrão de 320 px do tokens.css. Alça só existe no
    // layout de três colunas; na gaveta e no celular a largura não se aplica.
    const estiloLarguras = {
      '--largura-balcao': `${cardapioAberto === 'esquerda' ? balcaoComCardapio(larguras) : larguras.balcao}px`,
      '--largura-ficha': `${cardapioAberto === 'direita' ? fichaComCardapio(larguras) : larguras.ficha}px`,
    }
    return (
      <div className={`${css.app} ${css.appTrilho}`} style={estiloLarguras}>
        <Trilho
          regras={regras}
          aberta={aberta}
          aoAlternarLoja={alternarComConfirmacao}
          aoAbrirAutomacoes={aoAbrirAutomacoes}
          aoAbrirGestao={aoAbrirGestao}
          aoAbrirEntregas={aoAbrirEntregas}
          simulando={simulando}
          aoAlternarSimulacoes={aoAlternarSimulacoes}
          atendente={atendente}
          aoAbrirAjuda={aoAbrirAjuda}
          disposicao={<DisposicaoConversas larguras={larguras} padrao={largurasPadrao} aoRedimensionar={aoRedimensionar} />}
        />
        <main className={`${css.palco} ${css.palcoTres}`} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label="Atendimento">
          {/* Sem contagem ao lado do título (seção 1, corte #11): a aba
              "Precisa de você"/"Todas" já conta, duas vezes é ruído. */}
          <Coluna variante="entrada" titulo="Conversas" semTitulo>
            <LimiteDeErro parte="Lista de conversas"><PainelConversas buscaDeClientes={ChamarClientes} /></LimiteDeErro>
          </Coluna>
          <AlcaLargura
            rotulo="Redimensionar Conversas"
            valor={larguras.balcao}
            min={244}
            max={480}
            padrao={largurasPadrao.balcao}
            aoMudar={(valor) => aoRedimensionar('balcao', valor)}
          />
          <section className={`${css.coluna} ${css.centro}`} aria-label="Conversa">{atendimento}</section>
          <AlcaLargura
            rotulo="Redimensionar ficha"
            valor={larguras.ficha}
            min={292}
            max={520}
            padrao={largurasPadrao.ficha}
            invertida
            aoMudar={(valor) => aoRedimensionar('ficha', valor)}
          />
          <Coluna variante="ficha" titulo="Ficha" semTitulo>
            <div className={css.painelFicha}><LimiteDeErro parte="Resumo"><PainelFicha aoAbrirCardapio={aoAbrirCardapio} aoAbrirNota={aoAbrirNota} aoRascunho={aoRascunho} acaoCompartilhar={acaoCompartilhar} {...solicitacaoDaFicha} /></LimiteDeErro></div>
            {/* #1474: lugar da fila de canhotos (FilaCanhotos), no pé da Ficha e no fluxo da
                coluna: a pílula não cobre mais o título nem o topo da coluna. Vazio, não ocupa. */}
            <div className={css.slotFila} data-fila-canhotos="" />
          </Coluna>
        </main>
        {confirmacaoDeFechar}
      </div>
    )
  }

  if (tamanho === TABLET) {
    return (
      <div className={css.app}>
        {cabecalho}
        <main className={css.palco} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label="Atendimento">
          <Coluna variante="entrada" titulo="Conversas" semTitulo>
            <LimiteDeErro parte="Lista de conversas"><PainelConversas buscaDeClientes={ChamarClientes} /></LimiteDeErro>
          </Coluna>
          <section className={`${css.coluna} ${css.centro}`} aria-label="Conversa">{atendimento}</section>
          {gavetaAberta && (
            <Gaveta titulo="Ficha" aoFechar={fecharGaveta}>
              <LimiteDeErro parte="Resumo"><PainelFicha aoAbrirCardapio={() => { fecharGaveta(); aoAbrirCardapio() }} aoAbrirNota={aoAbrirNota} aoRascunho={aoRascunho} acaoCompartilhar={acaoCompartilhar}
                {...solicitacaoDaFicha} acoesCabecalho={<BotaoIcone icone="fechar" rotulo="Fechar Ficha" data-fechar-ficha onClick={fecharGaveta} />} /></LimiteDeErro>
            </Gaveta>
          )}
        </main>
        <div className={css.slotFila} data-fila-canhotos="" />
        <footer className={css.rodape}>
          <Botao ref={botaoFichaRef} onClick={() => setGavetaAberta(true)}>
            Ficha de {selecionada ? apresentacaoDoNome(nomeDaConversa(selecionada), { comanda: selecionada.numeroComanda }).rotulo : 'cliente'}
          </Botao>
        </footer>
        <BotaoSimular ativo={simulando} aoAlternar={aoAlternarSimulacoes} />
        {confirmacaoDeFechar}
      </div>
    )
  }

  const painelDaAba = {
    balcao: <LimiteDeErro parte="Lista de conversas"><PainelConversas aoAbrir={irParaAtendimento} buscaDeClientes={ChamarClientes} acoesCabecalho={<span data-ancora-assistente />} /></LimiteDeErro>,
    atendimento,
    ficha: <LimiteDeErro parte="Resumo"><PainelFicha aoAbrirCardapio={aoAbrirCardapio} aoAbrirNota={aoAbrirNota} aoRascunho={aoRascunho} acaoCompartilhar={acaoCompartilhar} acoesCabecalho={<span data-ancora-assistente />}
      visivel={aba === 'ficha'} comandaVisivel={aba === 'ficha' || aba === 'atendimento'} {...solicitacaoDaFicha} /></LimiteDeErro>,
  }

  return (
    <div className={css.app}>
      {aba === 'balcao' && cabecalho}
      {/* Sem rolagem nesta seção: cada painel tem a área rolável dele por
          dentro, e é isso que mantém o composer preso no pé do celular. */}
      <main className={css.palco} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label="Atendimento">
        <section
          className={`${css.coluna} ${css.centro}`}
          role="tabpanel"
          id="painel-da-aba"
          aria-labelledby={`aba-${aba}`}
          tabIndex={0}
        >
          {Object.entries(painelDaAba).map(([id, painel]) => (
            <div key={id} hidden={aba !== id} data-painel-mobile={id}
              style={aba === id ? { display: 'contents' } : undefined}>{painel}</div>
          ))}
        </section>
      </main>
      <div className={css.slotFila} data-fila-canhotos="" />
      <BarraDeAbas
        ativa={aba}
        aoTrocar={setAba}
        abas={abasDoCelular(precisamDeVoce)}
      />
      <BotaoSimular ativo={simulando} aoAlternar={aoAlternarSimulacoes} />
      {confirmacaoDeFechar}
    </div>
  )
}
