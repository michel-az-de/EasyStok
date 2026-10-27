import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Chip } from '../../../componentes/Chip'
import { FolhaImpressao } from '../../../componentes/FolhaImpressao'
import { Icone } from '../../../componentes/Icone'
import { Modal } from '../../../componentes/Modal'
import { Vazio } from '../../../componentes/Vazio'
import { useCatalogo } from '../../../aplicacao/contextos'
import { useEstoqueDoDiaApi } from '../../../aplicacao/useEstoqueDoDiaApi'
import { aoLancarLote, dispensarLote, guardarRascunho, linhasDoRascunho, lotesLancados, marcarPassosVistos, passosRecolhidos, producaoDoDia } from '../../../aplicacao/producaoDoDia'
import { lerSugestao, retirarProducaoPlanejada } from '../../../aplicacao/planejamento'
import { estaRemovido } from '../../../dominio/cardapio'
import { plural } from '../../../dominio/formato'
import {
  VALIDADES_RAPIDAS, cartoesDaGrade, categoriasDaGrade, definirNoLote, faltaProduzir, filtrarGrade, linhasIniciais, nomeDoCartao,
  ordenarPeloCardapio, porcoesNoLote, resumoDoLote, soDigitos, somarNoLote, tamanhoNoCartao,
} from '../../../dominio/gradeDeProducao'
import { hashDoModulo } from '../../../dominio/modulos'
import { erroDaLinha, sobraDaLinha } from '../../../dominio/producao'
import { linhaComPorcao } from '../../../dominio/porcoes'
import css from '../cardapio/abaCardapio.module.css'
import grade from './producaoDoDia.module.css'
import { ConectarAparelho } from './ConectarAparelho'
import { EtiquetaDeProducao } from './etiqueta/EtiquetaDeProducao'
import etq from './etiqueta/etiqueta.module.css'

// M2 › Produção do dia (M2.2, #1491). #1624: a dona produz como no PWA. Toca no prato para somar uma
// porção, vê o saldo no cartão e o que falta para os pedidos, confere validade e peso no lote e
// confirma. Confirmar cria o lote no EasyStok (etiquetas e entrada em porções); prato que ainda não
// tinha estoque passa a ter. Depois, "Imprimir etiquetas" imprime uma por porção (D-M2-04).
const VALIDADE_PADRAO = 5
const ID_DO_LOTE = 'lote-da-producao'
const ID_DOS_AVISOS = 'lotes-lancados'
const novaLinha = () => ({ id: crypto.randomUUID(), sku: '', variacaoId: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: String(VALIDADE_PADRAO) })
const inteiro = (texto) => (String(texto).trim() === '' ? null : Number(texto))
const diaCurto = (iso) => (iso ? iso.split('-').reverse().slice(0, 2).join('/') : 'amanhã')
const CAMPO_DO_ERRO = {
  'Escolha a porção produzida.': 'variacaoId',
  'Quantas porções saíram?': 'porcoes',
  'Validade em dias inteiros.': 'validadeDias',
  'Peso da porção em gramas inteiros.': 'pesoPorPorcaoG',
  'Peso real em gramas inteiros.': 'pesoRealG',
  'O peso real não fecha as porções.': 'pesoRealG',
}

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

// #1627: o que ela confere de cada prato cabe numa linha; validade e peso ficam em "Ajustar".
// #2081 (A11): as porções saíram do resumo e viraram o campo ao lado do nome, à vista como no APK.
function resumoDaLinha(linha, faltaPorcao) {
  const enviada = paraEnviar(linha)
  return [
    faltaPorcao ? 'escolha a porção' : null,
    enviada.validadeDias > 0 ? `validade ${plural(enviada.validadeDias, 'dia', 'dias')}` : 'sem validade',
    enviada.pesoPorPorcaoG > 0 ? `${enviada.pesoPorPorcaoG} g cada` : 'sem peso',
  ].filter(Boolean).join(' · ')
}

