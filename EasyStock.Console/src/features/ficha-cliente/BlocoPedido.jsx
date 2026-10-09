import { useEffect, useRef, useState } from 'react'
import { useDndContext, useDroppable } from '@dnd-kit/core'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { moeda } from '../../dominio/formato'
import {
  agruparPorLinha, itensDetalhados, numeroDaComanda, pedidoEncerrado, totalDoPedido,
} from '../../dominio/pedido'
import { diferencaAposPagamento, totalPago } from '../../dominio/pagamento'
import { situacaoDoItem } from '../../dominio/cardapio'
import {
  LINHA_COMANDA_VAI_JUNTO, ROTULO_ENVIAR, avisoDoEnvio, rotuloDoTotal, situacaoDoEnvio,
} from '../../dominio/resumoPedido'
import { Cabecalho } from './Cabecalho'
import { ModalCanhoto } from './Canhoto'
import { SeletorJanela } from './SeletorJanela'
import { SeletorJanelaApi } from './SeletorJanelaApi'
import { EscolhaDeMeio } from './EscolhaDeMeio'
import css from './comanda.module.css'

// Só o que pede ação aparece na comanda: esgotado e fora do dia. Saldo curto já
// tem lugar no cardápio, e pílula demais é o que transforma a ficha em mancha.
const PRECISA_DE_MARCA = new Set(['esgotado', 'fora-do-dia'])

function MarcaDoItem({ produto }) {
  if (!produto) return null
  const situacao = situacaoDoItem(produto)
  if (!PRECISA_DE_MARCA.has(situacao.chave)) return null
  return <Pilula tom={situacao.tom} fina>{situacao.rotulo}</Pilula>
}

function ItemComanda({
  linha, fechado, observacaoTravada, obsAberta, aoAlternarObs, aoAjustar, aoAjustarObservacao,
}) {
  // Seção 3.3, gesto novo: toque em qualquer lugar do nome abre a anotação,
  // não só no lápis de 48 px — alvo do tamanho da linha inteira, não de um
  // ícone. O lápis continua ao lado do nome, mas só como sinal visual
  // (`aria-hidden`); quem lê por teclado ou leitor de tela usa o rótulo do
  // próprio botão do nome.
  const podeAnotar = !observacaoTravada
  const corpo = (
    <>
      <span className={css.nomeItem}>
        {linha.produto?.nome}
        <MarcaDoItem produto={linha.produto} />
        {podeAnotar && <Icone nome="lapis" tamanho={16} />}
      </span>
      <span className={css.porcao}>{linha.produto?.porcao}</span>
    </>
  )
  return (
    <li className={css.item}>
      <div className={css.linhaItem}>
        <span className={css.qtd}>{linha.qtd}×</span>
        {podeAnotar ? (
          <button
            type="button"
            className={`${css.corpo} ${css.corpoAnotar}`}
            aria-pressed={obsAberta}
            aria-label={`Anotar na ${linha.produto?.nome}`}
            onClick={aoAlternarObs}
          >
            {corpo}
          </button>
        ) : (
          <span className={css.corpo}>{corpo}</span>
        )}
        {fechado
          ? <span className={css.preco}>{moeda((linha.produto?.preco ?? 0) * linha.qtd)}</span>
          : (
            <span className={css.controlesItem}>
              <span className={css.contador}>
                <button type="button" aria-label="Menos um" onClick={() => aoAjustar(linha.sku, -1)}>
                  <Icone nome="menos" rotulo={`Tirar uma unidade de ${linha.produto?.nome}`} />
                </button>
                <b>{linha.qtd}</b>
                <button type="button" aria-label="Mais um" onClick={() => aoAjustar(linha.sku, 1)}>
                  <Icone nome="mais" rotulo={`Mais uma unidade de ${linha.produto?.nome}`} />
                </button>
              </span>
            </span>
          )}
      </div>
      {!observacaoTravada && obsAberta ? (
        <CampoArea
          rotulo={`Observação de ${linha.produto?.nome}`}
          rotuloOculto
          className={css.obsCampo}
          rows={1}
          placeholder="Observação: sem queijo, molho à parte..."
          value={linha.obs}
          onChange={(e) => aoAjustarObservacao(linha.sku, e.target.value)}
          onBlur={aoAlternarObs}
        />
      ) : (
        linha.obs && <span className={css.obs}>{linha.obs}</span>
      )}
    </li>
  )
}

