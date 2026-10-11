import { useEffect, useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Chip } from '../../../componentes/Chip'
import { FolhaImpressao } from '../../../componentes/FolhaImpressao'
import { Icone } from '../../../componentes/Icone'
import { useCatalogo } from '../../../aplicacao/contextos'
import { useEstoqueDoDiaApi } from '../../../aplicacao/useEstoqueDoDiaApi'
import { aoLancarLote, dispensarLote, guardarRascunho, linhasDoRascunho, lotesLancados, producaoDoDia } from '../../../aplicacao/producaoDoDia'
import { lerSugestao, retirarProducaoPlanejada } from '../../../aplicacao/planejamento'
import { estaRemovido } from '../../../dominio/cardapio'
import { plural } from '../../../dominio/formato'
import {
  VALIDADES_RAPIDAS, cartoesDaGrade, categoriasDaGrade, definirNoLote, faltaProduzir, filtrarGrade, nomeDoCartao,
  porcoesNoLote, resumoDoLote, somarNoLote, tamanhoNoCartao,
} from '../../../dominio/gradeDeProducao'
import { hashDoModulo } from '../../../dominio/modulos'
import { sobraDaLinha } from '../../../dominio/producao'
import { linhaComPorcao } from '../../../dominio/porcoes'
import css from '../cardapio/abaCardapio.module.css'
import grade from './producaoDoDia.module.css'

