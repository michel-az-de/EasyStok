import { useCallback, useEffect, useRef, useState } from 'react'
import { AlcaLargura } from '../componentes/AlcaLargura'
import { Avatar } from '../componentes/Avatar'
import { Marca } from '../componentes/Marca'
import { BarraDeAbas } from '../componentes/BarraDeAbas'
import { Botao } from '../componentes/Botao'
import { Icone } from '../componentes/Icone'
import { Pilula } from '../componentes/Pilula'
import { useAcoes, useAtendimento } from '../aplicacao/contextos'
import { useAcessoModulos } from '../aplicacao/acessoModulos'
import { contarAbertas, contarNaEsteira } from '../dominio/conversa'
import { contarAtivas } from '../dominio/automacao'
import { useEscape } from '../hooks/useEscape'
import { DESKTOP, TABLET, useTopoCompacto } from '../hooks/useTamanhoTela'
import { PainelConversas } from '../features/caixa-de-entrada/PainelConversas'
import { PainelAtendimento } from '../features/atendimento/PainelAtendimento'
import { PainelFicha } from '../features/ficha-cliente/PainelFicha'
import { ResumoEntregas } from '../features/entregas/ResumoEntregas'
import { Sininho } from '../features/lembretes/Sininho'
import { Popover } from '../componentes/Popover'
import { FONTE_API } from '../infra/fonteDados'
import { HASH_MODULO_COZINHA, HASH_MODULO_ENTREGAS } from '../dominio/rota'
import css from './moldura.module.css'

// Título de coluna sem ícone: quando todo título tem ícone, ícone nenhum marca.
// O ícone ficou para navegação, mídia e alerta.
const ICONE_DA_COLUNA = { entrada: 'inbox', ficha: 'ficha' }

function Coluna({ variante, titulo, contagem, children }) {
  return (
    <section className={`${css.coluna} ${css[variante]}`} aria-label={titulo}>
      <header className={css.tituloColuna}>
        {ICONE_DA_COLUNA[variante] && <Icone nome={ICONE_DA_COLUNA[variante]} tamanho={20} />}
        {titulo}
        {contagem != null && <span className={css.contagem}>{contagem}</span>}
      </header>
      {children}
    </section>
  )
}

// "Em andamento" e "na esteira" não pedem ação nenhuma sozinhos: viraram texto
// dentro deste popover, em vez de competir com "Precisa de você" no topo.
function grupoDoDia(emAndamento, naEsteira) {
  return {
    titulo: 'Hoje',
    itens: [{
      chave: '__hoje',
      titulo: `${emAndamento} em andamento`,
      detalhe: `${naEsteira} na esteira`,
    }],
  }
}

// Abaixo de 1024 px o topo não comporta quatro controles: fica Automáticas,
// que é o trabalho do dia, e o resto desce para este menu. Um gatilho, uma
// camada, nenhuma barra quebrada em três linhas. Rodada 5 (seção 1, passo
// zero): "Canais" saiu daqui (e do topo largo) — o canal vira chip dentro do
// próprio Balcão, e "Dois lugares para a mesma pergunta" era exatamente o
// problema que a direção resolveu.
function MenuMais({
  refBotao, aoAbrirEntregas, aoAbrirGestao, emAndamento, naEsteira,
}) {
  const { permite } = useAcessoModulos()
  const [aberto, setAberto] = useState(false)
  const fechar = useCallback(() => setAberto(false), [])

  const grupos = [
    {
      titulo: 'Tema',
      itens: [
        { chave: '__claro', titulo: 'Claro', detalhe: 'Fundo claro' },
        { chave: '__escuro', titulo: 'Escuro', detalhe: 'Fundo escuro' },
      ],
    },
    {
      titulo: 'Ver',
      itens: [
        {
          chave: '__entregas',
          titulo: 'Entregas',
          detalhe: 'Ocupação de cada janela e quem ainda vai receber',
        },
        // Regressão da rodada 13 (achado da validação independente): "Gestão"
        // tinha ganhado botão próprio no topo compacto, ao lado de
        // Automáticas, e a 390 px isso empurrava "Mais" para fora da tela.
        // O botão saiu do topo; o caminho para Gestão no celular/tablet
        // estreito passa a ser só por aqui.
        // #1447: a Gestão virou o hall de módulos; o item leva para lá.
        { chave: '__gestao', titulo: 'Módulos', detalhe: 'Cozinha, entregas, financeiro e os outros módulos' },
      ].filter((item) => item.chave !== '__entregas' || permite('entregas')),
    },
    grupoDoDia(emAndamento, naEsteira),
  ]

  function escolher(item) {
    fechar()
    if (item.chave === '__entregas') aoAbrirEntregas()
    if (item.chave === '__gestao') aoAbrirGestao()
    if (item.chave === '__claro') trocarTema('light')
    if (item.chave === '__escuro') trocarTema('dark')
  }

  return (
    <span className={css.comPopover}>
      <Botao
        ref={refBotao}
        aria-haspopup="menu"
        aria-expanded={aberto}
        onClick={() => setAberto((v) => !v)}
        aria-label="Mais"
        title="Mais"
      >
        <Icone nome="ellipsis" tamanho={20} />
      </Botao>
      {/* `portal` (rodada 9, achado 1 da banca estética): `.topo` tem
          backdrop-filter, que vira containing block de position:fixed. Sem
          isso, o `.abaixo` do celular (fixed, ancorado no rodapé da tela)
          media a partir de `.topo`, não do viewport, e Entregas nasce fora da
          tela. */}
      {aberto && (
        <Popover
          rotulo="Mais controles do topo"
          posicao="abaixo"
          grupos={grupos}
          aoEscolher={escolher}
          aoFechar={fechar}
          portal
        />
      )}
    </span>
  )
}