export function BlocoPedido({
  pedido, editavel, aoTrocarJanela, aoAbrirCardapio, aoAjustar, aoAjustarObservacao, aoGerarPedido,
  aoForcarEncaixe, nomeCliente, endereco, faixa, bloqueado = false,
}) {
  const { cardapio, linhas } = useCatalogo()
  const { marcarCanhotoImpresso } = useAcoes()
  const { fonteApi } = useAtendimento()
  const [canhotoAberto, setCanhotoAberto] = useState(false)
  // Ponta (a): "Enviar comanda" sem meio marcado pergunta o meio antes de
  // mandar, em vez de mandar Pix por baixo.
  const [pedindoMeio, setPedindoMeio] = useState(false)
  // Rodada 12 (issue #14): o envio precisa de retorno na ficha, não só na
  // conversa. O aviso nasce quando a cobrança que o clique pediu aparece no
  // pedido, então só diz "enviada" o que o reducer aceitou de verdade.
  const [aviso, setAviso] = useState(null)
  const esperandoEnvio = useRef(false)
  // #1287: no modo API o envio é uma ida ao EasyStok; o botão fica travado até ela
  // voltar, senão o segundo clique cria outro pedido.
  const [enviando, setEnviando] = useState(false)
  const cobrancaEnviada = pedido.cobranca ?? null
  const primeiroNome = (nomeCliente ?? '').split(' ')[0]
  useEffect(() => {
    if (!esperandoEnvio.current || !cobrancaEnviada) return undefined
    esperandoEnvio.current = false
    setAviso(avisoDoEnvio(cobrancaEnviada, primeiroNome))
    const id = setTimeout(() => setAviso(null), 6000)
    return () => clearTimeout(id)
  }, [cobrancaEnviada, primeiroNome])
  // Sku com o campo de observação aberto no toque (seção 7: "o campo abre no
  // toque, não fica sempre à vista").
  const [obsAbertas, setObsAbertas] = useState(() => new Set())
  const fechado = pedidoEncerrado(pedido) || !editavel

  // Soltar aqui (seção 9): só acende quando o que está sendo arrastado é um
  // prato do cardápio. useDndContext em vez de prop: BlocoPedido não muda de
  // assinatura, quem monta (PainelFicha, da Frente A) não precisa saber de
  // arrasto nenhum.
  const { active, over } = useDndContext()
  const arrastandoPrato = active?.data?.current?.type === 'prato'
  const alvoDoArrasto = arrastandoPrato && over?.id === 'comanda-solta'
  const { setNodeRef: soltarRef } = useDroppable({
    id: 'comanda-solta', data: { type: 'comanda' }, disabled: fechado,
  })
  // A mensagem com a observação já saiu assim que a comanda foi mandada
  // (mesmo sinal que o resto da tela usa para "já mandou": `pedido.cobranca`
  // nasce em GERAR_PEDIDO). Reescrever o campo depois disso não reenvia nada,
  // e a ficha passava a divergir do que o cliente já leu (achado QA5).
  const observacaoTravada = fechado || Boolean(pedido.cobranca)
  const envio = situacaoDoEnvio(pedido, { bloqueado })
  const podeGerar = envio.pode
  const enviar = (meio) => {
    if (enviando) return
    esperandoEnvio.current = true
    const ida = aoGerarPedido(meio)
    if (!ida?.finally) return
    setEnviando(true)
    ida.finally(() => setEnviando(false))
  }
  const itens = itensDetalhados(pedido, cardapio)
  const total = totalDoPedido(pedido, cardapio)
  const grupos = agruparPorLinha(itens, linhas)
  // Defeito c (achado do arquiteto, 23/09/2026): pedido pago que ganha item
  // novo não fica calado sobre quanto falta cobrar (`dominio/pedido.js` tem
  // o porquê da conta e a lacuna conhecida de um segundo acréscimo).
  const diferencaPosPagamento = diferencaAposPagamento(pedido, cardapio)

  const alternarObs = (sku) => setObsAbertas((atual) => {
    const novo = new Set(atual)
    if (novo.has(sku)) novo.delete(sku); else novo.add(sku)
    return novo
  })

  return (
    <>
      <section
        ref={soltarRef}
        id="comanda-pedido"
        className={`${css.comanda} ${alvoDoArrasto ? css.alvoArrasto : ''}`}
        aria-label={numeroDaComanda(pedido, { fonteApi }) ? `Comanda número ${numeroDaComanda(pedido, { fonteApi })}` : 'Comanda em rascunho'}
      >
        {/* #1474 (R6, R3): o número é o do EasyStok quando o pedido existe; sem janela, o
            cabeçalho avisa. A faixa do modo API vem do seletor, não do catálogo local. */}
        <Cabecalho
          numero={numeroDaComanda(pedido, { fonteApi })}
          nomeCliente={nomeCliente}
          faixa={faixa}
          semJanela={!fechado && !pedido.janela}
          enviadaEm={pedido.cobranca?.criadaEm ?? null}
        />
        <hr className={css.separador} />

        {alvoDoArrasto && <p className={css.soltarAqui}>Soltar aqui</p>}

        {grupos.length === 0 && (
          <p className={css.vazio}>Comanda vazia. Abra o cardápio para montar o pedido.</p>
        )}

        {grupos.map((grupo) => (
          <div key={grupo.chave} className={css.grupo}>
            <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
            <ul className={css.itens}>
              {grupo.itens.map((linha) => (
                <ItemComanda
                  key={linha.sku}
                  linha={linha}
                  fechado={fechado}
                  observacaoTravada={observacaoTravada}
                  obsAberta={obsAbertas.has(linha.sku)}
                  aoAlternarObs={() => alternarObs(linha.sku)}
                  aoAjustar={aoAjustar}
                  aoAjustarObservacao={aoAjustarObservacao}
                />
              ))}
            </ul>
          </div>
        ))}

        <hr className={css.separador} />
        {/* F03: pedido criado no EasyStok traz o frete da zona; o total é o que foi cobrado. */}
        {pedido.frete > 0 && (
          <p className={css.vazio}>Frete {moeda(pedido.frete)}</p>
        )}
        <p className={css.total}>
          <span>{rotuloDoTotal(pedido, { fonteApi })}</span>
          <b>{moeda(pedido.totalApi ?? total)}</b>
        </p>
        {diferencaPosPagamento > 0 && (
          <p className={css.diferencaPaga} role="alert">
            <Icone nome="alerta" />
            Pago {moeda(totalPago(pedido))}. Item novo depois do pagamento:{' '}
            <b>{moeda(diferencaPosPagamento)}</b> a cobrar.
          </p>
        )}
      </section>

      {/* F03: no modo API a janela é da vitrine, com data, e só se escolhe antes do envio.
          #1474 (R3): vem antes das ações, logo abaixo da comanda, onde o motivo da barra aponta. */}
      {!fechado && fonteApi && !pedido.pedidoId && (
        <SeletorJanelaApi pedido={pedido} editavel={editavel} aoEscolher={aoTrocarJanela} />
      )}

      <div className={css.acoesPedido}>
        {!fechado && (
          <>
            <Botao largo onClick={aoAbrirCardapio}>Adicionar itens</Botao>
            {/* Rodada 12 (issue #14): sem botão morto. Ou ele envia, ou a
                linha diz por que não; comanda vazia já se explica no papel.
                #1474 (R4): no modo API quem cria o pedido é a barra fixa ("Gerar cobrança e
                enviar"); aqui só a linha que diz que a comanda vai junto. */}
            {fonteApi && (podeGerar || envio.chave === 'vazia') ? (
              <p className={css.situacaoEnvio}>{LINHA_COMANDA_VAI_JUNTO}</p>
            ) : (podeGerar || envio.chave === 'vazia') ? (
              <Botao
                largo variante="primario" icone="enviar" disabled={!podeGerar || enviando}
                onClick={() => (pedido.meio ? enviar(pedido.meio) : setPedindoMeio(true))}
              >
                {ROTULO_ENVIAR}
              </Botao>
            ) : (
              <p className={`${css.situacaoEnvio} ${envio.chave === 'enviada' ? css.envioFeito : ''}`}>
                <Icone nome={envio.chave === 'enviada' ? 'check' : 'alerta'} />
                {envio.texto}
              </p>
            )}
            {pedindoMeio && podeGerar && !pedido.meio && (
              <EscolhaDeMeio
                escolhido={null}
                aoEscolher={(meio) => { setPedindoMeio(false); enviar(meio) }}
                pergunta="Como o cliente vai pagar? A comanda sai com a cobrança."
              />
            )}
          </>
        )}
        {itens.length > 0 && (
          <Botao largo icone="printer" onClick={() => setCanhotoAberto(true)}>Ver canhoto</Botao>
        )}
      </div>

      {aviso && (
        <output className={css.avisoEnvio} aria-live="polite">
          <Icone nome="check" />
          <span>{aviso}</span>
        </output>
      )}

      {canhotoAberto && (
        <ModalCanhoto
          pedido={pedido}
          itens={itens}
          linhas={linhas}
          nomeCliente={nomeCliente}
          endereco={endereco}
          faixa={faixa}
          aoFechar={() => setCanhotoAberto(false)}
          aoImprimir={() => marcarCanhotoImpresso(pedido.numero)}
        />
      )}

      {!fechado && !fonteApi && (
        <SeletorJanela
          pedido={pedido}
          editavel={editavel}
          aoEscolher={aoTrocarJanela}
          aoForcarEncaixe={aoForcarEncaixe}
        />
      )}
    </>
  )
}
