import { useEffect, useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { FolhaImpressao } from '../../../componentes/FolhaImpressao'
import { useCatalogo } from '../../../aplicacao/contextos'
import { aoLancarLote, dispensarLote, guardarRascunho, linhasDoRascunho, lotesLancados, producaoDoDia } from '../../../aplicacao/producaoDoDia'
import { retirarProducaoPlanejada } from '../../../aplicacao/planejamento'
import { estaRemovido } from '../../../dominio/cardapio'
import { sobraDaLinha } from '../../../dominio/producao'
import { linhaComPorcao, linhaComPrato } from '../../../dominio/porcoes'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Produção do dia (M2.2, #1491). Ela escolhe o prato do cardápio e anota o que saiu: porções,
// peso de cada porção, peso real e validade. Confirmar cria o lote no EasyStok (etiquetas e
// entrada em porções); prato que ainda não tinha estoque passa a ter. Depois, "Imprimir etiquetas"
// imprime uma por porção. O destino vem da linha do prato (D-M2-04).
const VALIDADE_PADRAO = 5
const novaLinha = () => ({ id: crypto.randomUUID(), sku: '', variacaoId: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: String(VALIDADE_PADRAO) })
const inteiro = (texto) => (String(texto).trim() === '' ? null : Number(texto))
const dataCurta = (iso) => (iso ? new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' }) : '')

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
  // M2.5 (#1502): vindo do Planejamento, as porções planejadas já entram como linhas.
  // #1585: a tela desmonta a cada troca de rota; ela abre com o que ficou guardado.
  const [linhas, setLinhas] = useState(() => {
    const guardadas = linhasDoRascunho(retirarProducaoPlanejada())
    return guardadas ? guardadas.map((l) => ({ ...novaLinha(), ...l })) : [novaLinha()]
  })
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState(null)
  const [lotes, setLotes] = useState(lotesLancados)
  // O que está na folha, de qual lote: { loteId } buscando, com `etiquetas` pronta, com `erro` falhou.
  const [impressao, setImpressao] = useState(null)

  useEffect(() => { guardarRascunho(linhas) }, [linhas])
  // O lote gravado entra na lista e as linhas dele saem, mesmo quando o envio partiu antes de ela
  // sair da tela e voltar. A folha do lote anterior sai junto: não imprime etiqueta de outro lote.
  useEffect(() => aoLancarLote(() => {
    setLotes(lotesLancados())
    setLinhas([novaLinha()])
    setImpressao(null)
  }), [])

  const mudar = (id, campo) => (e) => setLinhas((atual) => atual.map((l) => (l.id === id ? { ...l, [campo]: e.target.value } : l)))
  // Trocar o prato zera a porção e o peso que veio dela. M1.4e (#1552): escolher a porção traz o peso
  // dela, que é o que faz a receita em peso baixar o insumo certo; o que ela digitou fica.
  const escolherPrato = (id) => (e) => setLinhas((atual) => atual.map((l) => (l.id === id ? linhaComPrato(l, pratos, e.target.value) : l)))
  const escolherPorcao = (id) => (e) => setLinhas((atual) => atual.map((l) => (l.id === id ? linhaComPorcao(l, pratos, e.target.value) : l)))
  const tirar = (id) => setLinhas((atual) => (atual.length > 1 ? atual.filter((l) => l.id !== id) : atual))

  async function confirmar(evento) {
    evento.preventDefault()
    if (enviando) return
    setEnviando(true)
    setErro(null)
    // Gravou: quem mostra o lote e zera as linhas é o `aoLancarLote` acima.
    const r = await producaoDoDia.produzir(linhas.map((l) => paraEnviar(l, pratos)))
    setEnviando(false)
    if (!r.ok) setErro(r.erro)
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
        Anote o que saiu da cozinha. Confirmar cria o lote com as etiquetas e soma no estoque.
      </p>

      {/* #1585: um aviso por lote lançado na visita, o mais novo primeiro, até ela dispensar. */}
      {lotes.length > 0 && (
        <section className={css.secao} aria-label="Lotes lançados nesta visita">
          {lotes.map((lote) => (
            <output key={lote.loteId} className={css.aviso}>
              <strong>Lote {lote.codigo}</strong> lançado: {lote.pratos.map((p) => `${p.porcoes}× ${p.nome}${p.sobraG ? ` (sobra ${p.sobraG} g)` : ''}`).join(', ')}.
              {' '}<Botao variante="primario" icone="printer" disabled={buscandoEtiquetas} aria-label={`Imprimir ${lote.etiquetas} ${lote.etiquetas === 1 ? 'etiqueta' : 'etiquetas'} do lote ${lote.codigo}`} onClick={() => imprimir(lote.loteId)}>Imprimir {lote.etiquetas} {lote.etiquetas === 1 ? 'etiqueta' : 'etiquetas'}</Botao>
              {' '}<Botao variante="texto" disabled={buscandoEtiquetas} aria-label={`Dispensar o aviso do lote ${lote.codigo}`} onClick={() => dispensar(lote.loteId)}>Dispensar</Botao>
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

      <form onSubmit={confirmar} className={css.aba}>
        <ol className={css.listaFormularios}>
          {linhas.map((l, indice) => {
            const sobra = sobraDaLinha(paraEnviar(l))
            return (
              <li key={l.id} className={`${css.campos} ${css.formulario} ${css.loteProducao}`}>
                <div className={css.cabecalhoFormulario}>
                  <span className={css.numeroPrato}>{String(indice + 1).padStart(2, '0')}</span>
                  <div>
                    <h3 className={css.tituloFormulario}>{pratos.find((p) => p.sku === l.sku)?.nome ?? 'Escolha o prato produzido'}</h3>
                    <p className={css.detalhe}>Quantidade, peso e validade deste lote.</p>
                  </div>
                </div>
                <div className={css.linhaCompleta}>
                  <CampoSelecao disabled={enviando} rotulo="Prato" value={l.sku} onChange={escolherPrato(l.id)}
                    opcoes={[{ valor: '', rotulo: 'Escolha o prato' }, ...pratos.map((p) => ({ valor: p.sku, rotulo: `${p.nome}${p.porcao ? ` · ${p.porcao}` : ''}` }))]} />
                </div>
                {(pratos.find((p) => p.sku === l.sku)?.porcoes ?? []).length > 0 && (
                  <div className={css.linhaCompleta}>
                    <CampoSelecao disabled={enviando} rotulo="Porção" value={l.variacaoId} onChange={escolherPorcao(l.id)}
                      opcoes={[{ valor: '', rotulo: 'Escolha a porção' }, ...pratos.find((p) => p.sku === l.sku).porcoes.map((v) => ({ valor: v.id, rotulo: v.rotulo }))]} />
                  </div>
                )}
                <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Porções" placeholder="Porções" value={l.porcoes} onChange={mudar(l.id, 'porcoes')} />
                <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Peso de cada porção (g)" placeholder="g por porção" value={l.pesoPorPorcaoG} onChange={mudar(l.id, 'pesoPorPorcaoG')} />
                <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Peso real total (g)" placeholder="Peso real (g)" value={l.pesoRealG} onChange={mudar(l.id, 'pesoRealG')} />
                <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Validade (dias)" placeholder="Validade (dias)" value={l.validadeDias} onChange={mudar(l.id, 'validadeDias')} />
                <div className={css.rodapeForm}>
                  <span className={css.detalhe}>{sobra == null ? 'O saldo será atualizado ao confirmar.' : sobra < 0 ? 'Peso real menor que as porções' : `Sobra ${sobra} g`}</span>
                  <Botao className={css.acaoDiscreta} variante="texto" icone="lixeira" aria-label={`Remover prato ${indice + 1} da produção`} disabled={enviando || linhas.length === 1} onClick={() => tirar(l.id)}>Remover prato</Botao>
                </div>
              </li>
            )
          })}
        </ol>
        <div className={css.acoesFormulario}>
          <Botao icone="plus" disabled={enviando} onClick={() => setLinhas((atual) => [...atual, novaLinha()])}>Mais um prato</Botao>
          <Botao tipo="submit" variante="primario" icone="check" disabled={enviando}>{enviando ? 'Lançando…' : 'Confirmar produção'}</Botao>
        </div>
        {erro && <p className={css.aviso} role="alert">{erro}</p>}
      </form>

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
