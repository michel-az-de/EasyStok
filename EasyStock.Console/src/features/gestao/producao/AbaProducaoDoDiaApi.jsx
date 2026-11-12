import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { BotaoIcone } from '../../../componentes/BotaoIcone'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Chip } from '../../../componentes/Chip'
import { Icone } from '../../../componentes/Icone'
import { Modal } from '../../../componentes/Modal'
import { Vazio } from '../../../componentes/Vazio'
import { useCatalogo } from '../../../aplicacao/contextos'
import { useEstoqueDoDiaApi } from '../../../aplicacao/useEstoqueDoDiaApi'
import { aoLancarLote, dispensarLote, guardarRascunho, linhasDoRascunho, lotesLancados, marcarPassosVistos, producaoDoDia } from '../../../aplicacao/producaoDoDia'
import { lerSugestao, retirarProducaoPlanejada } from '../../../aplicacao/planejamento'
import { estaRemovido } from '../../../dominio/cardapio'
import { plural } from '../../../dominio/formato'
import {
  VALIDADES_RAPIDAS, cartoesDaGrade, categoriasDaGrade, definirNoLote, faltaProduzir, filtrarGrade, linhasIniciais, nomeDoCartao,
  ordenarPeloCardapio, porcoesNoLote, quantidadeDigitada, resumoDoLote, somarNoLote, tamanhoNoCartao,
} from '../../../dominio/gradeDeProducao'
import { hashDoModulo } from '../../../dominio/modulos'
import { erroDaLinha, sobraDaLinha } from '../../../dominio/producao'
import { linhaComPorcao } from '../../../dominio/porcoes'
import css from '../cardapio/abaCardapio.module.css'
import grade from './producaoDoDia.module.css'
import { ConectarAparelho } from './ConectarAparelho'
import { PreviaDasEtiquetas } from './etiqueta/PreviaDasEtiquetas'