const CHAVE_TEMA = 'casa-da-baba:tema'

const OPCOES_TEMA = [
  { valor: 'light', rotulo: 'Claro', icone: 'sun' },
  { valor: 'dark', rotulo: 'Escuro', icone: 'moon' },
]

function lerTemaSalvo() {
  try {
    const salvo = window.localStorage.getItem(CHAVE_TEMA)
    if (salvo === 'light' || salvo === 'dark') return salvo
  } catch {
    // sem storage, a tela segue o sistema
  }
  return 'sistema'
}

function aplicarTema(valor) {
  if (valor === 'light' || valor === 'dark') {
    document.documentElement.setAttribute('data-theme', valor)
  } else {
    document.documentElement.removeAttribute('data-theme')
  }
}

// Troca de tema fora do interruptor (menu Mais do topo compacto): mesma
// dissolvência e mesma gravação do interruptor.
function trocarTema(valor) {
  const trocar = () => aplicarTema(valor)
  if (document.startViewTransition) document.startViewTransition(trocar)
  else trocar()
  gravarTema(valor)
}

function gravarTema(valor) {
  try {
    window.localStorage.setItem(CHAVE_TEMA, valor)
  } catch {
    // sem persistência, a escolha vale só para esta sessão
  }
}

// Dois estados, Claro e Escuro (corte #2: "Seguir o sistema" era um terceiro
// botão de tema sem pedido nenhum atrás). Rótulo escrito nos dois, nunca
// ícone sozinho. A pintura inicial já sai certa do script inline no
// index.html; aqui só refletimos o que ele decidiu.
function InterruptorTema({ classe }) {
  const [tema, setTema] = useState(lerTemaSalvo)

  // A troca vira uma dissolvência única da tela (View Transitions), em vez de
  // cada componente mudar de cor no seu ritmo. Sem suporte, troca direto.
  function escolher(valor) {
    const trocar = () => {
      setTema(valor)
      aplicarTema(valor)
    }
    if (document.startViewTransition && valor !== tema) document.startViewTransition(trocar)
    else trocar()
    gravarTema(valor)
  }

  return (
    <div className={classe ?? css.tema} role="radiogroup" aria-label="Tema">
      {OPCOES_TEMA.map((opcao) => (
        <Botao
          key={opcao.valor}
          role="radio"
          aria-checked={tema === opcao.valor}
          className={tema === opcao.valor ? css.temaAtiva : ''}
          onClick={() => escolher(opcao.valor)}
          title={opcao.rotulo}
        >
          <Icone nome={opcao.icone} tamanho={18} />
          <span className={css.rotuloTema}>{opcao.rotulo}</span>
        </Botao>
      ))}
    </div>
  )
}

