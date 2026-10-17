import { useEffect, useRef, useState } from 'react'
import { useDndContext, useDroppable } from '@dnd-kit/core'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { moeda } from '../../dominio/formato'
import {
  LINHA_CLIENTE_ESCOLHENDO, agruparPorLinha, comandaTravada, itensDetalhados, numeroDaComanda, pedidoEncerrado, porcaoForaDoNome,
  precoDaLinha, totalDoPedido,
} from '../../dominio/pedido'
import { diferencaAposPagamento, totalPago } from '../../dominio/pagamento'
import { linhasDaSimulacao } from '../../dominio/simulacaoPedido'
import { situacaoDoItem } from '../../dominio/cardapio'
import {
  LINHA_COMANDA_TRAVADA, LINHA_COMANDA_VAI_JUNTO, ROTULO_ENVIAR, avisoDoEnvio, previaDoSinal, rotuloDoTotal, situacaoDoEnvio,
} from '../../dominio/resumoPedido'
import { Cabecalho } from './Cabecalho'
import { ModalCanhoto } from './Canhoto'
import { SeletorJanela } from './SeletorJanela'
import { SeletorJanelaApi } from './SeletorJanelaApi'
import { EscolhaDeMeio } from './EscolhaDeMeio'
import { janelaNoCabecalhoDoPapel } from './apresentacaoDaFicha'
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
  // #1761 (L18): sem a porção que o nome já diz; e o "1×" só no item fechado, porque aberto a
  // quantidade está no seletor, que fica na mesma linha do nome.
  const porcao = porcaoForaDoNome(linha.produto)
  const corpo = (
    <>
      <span className={css.nomeItem}>
        {linha.produto?.nome}
        <MarcaDoItem produto={linha.produto} />
        {podeAnotar && <Icone nome="lapis" tamanho={16} />}
      </span>
      {porcao && <span className={css.porcao}>{porcao}</span>}
    </>
  )
  return (
    <li className={css.item}>
      <div className={css.linhaItem}>
        {fechado && <span className={css.qtd}>{linha.qtd}×</span>}
        {podeAnotar ? (
          <button
            type="button"
            className={`${css.corpo} ${css.corpoAnotar}`}
            aria-pressed={obsAberta}
            aria-label={`Anotação para ${linha.produto?.nome}`}
            onClick={aoAlternarObs}
          >
            {corpo}
          </button>
        ) : (
          <span className={css.corpo}>{corpo}</span>
        )}
        {fechado
          ? <span className={css.preco}>{moeda(precoDaLinha(linha))}</span>
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
  pedido, editavel, aoTrocarJanela, aoAbrirCardapio, aoAjustar, aoAjustarObservacao, aoGerarPedido, gerarSemEnviar = false,
  aoForcarEncaixe, nomeCliente, endereco, faixa, bloqueado = false, conversaId, numeroComanda,
  clienteEscolhendo = false, simulacao = null,
}) {
  const { cardapio, linhas } = useCatalogo()
  const { marcarCanhotoImpresso } = useAcoes()
  const { fonteApi, agora } = useAtendimento()
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
  // #1587: pedido já criado no EasyStok não troca item por aqui. Antes o "+", o "−" e o
  // "Adicionar itens" seguiam ativos e só avisavam no rodapé; agora os itens travam e a linha
  // diz o motivo. Janela e cobrança continuam como estavam (dependem só de `fechado`).
  // #1683: enquanto o cliente escolhe pelo link, os itens são os da escolha dele, no bloco de cima.
  const itensTravados = fechado || comandaTravada(pedido, { fonteApi }) || clienteEscolhendo

  // Soltar aqui (seção 9): só acende quando o que está sendo arrastado é um
  // prato do cardápio. useDndContext em vez de prop: BlocoPedido não muda de
  // assinatura, quem monta (PainelFicha, da Frente A) não precisa saber de
  // arrasto nenhum.
  const { active, over } = useDndContext()
  const arrastandoPrato = active?.data?.current?.type === 'prato'
  const alvoDoArrasto = arrastandoPrato && over?.id === 'comanda-solta'
  const { setNodeRef: soltarRef } = useDroppable({
    id: 'comanda-solta', data: { type: 'comanda' }, disabled: itensTravados,
  })
  // A mensagem com a observação já saiu assim que a comanda foi mandada
  // (mesmo sinal que o resto da tela usa para "já mandou": `pedido.cobranca`
  // nasce em GERAR_PEDIDO). Reescrever o campo depois disso não reenvia nada,
  // e a ficha passava a divergir do que o cliente já leu (achado QA5).
  const observacaoTravada = itensTravados || Boolean(pedido.cobranca)
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
  // #1875 ("Quanto fica?"): o que já dá para dizer antes de gerar a cobrança. O frete é do servidor.
  const previaSinal = previaDoSinal(pedido, { fonteApi, agora, total: pedido.totalApi ?? total })
  // #1884: com a simulação do servidor a comanda mostra frete, total com frete e sinal em número.
  const linhasSimulacao = !pedido.pedidoId ? linhasDaSimulacao(simulacao) : null
  const linhaDe = (chave) => linhasSimulacao?.find((l) => l.chave === chave)?.texto ?? null
  // #1875: na Seleção do atendimento (pedido ainda não criado) a janela vem antes da comanda: com a comanda
  // primeiro, em 1366x768 a lista de 10 janelas começava a 541 px de um conteúdo de 1026 e nenhuma cabia sem rolar.
  const janelaPrimeiro = !fechado && fonteApi && !pedido.pedidoId
  const seletorDeJanela = !fechado && fonteApi
    ? <SeletorJanelaApi key={conversaId} pedido={pedido} editavel={editavel} aoEscolher={aoTrocarJanela} />
    : null

  const alternarObs = (sku) => setObsAbertas((atual) => {
    const novo = new Set(atual)
    if (novo.has(sku)) novo.delete(sku); else novo.add(sku)
    return novo
  })

  return (
    <>
      {janelaPrimeiro && seletorDeJanela}
      <section
        ref={soltarRef}
        id="comanda-pedido"
        tabIndex={-1}
        className={`${css.comanda} ${alvoDoArrasto ? css.alvoArrasto : ''}`}
        aria-label={numeroComanda ? `Comanda número ${numeroComanda}` : 'Comanda em rascunho'}
      >
        {/* #1474 (R6, R3): o número é o do EasyStok quando o pedido existe; sem janela, o
            cabeçalho avisa. A faixa do modo API vem do seletor, não do catálogo local.
            #1868: pedido criado no modo API mostra a janela só na linha "Janela de entrega" abaixo. */}
        <Cabecalho
          numero={numeroComanda || numeroDaComanda(pedido, { fonteApi })}
          nomeCliente={nomeCliente}
          faixa={!janelaNoCabecalhoDoPapel({ fonteApi, pedidoCriado: Boolean(pedido.pedidoId), fechado }) ? null : fonteApi ? pedido.janelaRotulo : faixa}
          semJanela={!fechado && !pedido.janela}
          enviadaEm={pedido.cobranca?.criadaEm ?? null}
        />
        <hr className={css.separador} />

        {/* O total fica à vista antes de percorrer os itens da montagem. */}
        <div className={css.resumoTotal}>
          {pedido.frete > 0 && <p className={css.vazio}>Frete {moeda(pedido.frete)}</p>}
          {linhasSimulacao && <p className={css.vazio} data-simulacao-frete>{linhaDe('frete')}</p>}
          <p className={css.total}>
            <span>{fonteApi && !pedido.pedidoId ? 'Total dos itens' : rotuloDoTotal(pedido, { fonteApi })}</span>
            <b>{moeda(pedido.totalApi ?? total)}</b>
          </p>
          {linhaDe('total') && <p className={css.total} data-simulacao-total><span>Total com frete</span><b>{moeda(simulacao.total)}</b></p>}
          {fonteApi && !pedido.pedidoId && !linhasSimulacao && <p className={css.notaTotal}>O frete entra ao gerar a cobrança.</p>}
          {linhaDe('sinal') && <p className={css.notaTotal} data-simulacao-sinal>{linhaDe('sinal')}</p>}
          {previaSinal && !linhasSimulacao && (
            <p className={css.notaTotal}>
              Encomenda para outro dia: sinal de 50% do total com o frete antes da produção (nos itens, {moeda(previaSinal.sinal)}); o restante na entrega.
            </p>
          )}
        </div>

        {alvoDoArrasto && <p className={css.soltarAqui}>Soltar aqui</p>}

        {grupos.length === 0 && (
          <p className={css.vazio}>{clienteEscolhendo ? LINHA_CLIENTE_ESCOLHENDO : 'Comanda vazia. Abra o cardápio para montar o pedido.'}</p>
        )}

        {grupos.map((grupo) => (
          <div key={grupo.chave} className={css.grupo}>
            <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
            <ul className={css.itens}>
              {grupo.itens.map((linha) => (
                <ItemComanda
                  key={linha.sku}
                  linha={linha}
                  fechado={itensTravados}
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
        {/* #1683: observação geral que o cliente deixou no pedido (a do item fica na linha). */}
        {pedido.observacoes && <p className={css.notaTotal}>Observação do pedido: {pedido.observacoes}</p>}

        <hr className={css.separador} />
        {diferencaPosPagamento > 0 && (
          <p className={css.diferencaPaga} role="alert">
            <Icone nome="alerta" />
            Pago {moeda(totalPago(pedido))}. Item novo depois do pagamento:{' '}
            <b>{moeda(diferencaPosPagamento)}</b> a cobrar.
          </p>
        )}
      </section>

      {/* No modo API a janela é da vitrine, com data; pedido criado troca a vaga no servidor.
          #1474 (R3): vem antes das ações, logo abaixo da comanda, onde o motivo da barra aponta.
          #1551: remonta por conversa; o "Dia da entrega" de um cliente não vale para o seguinte. */}
      {!janelaPrimeiro && seletorDeJanela}

      <div className={css.acoesPedido}>
        {!fechado && (
          <>
            {/* #1587: travada, o cardápio segue abrindo para consulta e foto, com o nome certo. */}
            {!clienteEscolhendo && (
              <>
                {itensTravados && <p className={css.situacaoEnvio}>{LINHA_COMANDA_TRAVADA}</p>}
                <Botao largo onClick={aoAbrirCardapio}>{itensTravados ? 'Ver cardápio' : 'Adicionar itens'}</Botao>
              </>
            )}
            {/* Rodada 12 (issue #14): sem botão morto. Ou ele envia, ou a
                linha diz por que não; comanda vazia já se explica no papel.
                #1474 (R4): no modo API quem cria o pedido é a barra fixa ("Gerar cobrança e
                enviar"); aqui só a linha que diz que a comanda vai junto.
                #1587: com o pedido já criado ela não vai mais a lugar nenhum; fica só o motivo acima. */}
            {fonteApi && (podeGerar || envio.chave === 'vazia') ? (
              !itensTravados && <p className={css.situacaoEnvio}>{gerarSemEnviar ? 'A cobrança será gerada sem envio. Copie o link em Pagamento ou use um modelo aprovado.' : LINHA_COMANDA_VAI_JUNTO}</p>
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
          pedidoIdApi={fonteApi ? pedido.pedidoId ?? null : null}
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