// #1809: a foto do prato no cartão, a mesma do cardápio. Sem foto, ou se ela não carregar, o
// lugar fica reservado com o desenho de imagem: o cartão não muda de altura e a grade não pula.
// #2081 (A14): quando nenhum prato da grade tem foto, a reserva some e o cartão fica compacto; no
// tablet eram 43 cartões de 250 px com o mesmo desenho vazio.
function FotoDoCartao({ foto }) {
  const [falhou, setFalhou] = useState(null)
  return (
    <span className={grade.fotoDoPrato}>
      {foto && falhou !== foto
        ? <img src={foto} alt="" loading="lazy" decoding="async" onError={() => setFalhou(foto)} />
        : <Icone nome="imagem" tamanho={24} />}
    </span>
  )
}

// #2081 (A4): a prévia abre sozinha depois do lote, como no APK: a etiqueta de verdade (80x40, QR e
// barras) e um botão para imprimir todas. Fechar não perde nada: o aviso do lote continua imprimindo.
function PreviaDasEtiquetas({ lote, impressao, aoImprimir, aoFechar }) {
  const folha = impressao?.loteId === lote.loteId ? impressao.folha : null
  const erro = impressao?.loteId === lote.loteId ? impressao.erro : null
  const quantas = folha?.etiquetas.length ?? lote.etiquetas
  const rotulo = `Imprimir ${quantas} ${quantas === 1 ? 'etiqueta' : 'etiquetas'}`
  return (
    <Modal titulo={`Etiquetas do lote ${lote.codigo}`} largura="min(520px, calc(100vw - 24px))" aoFechar={aoFechar}
      descricao="Uma etiqueta por porção, no papel de 80 x 40 mm."
      rodape={<>
        <Botao variante="texto" onClick={aoFechar}>Fechar</Botao>
        <Botao variante="primario" icone="printer" disabled={!folha || folha.etiquetas.length === 0} onClick={aoImprimir}>{rotulo}</Botao>
      </>}>
      {erro ? <p className={css.aviso} role="alert">Não consegui buscar as etiquetas deste lote. {erro}</p>
        : !folha ? <p className={css.descricao}>Buscando as etiquetas…</p>
          : folha.etiquetas.length === 0 ? <p className={css.descricao}>Este lote não tem etiquetas para imprimir.</p>
            : (
              <div className={etq.previa}>
                <div className={etq.moldura}><EtiquetaDeProducao etiqueta={folha.etiquetas[0]} empresa={folha.empresa} total={folha.etiquetas.length} /></div>
                {folha.etiquetas.length > 1 && <p className={css.descricao}>E mais {folha.etiquetas.length - 1}, numeradas até {folha.etiquetas.length}/{folha.etiquetas.length}.</p>}
              </div>
            )}
    </Modal>
  )
}

