import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import { useEstoqueDoDiaApi } from '../../../aplicacao/useEstoqueDoDiaApi'
import { contagemInicial, detalheDoLote, podeRegistrarPerda, podeReimprimir, motivoDaContagem, porcaoQueNaoRepete, porcaoSugerida, porcoesParaAjuste, resumoDoEstoque, situacaoDoPrato, validadeDoLote, vencidasNaContagem } from '../../../aplicacao/estoqueDoDia'
import { producaoDoDia } from '../../../aplicacao/producaoDoDia'
import { hashDoModulo } from '../../../dominio/modulos'
import css from '../cardapio/abaCardapio.module.css'
import { PreviaDasEtiquetas } from './etiqueta/PreviaDasEtiquetas'
import tabela from './tabelaProducao.module.css'

// M2 › Estoque do dia (M2.1, #1490). O que a Thati usa todo dia, lido do EasyStok: os alertas de
// venda sem saldo (avisa, não trava; RN-48), o saldo em porções por prato e os lotes com o
// vencimento destacado (US-065). "Ajustar" é a contagem dela com o motivo e fecha o alerta.
// Em prato com porções (#1542) a contagem é de uma porção: o saldo é dela.
const porcoes = (n) => (n === 1 ? '1 porção' : `${Number(n).toLocaleString('pt-BR')} porções`)
// #2439: o tom da situação vira só a cor do texto, no número do topo e na tabela.
const TOM = { perigo: tabela.tomPerigo, atencao: tabela.tomAtencao, ok: tabela.tomOk, neutro: tabela.tomNeutro }
const saldoDaPorcao = (s) => `${s.rotulo}: ${s.saldo}${s.descoberto > 0 ? ` (${s.descoberto} sem saldo)` : ''}`

// #2235 (banca T1-1): o número que pede ação ganha a cor do estado, com o texto dizendo o porquê.
function NumeroDoResumo({ rotulo, valor, tom, dica }) {
  return (
    <div className={tom ? TOM[tom] : undefined}>
      <dt>{rotulo}</dt>
      <dd>{valor}</dd>
      {dica && <dd className={tabela.dica}>{dica}</dd>}
    </div>
  )
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
  // #2258 (auditoria P6): parado, o botão diz o que falta em vez de ficar apagado sem explicação.
  const parado = motivoDaContagem({ salvando, contagem, motivo, porPorcao, contado: Boolean(contado) })

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
        <Botao tipo="submit" variante="primario" icone="check" disabled={salvando || Boolean(parado)} motivo={parado ?? undefined}>{salvando ? 'Salvando…' : 'Salvar contagem'}</Botao>
      </div>
    </form>
  )
}

