import { useCallback, useEffect, useRef, useState } from 'react'
import { AlcaLargura } from '../componentes/AlcaLargura'
import { MenuDaConta } from '../componentes/MenuDaConta'
import { Marca } from '../componentes/Marca'
import { InterruptorTema } from '../componentes/InterruptorTema'
import { trocarTema } from '../hooks/useTema'
import { balcaoComCardapio, fichaComCardapio } from '../hooks/larguras'
import { BarraDeAbas } from '../componentes/BarraDeAbas'
import { Botao } from '../componentes/Botao'
import { Modal } from '../componentes/Modal'
import { Icone } from '../componentes/Icone'
import { MarcaIcone } from '../componentes/MarcaIcone'
import { Pilula } from '../componentes/Pilula'
import { useAcoes, useAtendimento } from '../aplicacao/contextos'
import { useAcessoModulos } from '../aplicacao/acessoModulos'
import { contarAbertas, contarNaEsteira } from '../dominio/conversa'
import { contarAtivas } from '../dominio/automacao'
import { DESKTOP, TABLET, useTopoCompacto } from '../hooks/useTamanhoTela'
import { PainelConversas } from '../features/caixa-de-entrada/PainelConversas'
import { PainelAtendimento } from '../features/atendimento/PainelAtendimento'
import { CompartilharComanda } from '../features/atendimento/CompartilharComanda'
import { ChamarClientes } from '../features/atendimento/ChamarClientes'
import { PainelFicha } from '../features/ficha-cliente/PainelFicha'
import { ResumoEntregas } from '../features/entregas/ResumoEntregas'
import { Sininho } from '../features/lembretes/Sininho'
import { Popover } from '../componentes/Popover'
import { FONTE_API } from '../infra/fonteDados'
import { HASH_HALL, HASH_MODULO_COZINHA } from '../dominio/rota'
import { moduloPorId } from '../dominio/modulos'
import { IconeDoModulo } from '../componentes/IconeDoModulo'
import { JANELA_COZINHA } from '../dominio/janelaAvulsa'
import { AvisoDaAcaoApi } from './FaixaApi'
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

