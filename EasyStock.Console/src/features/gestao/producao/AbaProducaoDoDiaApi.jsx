import { useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { FolhaImpressao } from '../../../componentes/FolhaImpressao'
import { useCatalogo } from '../../../aplicacao/contextos'
import { criarProducaoDoDia } from '../../../aplicacao/producaoDoDia'
import { estaRemovido } from '../../../dominio/cardapio'
import { sobraDaLinha } from '../../../dominio/producao'
import css from '../cardapio/abaCardapio.module.css'

// M2 › Produção do dia (M2.2, #1491). Ela escolhe o prato do cardápio e anota o que saiu: porções,
// peso de cada porção, peso real e validade. Confirmar cria o lote no EasyStok (etiquetas e
// entrada em porções); prato que ainda não tinha estoque passa a ter. Depois, "Imprimir etiquetas"
// imprime uma por porção. O destino vem da linha do prato (D-M2-04).
const VALIDADE_PADRAO = 5
const novaLinha = () => ({ id: crypto.randomUUID(), sku: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: String(VALIDADE_PADRAO) })
const inteiro = (texto) => (String(texto).trim() === '' ? null : Number(texto))
const dataCurta = (iso) => (iso ? new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' }) : '')

function paraEnviar(linha) {
  return {
    sku: linha.sku,
    porcoes: inteiro(linha.porcoes),
    pesoPorPorcaoG: inteiro(linha.pesoPorPorcaoG),
    pesoRealG: inteiro(linha.pesoRealG),
    validadeDias: inteiro(linha.validadeDias),
  }
}

export function AbaProducaoDoDiaApi() {
  const { cardapio } = useCatalogo()
  const producao = useMemo(() => criarProducaoDoDia(), [])
  const pratos = useMemo(() => (cardapio ?? []).filter((i) => !estaRemovido(i)), [cardapio])
  const [linhas, setLinhas] = useState(() => [novaLinha()])
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState(null)
  const [feito, setFeito] = useState(null)
  const [etiquetas, setEtiquetas] = useState(null)

  const mudar = (id, campo) => (e) => setLinhas((atual) => atual.map((l) => (l.id === id ? { ...l, [campo]: e.target.value } : l)))
  const tirar = (id) => setLinhas((atual) => (atual.length > 1 ? atual.filter((l) => l.id !== id) : atual))

  async function confirmar(evento) {
    evento.preventDefault()
    setEnviando(true)
    setErro(null)
    const r = await producao.produzir(linhas.map(paraEnviar))
    setEnviando(false)
    if (!r.ok) { setErro(r.erro); return }
    setFeito(r)
    setLinhas([novaLinha()])
  }

  async function imprimir() {
    try {
      const lista = await producao.etiquetas(feito.loteId)
      setEtiquetas(lista)
      setTimeout(() => window.print(), 50)
    } catch (e) {
      setErro(`Etiquetas: ${e.message}`)
    }
  }

  return (
    <div className={css.aba}>
      <p className={css.descricao}>
        Anote o que saiu da cozinha. Cada linha é um prato: porções, peso de cada porção, o peso real que
        você pesou e a validade. Confirmar cria o lote com as etiquetas e soma no estoque.
      </p>

      {feito && (
        <output className={css.aviso}>
          <strong>Lote {feito.codigo}</strong> lançado: {feito.pratos.map((p) => `${p.porcoes}× ${p.nome}${p.sobraG ? ` (sobra ${p.sobraG} g)` : ''}`).join(', ')}.
          {' '}<Botao variante="primario" onClick={imprimir}>Imprimir {feito.etiquetas} etiqueta(s)</Botao>
          {' '}<Botao variante="texto" onClick={() => { setFeito(null); setEtiquetas(null) }}>Fechar</Botao>
          {feito.avisos.length > 0 && (
            <ul aria-label="Avisos de insumo">
              {feito.avisos.map((a) => <li key={a}>{a}</li>)}
            </ul>
          )}
        </output>
      )}

      <form onSubmit={confirmar} className={css.aba}>
        <ol className={css.lista}>
          {linhas.map((l) => {
            const sobra = sobraDaLinha(paraEnviar(l))
            return (
              <li key={l.id} className={css.linha}>
                <select className={css.busca} aria-label="Prato" value={l.sku} onChange={mudar(l.id, 'sku')}>
                  <option value="">Escolha o prato</option>
                  {pratos.map((p) => <option key={p.sku} value={p.sku}>{p.nome}{p.porcao ? ` · ${p.porcao}` : ''}</option>)}
                </select>
                <input className={css.busca} inputMode="numeric" aria-label="Porções" placeholder="Porções" value={l.porcoes} onChange={mudar(l.id, 'porcoes')} />
                <input className={css.busca} inputMode="numeric" aria-label="Peso de cada porção (g)" placeholder="g por porção" value={l.pesoPorPorcaoG} onChange={mudar(l.id, 'pesoPorPorcaoG')} />
                <input className={css.busca} inputMode="numeric" aria-label="Peso real total (g)" placeholder="Peso real (g)" value={l.pesoRealG} onChange={mudar(l.id, 'pesoRealG')} />
                <input className={css.busca} inputMode="numeric" aria-label="Validade (dias)" placeholder="Validade (dias)" value={l.validadeDias} onChange={mudar(l.id, 'validadeDias')} />
                <span className={css.detalhe}>{sobra == null ? '' : sobra < 0 ? 'peso real menor que as porções' : `sobra ${sobra} g`}</span>
                <Botao variante="texto" disabled={linhas.length === 1} onClick={() => tirar(l.id)}>Tirar</Botao>
              </li>
            )
          })}
        </ol>
        <div className={css.topo}>
          <Botao onClick={() => setLinhas((atual) => [...atual, novaLinha()])}>Mais um prato</Botao>
          <Botao tipo="submit" variante="primario" disabled={enviando}>{enviando ? 'Lançando…' : 'Confirmar produção'}</Botao>
        </div>
        {erro && <p className={css.aviso} role="alert">{erro}</p>}
      </form>

      {etiquetas && (
        <FolhaImpressao>
          {etiquetas.map((e) => (
            <div key={e.id} style={{ breakInside: 'avoid', border: '1px solid #000', padding: '4mm', marginBottom: '3mm', color: '#000' }}>
              <strong>{e.nome}</strong>{e.pesoG ? ` · ${e.pesoG} g` : ''}
              <div>Lote {e.lote} · {e.sequencial}/{etiquetas.length}</div>
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
