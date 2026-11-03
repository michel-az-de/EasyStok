import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { BotaoIcone } from '../../../componentes/BotaoIcone'
import { FolhaImpressao } from '../../../componentes/FolhaImpressao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useEstoqueDoDiaApi } from '../../../aplicacao/useEstoqueDoDiaApi'
import { contagemInicial, podeReimprimir, porcaoQueNaoRepete, porcaoSugerida, porcoesParaAjuste, vencidasNaContagem } from '../../../aplicacao/estoqueDoDia'
import { producaoDoDia } from '../../../aplicacao/producaoDoDia'
import { hashDoModulo } from '../../../dominio/modulos'
import css from '../cardapio/abaCardapio.module.css'
import { EtiquetaDeProducao } from './etiqueta/EtiquetaDeProducao'
import etq from './etiqueta/etiqueta.module.css'

// M2 › Estoque do dia (M2.1, #1490). O que a Thati usa todo dia, lido do EasyStok: os alertas de
// venda sem saldo (avisa, não trava; RN-48), o saldo em porções por prato e os lotes com o
// vencimento destacado (US-065). "Ajustar" é a contagem dela com o motivo e fecha o alerta.
// Em prato com porções (#1542) a contagem é de uma porção: o saldo é dela.
const porcoes = (n) => (n === 1 ? '1 porção' : `${n} porções`)
const saldoDaPorcao = (s) => `${s.rotulo}: ${s.saldo}${s.descoberto > 0 ? ` (${s.descoberto} sem saldo)` : ''}`
const dataCurta = (iso) => (iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : 'sem validade')

function quandoVence(lote) {
  if (!lote.validade) return 'Sem validade informada'
  if (lote.vencido) return 'Venceu'
  if (lote.diasParaVencer === 0) return 'Vence hoje'
  if (lote.diasParaVencer === 1) return 'Vence amanhã'
  return `Vence ${dataCurta(lote.validade)}`
}

function FormularioAjuste({ alvo, aoSalvar, aoCancelar, aviso, aoFecharAviso }) {
  const opcoes = porcoesParaAjuste(alvo)
  const porPorcao = opcoes.length > 0
  const [porcao, setPorcao] = useState(() => porcaoSugerida(opcoes))
  // O que está sendo contado: a porção escolhida ou, sem porções, o prato.
  const contado = porPorcao ? opcoes.find((s) => s.chave === porcao) ?? null : alvo
  const [contagem, setContagem] = useState(() => String(contagemInicial(contado)))
  // Depois que ela digita, o número é dela: escolher a porção em seguida não o troca pelo do sistema.
  const [digitada, setDigitada] = useState(false)
  const [motivo, setMotivo] = useState('')
  const [salvando, setSalvando] = useState(false)
  const vencidas = vencidasNaContagem(contado)

  const trocarPorcao = (evento) => {
    const chave = evento.target.value
    setPorcao(chave)
    if (!digitada) setContagem(String(contagemInicial(opcoes.find((s) => s.chave === chave) ?? null)))
  }

  const salvar = (evento) => {
    evento.preventDefault()
    if (salvando) return
    setSalvando(true)
    Promise.resolve(aoSalvar(contagem, motivo, { porcoes: opcoes, porcao }))
      .then((ok) => { if (ok !== false) aoCancelar() }).finally(() => setSalvando(false))
  }

  return (
    <form className={`${css.campos} ${css.formulario} ${css.ajusteEstoque}`} onSubmit={salvar}>
      {porPorcao && (
        <div className={css.linhaCompleta}>
          <CampoSelecao rotulo="O que você contou" value={porcao} disabled={salvando} onChange={trocarPorcao}
            dica="O saldo deste prato é de cada porção. Conte uma de cada vez."
            opcoes={[{ valor: '', rotulo: 'Escolha a porção que você contou' },
              ...opcoes.map((s) => ({ valor: s.chave, rotulo: `${s.rotulo} (tem ${s.fisico} guardadas)` }))]} />
        </div>
      )}
      <CampoTexto
        rotulo="Porções contadas"
        inputMode="numeric"
        aria-label={`Quantas porções de ${alvo.nome} você contou`}
        value={contagem}
        disabled={salvando}
        dica="Informe o total que você contou, não a diferença em relação ao saldo."
        onChange={(e) => { setDigitada(true); setContagem(e.target.value) }}
      />
      <CampoTexto
        rotulo="Motivo do ajuste"
        placeholder="Ex.: contei o congelador"
        maxLength={500}
        value={motivo}
        disabled={salvando}
        onChange={(e) => setMotivo(e.target.value)}
      />
      {vencidas > 0 && <p className={`${css.detalhe} ${css.linhaCompleta}`}>
        A contagem inclui {vencidas === 1 ? '1 vencida' : `${vencidas} vencidas`} que ainda {vencidas === 1 ? 'está guardada' : 'estão guardadas'}.
        {' '}Conte tudo o que está na prateleira; para tirar as vencidas, lance em <a className={css.link} href={hashDoModulo('producao', 'perdas')}>Perdas</a>.
      </p>}
      {aviso && <p className={`${css.aviso} ${css.linhaCompleta}`} role="alert">
        Não foi possível salvar a contagem. {aviso}
        {' '}<Botao variante="texto" onClick={aoFecharAviso}>Fechar aviso</Botao>
      </p>}
      <div className={css.rodapeForm}>
        <Botao variante="texto" disabled={salvando} onClick={aoCancelar}>Cancelar</Botao>
        <Botao tipo="submit" variante="primario" icone="check" disabled={salvando || !motivo.trim() || contagem === '' || (porPorcao && !contado)}>{salvando ? 'Salvando…' : 'Salvar contagem'}</Botao>
      </div>
    </form>
  )
}

