import { useId, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Vazio } from '../../componentes/Vazio'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { MODULOS, TELA_AJUSTE, hashDoModulo, telasDoMenu } from '../../dominio/modulos'
import { HASH_HALL, rotaDaHash } from '../../dominio/rota'
import { AbaProducao } from './producao/AbaProducao'
import { AbaCaixa } from './caixa/AbaCaixa'
import { AbaCaixaApi } from './caixa/AbaCaixaApi'
import { AbaJanelas } from './janelas/AbaJanelas'
import { AbaFidelidade } from './fidelidade/AbaFidelidade'
import { AbaIntegracoes } from './integracoes/AbaIntegracoes'
import { AbaAtendimento } from './atendimento/AbaAtendimento'
import { AbaRespostas } from './respostas/AbaRespostas'
import { TelaCanais } from './canais/TelaCanais'
import { AbaCardapioApi } from './cardapio/AbaCardapioApi'
import { AbaCategoriasApi } from './cardapio/AbaCategoriasApi'
import { AbaEstoqueDoDiaApi } from './producao/AbaEstoqueDoDiaApi'
import { AbaProducaoDoDiaApi } from './producao/AbaProducaoDoDiaApi'
import { AbaInsumosApi } from './producao/AbaInsumosApi'
import { AbaReceitasApi } from './producao/AbaReceitasApi'
import { AbaPlanejamentoApi } from './producao/AbaPlanejamentoApi'
import { AbaPerdasApi } from './producao/AbaPerdasApi'
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
export function ModalGestao({ aoFechar, abaInicial = null, aoTrocarAba = null, focoInicial = null, janelasApi = null, avisosApi = null }) {
  const { fonteApi } = useAtendimento()
  const { permite } = useAcessoModulos()
  const areas = useMemo(() => MODULOS
    .filter((modulo) => permite(modulo.id))
    .map((modulo) => ({ ...modulo, telas: telasDoMenu(modulo, { fonteApi }).filter((tela) => tela.tipo === TELA_AJUSTE) }))
    .filter((modulo) => modulo.telas.length > 0), [fonteApi, permite])
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
                  <Icone nome={modulo.icone} tamanho={20} />
                  <span>{modulo.nome}</span>
                </button>
              ))}
            </div>
          </div>

          <div className={css.areaCompacta}>
            <label htmlFor={`${id}-escolher-area`}>Área da loja</label>
            <div className={css.seletorArea}>
              <Icone nome={area.icone} tamanho={20} />
              <select id={`${id}-escolher-area`} value={area.id} onChange={(evento) => escolherArea(areas.find((modulo) => modulo.id === evento.target.value))}>
                {areas.map((modulo) => <option key={modulo.id} value={modulo.id}>{modulo.nome}</option>)}
              </select>
            </div>
          </div>

          <section id={`${id}-area`} role="tabpanel" aria-label={area.nome} className={css.corpoGestao}>
            <header className={css.cabecalhoArea}>
              <h3 className={css.tituloArea}><Icone nome={area.icone} tamanho={24} />{area.nome}</h3>
              {area.telas.length > 1 ? (
                <div className={css.subtelasGestao} role="tablist" aria-label={`Telas de ${area.nome}`}>
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
                      onKeyDown={(evento) => navegarAbas(evento, area.telas, indice, escolherTela, botoesTela)}
                    >
                      {item.rotulo}
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
              <PainelDeAjuste aba={tela.aba} focoInicial={focoInicial} janelasApi={janelasApi} aoAbrirAba={abrirAba} />
            </div>
          </section>
        </div>
      ) : (
        <Vazio titulo="Nenhum ajuste disponível">Consulte as telas disponíveis em Ver módulos.</Vazio>
      )}
    </Modal>
  )
}

// Modo API (F06, decisão do Felipe em 30/09): as abas sem backend não somem. Abrem com a
// faixa e os controles desabilitados até a fatia de cada uma entrar. Nada é gravado: as
// ações delas também avisam (`aplicacao/api/naoLigadas.js`), e Integrações nunca guarda chave.
const AINDA_NAO_LIGADO = {
  producao: 'Produção e cardápio ainda não funcionam por esta tela. Nada aqui é gravado.',
  fidelidade: 'Fidelidade e cupons ainda não funcionam por esta tela. Nada aqui é gravado.',
  integracoes: 'As integrações ainda não funcionam por esta tela. Nenhuma chave é guardada no navegador.',
}

function AbaNaoLigada({ texto, children }) {
  return (
    <>
      <p className={css.naoLigado} role="note">{texto}</p>
      <fieldset disabled className={css.desligado}>{children}</fieldset>
    </>
  )
}

// Uma tela por aba: o objeto só decide qual elemento entra na árvore.
// `janelasApi` (#1440): no modo API o App entrega aqui o cadastro de janelas, zonas e bloqueios
// da S45 (o mesmo de Entregas › Janelas e frete), no lugar da aba de demonstração.
export function PainelDeAjuste({ aba, focoInicial = null, janelasApi = null, aoAbrirAba = null }) {
  const { fonteApi } = useAtendimento()
  const painelDaAba = useMemo(() => ({
    atendimento: <AbaAtendimento />,
    // #1441: respostas prontas e automáticas, fora do caminho do atendimento.
    respostas: <AbaRespostas focoInicial={focoInicial} />,
    producao: <AbaProducao />,
    // #1443: no modo API o caixa é o do EasyStok; a demonstração segue com a massa local.
    caixa: fonteApi ? <AbaCaixaApi /> : <AbaCaixa />,
    janelas: fonteApi && janelasApi ? janelasApi : <AbaJanelas />,
    fidelidade: <AbaFidelidade />,
    integracoes: <AbaIntegracoes />,
    canais: <TelaCanais />,
    // M1.1 (#1481): gestão do cardápio (só modo API).
    cardapio: <AbaCardapioApi />,
    // M1.3 (#1483): categorias do cardápio (só modo API).
    categorias: <AbaCategoriasApi />,
    // M2.1 (#1490): estoque do dia em porções (só modo API).
    'estoque-do-dia': <AbaEstoqueDoDiaApi />,
    // M2.2 (#1491): produção do dia por prato (só modo API).
    'producao-do-dia': <AbaProducaoDoDiaApi />,
    // M2.3 (#1496): insumos (só modo API).
    insumos: <AbaInsumosApi />,
    // M2.4a (#1498): receitas (só modo API).
    receitas: <AbaReceitasApi />,
    // M2.5 (#1502): planejamento da produção (só modo API).
    planejamento: <AbaPlanejamentoApi aoAbrirAba={aoAbrirAba} />,
    // M2.6 (#1511): controle de perdas (só modo API).
    perdas: <AbaPerdasApi />,
  }), [fonteApi, focoInicial, janelasApi, aoAbrirAba])
  const painel = painelDaAba[aba] ?? null
  if (fonteApi && AINDA_NAO_LIGADO[aba]) return <AbaNaoLigada texto={AINDA_NAO_LIGADO[aba]}>{painel}</AbaNaoLigada>
  return painel
}