export function AbaProducaoDoDiaApi() {
  const { cardapio } = useCatalogo()
  const pratos = useMemo(() => (cardapio ?? []).filter((i) => !estaRemovido(i)), [cardapio])
  // O saldo de cada cartão. Sem ele (API fora, prato sem estoque ligado) a grade continua servindo.
  const estoque = useEstoqueDoDiaApi()
  const recarregarEstoque = estoque.recarregar
  // M2.5 (#1502): vindo do Planejamento, as porções planejadas já entram no lote.
  // #1585: a tela desmonta a cada troca de rota; ela abre com o que ficou guardado.
  const [linhas, setLinhas] = useState(() => linhasIniciais(linhasDoRascunho(retirarProducaoPlanejada()), { pratos, novaLinha }))
  // #1627: o envio em voo é do módulo. A tela que volta no meio dele nasce travada, sem deixar
  // confirmar de novo nem mexer num lote que já foi.
  const [enviando, setEnviando] = useState(() => producaoDoDia.enviando())
  const [erro, setErro] = useState(null)
  const [lotes, setLotes] = useState(lotesLancados)
  // O que está na folha, de qual lote: { loteId } buscando, com `folha` pronta, com `erro` falhou.
  const [impressao, setImpressao] = useState(null)
  // #2081 (A4): o lote cuja prévia de etiquetas está aberta.
  const [previa, setPrevia] = useState(null)
  // #2081 (A14): os passos começam abertos e recolhem depois do primeiro lote.
  const [passosAbertos, setPassosAbertos] = useState(() => !passosRecolhidos())
  // "Falta produzir": a sugestão do planejamento. Sem permissão ou sem rede, o bloco não aparece.
  const [sugestao, setSugestao] = useState(null)
  const [leitura, setLeitura] = useState(0)
  const [categoria, setCategoria] = useState(null)
  const [busca, setBusca] = useState('')
  const [confirmandoLimpar, setConfirmandoLimpar] = useState(false)
  const [revisando, setRevisando] = useState(false)
  const montada = useRef(true)

  useEffect(() => {
    montada.current = true
    return () => { montada.current = false }
  }, [])
  // #1585: cada lote imprime as próprias etiquetas. A folha esvazia antes de buscar: se a busca
  // falhar, não sobra a do lote anterior, e o erro aparece junto do botão do lote.
  // #1627: se ela saiu da tela antes de as etiquetas chegarem, a impressão não abre sobre outra tela.
  // #2081 (A4): a folha traz a empresa e as etiquetas, que saem no papel de 80x40 com QR e barras.
  async function buscarEtiquetas(loteId, imprimirAoChegar) {
    setImpressao({ loteId })
    try {
      const folha = await producaoDoDia.folhaDeEtiquetas(loteId)
      setImpressao({ loteId, folha })
      if (imprimirAoChegar) setTimeout(() => { if (montada.current) window.print() }, 50)
    } catch (e) {
      setImpressao({ loteId, erro: e.message })
    }
  }
  const imprimir = (loteId) => buscarEtiquetas(loteId, true)
  useEffect(() => producaoDoDia.aoMudarEnvio(setEnviando), [])
  useEffect(() => { guardarRascunho(linhas) }, [linhas])
  // O lote gravado entra na lista e as linhas dele saem, mesmo quando o envio partiu antes de ela
  // sair da tela e voltar. A folha do lote anterior sai junto: não imprime etiqueta de outro lote.
  // O saldo dos cartões e o que falta produzir mudaram: os dois são relidos.
  // #2081 (A4): a prévia das etiquetas do lote novo abre sozinha, como no APK.
  useEffect(() => aoLancarLote((lote) => {
    setLotes(lotesLancados())
    setLinhas([])
    setErro(null)
    setRevisando(false)
    setLeitura((n) => n + 1)
    recarregarEstoque()
    marcarPassosVistos()
    setPrevia(lote.loteId)
    buscarEtiquetas(lote.loteId, false)
  }), [recarregarEstoque])
  // O campo inválido ganha foco dentro da revisão; falha da API anuncia o erro sem perder o lote.
  useEffect(() => {
    if (!erro) return
    const campo = globalThis.document?.querySelector(`#${ID_DO_LOTE} [aria-invalid="true"]`)
      ?? globalThis.document?.querySelector(`#${ID_DO_LOTE} [role="alert"]`)
    campo?.focus()
    campo?.scrollIntoView({ block: 'nearest' })
  }, [erro])
  useEffect(() => {
    if (leitura === 0) return
    const avisos = globalThis.document?.getElementById(ID_DOS_AVISOS)
    avisos?.scrollIntoView({ block: 'nearest' })
    avisos?.querySelector('button')?.focus({ preventScroll: true })
  }, [leitura])
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

  // Mexeu no lote: o aviso do envio anterior não vale mais (o número do prato já pode ser outro) e
  // a pergunta do Limpar se desfaz.
  const mexerNoLote = (mudanca) => {
    setErro(null)
    setConfirmandoLimpar(false)
    setLinhas(mudanca)
  }
  const naLinha = (id, mudanca) => mexerNoLote((atual) => atual.map((l) => (l.id === id ? mudanca(l) : l)))
  const somar = (cartao, delta) => mexerNoLote((atual) => somarNoLote(atual, cartao, delta, apoio))
  // Um toque no que falta: prato de um tamanho só entra no lote com a quantidade (nunca abaixo do
  // que ela já pôs); prato com porções mostra os tamanhos dele na grade.
  const atender = (item) => {
    if (item.cartao) {
      mexerNoLote((atual) => definirNoLote(atual, item.cartao, Math.max(item.quantidade, porcoesNoLote(atual, item.cartao)), apoio))
      return
    }
    setCategoria(null)
    setBusca(item.nome)
  }
  const mudar = (id, campo) => (e) => naLinha(id, (l) => ({ ...l, [campo]: e.target.value }))
  // Porções e dias são contagem: o campo só guarda dígitos. Peso continua como ela digitou, porque
  // "350,5" tem de virar aviso, não 3505.
  const mudarContagem = (id, campo) => (e) => naLinha(id, (l) => ({ ...l, [campo]: soDigitos(e.target.value) }))
  const definirValidade = (id, dias) => naLinha(id, (l) => ({ ...l, validadeDias: String(dias) }))
  // M1.4e (#1552): escolher a porção traz o peso dela, que é o que faz a receita em peso baixar o
  // insumo certo; o que ela digitou fica.
  const escolherPorcao = (id) => (e) => mexerNoLote((atual) => ordenarPeloCardapio(atual.map((l) => (l.id === id ? linhaComPorcao(l, pratos, e.target.value) : l)), pratos))
  const tirar = (id) => mexerNoLote((atual) => atual.filter((l) => l.id !== id))

  async function confirmar(evento) {
    evento.preventDefault()
    if (producaoDoDia.enviando()) return
    setErro(null)
    setConfirmandoLimpar(false)
    // Gravou: quem mostra o lote e zera as linhas é o `aoLancarLote` acima.
    const r = await producaoDoDia.produzir(linhas.map((l) => paraEnviar(l, pratos)))
    if (!r.ok) setErro(r.erro)
  }


  function dispensar(loteId) {
    setLotes(dispensarLote(loteId))
    if (impressao?.loteId === loteId) setImpressao(null)
    if (previa === loteId) setPrevia(null)
  }

  const buscandoEtiquetas = impressao !== null && !impressao.folha && !impressao.erro
  const loteDaPrevia = previa ? lotes.find((l) => l.loteId === previa) ?? null : null
  // #2081 (A14): sem nenhuma foto na grade, o cartão não reserva o lugar da foto.
  const gradeComFoto = cartoes.some((c) => c.foto)
  const semLote = linhas.length === 0
  const rotuloDoConfirmar = enviando ? 'Confirmando…' : 'Confirmar produção'

  return (
    <div className={css.aba}>
      {/* #1809: o caminho em três passos, à vista, para quem abre a tela pela primeira vez.
          #2081 (A14): depois do primeiro lote eles recolhem; ao lado, o pareamento do aparelho (A6). */}
      <div className={grade.topoDaProducao}>
        <details className={grade.ajudaDosPassos} open={passosAbertos} onToggle={(e) => setPassosAbertos(e.currentTarget.open)}>
          <summary>Como registrar a produção</summary>
          <ol className={grade.passos} aria-label="Como registrar a produção">
            <li><span className={grade.numeroDoPasso} aria-hidden="true">1</span><span><strong>Toque nos pratos que ficaram prontos.</strong> Cada toque soma uma porção; o − tira.</span></li>
            <li><span className={grade.numeroDoPasso} aria-hidden="true">2</span><span><strong>Confira</strong> porções, validade e peso em Conferir produção.</span></li>
            <li><span className={grade.numeroDoPasso} aria-hidden="true">3</span><span><strong>Registre a produção</strong> em Confirmar produção. O estoque só muda nessa hora.</span></li>
          </ol>
        </details>
        <div className={grade.acoesDoTopo}><ConectarAparelho /></div>
      </div>

      {falta.length > 0 && (
        <section className={grade.falta} aria-label="Falta produzir">
          <div>
            <h3 className={css.tituloSecao}>Falta produzir</h3>
            <p className={css.detalhe}>
              Conta os pedidos até {diaCurto(sugestao?.ate)}, o estoque mínimo e o que foi vendido sem ter no estoque. Toque para pôr no lote.
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
            {estoque.estado === 'erro' && (
              <p className={css.aviso}>
                Não consegui ler o saldo dos pratos agora. Dá para lançar a produção assim mesmo.
                {' '}<Botao variante="texto" icone="refresh-cw" onClick={recarregarEstoque}>Tentar de novo</Botao>
              </p>
            )}
            {cartoes.length === 0 ? (
              <Vazio icone="cardapio" titulo="Nenhum prato para produzir"
                acao={<a className={css.link} href={hashDoModulo('cardapio', 'itens')}>Abrir Itens do cardápio</a>}>
                A grade mostra os pratos do cardápio. Cadastre um prato em Itens do cardápio e ele aparece aqui para você somar as porções.
              </Vazio>
            ) : visiveis.length === 0 ? (
              <p className={css.descricao}>
                Nenhum prato com esse nome.
                {' '}<Botao variante="texto" onClick={() => { setBusca(''); setCategoria(null) }}>Ver todos os pratos</Botao>
              </p>
            ) : (
              <ul className={grade.grade}>
                {visiveis.map((cartao) => {
                  const noLote = porcoesNoLote(linhas, cartao)
                  const nome = nomeDoCartao(cartao)
                  const tamanho = cartao.tamanho?.trim() ?? ''
                  const nomeVisual = tamanho && !tamanhoNoCartao(cartao) ? cartao.nome.trimEnd().slice(0, -tamanho.length).trimEnd() : cartao.nome
                  // O rótulo do botão diz a ação; saldo e quantidade no lote vêm como descrição, para o leitor de tela também.
                  const idDoSaldo = cartao.saldo == null ? undefined : `saldo-${cartao.chave}`
                  const idDaContagem = noLote > 0 ? `lote-${cartao.chave}` : undefined
                  const naSelecao = `${plural(noLote, 'porção', 'porções')} de ${nome} na seleção`
                  return (
                    <li key={cartao.chave} data-prato={cartao.chave} className={`${grade.cartao} ${noLote > 0 ? grade.noLote : ''}`}>
                      <button type="button" className={grade.tocar} disabled={enviando} aria-label={`Somar 1 porção de ${nome}`}
                        title={`Adicionar 1 porção de ${nome} à seleção. O estoque só muda ao confirmar.`}
                        aria-describedby={[idDoSaldo, idDaContagem].filter(Boolean).join(' ') || undefined} onClick={() => somar(cartao, 1)}>
                        {gradeComFoto && <FotoDoCartao foto={cartao.foto} />}
                        <span className={grade.nomeDoPrato}>{nomeVisual}</span>
                        {tamanho && <span className={grade.tamanho}>{tamanho}</span>}
                        {cartao.saldo != null && (
                          <span id={idDoSaldo} className={grade.saldo}>
                            {cartao.saldo} no estoque{noLote > 0 && <strong> · {cartao.saldo + noLote} depois</strong>}
                          </span>
                        )}
                      </button>
                      {noLote > 0 && (
                        <div className={grade.passo}>
                          <button type="button" className={grade.menos} disabled={enviando} title={`Tirar 1 porção de ${nome}`} aria-label={`Tirar 1 porção de ${nome}`} onClick={() => somar(cartao, -1)}>
                            <Icone nome="menos" tamanho={20} />
                          </button>
                          <span id={idDaContagem} className={grade.contagem} title={naSelecao} aria-label={naSelecao}><span aria-hidden="true">{noLote}</span><span className="sr">{naSelecao}</span></span>
                        </div>
                      )}
                    </li>
                  )
                })}
              </ul>
            )}
          </section>

          <div className={grade.colunaDoLote}>
            {/* #1585: um aviso por lote lançado na visita, o mais novo primeiro, até ela fechar. */}
            {lotes.length > 0 && (
              <section id={ID_DOS_AVISOS} className={`${css.secao} ${grade.aparecer}`} aria-label="Lotes lançados nesta visita">
                {lotes.map((lote) => (
                  <output key={lote.loteId} className={css.aviso}>
                    <strong>Lote {lote.codigo}</strong> lançado: {lote.pratos.map((p) => `${p.porcoes}× ${p.nome}${p.sobraG ? ` (sobra ${p.sobraG} g)` : ''}`).join(', ')}.
                    <span className={grade.acoesDoAviso}>
                      <Botao variante="primario" icone="printer" disabled={buscandoEtiquetas} aria-label={`Imprimir ${lote.etiquetas} ${lote.etiquetas === 1 ? 'etiqueta' : 'etiquetas'} do lote ${lote.codigo}`} onClick={() => imprimir(lote.loteId)}>Imprimir {lote.etiquetas} {lote.etiquetas === 1 ? 'etiqueta' : 'etiquetas'}</Botao>
                      <Botao variante="texto" disabled={buscandoEtiquetas} aria-label={`Fechar o aviso do lote ${lote.codigo}`} onClick={() => dispensar(lote.loteId)}>Fechar aviso</Botao>
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

            {revisando && <Modal titulo="Conferir produção" largura="680px" aoFechar={enviando ? undefined : () => setRevisando(false)}
              descricao="Confira as porções, a validade e o peso. Ao confirmar, o lote entra no estoque e as etiquetas ficam prontas."
              rodape={<>
                <Botao variante="texto" disabled={enviando} onClick={() => setRevisando(false)}>Voltar aos pratos</Botao>
                <Botao tipo="submit" form={ID_DO_LOTE} variante="primario" icone="check" disabled={enviando || semLote}>{rotuloDoConfirmar}</Botao>
              </>}>
            <form id={ID_DO_LOTE} className={grade.lote} aria-label="Lote de hoje" onSubmit={confirmar}>
              <p className={grade.resumoDoLote}>{plural(resumo.porcoes, 'porção', 'porções')} · {plural(resumo.pratos, 'prato', 'pratos')}</p>
              {semLote && <p className={css.descricao}>O lote ficou vazio. Volte aos pratos para escolher o que foi produzido.</p>}
              {erro && <p id="erro-da-producao" className={`${css.aviso} ${grade.aparecer}`} role="alert" tabIndex={-1}>{erro}</p>}
              {!semLote && (
                <ol className={grade.itens}>
                  {linhas.map((l, indice) => {
                    const prato = pratos.find((p) => p.sku === l.sku)
                    const porcoesDoPrato = prato?.porcoes ?? []
                    const tamanho = porcoesDoPrato.length > 0 ? porcoesDoPrato.find((v) => v.id === l.variacaoId)?.rotulo : prato?.porcao
                    const nome = nomeDoCartao({ nome: prato?.nome ?? 'Prato fora do cardápio', tamanho: tamanho ?? '' })
                    const faltaPorcao = porcoesDoPrato.length > 0 && !l.variacaoId
                    const sobra = sobraDaLinha(paraEnviar(l))
                    const campoComErro = erro ? CAMPO_DO_ERRO[erroDaLinha(paraEnviar(l, pratos))] : null
                    const validacao = (campo) => campoComErro === campo ? { 'aria-invalid': true, 'aria-describedby': 'erro-da-producao' } : {}
                    return (
                      <li key={l.id} className={grade.itemDoLote}>
                        {/* #2081 (A11): as porções à vista, ao lado do nome; validade e peso ficam em "Ajustar". */}
                        <div className={grade.porcoesDoItem}>
                          <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Porções" aria-label={`Porções de ${nome}`} value={l.porcoes} onChange={mudarContagem(l.id, 'porcoes')} {...validacao('porcoes')} />
                        </div>
                        {/* Abre sozinho quando o prato pede decisão dela ou quando o envio foi recusado. */}
                        <details open={faltaPorcao || !prato || Boolean(erro) || undefined}>
                          <summary className={grade.resumoDoItem}>
                            <span className={css.numeroPrato}>{String(indice + 1).padStart(2, '0')}</span>
                            <span className={grade.textoDoItem}>
                              <h4 className={grade.nomeDoItem}>{nome}</h4>
                              <span className={css.detalhe}>{resumoDaLinha(l, faltaPorcao)}</span>
                            </span>
                            <span className={grade.ajustar}>Ajustar</span>
                          </summary>
                          <div className={grade.editorDoItem}>
                            {porcoesDoPrato.length > 0 && (
                              <CampoSelecao disabled={enviando} rotulo="Porção" aria-label={`Porção de ${prato.nome}`} value={l.variacaoId} onChange={escolherPorcao(l.id)} {...validacao('variacaoId')}
                                opcoes={[{ valor: '', rotulo: 'Escolha a porção' }, ...porcoesDoPrato.map((v) => ({ valor: v.id, rotulo: v.rotulo }))]} />
                            )}
                            <div className={grade.camposDoItem}>
                              <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Validade (dias)" aria-label={`Validade em dias de ${nome}`} value={l.validadeDias} onChange={mudarContagem(l.id, 'validadeDias')} {...validacao('validadeDias')} />
                            </div>
                            <div className={grade.chips} role="radiogroup" aria-label={`Validade de ${nome}`}>
                              {VALIDADES_RAPIDAS.map((dias) => (
                                <Chip key={dias} papel="escolha" reservarIcone disabled={enviando} ativo={String(l.validadeDias).trim() === String(dias)} onClick={() => definirValidade(l.id, dias)}>
                                  {dias} dias
                                </Chip>
                              ))}
                            </div>
                            <div className={grade.camposDoItem}>
                              <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Peso de cada porção (g)" aria-label={`Peso de cada porção de ${nome}, em gramas`} placeholder="g por porção" value={l.pesoPorPorcaoG} onChange={mudar(l.id, 'pesoPorPorcaoG')} {...validacao('pesoPorPorcaoG')} />
                              <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Peso real total (g)" aria-label={`Peso real total de ${nome}, em gramas`} placeholder="Opcional" value={l.pesoRealG} onChange={mudar(l.id, 'pesoRealG')} {...validacao('pesoRealG')} />
                            </div>
                            {sobra != null && <p className={css.detalhe}>{sobra < 0 ? 'Peso real menor que as porções' : `Sobra ${sobra} g`}</p>}
                            <div>
                              <Botao className={css.acaoDiscreta} variante="texto" icone="lixeira" aria-label={`Tirar ${nome} do lote`} disabled={enviando} onClick={() => tirar(l.id)}>Tirar do lote</Botao>
                            </div>
                          </div>
                        </details>
                      </li>
                    )
                  })}
                </ol>
              )}
            </form>
            </Modal>}
          </div>
        </div>

        <div className={grade.barraDoLote}>
          {confirmandoLimpar ? (
            <>
              <p className={grade.resumoDoLote} role="alert">Tirar {linhas.length === 1 ? 'o prato' : `os ${linhas.length} pratos`} do lote?</p>
              <div className={grade.acoesDoLote}>
                <Botao variante="texto" onClick={() => setConfirmandoLimpar(false)}>Manter o lote</Botao>
                <Botao onClick={() => mexerNoLote(() => [])}>Sim, limpar</Botao>
              </div>
            </>
          ) : (
            <>
              <p className={grade.resumoDoLote} aria-live="polite">
                {resumo.pratos === 0 ? 'Nenhum prato no lote' : `${plural(resumo.porcoes, 'porção', 'porções')} · ${plural(resumo.pratos, 'prato', 'pratos')}`}
              </p>
              <div className={grade.acoesDoLote}>
                <Botao variante="texto" disabled={enviando || semLote} onClick={() => setConfirmandoLimpar(true)}>Limpar</Botao>
                <Botao variante="primario" icone="check" disabled={semLote}
                  onClick={() => setRevisando(true)}>Conferir produção</Botao>
              </div>
            </>
          )}
        </div>
      </div>

      {loteDaPrevia && <PreviaDasEtiquetas lote={loteDaPrevia} impressao={impressao}
        aoImprimir={() => window.print()} aoFechar={() => setPrevia(null)} />}

      {/* #2081 (A4): a etiqueta do APK, 80x40 mm com QR e barras, uma por página. */}
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