// Abre a Cozinha (US-037, US-038) numa janela própria, o tablet da parede
// (D6). Botão discreto e fixo, fora do `compacto ? MenuMais : ResumoEntregas`
// que já existe: assim ela sempre acha o mesmo controle, larga ou estreita a
// tela, sem duplicar o item em dois menus.
//
// #1474: no modo API a Cozinha lê o KDS sozinha (não depende desta janela), e a janela
// avulsa em `#/cozinha` não tinha como voltar. Abre a tela do módulo, com "← Módulos".
function abrirCozinha() {
  if (FONTE_API) {
    window.location.hash = HASH_MODULO_COZINHA
    return
  }
  const janela = window.open('#/cozinha', 'cdb-cozinha', 'width=1280,height=900')
  // Issue #40 (rodada 13, achado da varredura): popup bloqueado devolve
  // `null` e nada acontece. Cai para a mesma aba: `App.jsx` agora reage à
  // troca de hash (useHash), então a Cozinha aparece de qualquer jeito.
  if (!janela) window.location.hash = '#/cozinha'
}

// Estado da loja (pedido do dono, 24/09/2026): um toque, Aberta ou Fechada,
// sempre visível no topo. Mostra o efetivo (horário configurado combinado
// com o estado manual, dominio/funcionamento.js), não só o que ela tocou por
// último, porque "Estado visível sempre" é sobre o que vale agora.
function ControleLoja({ aberta, aoAlternar }) {
  return (
    <button type="button" className={css.controleLoja} aria-pressed={aberta} onClick={aoAlternar}>
      <Pilula tom={aberta ? 'ok' : 'perigo'} icone={aberta ? 'circle-check' : 'lock'}>
        {aberta ? 'Loja aberta' : 'Loja fechada'}
      </Pilula>
    </button>
  )
}

// Modo API (F06): sem regra vinda da API, sem número. A massa mostrava "Automáticas 6".
const rotuloAutomaticas = (regras) => (regras ? `Automáticas, ${contarAtivas(regras)} ligadas` : 'Automáticas')

function Cabecalho({
  conversas, regras, aberta, aoAlternarLoja, aoAbrirAutomacoes, aoAbrirGestao, aoAbrirEntregas,
}) {
  const { permite } = useAcessoModulos()
  const compacto = useTopoCompacto()
  const botaoMaisRef = useRef(null)
  const emAndamento = contarAbertas(conversas)
  const naEsteira = contarNaEsteira(conversas)

  return (
    <header className={css.topo}>
      <h1 className={css.marca}>
        <Marca compacta />
      </h1>
      {!compacto && <InterruptorTema />}
      <ControleLoja aberta={aberta} aoAlternar={aoAlternarLoja} />
      <div className={css.controles}>
        {/* Sininho é o primeiro do grupo, à esquerda de Automáticas (seção 8
            da direção visual). A faixa de lembretes abaixo do topo saiu. */}
        <Sininho />
        {permite('cozinha') && <Botao variante="texto" icone="cooking-pot" onClick={abrirCozinha}>
          Cozinha
        </Botao>}
        {/* Achado 6, P3 (banca 10): no celular a regra `.controles > button`
            zera o font-size do texto (caber em 390 px) e só sobrava
            ícone+número, sem palavra nenhuma. `.rotuloCurto` mostra uma
            abreviação com font-size próprio (não herda o zero do botão). */}
        <Botao icone="raio" onClick={aoAbrirAutomacoes} aria-label={rotuloAutomaticas(regras)}>
          <span className={css.rotuloLongo}>Automáticas</span>
          <span className={css.rotuloCurto} aria-hidden="true">Auto</span>
          {regras && <span className={css.conta}>{contarAtivas(regras)}</span>}
        </Botao>
        {/* Casca da rodada 13 (issue das 5 frentes): mesmo lugar de
            Automáticas, ao lado dela. Rodada 13, achado da validação
            independente: no topo COMPACTO este botão empurrava "Mais" para
            fora da tela a 390 px (issue de regressão). Só sobrevive aqui no
            topo largo (tablet 1024-1179 px, sem MenuMais); no compacto,
            Gestão mudou para dentro do Mais logo abaixo. */}
        {!compacto && (
          <Botao icone="painel" onClick={aoAbrirGestao} aria-label="Módulos">
            <span className={css.rotuloLongo}>Módulos</span>
            <span className={css.rotuloCurto} aria-hidden="true">Módulos</span>
          </Botao>
        )}
        {compacto ? (
          <MenuMais
            refBotao={botaoMaisRef}
            aoAbrirEntregas={aoAbrirEntregas}
            aoAbrirGestao={aoAbrirGestao}
            emAndamento={emAndamento}
            naEsteira={naEsteira}
          />
        ) : (
          // Rodada 2: a ocupação das janelas mora no topo, porque capacidade
          // estourada é a coisa que ela mais teme e não pode custar dois cliques.
          permite('entregas') && <ResumoEntregas aoAbrir={aoAbrirEntregas} />
        )}
      </div>
    </header>
  )
}