// M2 › Produção do dia (M2.2, #1491). #1624: a dona produz como no PWA. Toca no prato para somar uma
// porção, vê o saldo no cartão e o que falta para os pedidos, confere validade e peso no lote e
// confirma. Confirmar cria o lote no EasyStok (etiquetas e entrada em porções); prato que ainda não
// tinha estoque passa a ter. Depois, "Imprimir etiquetas" imprime uma por porção (D-M2-04).
const VALIDADE_PADRAO = 5
const ID_DO_LOTE = 'lote-da-producao'
const ID_DOS_AVISOS = 'lotes-lancados'
const novaLinha = () => ({ id: crypto.randomUUID(), sku: '', variacaoId: '', porcoes: '', pesoPorPorcaoG: '', pesoRealG: '', validadeDias: String(VALIDADE_PADRAO) })
const inteiro = (texto) => (String(texto).trim() === '' ? null : Number(texto))
// #2364: quanto tempo segurando o cartão abre o campo da quantidade.
const SEGURAR_MS = 500
const diaCurto = (iso) => (iso ? iso.split('-').reverse().slice(0, 2).join('/') : 'amanhã')
const CAMPO_DO_ERRO = {
  'Escolha a porção produzida.': 'variacaoId',
  'Quantas porções saíram?': 'porcoes',
  'Informe porções inteiras maiores que zero.': 'porcoes',
  'O lote aceita até 9.999 porções.': 'porcoes',
  'Validade em dias inteiros.': 'validadeDias',
  'Validade de até 3.650 dias.': 'validadeDias',
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
// #2235 (banca T3-5): 600 g, 800 g e 1 kg usam a mesma foto; o tamanho vai em selo grande sobre ela.
function FotoDoCartao({ foto, selo }) {
  const [falhou, setFalhou] = useState(null)
  return (
    <span className={grade.fotoDoPrato}>
      {foto && falhou !== foto
        ? <img src={foto} alt="" loading="lazy" decoding="async" onError={() => setFalhou(foto)} />
        : <Icone nome="imagem" tamanho={24} />}
      {selo && <span className={grade.seloDoTamanho} aria-hidden="true">{selo}</span>}
    </span>
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
  const [ajustesAbertos, setAjustesAbertos] = useState({})
  const [lotes, setLotes] = useState(lotesLancados)
  // O que está na folha, de qual lote: { loteId } buscando, com `folha` pronta, com `erro` falhou.
  const [impressao, setImpressao] = useState(null)
  // #2081 (A4): o lote cuja prévia de etiquetas está aberta.
  const [previa, setPrevia] = useState(null)
  // A ajuda abre a pedido para manter a grade à vista também na primeira visita pelo celular.
  const [passosAbertos, setPassosAbertos] = useState(false)
  // "Falta produzir": a sugestão do planejamento. Sem permissão ou sem rede, o bloco não aparece.
  const [sugestao, setSugestao] = useState(null)
  const [leitura, setLeitura] = useState(0)
  const [categoria, setCategoria] = useState(null)
  const [busca, setBusca] = useState('')
  const [confirmandoLimpar, setConfirmandoLimpar] = useState(false)
  const [revisando, setRevisando] = useState(false)
  // #2364: o cartão cuja quantidade está sendo digitada: { cartao, texto, erro }.
  const [digitando, setDigitando] = useState(null)
  const segurar = useRef(null)
  const segurou = useRef(false)
  // #1585: cada lote imprime as próprias etiquetas. A folha esvazia antes de buscar: se a busca
  // falhar, não sobra a do lote anterior, e o erro aparece junto do botão do lote.
  // #2081 (A4): a folha traz a empresa e as etiquetas. #2247 (v3): nada imprime sozinho; quem
  // imprime é o botão da prévia, depois que a etiqueta aparece.
  async function buscarEtiquetas(loteId) {
    setImpressao({ loteId })
    try {
      const folha = await producaoDoDia.folhaDeEtiquetas(loteId)
      setImpressao({ loteId, folha })
    } catch (e) {
      setImpressao({ loteId, erro: e.message })
    }
  }
  // #2247 (v3): todo caminho para o papel passa pela prévia, que mostra a etiqueta que sai.
  const abrirPrevia = (loteId) => {
    setPrevia(loteId)
    buscarEtiquetas(loteId)
  }
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
    setAjustesAbertos({})
    setRevisando(false)
    setLeitura((n) => n + 1)
    recarregarEstoque()
    marcarPassosVistos()
    abrirPrevia(lote.loteId)
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
  // #2364: 30 lasanhas eram 30 toques. Tocar no contador (ou segurar o cartão) abre o campo com a
  // quantidade atual; confirmar troca o número de uma vez e 0 tira o prato do lote.
  const abrirDigitacao = (cartao) => {
    const atual = porcoesNoLote(linhas, cartao)
    setDigitando({ cartao, texto: atual > 0 ? String(atual) : '', erro: undefined })
  }
  const usarQuantidade = () => {
    const lida = quantidadeDigitada(digitando.texto, linhas, digitando.cartao)
    if (lida.erro) {
      setDigitando({ ...digitando, erro: lida.erro })
      return
    }
    const { cartao } = digitando
    mexerNoLote((atual) => definirNoLote(atual, cartao, lida.valor, apoio))
    setDigitando(null)
  }
  // Segurar o cartão abre o campo; o clique que vem junto ao soltar não soma. Rolar a grade
  // cancela o ponteiro e o toque longo não dispara.
  const comecarASegurar = (cartao) => () => {
    segurou.current = false
    clearTimeout(segurar.current)
    segurar.current = setTimeout(() => {
      segurou.current = true
      abrirDigitacao(cartao)
    }, SEGURAR_MS)
  }
  const soltar = () => clearTimeout(segurar.current)
  const tocarNoCartao = (cartao) => () => {
    if (segurou.current) {
      segurou.current = false
      return
    }
    somar(cartao, 1)
  }
  useEffect(() => () => clearTimeout(segurar.current), [])
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
  const alternarAjustes = (id) => (e) => {
    const aberto = e.currentTarget.open
    setAjustesAbertos((atual) => ({ ...atual, [id]: aberto }))
  }
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
    if (!r.ok) {
      setErro(r.erro)
      setAjustesAbertos(Object.fromEntries(linhas.map((l) => [l.id, true])))
    }
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
      {/* #2235: orientação em uma linha e detalhes a pedido; ao lado, o pareamento do aparelho. */}
      <div className={grade.topoDaProducao}>
        {/* #2235 (banca P2-1): uma linha de dica; o passo a passo abre a pedido. Fechar à mão também conta como visto.
            A dica e o aparelho dividem a primeira linha; o "Ver passo a passo" vem embaixo, só com o rótulo. */}
        <div className={grade.linhaDoTopo}>
          <p className={grade.dicaDosPassos}>Toque nos pratos prontos, confira e confirme. O estoque só muda ao confirmar.</p>
          <div className={grade.acoesDoTopo}><ConectarAparelho /></div>
        </div>
        <details className={grade.ajudaDosPassos} open={passosAbertos} onToggle={(e) => {
          const aberto = e.currentTarget.open
          setPassosAbertos(aberto)
          if (!aberto) marcarPassosVistos()
        }}>
          <summary><span className={grade.verPassos}>{passosAbertos ? 'Esconder o passo a passo' : 'Ver passo a passo'}</span></summary>
          <ol className={grade.passos} aria-label="Como registrar a produção">
            <li><span className={grade.numeroDoPasso} aria-hidden="true">1</span><span><strong>Toque nos pratos que ficaram prontos.</strong> Cada toque soma uma porção; o − tira.</span></li>
            <li><span className={grade.numeroDoPasso} aria-hidden="true">2</span><span><strong>Confira</strong> porções, validade e peso em Conferir produção.</span></li>
            <li><span className={grade.numeroDoPasso} aria-hidden="true">3</span><span><strong>Registre a produção</strong> em Confirmar produção. O estoque só muda nessa hora.</span></li>
          </ol>
        </details>
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
            {/* #2235 (banca T3-4): buscar é o atalho mais rápido e vem antes dos filtros. */}
            <div className={grade.filtros}>
              <div className={grade.busca}>
                <CampoTexto rotulo="Buscar prato" rotuloOculto tipo="search" placeholder="Buscar prato" value={busca} onChange={(e) => setBusca(e.target.value)} />
              </div>
              {categorias.length > 1 && (
                <div className={grade.chips}>
                  <Chip papel="filtro" ativo={!categoria} onClick={() => setCategoria(null)}>Todos</Chip>
                  {categorias.map((c) => (
                    <Chip key={c} papel="filtro" ativo={categoria === c} onClick={() => setCategoria(categoria === c ? null : c)}>{c}</Chip>
                  ))}
                </div>
              )}
            </div>
            {estoque.estado === 'erro' && (
              <p className={css.aviso}>
                Não foi possível carregar o saldo dos pratos. Você pode registrar a produção mesmo assim.
                {' '}<Botao variante="texto" icone="refresh-cw" onClick={recarregarEstoque}>Tentar de novo</Botao>
              </p>
            )}
            {cartoes.length === 0 ? (
              <Vazio icone="cardapio" titulo="Nenhum prato para produzir"
                acao={<a className={css.link} href={hashDoModulo('cardapio', 'itens')}>Abrir Itens do cardápio</a>}>
                Cadastre um prato no cardápio para começar.
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
                  // #2235 (banca T3-6): todo cartão tem a linha do estoque. Sem estoque ligado, diz isso em vez
                  // de inventar um zero; com o saldo fora do ar, nenhum cartão mostra (o aviso acima explica).
                  const semEstoqueLigado = cartao.saldo == null && estoque.estado === 'ok'
                  const idDoSaldo = cartao.saldo == null && !semEstoqueLigado ? undefined : `saldo-${cartao.chave}`
                  const idDaContagem = noLote > 0 ? `lote-${cartao.chave}` : undefined
                  const naSelecao = `${plural(noLote, 'porção', 'porções')} de ${nome} no lote`
                  return (
                    <li key={cartao.chave} data-prato={cartao.chave} className={`${grade.cartao} ${noLote > 0 ? grade.noLote : ''}`}>
                      <button type="button" className={grade.tocar} disabled={enviando} aria-label={`Somar 1 porção de ${nome}`}
                        title={`Adicionar 1 porção de ${nome} ao lote. O estoque só muda ao confirmar.`}
                        aria-describedby={[idDoSaldo, idDaContagem].filter(Boolean).join(' ') || undefined} onClick={tocarNoCartao(cartao)}
                        onPointerDown={comecarASegurar(cartao)} onPointerUp={soltar} onPointerLeave={soltar} onPointerCancel={soltar}
                        onContextMenu={(e) => e.preventDefault()}>
                        {gradeComFoto && <FotoDoCartao foto={cartao.foto} selo={tamanho} />}
                        <span className={grade.nomeDoPrato}>{nomeVisual}</span>
                        {tamanho && <span className={gradeComFoto ? grade.tamanhoComFoto : grade.tamanho}>{tamanho}</span>}
                        {cartao.saldo != null && (
                          <span id={idDoSaldo} className={grade.saldo}>
                            {cartao.saldo} no estoque{noLote > 0 && <strong> · {cartao.saldo + noLote} depois</strong>}
                          </span>
                        )}
                        {semEstoqueLigado && <span id={idDoSaldo} className={grade.saldo}>Ainda sem estoque</span>}
                      </button>
                      {noLote > 0 && (
                        <div className={grade.passo}>
                          <button type="button" className={grade.menos} disabled={enviando} title={`Tirar 1 porção de ${nome}`} aria-label={`Tirar 1 porção de ${nome}`} onClick={() => somar(cartao, -1)}>
                            <Icone nome="menos" tamanho={20} />
                          </button>
                          <span id={idDaContagem} className="sr">{naSelecao}</span>
                          <button type="button" className={grade.contagem} disabled={enviando} title={`${naSelecao}. Toque para digitar a quantidade.`}
                            aria-label={`Digitar a quantidade de ${nome}`} onClick={() => abrirDigitacao(cartao)}>
                            <span aria-hidden="true">{noLote}</span>
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
            {/* #1585: um aviso por lote lançado na visita, o mais novo primeiro, até ela fechar. */}
            {lotes.length > 0 && (
              <section id={ID_DOS_AVISOS} className={`${css.secao} ${grade.aparecer}`} aria-label="Lotes lançados nesta visita">
                {lotes.map((lote) => (
                  <output key={lote.loteId} className={css.aviso}>
                    <strong>Lote {lote.codigo}</strong> lançado: {lote.pratos.map((p) => `${p.porcoes}× ${p.nome}${p.sobraG ? ` (sobra ${p.sobraG} g)` : ''}`).join(', ')}.
                    <span className={grade.acoesDoAviso}>
                      <BotaoIcone icone="printer" disabled={buscandoEtiquetas} rotulo={`Etiquetas do lote ${lote.codigo}`} dica="Etiquetas" onClick={() => abrirPrevia(lote.loteId)} />
                      <Botao variante="texto" disabled={buscandoEtiquetas} aria-label={`Fechar o aviso do lote ${lote.codigo}`} onClick={() => dispensar(lote.loteId)}>Fechar aviso</Botao>
                    </span>
                    {impressao?.loteId === lote.loteId && impressao.erro && <>{' '}<strong>Não foi possível carregar as etiquetas deste lote. {impressao.erro}</strong></>}
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
                          <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Porções" aria-label={`Porções de ${nome}`} value={l.porcoes} onChange={mudar(l.id, 'porcoes')} {...validacao('porcoes')} />
                        </div>
                        {/* Abre sozinho quando o prato pede decisão dela ou quando o envio foi recusado. */}
                        <details open={faltaPorcao || !prato || ajustesAbertos[l.id] || undefined} onToggle={alternarAjustes(l.id)}>
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
                              <CampoTexto disabled={enviando} inputMode="numeric" rotulo="Validade (dias)" aria-label={`Validade em dias de ${nome}`} value={l.validadeDias} onChange={mudar(l.id, 'validadeDias')} {...validacao('validadeDias')} />
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
                              <Botao variante="texto" icone="lixeira" aria-label={`Tirar ${nome} do lote`} disabled={enviando} onClick={() => tirar(l.id)}>Tirar do lote</Botao>
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
                {resumo.pratos === 0 ? 'Lote vazio: toque nos pratos que ficaram prontos' : `No lote: ${plural(resumo.porcoes, 'porção', 'porções')} · ${plural(resumo.pratos, 'prato', 'pratos')}`}
              </p>
              <div className={grade.acoesDoLote}>
                {/* #2235 (banca T3-8): sem nada no lote, não há o que limpar. */}
                {!semLote && <Botao variante="texto" disabled={enviando} onClick={() => setConfirmandoLimpar(true)}>Limpar</Botao>}
                <Botao variante="primario" icone="check" disabled={semLote}
                  onClick={() => setRevisando(true)}>Conferir produção</Botao>
              </div>
            </>
          )}
        </div>
      </div>

      {digitando && (
        <Modal titulo={`Quantas porções de ${nomeDoCartao(digitando.cartao)}?`} largura="360px" focarCampo aoFechar={() => setDigitando(null)}
          descricao="O número substitui o que está no lote. 0 tira o prato."
          rodape={<>
            <Botao variante="texto" onClick={() => setDigitando(null)}>Cancelar</Botao>
            <Botao variante="primario" icone="check" onClick={usarQuantidade}>Usar quantidade</Botao>
          </>}>
          <form onSubmit={(e) => { e.preventDefault(); usarQuantidade() }}>
            <CampoTexto rotulo="Porções" inputMode="numeric" autoComplete="off" enterKeyHint="done" value={digitando.texto} erro={digitando.erro}
              onChange={(e) => setDigitando({ ...digitando, texto: e.target.value, erro: undefined })} />
          </form>
        </Modal>
      )}
      {loteDaPrevia && <PreviaDasEtiquetas loteId={loteDaPrevia.loteId} codigo={loteDaPrevia.codigo} quantas={loteDaPrevia.etiquetas}
        impressao={impressao} aoFechar={() => setPrevia(null)} />}
    </div>
  )
}
