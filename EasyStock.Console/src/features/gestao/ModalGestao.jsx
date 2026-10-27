import { useId, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { IconeDoModulo } from '../../componentes/IconeDoModulo'
import { Modal } from '../../componentes/Modal'
import { Vazio } from '../../componentes/Vazio'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { MODULOS, TELA_AJUSTE, hashDoModulo, moduloPorId, telasDoMenu } from '../../dominio/modulos'
import { HASH_HALL, ROTA_PRINCIPAL, rotaDaHash } from '../../dominio/rota'
import { AbaCaixaApi } from './caixa/AbaCaixaApi'
import { PainelFinanceiro } from './financeiro/PainelFinanceiro'
import { AbaIntegracoesApi } from './integracoes/AbaIntegracoesApi'
import { AbaAtendimento } from './atendimento/AbaAtendimento'
import { AbaAvisos } from './avisos/AbaAvisos'
import { AbaImpressao } from './impressao/AbaImpressao'
import { AbaRespostas } from './respostas/AbaRespostas'
import { TelaCanais } from './canais/TelaCanais'
import { AbaUsuarios } from './usuarios/AbaUsuarios'
import { AbaAuditoria } from './usuarios/AbaAuditoria'
import { AbaAcervo } from './acervo/AbaAcervo'
import { AbaCardapioApi } from './cardapio/AbaCardapioApi'
import { AbaCategoriasApi } from './cardapio/AbaCategoriasApi'
import { AbaEstoqueDoDiaApi } from './producao/AbaEstoqueDoDiaApi'
import { AbaProducaoDoDiaApi } from './producao/AbaProducaoDoDiaApi'
import { AbaInsumosApi } from './producao/AbaInsumosApi'
import { AbaReceitasApi } from './producao/AbaReceitasApi'
import { AbaCalculadoraApi } from './producao/AbaCalculadoraApi'
import { AbaPlanejamentoApi } from './producao/AbaPlanejamentoApi'
import { AbaPerdasApi } from './producao/AbaPerdasApi'
import { AbaDadosDaLoja } from './loja/AbaDadosDaLoja'
import { AbaSiteEImpressos } from './vitrine/AbaSiteEImpressos'
import { AbaAlertasDeEstoque } from './estoque/AbaAlertasDeEstoque'
import { AbaCadernoDoAgente } from './caderno/AbaCadernoDoAgente'
import { AvisosNesteAparelho } from './avisos/AvisosNesteAparelho'
import { useAtendimento } from '../../aplicacao/contextos'
import css from './gestao.module.css'

function navegarAbas(evento, itens, indice, escolher, botoes, vertical = false) {
  const anterior = vertical ? 'ArrowUp' : 'ArrowLeft'
  const proxima = vertical ? 'ArrowDown' : 'ArrowRight'
  let destino
  if (evento.key === anterior) destino = (indice - 1 + itens.length) % itens.length
  else if (evento.key === proxima) destino = (indice + 1) % itens.length
  else if (evento.key === 'Home') destino = 0
  else if (evento.key === 'End') destino = itens.length - 1
  else return
  evento.preventDefault()
  const item = itens[destino]
  escolher(item)
  botoes.current[item.id]?.focus({ preventScroll: true })
  botoes.current[item.id]?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
}

// A janela e as rotas usam o mesmo catálogo, as mesmas permissões e o mesmo
// PainelDeAjuste. Trocar uma aba aqui mantém a conversa e o pedido montados.
// `slots` (#2070): o que só o App compõe (editor do cardápio, horários, cadastro de entrega e de entregadores, abas
// da demonstração), o mesmo objeto da tela de rota e dos Primeiros passos. Tela `foraDaGestao` não entra aqui.
export function ModalGestao({ aoFechar, abaInicial = null, aoTrocarAba = null, focoInicial = null, avisosApi = null, ...slots }) {
  const { fonteApi } = useAtendimento()
  const { permite, acoes } = useAcessoModulos()
  const areas = useMemo(() => MODULOS
    .filter((modulo) => permite(modulo.id))
    .map((modulo) => ({ ...modulo, telas: telasDoMenu(modulo, { fonteApi, acoes }).filter((tela) => tela.tipo === TELA_AJUSTE && !tela.foraDaGestao) }))
    .filter((modulo) => modulo.telas.length > 0), [fonteApi, permite, acoes])
  const [abaAtiva, setAbaAtiva] = useState(abaInicial)
  const area = areas.find((modulo) => modulo.telas.some((tela) => tela.aba === abaAtiva)) ?? areas[0]
  const tela = area?.telas.find((item) => item.aba === abaAtiva) ?? area?.telas[0]
  const id = useId()
  const botoesArea = useRef({})
  const botoesTela = useRef({})
  const painelRef = useRef(null)
  function escolherTela(item) {
    setAbaAtiva(item.aba)
    aoTrocarAba?.(item.aba)
  }
  const escolherArea = (modulo) => escolherTela(modulo.telas[0])

  function abrirAba(aba) {
    if (!areas.some((modulo) => modulo.telas.some((item) => item.aba === aba))) return
    escolherTela({ aba })
    painelRef.current?.focus()
  }

  function seguirLink(evento) {
    if (evento.defaultPrevented || evento.button !== 0 || evento.metaKey || evento.ctrlKey || evento.shiftKey || evento.altKey) return
    const link = evento.target.closest?.('a[href]')
    if (!link || link.target || link.hasAttribute('download')) return
    const href = link.getAttribute('href')
    if (!href?.startsWith('#/m/')) return
    const destino = rotaDaHash(href, { fonteApi })
    // #1582: esta janela só abre por cima do Balcão; link para o Balcão é fechar a janela.
    if (destino.tipo === ROTA_PRINCIPAL) {
      evento.preventDefault()
      aoFechar()
      return
    }
    const permitido = areas.some((modulo) => modulo.id === destino.modulo && modulo.telas.some((item) => item.aba === destino.aba))
    if (!permitido || ![hashDoModulo(destino.modulo), hashDoModulo(destino.modulo, destino.tela)].includes(href)) return
    evento.preventDefault()
    abrirAba(destino.aba)
  }

  return (
    <Modal
      titulo="Gestão da loja"
      aoFechar={aoFechar}
      largura="min(1180px, calc(100vw - 24px))"
      rodape={(
        <div className={css.rodapeGestao}>
          {avisosApi && <div className={css.avisosGestao}>{avisosApi}</div>}
          <div className={css.acoesGestao}>
            <a className={css.verModulos} href={HASH_HALL} onClick={aoFechar}>
              <Icone nome="painel" tamanho={18} /> Ver módulos
            </a>
            <Botao onClick={aoFechar}>Fechar</Botao>
          </div>
        </div>
      )}
    >
      {area ? (
        <div className={css.janelaGestao}>
          <div className={css.areasGestao}>
            <p className={css.rotuloAreas}>Áreas da loja</p>
            <div role="tablist" aria-label="Áreas da loja" aria-orientation="vertical" className={css.listaAreas}>
              {areas.map((modulo, indice) => (
                <button
                  key={modulo.id}
                  ref={(no) => { botoesArea.current[modulo.id] = no }}
                  type="button"
                  role="tab"
                  id={`${id}-area-${modulo.id}`}
                  aria-selected={modulo.id === area.id}
                  aria-controls={`${id}-area`}
                  tabIndex={modulo.id === area.id ? 0 : -1}
                  className={css.areaGestao}
                  onClick={() => escolherArea(modulo)}
                  onKeyDown={(evento) => navegarAbas(evento, areas, indice, escolherArea, botoesArea, true)}
                >
                  <IconeDoModulo modulo={modulo} />
                  <span>{modulo.nome}</span>
                </button>
              ))}
            </div>
          </div>

          <div className={css.areaCompacta}>
            <label htmlFor={`${id}-escolher-area`}>Área da loja</label>
            <div className={css.seletorArea}>
              <IconeDoModulo modulo={area} />
              <select id={`${id}-escolher-area`} value={area.id} onChange={(evento) => escolherArea(areas.find((modulo) => modulo.id === evento.target.value))}>
                {areas.map((modulo) => <option key={modulo.id} value={modulo.id}>{modulo.nome}</option>)}
              </select>
            </div>
          </div>

          <section id={`${id}-area`} role="tabpanel" aria-label={area.nome} className={css.corpoGestao}>
            <header className={css.cabecalhoArea}>
              <h3 className={css.tituloArea}><IconeDoModulo modulo={area} />{area.nome}</h3>
              {area.telas.length > 1 ? (
                <div className={css.subtelasGestao} role="tablist" aria-label={`Telas de ${area.nome}`} aria-orientation="vertical">
                  {area.telas.map((item, indice) => (
                    <button
                      key={item.id}
                      ref={(no) => { botoesTela.current[item.id] = no }}
                      type="button"
                      role="tab"
                      id={`${id}-tela-${item.id}`}
                      aria-selected={item.id === tela.id}
                      aria-controls={`${id}-tela`}
                      tabIndex={item.id === tela.id ? 0 : -1}
                      className={css.subtelaGestao}
                      onClick={() => escolherTela(item)}
                      onKeyDown={(evento) => navegarAbas(evento, area.telas, indice, escolherTela, botoesTela, true)}
                    >
                      <IconeDoModulo modulo={area} telaId={item.id} />{item.rotulo}{fonteApi && item.indisponivelNaApi ? ' · Em preparação' : ''}
                    </button>
                  ))}
                </div>
              ) : <p className={css.nomeTela}>{tela.rotulo}</p>}
            </header>
            <div
              id={`${id}-tela`}
              role={area.telas.length > 1 ? 'tabpanel' : undefined}
              aria-labelledby={area.telas.length > 1 ? `${id}-tela-${tela.id}` : undefined}
              tabIndex={0}
              ref={painelRef}
              className={css.painelGestao}
              onClickCapture={seguirLink}
            >
              <PainelDeAjuste {...slots} aba={tela.aba} focoInicial={focoInicial} aoAbrirAba={abrirAba} />
            </div>
          </section>
        </div>
      ) : (
        <Vazio titulo="Nenhum ajuste disponível">Consulte as telas disponíveis em Ver módulos.</Vazio>
      )}
    </Modal>
  )
}

// Modo API: as abas sem backend conservam o endereço, sem montar dados e formulários
// de demonstração. Quando existe destino real, a saída respeita o acesso da sessão.
// #1582: `saida` aponta as telas que já fazem o trabalho da aba, quando existem.
const AINDA_NAO_LIGADO = {
  producao: {
    texto: 'Esta tela ainda não registra produção nem altera o estoque.',
    saida: { modulo: 'producao', telas: ['producao'] },
  },
  fidelidade: { texto: 'Cupons e fidelidade ainda não estão disponíveis nesta tela.' },
}

function AbaNaoLigada({ texto, saida = null }) {
  const { permite } = useAcessoModulos()
  const modulo = saida && permite(saida.modulo) ? moduloPorId(saida.modulo) : null
  const telas = modulo ? saida.telas.map((id) => modulo.telas.find((tela) => tela.id === id)).filter(Boolean) : []
  return (
    <Vazio titulo="Em preparação" acao={telas.length > 0
      ? telas.map(tela => <a className={css.saidaPreparacao} key={tela.id} href={hashDoModulo(modulo.id, tela.id)}>{tela.rotulo}</a>)
      : <a className={css.saidaPreparacao} href={HASH_HALL}>Ver módulos</a>}>
      {texto}
    </Vazio>
  )
}

// Uma tela por aba: cada entrada monta só o elemento da aba pedida.
// `janelasApi` (#1440): no modo API o App entrega o cadastro de janelas, zonas e bloqueios da S45 (o mesmo de
// Entregas › Janelas e frete), no lugar da aba de demonstração; `secao` (#2070) mostra só as janelas ou só o frete.
// `abasDaDemonstracao` (#2017): as abas que só a demonstração tem (caixa, janelas, integrações, produção e
// fidelidade com dados do navegador). O App as entrega; no build de produção não existem.
// #2070: os formulários novos recebem `formulario` (`{ aoMudarPendencia, aoSalvo, compacto }`); `secoes` monta só
// parte da AbaAtendimento; `horariosDaSemana` e `entregadoresApi` chegam do App (feature não importa feature).
const PAINEIS = {
  atendimento: ({ horariosAtendimento, secoes, horariosDaSemana, formulario }) => (
    <AbaAtendimento horariosAtendimento={horariosAtendimento} secoes={secoes} aoMudarPendencia={formulario.aoMudarPendencia}
      horariosDaSemana={secoes?.includes('horarios') ? horariosDaSemana?.(formulario) ?? null : null} />
  ),
  // #1441: respostas prontas e automáticas, fora do caminho do atendimento.
  respostas: ({ focoInicial }) => <AbaRespostas focoInicial={focoInicial} />,
  producao: ({ demonstracao: { AbaProducao } }) => AbaProducao && <AbaProducao />,
  // #1443: no modo API o caixa é o do EasyStok; a demonstração segue com a massa local.
  caixa: ({ fonteApi, demonstracao: { AbaCaixa } }) => (fonteApi ? <AbaCaixaApi /> : AbaCaixa && <AbaCaixa />),
  janelas: ({ fonteApi, janelasApi, secao, demonstracao: { AbaJanelas } }) => (fonteApi && janelasApi ? janelasApi({ secao }) : AbaJanelas && <AbaJanelas />),
  fidelidade: ({ demonstracao: { AbaFidelidade } }) => AbaFidelidade && <AbaFidelidade />,
  integracoes: ({ fonteApi, demonstracao: { AbaIntegracoes } }) => (fonteApi ? <AbaIntegracoesApi /> : AbaIntegracoes && <AbaIntegracoes />),
  canais: () => <TelaCanais />,
  // #1753 (M7.2): usuários e histórico de acesso (só modo API).
  usuarios: () => <AbaUsuarios />,
  auditoria: () => <AbaAuditoria />,
  // #1632: logo da loja e papel padrão dos impressos (só modo API).
  impressao: () => <AbaImpressao />,
  // M1.1 (#1481): gestão do cardápio (só modo API).
  acervo: () => <AbaAcervo />,
  cardapio: ({ renderizarEditorCardapio }) => <AbaCardapioApi renderizarEditor={renderizarEditorCardapio} />,
  // M1.3 (#1483): categorias do cardápio (só modo API).
  categorias: () => <AbaCategoriasApi />,
  // M2.1 (#1490): estoque do dia em porções (só modo API).
  'estoque-do-dia': () => <AbaEstoqueDoDiaApi />,
  // M2.2 (#1491): produção do dia por prato (só modo API).
  'producao-do-dia': () => <AbaProducaoDoDiaApi />,
  // M2.3 (#1496): insumos (só modo API).
  insumos: () => <AbaInsumosApi />,
  // M2.4a (#1498): receitas (só modo API).
  receitas: () => <AbaReceitasApi />,
  'calculadora-producao': () => <AbaCalculadoraApi />,
  // M2.5 (#1502): planejamento da produção (só modo API).
  planejamento: ({ aoAbrirAba }) => <AbaPlanejamentoApi aoAbrirAba={aoAbrirAba} />,
  // M2.6 (#1511): controle de perdas (só modo API).
  perdas: () => <AbaPerdasApi />,
  // #2070: os formulários da loja que só tinham API (só modo API).
  'dados-da-loja': ({ formulario }) => <AbaDadosDaLoja {...formulario} />,
  'site-e-impressos': ({ formulario }) => <AbaSiteEImpressos {...formulario} />,
  'alertas-de-estoque': ({ formulario }) => <AbaAlertasDeEstoque {...formulario} />,
  caderno: ({ formulario }) => <AbaCadernoDoAgente {...formulario} />,
  // #2096: o que avisa a dona e o gerente; #2070: acima, ligar os avisos neste aparelho.
  avisos: () => <><AvisosNesteAparelho /><AbaAvisos /></>,
  entregadores: ({ entregadoresApi, formulario }) => entregadoresApi?.(formulario) ?? null,
}

export function PainelDeAjuste({
  aba, focoInicial = null, aoAbrirAba = null, secoes = null, secao = null, aoMudarPendencia, aoSalvo, compacto = false,
  renderizarEditorCardapio, horariosAtendimento, janelasApi = null, horariosDaSemana = null, entregadoresApi = null, abasDaDemonstracao = null,
}) {
  const { fonteApi } = useAtendimento()
  if (fonteApi && aba?.startsWith('financeiro-')) return <PainelFinanceiro key={aba} tela={aba.slice('financeiro-'.length)} />
  if (fonteApi && AINDA_NAO_LIGADO[aba]) return <AbaNaoLigada {...AINDA_NAO_LIGADO[aba]} />
  return PAINEIS[aba]?.({
    fonteApi, focoInicial, aoAbrirAba, secoes, secao, renderizarEditorCardapio, horariosAtendimento, janelasApi, horariosDaSemana, entregadoresApi,
    demonstracao: abasDaDemonstracao ?? {}, formulario: { aoMudarPendencia, aoSalvo, compacto },
  }) ?? null
}