// M2 › Produção do dia (M2.2, #1491). #1624: a dona produz como no PWA. Toca no prato para somar uma
// porção, vê o saldo no cartão e o que falta para os pedidos, confere validade e peso no lote e
// confirma. Confirmar cria o lote no EasyStok (etiquetas e entrada em porções); prato que ainda não
// tinha estoque passa a ter. Depois, "Imprimir etiquetas" imprime uma por porção (D-M2-04).
const VALIDADE_PADRAO = 5
const ID_DO_LOTE = 'lote-da-producao'
const novaLinha = () => ({ id: crypto.randomUUID(), sku: '', variacaoId: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: String(VALIDADE_PADRAO) })
const inteiro = (texto) => (String(texto).trim() === '' ? null : Number(texto))
const dataCurta = (iso) => (iso ? new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' }) : '')
const diaCurto = (iso) => (iso ? iso.split('-').reverse().slice(0, 2).join('/') : 'amanhã')

// M1.4c (#1537): prato com porções exige a porção produzida (o saldo é dela).
function paraEnviar(linha, pratos = []) {
  const prato = pratos.find((p) => p.sku === linha.sku)
  const porcoesDoPrato = prato?.porcoes ?? []
  return {
    sku: linha.sku,
    // #1585: só para o erro dizer de qual prato é; não vai no corpo.
    nome: prato?.nome ?? null,
    variacaoId: linha.variacaoId || null,
    exigePorcao: porcoesDoPrato.length > 0,
    porcoes: inteiro(linha.porcoes),
    pesoPorPorcaoG: inteiro(linha.pesoPorPorcaoG),
    pesoRealG: inteiro(linha.pesoRealG),
    validadeDias: inteiro(linha.validadeDias),
  }
}

export function AbaProducaoDoDiaApi() {
  const { cardapio } = useCatalogo()
  const pratos = useMemo(() => (cardapio ?? []).filter((i) => !estaRemovido(i)), [cardapio])
  // O saldo de cada cartão. Sem ele (API fora, prato sem estoque ligado) a grade continua servindo.
  const estoque = useEstoqueDoDiaApi()
  const recarregarEstoque = estoque.recarregar
  // M2.5 (#1502): vindo do Planejamento, as porções planejadas já entram no lote.
  // #1585: a tela desmonta a cada troca de rota; ela abre com o que ficou guardado.
  const [linhas, setLinhas] = useState(() => {
    const guardadas = linhasDoRascunho(retirarProducaoPlanejada())
    return guardadas ? guardadas.map((l) => ({ ...novaLinha(), ...l })) : []
  })
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState(null)
  const [lotes, setLotes] = useState(lotesLancados)
  // O que está na folha, de qual lote: { loteId } buscando, com `etiquetas` pronta, com `erro` falhou.
  const [impressao, setImpressao] = useState(null)
  // "Falta produzir": a sugestão do planejamento. Sem permissão ou sem rede, o bloco não aparece.
  const [sugestao, setSugestao] = useState(null)
  const [leitura, setLeitura] = useState(0)
  const [categoria, setCategoria] = useState(null)
  const [busca, setBusca] = useState('')

  useEffect(() => { guardarRascunho(linhas) }, [linhas])
  // O lote gravado entra na lista e as linhas dele saem, mesmo quando o envio partiu antes de ela
  // sair da tela e voltar. A folha do lote anterior sai junto: não imprime etiqueta de outro lote.
  // O saldo dos cartões e o que falta produzir mudaram: os dois são relidos.
  useEffect(() => aoLancarLote(() => {
    setLotes(lotesLancados())
    setLinhas([])
    setImpressao(null)
    setLeitura((n) => n + 1)
    recarregarEstoque()
    globalThis.document?.getElementById(ID_DO_LOTE)?.scrollIntoView({ block: 'nearest' })
  }), [recarregarEstoque])
  useEffect(() => {
    let vivo = true
    lerSugestao().then((r) => { if (vivo) setSugestao(r) }).catch(() => { if (vivo) setSugestao(null) })
    return () => { vivo = false }
  }, [leitura])

  const cartoes = useMemo(() => cartoesDaGrade(pratos, estoque.pratos), [pratos, estoque.pratos])
  const categorias = categoriasDaGrade(cartoes)
  const visiveis = filtrarGrade(cartoes, { categoria, busca })
  const falta = faltaProduzir(sugestao?.pratos, cartoes)
  const resumo = resumoDoLote(linhas)
  const apoio = { pratos, novaLinha }

  const somar = (cartao, delta) => setLinhas((atual) => somarNoLote(atual, cartao, delta, apoio))
  // Um toque no que falta: prato de um tamanho só entra no lote com a quantidade (nunca abaixo do
  // que ela já pôs); prato com porções mostra os tamanhos dele na grade.
  const atender = (item) => {
    if (item.cartao) {
      setLinhas((atual) => definirNoLote(atual, item.cartao, Math.max(item.quantidade, porcoesNoLote(atual, item.cartao)), apoio))
      return
    }
    setCategoria(null)
    setBusca(item.nome)
  }
  const mudar = (id, campo) => (e) => setLinhas((atual) => atual.map((l) => (l.id === id ? { ...l, [campo]: e.target.value } : l)))
  const definirValidade = (id, dias) => setLinhas((atual) => atual.map((l) => (l.id === id ? { ...l, validadeDias: String(dias) } : l)))
  // M1.4e (#1552): escolher a porção traz o peso dela, que é o que faz a receita em peso baixar o
  // insumo certo; o que ela digitou fica.
  const escolherPorcao = (id) => (e) => setLinhas((atual) => atual.map((l) => (l.id === id ? linhaComPorcao(l, pratos, e.target.value) : l)))
  const tirar = (id) => setLinhas((atual) => atual.filter((l) => l.id !== id))

  async function confirmar(evento) {
    evento.preventDefault()
    if (enviando) return
    const formulario = evento.currentTarget
    setEnviando(true)
    setErro(null)
    // Gravou: quem mostra o lote e zera as linhas é o `aoLancarLote` acima.
    const r = await producaoDoDia.produzir(linhas.map((l) => paraEnviar(l, pratos)))
    setEnviando(false)
    if (r.ok) return
    setErro(r.erro)
    // Em tela estreita o lote fica abaixo da grade: o aviso tem de aparecer para quem tocou na barra.
    formulario?.scrollIntoView?.({ block: 'nearest' })
  }

  // #1585: cada lote imprime as próprias etiquetas. A folha esvazia antes de buscar: se a busca
  // falhar, não sobra a do lote anterior, e o erro aparece junto do botão do lote.
  async function imprimir(loteId) {
    setImpressao({ loteId })
    try {
      const etiquetas = await producaoDoDia.etiquetas(loteId)
      setImpressao({ loteId, etiquetas })
      setTimeout(() => window.print(), 50)
    } catch (e) {
      setImpressao({ loteId, erro: e.message })
    }
  }

  function dispensar(loteId) {
    setLotes(dispensarLote(loteId))
    if (impressao?.loteId === loteId) setImpressao(null)
  }

  const buscandoEtiquetas = impressao !== null && !impressao.etiquetas && !impressao.erro

  return (
    <div className={css.aba}>
      <p className={css.descricao}>
        Toque no prato que saiu da cozinha: cada toque soma uma porção. Confirmar cria o lote com as etiquetas e soma no estoque.
      </p>

      {falta.length > 0 && (
        <section className={grade.falta} aria-label="Falta produzir">
          <div>
            <h3 className={css.tituloSecao}>Falta produzir</h3>
            <p className={css.detalhe}>
              Para os pedidos até {diaCurto(sugestao?.ate)}, o mínimo e o que saiu sem saldo. Toque para pôr no lote.
              {' '}<a className={css.link} href={hashDoModulo('producao', 'planejamento')}>Ver a conta no Planejamento</a>
            </p>
          </div>
          <div className={grade.chips}>
            {falta.map((item) => (
              <Chip key={item.sku} papel="acao" disabled={enviando}
                icone={item.cartao && porcoesNoLote(linhas, item.cartao) >= item.quantidade ? 'check' : undefined}
                onClick={() => atender(item)}>
                {item.quantidade}× {item.nome}
              </Chip>
            ))}
          </div>
        </section>
      )}

      <div className={grade.palco}>
        <div className={grade.bancada}>
          <section className={grade.cardapio} aria-label="Pratos para produzir">
            <div className={grade.filtros}>
              {categorias.length > 1 && (
                <div className={grade.chips}>
                  <Chip papel="filtro" ativo={!categoria} onClick={() => setCategoria(null)}>Todos</Chip>
                  {categorias.map((c) => (
                    <Chip key={c} papel="filtro" ativo={categoria === c} onClick={() => setCategoria(categoria === c ? null : c)}>{c}</Chip>
                  ))}
                </div>
              )}
              <div className={grade.busca}>
                <CampoTexto rotulo="Buscar prato" rotuloOculto tipo="search" placeholder="Buscar prato" value={busca} onChange={(e) => setBusca(e.target.value)} />
              </div>
            </div>
            {visiveis.length === 0 ? (
              <p className={css.descricao}>
                {cartoes.length === 0 ? 'O cardápio ainda não tem pratos.' : 'Nenhum prato com esse nome.'}
                {cartoes.length > 0 && <>{' '}<Botao variante="texto" onClick={() => { setBusca(''); setCategoria(null) }}>Ver todos os pratos</Botao></>}
              </p>
            ) : (
              <ul className={grade.grade}>
                {visiveis.map((cartao) => {
                  const noLote = porcoesNoLote(linhas, cartao)
                  const nome = nomeDoCartao(cartao)
                  return (
                    <li key={cartao.chave} data-prato={cartao.chave} className={`${grade.cartao} ${noLote > 0 ? grade.noLote : ''}`}>
                      <button type="button" className={grade.tocar} disabled={enviando} aria-label={`Somar 1 porção de ${nome}`} onClick={() => somar(cartao, 1)}>
                        <span className={grade.nomeDoPrato}>{cartao.nome}</span>
                        {tamanhoNoCartao(cartao) && <span className={grade.tamanho}>{cartao.tamanho}</span>}
                        {cartao.saldo != null && (
                          <span className={grade.saldo}>
                            {cartao.saldo} no estoque{noLote > 0 && <strong> · {cartao.saldo + noLote} depois</strong>}
                          </span>
                        )}
                      </button>
                      {noLote > 0 && (
                        <div className={grade.passo}>
                          <span className={grade.contagem} aria-hidden="true">{noLote}</span>
                          <button type="button" className={grade.menos} disabled={enviando} aria-label={`Tirar 1 porção de ${nome}`} onClick={() => somar(cartao, -1)}>
                            <Icone nome="menos" tamanho={20} />
                          </button>
                        </div>
                      )}
                    </li>
                  )
                })}
              </ul>
            )}
          </section>

          <div className={grade.colunaDoLote}>
            {/* #1585: um aviso por lote lançado na visita, o mais novo primeiro, até ela dispensar. */}
            {lotes.length > 0 && (
              <section className={css.secao} aria-label="Lotes lançados nesta visita">
                {lotes.map((lote) => (
                  <output key={lote.loteId} className={css.aviso}>
                    <strong>Lote {lote.codigo}</strong> lançado: {lote.pratos.map((p) => `${p.porcoes}× ${p.nome}${p.sobraG ? ` (sobra ${p.sobraG} g)` : ''}`).join(', ')}.
                    <span className={grade.acoesDoAviso}>
                      <Botao variante="primario" icone="printer" disabled={buscandoEtiquetas} aria-label={`Imprimir ${lote.etiquetas} ${lote.etiquetas === 1 ? 'etiqueta' : 'etiquetas'} do lote ${lote.codigo}`} onClick={() => imprimir(lote.loteId)}>Imprimir {lote.etiquetas} {lote.etiquetas === 1 ? 'etiqueta' : 'etiquetas'}</Botao>
                      <Botao variante="texto" disabled={buscandoEtiquetas} aria-label={`Dispensar o aviso do lote ${lote.codigo}`} onClick={() => dispensar(lote.loteId)}>Dispensar</Botao>
                    </span>
                    {impressao?.loteId === lote.loteId && impressao.erro && <>{' '}<strong>Não consegui buscar as etiquetas deste lote. {impressao.erro}</strong></>}
                    {lote.avisos.length > 0 && (
                      <ul aria-label="Avisos de insumo">
                        {lote.avisos.map((a) => <li key={a}>{a}</li>)}
                      </ul>
                    )}
                  </output>
                ))}
              </section>
            )}

            <form id={ID_DO_LOTE} className={grade.lote} aria-label="Lote de hoje" onSubmit={confirmar}>
              <div>
                <h3 className={css.tituloSecao}>Lote de hoje</h3>
                <p className={css.detalhe}>
                  {linhas.length === 0 ? 'Os pratos que você tocar aparecem aqui.' : 'Confira a validade e o peso antes de confirmar.'}
                </p>
              </div>
              {erro && <p className={css.aviso} role="alert">{erro}</p>}
              {linhas.length > 0 && (
                <ol className={grade.itens}>
                  {linhas.map((l, indice) => {
                    const prato = pratos.find((p) => p.sku === l.sku)
                    const porcoesDoPrato = prato?.porcoes ?? []
                    const tamanho = porcoesDoPrato.length > 0 ? porcoesDoPrato.find((v) => v.id === l.variacaoId)?.rotulo : prato?.porcao
                    const nome = nomeDoCartao({ nome: prato?.nome ?? 'Prato fora do cardápio', tamanho: tamanho ?? '' })
                    const sobra = sobraDaLinha(paraEnviar(l))
                    return (
                      <li key={l.id} className={grade.itemDoLote}>
                        <div className={grade.topoDoItem}>
                          <span className={css.numeroPrato}>{String(indice + 1).padStart(2, '0')}</span>
                          <h4 className={grade.nomeDoItem}>{nome}</h4>
                          <Botao className={css.acaoDiscreta} variante="texto" icone="lixeira" aria-label={`Tirar ${nome} do lote`} disabled={enviando} onClick={() => tirar(l.id)} />
                        </div>
                        {porcoesDoPrato.length > 0 && (
                          <CampoSelecao disabled={enviando} rotulo="Porção" value={l.variacaoId} onChange={escolherPorcao(l.id)}
                            opcoes={[{ valor: '', rotulo: 'Escolha a porção' }, ...porcoesDoPrato.map((v) => ({ valor: v.id, rotulo: v.rotulo }))]} />
                        )}
                        <div className={grade.camposDoItem}>
                          <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Porções" value={l.porcoes} onChange={mudar(l.id, 'porcoes')} />
                          <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Validade (dias)" value={l.validadeDias} onChange={mudar(l.id, 'validadeDias')} />
                        </div>
                        <div className={grade.chips} role="radiogroup" aria-label={`Validade de ${nome}`}>
                          {VALIDADES_RAPIDAS.map((dias) => (
                            <Chip key={dias} papel="escolha" disabled={enviando} ativo={String(l.validadeDias).trim() === String(dias)} onClick={() => definirValidade(l.id, dias)}>
                              {dias} dias
                            </Chip>
                          ))}
                        </div>
                        <div className={grade.camposDoItem}>
                          <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Peso de cada porção (g)" placeholder="g por porção" value={l.pesoPorPorcaoG} onChange={mudar(l.id, 'pesoPorPorcaoG')} />
                          <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Peso real total (g)" placeholder="Opcional" value={l.pesoRealG} onChange={mudar(l.id, 'pesoRealG')} />
                        </div>
                        {sobra != null && <p className={css.detalhe}>{sobra < 0 ? 'Peso real menor que as porções' : `Sobra ${sobra} g`}</p>}
                      </li>
                    )
                  })}
                </ol>
              )}
            </form>
          </div>
        </div>

        <div className={grade.barraDoLote}>
          <p className={grade.resumoDoLote} aria-live="polite">
            {resumo.pratos === 0 ? 'Nenhum prato no lote' : `${plural(resumo.porcoes, 'porção', 'porções')} · ${plural(resumo.pratos, 'prato', 'pratos')}`}
          </p>
          <div className={grade.acoesDoLote}>
            <Botao variante="texto" disabled={enviando || linhas.length === 0} onClick={() => { setLinhas([]); setErro(null) }}>Limpar</Botao>
            <Botao tipo="submit" form={ID_DO_LOTE} variante="primario" icone="check" disabled={enviando || linhas.length === 0}>{enviando ? 'Lançando…' : 'Confirmar produção'}</Botao>
          </div>
        </div>
      </div>

      {impressao?.etiquetas && (
        <FolhaImpressao>
          {impressao.etiquetas.map((e) => (
            <div key={e.id} style={{ breakInside: 'avoid', border: '1px solid var(--impressao-tinta)', padding: '4mm', marginBottom: '3mm', color: 'var(--impressao-tinta)' }}>
              <strong>{e.nome}</strong>{e.pesoG ? ` · ${e.pesoG} g` : ''}
              <div>Lote {e.lote} · {e.sequencial}/{impressao.etiquetas.length}</div>
              <div>Produzido {dataCurta(e.produzidoEm)} · Validade {dataCurta(e.validadeEm)}</div>
              {e.alergenos.length > 0 && <div>Alérgenos: {e.alergenos.join(', ')}</div>}
              <div>{e.codigo}</div>
            </div>
          ))}
        </FolhaImpressao>
      )}
    </div>
  )
}