export function AbaEstoqueDoDiaApi() {
  const estoque = useEstoqueDoDiaApi()
  const [ajustando, setAjustando] = useState(null)
  const [avisoDaContagem, setAvisoDaContagem] = useState(null)
  const avisoNoFormulario = ajustando !== null && ajustando === avisoDaContagem
  // #2081 (A8): reimprimir as etiquetas de um lote ativo. { loteId } buscando, com `folha` pronta, com `erro` falhou.
  const [impressao, setImpressao] = useState(null)
  const montada = useRef(true)
  useEffect(() => {
    montada.current = true
    return () => { montada.current = false }
  }, [])

  async function reimprimir(loteId) {
    setImpressao({ loteId })
    try {
      const folha = await producaoDoDia.folhaDeEtiquetas(loteId)
      if (!montada.current) return
      setImpressao({ loteId, folha })
      setTimeout(() => { if (montada.current) window.print() }, 50)
    } catch (e) {
      if (montada.current) setImpressao({ loteId, erro: e.message })
    }
  }

  const trocarAjuste = (alvo) => {
    setAvisoDaContagem(null)
    setAjustando(alvo)
  }
  const salvarContagem = (alvo, sku, contagem, motivo, porcao) => {
    estoque.fecharAviso()
    setAvisoDaContagem(alvo)
    return estoque.ajustar(sku, contagem, motivo, porcao)
  }

  if (estoque.estado === 'carregando') return <output className={css.descricao}>Carregando o estoque…</output>
  if (estoque.estado === 'erro' && estoque.pratos.length === 0) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não foi possível carregar o estoque"
        acao={<Botao icone="refresh-cw" onClick={estoque.recarregar}>Tentar de novo</Botao>}>
        {estoque.erro}
      </Vazio>
    )
  }

  // O alerta é do produto; o que se conta (porções, guardado, vencidas) vem do prato dele.
  const pratoDoAlerta = (a) => ({ saldo: 0, fisico: 0, porcoes: [], ...estoque.pratos.find((p) => p.sku === a.sku), nome: a.nome })
  const lotes = estoque.pratos
    .flatMap((p) => p.lotes.map((l) => ({ ...l, prato: p.nome })))
    .sort((a, b) => (a.validade ?? '9999').localeCompare(b.validade ?? '9999'))

  return (
    <div className={css.aba}>
      <dl className={css.resumoOperacao} aria-label="Resumo do estoque">
        <div><dt>Pratos no estoque</dt><dd>{estoque.pratos.length}</dd></div>
        <div><dt>Lotes com saldo</dt><dd>{lotes.length}</dd></div>
        <div><dt>Alertas de saldo</dt><dd>{estoque.alertas.length}</dd></div>
      </dl>
      {estoque.estado === 'erro' && <p className={css.aviso} role="alert">
        O estoque não foi atualizado. Os saldos abaixo podem estar desatualizados. {estoque.erro}
        {' '}<Botao variante="texto" icone="refresh-cw" onClick={estoque.recarregar}>Recarregar estoque</Botao>
      </p>}
      {estoque.aviso && !avisoNoFormulario && (
        <p className={css.aviso} role="alert">
          {estoque.aviso} <Botao variante="texto" onClick={estoque.fecharAviso}>Fechar</Botao>
        </p>
      )}

      <section className={css.secao} aria-labelledby="estoque-alertas">
        <h3 id="estoque-alertas" className={css.tituloSecao}>Venda sem saldo</h3>
        {estoque.alertas.length === 0 ? (
          <p className={css.descricao}>Nenhum prato vendido sem produção lançada.</p>
        ) : (
          <ul className={css.lista}>
            {estoque.alertas.map((a) => (
              <li key={a.produtoId} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{a.nome}</span>
                  <span className={css.detalhe}>{a.texto}</span>
                </div>
                {ajustando === a.produtoId ? (
                  <FormularioAjuste
                    alvo={pratoDoAlerta(a)}
                    aoSalvar={(contagem, motivo, porcao) => salvarContagem(a.produtoId, a.sku, contagem, motivo, porcao)}
                    aoCancelar={() => trocarAjuste(null)}
                    aviso={avisoNoFormulario ? estoque.aviso : null}
                    aoFecharAviso={estoque.fecharAviso}
                  />
                ) : (
                  <div className={css.acoes}>
                    <Botao variante="primario" icone="lapis" aria-label={`Ajustar contagem de ${a.nome}`} disabled={!a.sku} onClick={() => trocarAjuste(a.produtoId)}>Ajustar</Botao>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className={css.secao} aria-labelledby="estoque-saldo">
        <h3 id="estoque-saldo" className={css.tituloSecao}>Saldo por prato</h3>
        {estoque.pratos.length === 0 ? (
          <p className={css.descricao}>Nenhum prato com estoque cadastrado. Registre o primeiro lote em <a className={css.link} href={hashDoModulo('producao', 'producao')}>Produção do dia</a>.</p>
        ) : (
          <ul className={css.lista}>
            {estoque.pratos.map((p) => (
              <li key={p.sku} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{p.nome}</span>
                  {/* #2081 (A16): a porção só aparece quando o nome ainda não diz qual é. */}
                  {(p.porcoes.length > 0 || porcaoQueNaoRepete(p.nome, p.porcao)) && <span className={css.detalhe}>
                    {p.porcoes.length > 0 ? p.porcoes.map(saldoDaPorcao).join(' · ') : porcaoQueNaoRepete(p.nome, p.porcao)}
                  </span>}
                </div>
                <span className={css.preco}>{porcoes(p.saldo)}</span>
                <div className={css.estados}>
                  {p.saldo === 0 && <Pilula tom="perigo" fina>Sem saldo</Pilula>}
                  {p.descoberto > 0 && <Pilula tom="aviso" fina>{porcoes(p.descoberto)} vendidas sem saldo</Pilula>}
                  {p.lotes.some((l) => l.vencendo) && <Pilula tom="aviso" fina>Lote vencendo</Pilula>}
                </div>
                {ajustando === p.sku ? (
                  <FormularioAjuste
                    alvo={p}
                    aoSalvar={(contagem, motivo, porcao) => salvarContagem(p.sku, p.sku, contagem, motivo, porcao)}
                    aoCancelar={() => trocarAjuste(null)}
                    aviso={avisoNoFormulario ? estoque.aviso : null}
                    aoFecharAviso={estoque.fecharAviso}
                  />
                ) : (
                  <div className={css.acoes}>
                    <Botao variante="texto" icone="lapis" aria-label={`Ajustar contagem de ${p.nome}`} onClick={() => trocarAjuste(p.sku)}>Ajustar</Botao>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className={css.secao} aria-labelledby="estoque-lotes">
        <h3 id="estoque-lotes" className={css.tituloSecao}>Lotes</h3>
        {lotes.length === 0 ? (
          <p className={css.descricao}>Nenhum lote com saldo.</p>
        ) : (
          <ul className={css.lista}>
            {lotes.map((l, i) => (
              <li key={`${l.codigo}-${i}`} className={css.linha}>
                <div className={css.resumo}>
                  <span className={css.nome}>{l.prato}</span>
                  {/* #2081 (A10): de onde veio o lote, quem registrou e quando foi fabricado. */}
                  <span className={css.detalhe}>{[l.codigo ?? 'sem código', l.origem].filter(Boolean).join(' · ')}</span>
                </div>
                <span className={css.preco}>{porcoes(l.quantidade)}</span>
                <div className={css.estados}>
                  <Pilula tom={l.vencido ? 'perigo' : l.vencendo ? 'aviso' : 'neutro'} fina>{quandoVence(l)}</Pilula>
                </div>
                {podeReimprimir(l) && (
                  <div className={css.acoes}>
                    <BotaoIcone icone="printer" disabled={impressao !== null && !impressao.folha && !impressao.erro}
                      rotulo={`Etiquetas do lote ${l.codigo}`} dica="Etiquetas" onClick={() => reimprimir(l.id)} />
                  </div>
                )}
                {impressao?.loteId === l.id && impressao.erro && (
                  <p className={`${css.aviso} ${css.linhaCompleta}`} role="alert">Não foi possível carregar as etiquetas deste lote. {impressao.erro}</p>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {impressao?.folha && impressao.folha.etiquetas.length > 0 && (
        <FolhaImpressao>
          <div className={etq.folha}>
            {impressao.folha.etiquetas.map((e) => <EtiquetaDeProducao key={e.id} etiqueta={e} empresa={impressao.folha.empresa} total={impressao.folha.etiquetas.length} />)}
          </div>
        </FolhaImpressao>
      )}
    </div>
  )
}