// Trilho lateral do desktop (rodada 6c): a navegação de app com ícone e
// rótulo curto, no lugar da barra de botões de texto do topo. Os mesmos
// controles, os mesmos handlers; Balcão é a tela atual, não um botão.
function Trilho({
  regras, aberta, aoAlternarLoja, aoAbrirAutomacoes, aoAbrirGestao, aoAbrirEntregas, simulando, aoAlternarSimulacoes,
  atendente,
}) {
  const { permite } = useAcessoModulos()
  return (
    <nav className={css.trilho} aria-label="Navegação">
      <Marca compacta />
      <h1 className="sr">Casa da Baba</h1>
      {/* Estado da loja logo abaixo da marca: é o que vale para tudo embaixo.
          Mesmo texto e mesmo nome do controle do topo da main ("Loja aberta"
          ou "Loja fechada"), que as provas procuram pelo nome exato. */}
      <button
        type="button"
        className={`${css.itemTrilho} ${css.lojaTrilho} ${aberta ? css.lojaAberta : css.lojaFechada}`}
        aria-pressed={aberta}
        onClick={aoAlternarLoja}
        title={aberta ? 'Loja aberta: toque para fechar' : 'Loja fechada: toque para abrir'}
      >
        <Icone nome={aberta ? 'circle-check' : 'lock'} tamanho={22} />
        <span>{aberta ? 'Loja aberta' : 'Loja fechada'}</span>
      </button>
      <span className={`${css.itemTrilho} ${css.itemAtual}`} aria-current="page">
        <Icone nome="inbox" tamanho={22} />
        <span>Balcão</span>
      </span>
      {/* #1474: no modo API Entregas abre a tela do módulo (com "← Módulos"), não a gaveta. */}
      {permite('entregas') && <ResumoEntregas
        aoAbrir={FONTE_API ? () => { window.location.hash = HASH_MODULO_ENTREGAS } : aoAbrirEntregas}
        noTrilho classeItem={css.itemTrilho} classeSelo={css.seloTrilho}
      />}
      {permite('cozinha') && <Botao variante="texto" className={css.itemTrilho} onClick={abrirCozinha} title={FONTE_API ? 'Cozinha' : 'Cozinha (abre em outra janela)'}>
        <Icone nome="cooking-pot" tamanho={22} />
        <span>Cozinha</span>
      </Botao>}
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirAutomacoes} aria-label={rotuloAutomaticas(regras)} title="Automáticas">
        <Icone nome="raio" tamanho={22} />
        <span>Automáticas</span>
        {regras && <span className={css.contaTrilho} aria-hidden="true">{contarAtivas(regras)}</span>}
      </Botao>
      {/* Casca da rodada 13: item de navegação novo, logo abaixo de
          Automáticas, para as 5 frentes da rodada terem um lugar comum sem
          mexer em navegação de novo depois. Desde a #1447 leva ao hall de
          módulos (o modal Gestão saiu). */}
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirGestao} title="Módulos">
        <Icone nome="painel" tamanho={22} />
        <span>Módulos</span>
      </Botao>
      <div className={css.sinoTrilho}><Sininho /></div>
      <span className={css.espacoTrilho} />
      {aoAlternarSimulacoes && !simularEscondido() && (
        <Botao variante="texto" className={css.itemTrilho} aria-pressed={simulando} onClick={aoAlternarSimulacoes} title="Simular (F2)">
          <Icone nome="flask-conical" tamanho={22} />
          <span>Simular</span>
        </Botao>
      )}
      <InterruptorTema classe={css.temaTrilho} />
      {atendente && (
        <span className={css.atendente} title={`${atendente}, atendendo`}>
          <Avatar nome={atendente} tamanho="medio" />
        </span>
      )}
    </nav>
  )
}