// "Em andamento" e "na esteira" não pedem ação nenhuma sozinhos: viraram texto
// dentro deste popover, em vez de competir com "Precisa de você" no topo.
function grupoDoDia(emAndamento, naEsteira) {
  return {
    titulo: 'Hoje',
    itens: [{
      chave: '__hoje',
      informativo: true,
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
  refBotao, aoAbrirEntregas, aoAbrirGestao, aoAbrirAjuda, emAndamento, naEsteira, atendente = null,
}) {
  const { permite, aoSair, sessaoPersistente, conta } = useAcessoModulos()
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
        { chave: '__modulos', titulo: 'Módulos', detalhe: 'Escolher outra área de trabalho' },
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
        { chave: '__gestao', titulo: 'Gestão', detalhe: 'Cardápio, produção e caixa' },
      ].filter((item) => item.chave !== '__entregas' || permite('entregas')),
    },
    { titulo: 'Ajuda', itens: [{ chave: '__ajuda', titulo: 'Ajuda do sistema', detalhe: 'O que cada módulo faz, primeiros passos e diagnóstico' }] },
    grupoDoDia(emAndamento, naEsteira),
    // #1242 (D1): a faixa de baixo saiu do celular; quem está logado e o Sair vêm para cá.
    ...(aoSair ? [{
      titulo: 'Conta',
      itens: [{ chave: '__sair', titulo: sessaoPersistente ? 'Sair deste aparelho' : 'Sair', detalhe: [atendente, conta?.casa].filter(Boolean).join(' · ') || 'Encerrar a sessão' }],
    }] : []),
  ]

  function escolher(item) {
    fechar()
    if (['__entregas', '__gestao', '__ajuda'].includes(item.chave)) refBotao.current?.focus()
    if (item.chave === '__modulos') window.location.hash = HASH_HALL
    if (item.chave === '__entregas') aoAbrirEntregas()
    if (item.chave === '__gestao') aoAbrirGestao()
    if (item.chave === '__ajuda') aoAbrirAjuda()
    if (item.chave === '__claro') trocarTema('light')
    if (item.chave === '__escuro') trocarTema('dark')
    if (item.chave === '__sair') aoSair?.()
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
        <Icone nome="ellipsis-vertical" tamanho={20} />
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

// Abre a Cozinha (US-037, US-038) numa janela própria, o tablet da parede
// (D6). Botão discreto e fixo, fora do `compacto ? MenuMais : ResumoEntregas`
// que já existe: assim ela sempre acha o mesmo controle, larga ou estreita a
// tela, sem duplicar o item em dois menus.
//
// Mantém o atendimento aberto. O KDS da API continua usando a rota protegida do módulo.
function abrirCozinha() {
  const destino = FONTE_API ? HASH_MODULO_COZINHA : '#/cozinha'
  const janela = window.open(destino, JANELA_COZINHA, 'width=1280,height=900')
  // Issue #40 (rodada 13, achado da varredura): popup bloqueado devolve
  // `null` e nada acontece. Cai para a mesma aba: `App.jsx` agora reage à
  // troca de hash (useHash), então a Cozinha aparece de qualquer jeito.
  if (!janela) window.location.hash = destino
}

// Estado da loja (pedido do dono, 24/09/2026): um toque, Aberta ou Fechada,
// sempre visível no topo. Mostra o efetivo (horário configurado combinado
// com o estado manual, dominio/funcionamento.js), não só o que ela tocou por
// último, porque "Estado visível sempre" é sobre o que vale agora.
//
// #2135: no celular o texto longo sai e entra o curto ("Aberta"/"Fechada") sob o ícone, com alvo de 44 px
// e a dica do trilho do desktop. O nome acessível segue "Loja aberta"/"Loja fechada" (`aria-label`),
// que contém o texto visível. Fechar dentro do horário continua pedindo justificativa (aplicacao/api/expediente.js).
function ControleLoja({ aberta, aoAlternar }) {
  const nome = aberta ? 'Loja aberta' : 'Loja fechada'
  return (
    <button
      type="button"
      className={css.controleLoja}
      aria-pressed={aberta}
      aria-label={nome}
      title={aberta ? 'Loja aberta: toque para fechar' : 'Loja fechada: toque para abrir'}
      onClick={aoAlternar}
    >
      <Pilula tom={aberta ? 'ok' : 'perigo'} icone={aberta ? 'circle-check' : 'lock'}>
        <span className={css.lojaTextoLongo}>{nome}</span>
        <span className={css.lojaTextoCurto} aria-hidden="true">{aberta ? 'Aberta' : 'Fechada'}</span>
      </Pilula>
    </button>
  )
}

// Modo API (F06): sem regra vinda da API, sem número. A massa mostrava "Automáticas 6".
const MODULO_ATENDIMENTO = moduloPorId('atendimento')

const rotuloAutomaticas = (regras) => (regras ? `Automáticas, ${contarAtivas(regras)} ligadas` : 'Automáticas')

function Cabecalho({
  conversas, regras, aberta, aoAlternarLoja, aoAbrirAutomacoes, aoAbrirGestao, aoAbrirEntregas, aoAbrirAjuda, atendente = null,
}) {
  const { permite, notificacoes, aoSair, sessaoPersistente, conta } = useAcessoModulos()
  const compacto = useTopoCompacto()
  const botaoMaisRef = useRef(null)
  const emAndamento = contarAbertas(conversas)
  const naEsteira = contarNaEsteira(conversas)

  return (
    <header className={css.topo}>
      {/* #1582: entre 1024 e 1179 px não há menu Mais nem trilho; a marca é o caminho ao hall,
          como no trilho do desktop. */}
      <a className={css.marca} href={HASH_HALL} aria-label="Ir para os módulos" title="Módulos">
        <Marca compacta />
      </a>
      <h1 className="sr">Casa da Baba</h1>
      {!compacto && <InterruptorTema />}
      <ControleLoja aberta={aberta} aoAlternar={aoAlternarLoja} />
      <div className={css.controles}>
        {/* Sininho é o primeiro do grupo, à esquerda de Automáticas (seção 8
            da direção visual). A faixa de lembretes abaixo do topo saiu. */}
        <Sininho />
        {notificacoes}
        {permite('cozinha') && <Botao variante="texto" icone="cooking-pot" onClick={abrirCozinha} title="Cozinha (abre em outra janela)">
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
            emAndamento={emAndamento}
            naEsteira={naEsteira}
            atendente={atendente}
          />
        ) : (
          // Rodada 2: a ocupação das janelas mora no topo, porque capacidade
          // estourada é a coisa que ela mais teme e não pode custar dois cliques.
          <>{permite('entregas') && <ResumoEntregas aoAbrir={aoAbrirEntregas} />}
            <Botao variante="texto" icone="ajuda" aria-haspopup="dialog" onClick={aoAbrirAjuda}>Ajuda</Botao>
            {/* #1242 (D1): no tablet largo o Sair mora no avatar, como no trilho do desktop. */}
            {aoSair && <MenuDaConta nome={atendente} detalhe={conta?.casa} aoSair={aoSair} lado="abaixo"
              rotuloSair={sessaoPersistente ? 'Sair deste aparelho' : 'Sair'} />}</>
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
      <Icone nome="painel" tamanho={22} /><span>Disposição</span>
    </Botao>
    {aberto && <Popover rotulo="Disposição das conversas" papel="dialog" portal posicao="acima" aoFechar={() => setAberto(false)}>
      <div className={css.ajusteDisposicao}>
        <strong>Seu espaço de trabalho</strong>
        <label>Conversas <output>{larguras.balcao} px</output>
          <input aria-label="Largura de Conversas" type="range" min="300" max="480" step="10" value={larguras.balcao} onChange={e => aoRedimensionar('balcao', Number(e.target.value))} />
        </label>
        <label>Ficha <output>{larguras.ficha} px</output>
          <input aria-label="Largura da ficha" type="range" min="320" max="520" step="10" value={larguras.ficha} onChange={e => aoRedimensionar('ficha', Number(e.target.value))} />
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
  const { permite, notificacoes, aoSair, sessaoPersistente, conta } = useAcessoModulos()
  return (
    <nav className={css.trilho} aria-label="Navegação">
      <a className={css.inicioTrilho} href={HASH_HALL} aria-label="Ir para os módulos" title="Módulos">
        <Marca compacta /><span>Módulos</span>
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
        title={aberta ? 'Loja aberta: toque para fechar' : 'Loja fechada: toque para abrir'}
      >
        <Icone nome={aberta ? 'circle-check' : 'lock'} tamanho={22} />
        <span>{aberta ? 'Loja aberta' : 'Loja fechada'}</span>
      </button>
      {/* #1870: o mesmo nome e o mesmo ícone do trilho dos módulos; a tela continua sendo o Balcão. */}
      <span className={`${css.itemTrilho} ${css.itemAtual}`} aria-current="page" title="Atendimento">
        <IconeDoModulo modulo={MODULO_ATENDIMENTO} />
        <span>{MODULO_ATENDIMENTO.nome}</span>
      </span>
      {permite('entregas') && <ResumoEntregas
        aoAbrir={aoAbrirEntregas}
        noTrilho classeItem={css.itemTrilho} classeSelo={css.seloTrilho}
      />}
      {permite('cozinha') && <Botao variante="texto" className={css.itemTrilho} onClick={abrirCozinha} title="Cozinha (abre em outra janela)">
        <Icone nome="cooking-pot" tamanho={22} />
        <span>Cozinha</span>
      </Botao>}
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirAutomacoes} aria-label={rotuloAutomaticas(regras)} title="Automáticas">
        <Icone nome="raio" tamanho={22} />
        <span>Automáticas</span>
        {regras && <span className={css.contaTrilho} aria-hidden="true">{contarAtivas(regras)}</span>}
      </Botao>
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirGestao} title="Gestão" aria-haspopup="dialog">
        <Icone nome="painel" tamanho={22} />
        <span>Gestão</span>
      </Botao>
      {/* #1807: Avisos (sino) e Lembretes (relógio) com ícone e rótulo próprios, cada rótulo
          dentro do próprio botão. Antes o "Lembretes" vinha de CSS e ficava colado ao sino de
          Avisos logo abaixo, e quem clicava em Avisos abria Lembretes. */}
      <div className={css.avisosTrilho}>{notificacoes}</div>
      <div className={css.sinoTrilho}><Sininho comRotulo /></div>
      <Botao variante="texto" className={css.itemTrilho} onClick={aoAbrirAjuda} aria-haspopup="dialog" title="Ajuda do sistema">
        <MarcaIcone nome="informacao" tamanho={22} /><span>Ajuda</span>
      </Botao>
      {disposicao}
      <span className={css.espacoTrilho} />
      {aoAlternarSimulacoes && !simularEscondido() && (
        <Botao variante="texto" className={css.itemTrilho} aria-pressed={simulando} onClick={aoAlternarSimulacoes} title="Simular (F2)">
          <Icone nome="flask-conical" tamanho={22} />
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

// #1551: a gaveta é um <dialog> e a FaixaApi fica atrás da cortina. O aviso da ação da API
// (janela faltando, pedido não criado, cobrança que não saiu) vem para o rodapé dela.
function Gaveta({ titulo, aoFechar, children }) {
  return (
    <Modal titulo={titulo} className={css.gaveta} largura="min(480px, 90vw)" aoFechar={aoFechar} rodape={<AvisoDaAcaoApi />}>
      {children}
    </Modal>
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
  aoAbrirEntregas, aoAbrirAjuda, simulando, aoAlternarSimulacoes, larguras, largurasPadrao = larguras, aoRedimensionar, cardapioAberto = null,
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
  const acaoCompartilhar = selecionada && <CompartilharComanda key={'compartilhar-' + selecionada.id} conversa={selecionada} />
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
  // Frente C da #1889: cada coluna do Balcão dentro do seu limite de erro (LimiteDeErro).
  const atendimento = (
    <LimiteDeErro parte="Conversa">
      <PainelAtendimento
        aoAbrirNota={aoAbrirNota}
        aoAbrirGaleria={aoAbrirGaleria}
        aoAbrirBiblioteca={aoAbrirBiblioteca}
        focarAoTrocar={tamanho !== DESKTOP && tamanho !== TABLET}
      />
    </LimiteDeErro>
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
          aoAlternarLoja={() => alternarLoja(agora)}
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
            min={300}
            max={480}
            padrao={largurasPadrao.balcao}
            aoMudar={(valor) => aoRedimensionar('balcao', valor)}
          />
          <section className={`${css.coluna} ${css.centro}`} aria-label="Conversa">{atendimento}</section>
          <AlcaLargura
            rotulo="Redimensionar ficha"
            valor={larguras.ficha}
            min={320}
            max={520}
            padrao={largurasPadrao.ficha}
            invertida
            aoMudar={(valor) => aoRedimensionar('ficha', valor)}
          />
          <Coluna variante="ficha" titulo="Ficha" semTitulo>
            <div className={css.painelFicha}><LimiteDeErro parte="Resumo"><PainelFicha aoAbrirCardapio={aoAbrirCardapio} acaoCompartilhar={acaoCompartilhar} /></LimiteDeErro></div>
            {/* #1474: lugar da fila de canhotos (FilaCanhotos), no pé da Ficha e no fluxo da
                coluna: a pílula não cobre mais o título nem o topo da coluna. Vazio, não ocupa. */}
            <div className={css.slotFila} data-fila-canhotos="" />
          </Coluna>
        </main>
      </div>
    )
  }

  if (tamanho === TABLET) {
    return (
      <div className={css.app}>
        {cabecalho}
        <main className={css.palco} tabIndex={-1} data-inicio-tela id="conteudo-principal" aria-label="Atendimento">
          {/* Sem contagem ao lado do título (seção 1, corte #11): a aba
              "Precisa de você"/"Todas" já conta, duas vezes é ruído. */}
          <Coluna variante="entrada" titulo="Conversas">
            <LimiteDeErro parte="Lista de conversas"><PainelConversas buscaDeClientes={ChamarClientes} /></LimiteDeErro>
          </Coluna>
          <section className={`${css.coluna} ${css.centro}`} aria-label="Conversa">{atendimento}</section>
          {gavetaAberta && (
            <Gaveta titulo="Ficha" aoFechar={fecharGaveta}>
              <LimiteDeErro parte="Resumo"><PainelFicha aoAbrirCardapio={() => { fecharGaveta(); aoAbrirCardapio() }} acaoCompartilhar={acaoCompartilhar} /></LimiteDeErro>
            </Gaveta>
          )}
        </main>
        <div className={css.slotFila} data-fila-canhotos="" />
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
    balcao: <LimiteDeErro parte="Lista de conversas"><PainelConversas aoAbrir={irParaAtendimento} buscaDeClientes={ChamarClientes} /></LimiteDeErro>,
    atendimento,
    ficha: <LimiteDeErro parte="Resumo"><PainelFicha aoAbrirCardapio={aoAbrirCardapio} acaoCompartilhar={acaoCompartilhar} /></LimiteDeErro>,
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
          {aba !== 'atendimento' && (
            <header className={`${css.tituloColuna} ${css.tituloCompacto}`}>
              <span>{aba === 'ficha' ? `Ficha de ${selecionada?.nome.split(' ')[0] ?? 'cliente'}` : 'Conversas'}</span>
              <span className={css.espaco} data-ancora-assistente />
            </header>
          )}
          {painelDaAba[aba]}
        </section>
      </main>
      <div className={css.slotFila} data-fila-canhotos="" />
      <BarraDeAbas
        ativa={aba}
        aoTrocar={setAba}
        abas={[
          { id: 'balcao', rotulo: 'Conversas', contador: visiveis.length },
          { id: 'atendimento', rotulo: 'Conversa' },
          { id: 'ficha', rotulo: 'Ficha' },
        ]}
      />
      <BotaoSimular ativo={simulando} aoAlternar={aoAlternarSimulacoes} />
    </div>
  )
}