export function AbaEstoqueDoDiaApi() {
  const estoque = useEstoqueDoDiaApi()
  const [ajustando, setAjustando] = useState(null)
  const [avisoDaContagem, setAvisoDaContagem] = useState(null)
  const avisoNoFormulario = ajustando !== null && ajustando === avisoDaContagem
  // #2235 (banca T1-5): a perda do lote vencido é lançada na própria linha, depois de confirmar.
  const [perdaDoLote, setPerdaDoLote] = useState(null)
  const [lancandoPerda, setLancandoPerda] = useState(false)
  const confirmandoPerda = (lote) => perdaDoLote !== null && perdaDoLote === lote.itemEstoqueId
  const avisoNaPerda = perdaDoLote !== null && Boolean(estoque.aviso)
  const abrirPerda = (id) => { estoque.fecharAviso(); setAvisoDaContagem(null); setPerdaDoLote(id) }
  const registrarPerda = async (lote) => {
    estoque.fecharAviso()
    setAvisoDaContagem(null)
    setLancandoPerda(true)
    try { if (await estoque.registrarPerda(lote)) setPerdaDoLote(null) } finally { setLancandoPerda(false) }
  }
  // Ao abrir a confirmação, o foco vai para "Manter lote"; ao cancelar, volta ao "Registrar perda".
  const perdaAnterior = useRef(null)
  useEffect(() => {
    const anterior = perdaAnterior.current
    perdaAnterior.current = perdaDoLote
    if (typeof document === 'undefined') return
    const alvo = perdaDoLote !== null ? `manter-${perdaDoLote}` : anterior !== null ? `perda-${anterior}` : null
    if (alvo) document.getElementById(alvo)?.focus()
  }, [perdaDoLote])
  // #2081 (A8): reimprimir as etiquetas de um lote ativo. { loteId } buscando, com `folha` pronta, com `erro` falhou.
  // #2247 (v3): a reimpressão abre a mesma prévia da Produção do dia; quem imprime é o botão dela.
  const [impressao, setImpressao] = useState(null)
  const [previa, setPrevia] = useState(null)
  const montada = useRef(true)
  useEffect(() => {
    montada.current = true
    return () => { montada.current = false }
  }, [])

  async function reimprimir(lote) {
    setPrevia({ loteId: lote.id, codigo: lote.codigo, quantas: lote.quantidade })
    setImpressao({ loteId: lote.id })
    try {
      const folha = await producaoDoDia.folhaDeEtiquetas(lote.id)
      if (montada.current) setImpressao({ loteId: lote.id, folha })
    } catch (e) {
      if (montada.current) setImpressao({ loteId: lote.id, erro: e.message })
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
  const resumo = resumoDoEstoque(estoque.pratos, estoque.alertas)
  const semPorcao = estoque.pratos.length > 0 && resumo.porcoes === 0

  return (
    <div className={css.aba}>
      <dl className={tabela.numeros}>
        <NumeroDoResumo rotulo="Porções disponíveis" valor={resumo.porcoes} tom={semPorcao ? 'perigo' : null} dica={semPorcao ? 'produza ou ajuste' : null} />
        <NumeroDoResumo rotulo={resumo.vencidos === 1 ? 'Lote vencido' : 'Lotes vencidos'} valor={resumo.vencidos} tom={resumo.vencidos > 0 ? 'atencao' : null} dica={resumo.vencidos > 0 ? 'registre a perda' : null} />
        <NumeroDoResumo rotulo="Vendas sem saldo" valor={resumo.vendasSemSaldo} tom={resumo.vendasSemSaldo > 0 ? 'atencao' : null} dica={resumo.vendasSemSaldo > 0 ? 'ajuste a contagem' : null} />
      </dl>
      {estoque.estado === 'erro' && <p className={css.aviso} role="alert">
        O estoque não foi atualizado. Os saldos abaixo podem estar desatualizados. {estoque.erro}
        {' '}<Botao variante="texto" icone="refresh-cw" onClick={estoque.recarregar}>Recarregar estoque</Botao>
      </p>}
      {estoque.aviso && !avisoNoFormulario && !avisoNaPerda && (
        <p className={css.aviso} role="alert">
          {estoque.aviso} <Botao variante="texto" onClick={estoque.fecharAviso}>Fechar</Botao>
        </p>
      )}
      <div className={css.vivo} role="status">{estoque.feito && <p className={css.descricao}>{estoque.feito}</p>}</div>

      {/* #2235 (banca T1-6): sem venda sem saldo, a seção some; o zero do topo já diz que está certo. */}
      {estoque.alertas.length > 0 && (
      <section className={css.secao} aria-labelledby="estoque-alertas">
        <h3 id="estoque-alertas" className={css.tituloSecao}>Venda sem saldo</h3>
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
      </section>
      )}

      <section className={css.secao} aria-labelledby="estoque-saldo">
        <h3 id="estoque-saldo" className={css.tituloSecao}>Saldo por prato</h3>
        {estoque.pratos.length === 0 ? (
          <p className={css.descricao}>Nenhum prato com estoque cadastrado. Registre o primeiro lote em <a className={css.link} href={hashDoModulo('producao', 'producao')}>Produção do dia</a>.</p>
        ) : (
          <div className={tabela.moldura}>
            <table className={tabela.tabela} aria-labelledby="estoque-saldo">
              <thead><tr><th scope="col">Prato</th><th scope="col" className={tabela.numero}>Saldo</th><th scope="col">Situação</th><th scope="col" className={tabela.colunaAcoes}>Ações</th></tr></thead>
                {estoque.pratos.map((p) => {
                  const situacao = situacaoDoPrato(p)
                  return (
                    <tbody key={p.sku}>
                      <tr>
                        <td>
                          <span className={tabela.nome}>{p.nome}</span>
                          {/* #2081 (A16): a porção só aparece quando o nome ainda não diz qual é. */}
                          {(p.porcoes.length > 0 || porcaoQueNaoRepete(p.nome, p.porcao)) && <span className={tabela.apoio}>
                            {p.porcoes.length > 0 ? p.porcoes.map(saldoDaPorcao).join(' · ') : porcaoQueNaoRepete(p.nome, p.porcao)}
                          </span>}
                        </td>
                        <td data-rotulo="Saldo" className={`${tabela.numero} ${tabela.valor}`}>{porcoes(p.saldo)}</td>
                        {/* #2235 (banca T1-2) + #2439: ícone, texto e cor por estado, numa frase só. */}
                        <td data-rotulo="Situação">
                          <span className={`${tabela.situacao} ${TOM[situacao.tom]}`}><Icone nome={situacao.icone} />{situacao.texto}</span>
                        </td>
                        <td className={tabela.colunaAcoes}>
                          {ajustando !== p.sku && <div className={tabela.acoes}>
                            <Botao variante="secundario" icone="lapis" aria-label={`Ajustar contagem de ${p.nome}`} onClick={() => trocarAjuste(p.sku)}>Ajustar</Botao>
                          </div>}
                        </td>
                      </tr>
                      {ajustando === p.sku && (
                        <tr><td colSpan={4} className={tabela.extra}>
                          <FormularioAjuste
                            alvo={p}
                            aoSalvar={(contagem, motivo, porcao) => salvarContagem(p.sku, p.sku, contagem, motivo, porcao)}
                            aoCancelar={() => trocarAjuste(null)}
                            aviso={avisoNoFormulario ? estoque.aviso : null}
                            aoFecharAviso={estoque.fecharAviso}
                          />
                        </td></tr>
                      )}
                    </tbody>
                  )
                })}
            </table>
          </div>
        )}
      </section>

      <section className={css.secao} aria-labelledby="estoque-lotes">
        <h3 id="estoque-lotes" className={css.tituloSecao}>Lotes</h3>
        {lotes.length === 0 ? (
          <p className={css.descricao}>Nenhum lote com saldo.</p>
        ) : (
          <div className={tabela.moldura}>
            <table className={tabela.tabela} aria-labelledby="estoque-lotes">
              <thead><tr><th scope="col">Prato e lote</th><th scope="col" className={tabela.numero}>Quantidade</th><th scope="col">Validade</th><th scope="col" className={tabela.colunaAcoes}>Ações</th></tr></thead>
                {lotes.map((l, i) => {
                  const validade = validadeDoLote(l)
                  return (
                    <tbody key={`${l.codigo}-${i}`}>
                      <tr>
                        <td>
                          <span className={tabela.nome}>{l.prato}</span>
                          {/* #2081 (A10), #2235 (T1-4): código e tamanho, de onde veio, quem lançou e a fabricação. */}
                          <span className={tabela.apoio}>{detalheDoLote(l)}</span>
                        </td>
                        <td data-rotulo="Quantidade" className={`${tabela.numero} ${tabela.valor}`}>{porcoes(l.quantidade)}</td>
                        <td data-rotulo="Validade">
                          <span>
                            <span className={`${tabela.situacao} ${TOM[validade.tom]}`}>
                              {validade.tom === 'atencao' && <Icone nome={l.vencido ? 'relogio' : 'hourglass'} />}{validade.texto}
                            </span>
                            {validade.apoio && <span className={tabela.apoio}>{validade.apoio}</span>}
                          </span>
                        </td>
                        <td className={tabela.colunaAcoes}>
                          {(podeReimprimir(l) || podeRegistrarPerda(l)) && !confirmandoPerda(l) && (
                            <div className={tabela.acoes}>
                              {podeRegistrarPerda(l) && (
                                <Botao id={`perda-${l.itemEstoqueId}`} variante="secundario" icone="lixeira" disabled={lancandoPerda}
                                  aria-label={`Registrar perda do lote ${l.codigo ?? ''} de ${l.prato}`} onClick={() => abrirPerda(l.itemEstoqueId)}>Registrar perda</Botao>
                              )}
                              {podeReimprimir(l) && (
                                <Botao variante="secundario" icone="printer" disabled={impressao !== null && !impressao.folha && !impressao.erro}
                                  aria-label={`Etiquetas do lote ${l.codigo} de ${l.prato}`} onClick={() => reimprimir(l)}>Etiquetas</Botao>
                              )}
                            </div>
                          )}
                        </td>
                      </tr>
                      {confirmandoPerda(l) && (
                        <tr><td colSpan={4} className={tabela.extra}>
                          <div className={css.acoes} role="group" aria-label={`Confirmar perda do lote ${l.codigo ?? ''} de ${l.prato}`}>
                            <span className={css.descricao}>Lançar {porcoes(l.quantidade)} de {l.prato} como vencido? O saldo do lote vai a zero.</span>
                            <Botao id={`manter-${l.itemEstoqueId}`} variante="texto" disabled={lancandoPerda} onClick={() => setPerdaDoLote(null)}>Manter lote</Botao>
                            <Botao variante="perigo" icone="lixeira" disabled={lancandoPerda} onClick={() => registrarPerda(l)}>{lancandoPerda ? 'Lançando…' : 'Lançar perda'}</Botao>
                            {avisoNaPerda && <p className={`${css.aviso} ${css.linhaCompleta}`} role="alert">{estoque.aviso}</p>}
                          </div>
                        </td></tr>
                      )}
                      {/* #2439: lote fora da lista de recentes não tem id; sem a guarda, undefined === undefined lia o erro de null. */}
                      {impressao && l.id && impressao.loteId === l.id && impressao.erro && (
                        <tr><td colSpan={4} className={tabela.extra}>
                          <p className={css.aviso} role="alert">Não foi possível carregar as etiquetas deste lote. {impressao.erro}</p>
                        </td></tr>
                      )}
                    </tbody>
                  )
                })}
            </table>
          </div>
        )}
      </section>

      {previa && <PreviaDasEtiquetas loteId={previa.loteId} codigo={previa.codigo} quantas={previa.quantas}
        impressao={impressao} aoFechar={() => setPrevia(null)} />}
    </div>
  )
}