function Gaveta({ titulo, aoFechar, children }) {
  const conteudoRef = useRef(null)
  useEscape(true, aoFechar)

  useEffect(() => { conteudoRef.current?.focus() }, [])

  return (
    <>
      <button type="button" className={css.cortina} aria-label="Fechar ficha" onClick={aoFechar} />
      <aside className={css.gaveta} aria-label={titulo}>
        <header className={css.tituloColuna}>
          <span tabIndex={-1} ref={conteudoRef}>{titulo}</span>
          <span className={css.espaco}>
            <Botao variante="discreto" onClick={aoFechar}><Icone nome="fechar" /> Fechar</Botao>
          </span>
        </header>
        <div className={css.rolavel}>{children}</div>
      </aside>
    </>
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
export function Moldura({
  tamanho, aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca, aoAbrirCardapio, aoAbrirAutomacoes, aoAbrirGestao,
  aoAbrirEntregas, simulando, aoAlternarSimulacoes, larguras, aoRedimensionar,
}) {
  const {
    visiveis, selecionada, conversas, regras: regrasDaMassa, agora, aberta, fonteApi, sessao,
  } = useAtendimento()
  // Modo API (F06): contadores e avatar vêm da sessão e da API; da massa, nada.
  const regras = fonteApi ? null : regrasDaMassa
  const atendente = fonteApi ? (sessao?.usuario?.nome ?? null) : 'Thatiane'
  const { alternarLoja } = useAcoes()
  const [aba, setAba] = useState('balcao')
  const [gavetaAberta, setGavetaAberta] = useState(false)
  const botaoFichaRef = useRef(null)

  const irParaAtendimento = useCallback(() => setAba('atendimento'), [])

  // Rodada 11 (issue #8, registro 92): o cenário do Simular abre a conversa
  // nova (SIMULAR_CONVERSA seleciona a `sim-…`), mas no celular a aba ficava
  // no Balcão e a dona não via o automático responder. Quando a seleção muda
  // para uma conversa simulada, a aba vai junto para Atendimento. Ajuste na
  // própria renderização (padrão do React para "estado que segue uma prop"),
  // não em efeito.
  const selecionadaId = selecionada?.id ?? null
  const [selecaoVista, setSelecaoVista] = useState(selecionadaId)
  if (selecionadaId !== selecaoVista) {
    setSelecaoVista(selecionadaId)
    if (selecionadaId?.startsWith('sim-')) setAba('atendimento')
  }

  const fecharGaveta = useCallback(() => {
    setGavetaAberta(false)
    botaoFichaRef.current?.focus()
  }, [])

  // F2 abre e fecha o painel de simulações de qualquer lugar da tela (seção
  // 7): Ctrl+Shift+S é captura de tela no Edge, F2 não tem dono em navegador
  // nenhum.
  useEffect(() => {
    function aoTeclar(evento) {
      if (evento.key !== 'F2') return
      evento.preventDefault()
      aoAlternarSimulacoes?.()
    }
    window.addEventListener('keydown', aoTeclar)
    return () => window.removeEventListener('keydown', aoTeclar)
  }, [aoAlternarSimulacoes])

  // O antigo painel do agente ("Sugerir", dentro do fio) saiu daqui e passou
  // a morar no balão flutuante (rodada 7, fala do dono 24/09/2026 04h12):
  // `app/App.jsx` monta `<PainelAgente>` e `<BalaoAssistente>` junto, porque
  // feature nenhuma importa outra feature (`ferramentas/verificar-camadas.mjs`).
  const atendimento = (
    <PainelAtendimento
      aoAbrirNota={aoAbrirNota}
      aoAbrirGaleria={aoAbrirGaleria}
      aoAbrirBiblioteca={aoAbrirBiblioteca}
      focarAoTrocar={tamanho !== DESKTOP && tamanho !== TABLET}
    />
  )
  const cabecalho = (
    <Cabecalho
      conversas={conversas}
      regras={regras}
      aberta={aberta}
      aoAlternarLoja={() => alternarLoja(agora)}
      aoAbrirAutomacoes={aoAbrirAutomacoes}
      aoAbrirGestao={aoAbrirGestao}
      aoAbrirEntregas={aoAbrirEntregas}
    />
  )

  if (tamanho === DESKTOP) {
    // Largura de Balcão e Ficha por variável CSS própria da moldura (seção 9):
    // só aqui elas saem do padrão de 320 px do tokens.css. Alça só existe no
    // layout de três colunas; na gaveta e no celular a largura não se aplica.
    const estiloLarguras = {
      '--largura-balcao': `${larguras.balcao}px`,
      '--largura-ficha': `${larguras.ficha}px`,
    }
    return (
      <div className={`${css.app} ${css.appTrilho}`} style={estiloLarguras}>
        <Trilho
          regras={regras}
          aberta={aberta}
          aoAlternarLoja={() => alternarLoja(agora)}
          aoAbrirAutomacoes={aoAbrirAutomacoes}
          aoAbrirGestao={aoAbrirGestao}
          aoAbrirEntregas={aoAbrirEntregas}
          simulando={simulando}
          aoAlternarSimulacoes={aoAlternarSimulacoes}
          atendente={atendente}
        />
        <div className={`${css.palco} ${css.palcoTres}`}>
          {/* Sem contagem ao lado do título (seção 1, corte #11): a aba
              "Precisa de você"/"Todas" já conta, duas vezes é ruído. */}
          <Coluna variante="entrada" titulo="Balcão">
            <PainelConversas />
          </Coluna>
          <AlcaLargura
            rotulo="Redimensionar Balcão"
            valor={larguras.balcao}
            min={300}
            max={480}
            padrao={360}
            aoMudar={(valor) => aoRedimensionar('balcao', valor)}
          />
          <section className={`${css.coluna} ${css.centro}`} aria-label="Atendimento">{atendimento}</section>
          <AlcaLargura
            rotulo="Redimensionar Ficha"
            valor={larguras.ficha}
            min={320}
            max={520}
            padrao={420}
            invertida
            aoMudar={(valor) => aoRedimensionar('ficha', valor)}
          />
          <Coluna variante="ficha" titulo="Ficha">
            <div className={css.rolavel}><PainelFicha aoAbrirCardapio={aoAbrirCardapio} /></div>
            {/* #1474: lugar da fila de canhotos (FilaCanhotos), no pé da Ficha e no fluxo da
                coluna: a pílula não cobre mais o título nem o topo da coluna. Vazio, não ocupa. */}
            <div className={css.slotFila} data-fila-canhotos="" />
          </Coluna>
        </div>
      </div>
    )
  }

  if (tamanho === TABLET) {
    return (
      <div className={css.app}>
        {cabecalho}
        <div className={css.palco}>
          {/* Sem contagem ao lado do título (seção 1, corte #11): a aba
              "Precisa de você"/"Todas" já conta, duas vezes é ruído. */}
          <Coluna variante="entrada" titulo="Balcão">
            <PainelConversas />
          </Coluna>
          <section className={`${css.coluna} ${css.centro}`} aria-label="Atendimento">{atendimento}</section>
          {gavetaAberta && (
            <Gaveta titulo="Ficha" aoFechar={fecharGaveta}>
              <PainelFicha aoAbrirCardapio={aoAbrirCardapio} />
            </Gaveta>
          )}
        </div>
        <footer className={css.rodape}>
          <Botao ref={botaoFichaRef} onClick={() => setGavetaAberta(true)}>
            Ficha de {selecionada?.nome.split(' ')[0] ?? 'cliente'}
          </Botao>
        </footer>
        <BotaoSimular ativo={simulando} aoAlternar={aoAlternarSimulacoes} />
      </div>
    )
  }

  const painelDaAba = {
    balcao: <PainelConversas aoAbrir={irParaAtendimento} />,
    atendimento,
    ficha: <PainelFicha aoAbrirCardapio={aoAbrirCardapio} />,
  }

  return (
    <div className={css.app}>
      {cabecalho}
      {/* Sem rolagem nesta seção: cada painel tem a área rolável dele por
          dentro, e é isso que mantém o composer preso no pé do celular. */}
      <div className={css.palco}>
        <section
          className={`${css.coluna} ${css.centro}`}
          role="tabpanel"
          id="painel-da-aba"
          aria-labelledby={`aba-${aba}`}
        >
          {painelDaAba[aba]}
        </section>
      </div>
      <BarraDeAbas
        ativa={aba}
        aoTrocar={setAba}
        abas={[
          { id: 'balcao', rotulo: 'Balcão', contador: visiveis.length },
          { id: 'atendimento', rotulo: 'Atendimento' },
          { id: 'ficha', rotulo: 'Ficha' },
        ]}
      />
      <BotaoSimular ativo={simulando} aoAlternar={aoAlternarSimulacoes} />
    </div>
  )
}
