import { useEffect, useRef, useState } from 'react'
import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { CampoMascarado } from '../../componentes/CampoMascarado'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { ListaDados } from '../../componentes/ListaDados'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { nomeDoMeio, podeAlterarMeio, situacaoDaCobranca } from '../../dominio/cobranca'
import {
  horaCurta, lerMoeda, mascaraMoeda, moeda, moedaAltaDemais,
} from '../../dominio/formato'
import { diferencaAposPagamento } from '../../dominio/pagamento'
import { totalDoPedido } from '../../dominio/pedido'
import { encontrarCupom, validarCupom } from '../../dominio/fidelidade'
import { EscolhaDeMeio } from './EscolhaDeMeio'
import { RelogioPix } from './RelogioPix'
import css from './ficha.module.css'

// Campo "Cupom" da Cobrança (frente Fidelidade e cupons, rodada 13, issue
// #45, registro 107): aplica o desconto ANTES de gerar a cobrança e mostra o
// total já líquido, para nunca existir uma tela dizendo um total e a
// cobrança saindo com outro.
function CampoCupom({
  pedido, total, cupons, agora, aoAplicar, aoRemover,
}) {
  const [codigo, setCodigo] = useState('')
  const [erro, setErro] = useState(null)
  const cupomAplicado = pedido.cupom

  if (cupomAplicado) {
    return (
      <p className={css.cupomAplicado}>
        <Icone nome="dollar-sign" /> Cupom <b>{cupomAplicado.codigo}</b> aplicado ·
        {' '}desconto {moeda(pedido.valorDesconto ?? 0)}
        <Botao variante="texto" onClick={aoRemover}>Remover</Botao>
      </p>
    )
  }

  function aplicar() {
    const cupom = encontrarCupom(cupons, codigo)
    const { valido, motivo } = validarCupom(cupom, { agora, totalPedido: total })
    if (!valido) { setErro(motivo); return }
    setErro(null)
    setCodigo('')
    aoAplicar(cupom.codigo)
  }

  return (
    <div className={css.linhaCupom}>
      <CampoTexto
        rotulo="Cupom" rotuloOculto dica={erro}
        placeholder="Cupom de desconto"
        value={codigo}
        onChange={(e) => { setCodigo(e.target.value); setErro(null) }}
      />
      <Botao disabled={!codigo.trim()} onClick={aplicar}>Aplicar</Botao>
    </div>
  )
}

// Emitir uma cobrança é ida e volta a um provedor. No protótipo isso é instantâneo,
// mas a tela mostra o estado de carregando mesmo assim: quem usa precisa aprender
// que existe uma espera ali, senão estranha no dia em que o Pix real demorar.
const MS_DE_EMISSAO = 700

// #1287: no modo API a emissão devolve a promessa da ida ao EasyStok; o "emitindo" só
// desliga quando ela volta, e o clique repetido no meio do caminho é ignorado.
function useEmissao(aoEmitir) {
  const [emitindo, setEmitindo] = useState(false)
  const pendente = useRef(null)
  const ocupado = useRef(false)

  useEffect(() => () => clearTimeout(pendente.current), [])

  const emitir = (...argumentos) => {
    if (ocupado.current) return
    ocupado.current = true
    setEmitindo(true)
    pendente.current = setTimeout(() => {
      const liberar = () => { ocupado.current = false; setEmitindo(false) }
      const ida = aoEmitir(...argumentos)
      if (ida?.finally) ida.finally(liberar)
      else liberar()
    }, MS_DE_EMISSAO)
  }

  return { emitindo, emitir }
}

function CopiaECola({ codigo }) {
  const [status, setStatus] = useState(null) // null | 'copiado' | 'falhou'
  const pendente = useRef(null)
  const codigoRef = useRef(null)

  useEffect(() => () => clearTimeout(pendente.current), [])

  const selecionarCodigo = () => {
    const el = codigoRef.current
    if (!el) return
    const selecao = window.getSelection()
    const intervalo = document.createRange()
    intervalo.selectNodeContents(el)
    selecao.removeAllRanges()
    selecao.addRange(intervalo)
  }

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(codigo)
      setStatus('copiado')
    } catch {
      // Navegador sem permissão de área de transferência: seleciona o código
      // na tela, porque silêncio aqui parece botão morto (achado QA7).
      setStatus('falhou')
      selecionarCodigo()
    }
    pendente.current = setTimeout(() => setStatus(null), 2000)
  }

  const rotuloBotao = { copiado: 'Copiado', falhou: 'Selecione e copie' }[status] ?? 'Copiar código'

  return (
    <div className={css.copiaECola}>
      <code ref={codigoRef} className={css.codigoPix}>{codigo}</code>
      <Botao onClick={copiar}>{rotuloBotao}</Botao>
    </div>
  )
}

// Baixa à mão, com o valor que ela viu cair. Nasce preenchida com o total,
// que é o caso normal, e aceita um valor diferente, que também acontece:
// cliente arredonda, paga só a entrada ou digita errado. Sem isto a tela
// obrigaria a mentir que bateu. Botão "Marcar pago" saiu do bloco Pix e foi
// para o bloco Pedido (secao 2 da direção visual); este formulário é o que
// abre quando ele é clicado, então mora aqui para o bloco Pedido reusar.
//
// Recebimento manual exige escolher o método usado; a API decide se o pedido foi quitado.
export function BaixaAMao({ valorCobrado, aoConfirmar, aoCancelar }) {
  // Nasce preenchida com o total cobrado, em centavos por dentro (seção 4 da
  // direção visual, passo zero): a tela só vê o texto já mascarado, "R$ X,XX".
  const [centavos, setCentavos] = useState(() => Math.round(valorCobrado * 100))
  const excedeu = moedaAltaDemais(centavos)
  const [metodo, setMetodo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const valido = centavos > 0 && !excedeu && Boolean(metodo)

  return (
    <div className={css.baixaDivergente}>
      <CampoSelecao rotulo="Como recebeu" value={metodo} onChange={(e) => setMetodo(e.target.value)}
        opcoes={[
          { valor: '', rotulo: 'Selecione' }, { valor: 'pix', rotulo: 'Pix' },
          { valor: 'dinheiro', rotulo: 'Dinheiro' }, { valor: 'credito', rotulo: 'Cartão de crédito' },
          { valor: 'debito', rotulo: 'Cartão de débito' }, { valor: 'transferencia', rotulo: 'Transferência' },
          { valor: 'outro', rotulo: 'Outro' },
        ]} />
      <CampoMascarado
        tipo="moeda"
        rotulo="Valor recebido"
        valor={mascaraMoeda(centavos)}
        erro={excedeu ? 'Valor alto demais' : null}
        aoMudarDigitos={(digitos) => setCentavos(Number(digitos || '0'))}
        aoColarTexto={(texto) => setCentavos(lerMoeda(texto))}
      />
      <div className={css.acoesCobranca}>
        <Botao largo onClick={aoCancelar}>Voltar</Botao>
        <Botao largo variante="primario" disabled={!valido || enviando} onClick={async () => {
          setEnviando(true)
          try { await aoConfirmar(centavos / 100, metodo) } finally { setEnviando(false) }
        }}>
          Confirmar
        </Botao>
      </div>
    </div>
  )
}

// "Alterar forma de pagamento" (rodada 12, issue #13; substitui o "Trocar
// meio" da decisão 19). O antigo só limpava a cobrança: com maquininha ou vale
// a esteira já tinha andado e o bloco caía em "Pago fora da cobrança", sem
// chips, até o pedido vencer. Agora escolhe a forma nova, confirma em linha e
// sai tudo numa ação: a pendente é cancelada e a nova vai ao cliente.
function AlterarForma({ cobranca, nomeCliente, aoAlterar }) {
  const [aberto, setAberto] = useState(false)
  const [novo, setNovo] = useState(null)
  const atual = nomeDoMeio(cobranca.meio)
  const fechar = () => { setAberto(false); setNovo(null) }
  if (!aberto) {
    return (
      <Botao largo variante="secundario" icone="refresh-cw" className={css.alterarForma} onClick={() => setAberto(true)}>
        Alterar forma de pagamento
      </Botao>
    )
  }
  return (
    <div className={css.confirmarTroca}>
      <EscolhaDeMeio escolhido={novo} excluir={cobranca.meio ?? 'pix'} aoEscolher={setNovo} pergunta={`Hoje: ${atual}. Mudar para:`} />
      {novo && (
        <p>
          A cobrança por {atual} é cancelada e {nomeCliente} recebe uma nova por {nomeDoMeio(novo)}.
          O pedido continua o mesmo.
        </p>
      )}
      <div className={css.acoesCobranca}>
        <Botao largo onClick={fechar}>Voltar</Botao>
        <Botao largo variante="primario" disabled={!novo} onClick={() => { const meio = novo; fechar(); aoAlterar(meio) }}>
          Confirmar troca
        </Botao>
      </div>
    </div>
  )
}

// Bloco Cobrança da ficha. A cobrança é do PEDIDO, não da conversa: ela mora
// dentro dele e viaja junto quando o pedido anda.
//
// Rodada 5, seção 4 — pedido do dono, literal: "Gerar Pix deveria ser Gerar
// cobrança, e precisamos ver o que o Mercado Pago oferece para gerar links de
// cobrança e receber por cartão, Pix, VR, VA." Quatro chips de meio
// (`infra/catalogo.js`, MEIOS_DE_PAGAMENTO, lido pelo contexto do catálogo):
// Pix e cartão por link emitem de verdade (link com prazo de 30 min, o mesmo
// anel do Pix); maquininha e vale não têm link nenhum para emitir — o
// Mercado Pago só aceita VR/VA na maquininha (estudo, decisão 18, seção
// 3.2) — e a cobrança vira só um registro de "vai cobrar na entrega".
export function BlocoCobranca({
  pedido, agora, editavel, nomeCliente, conversaId, aoGerar, aoReenviar, aoMarcarComprovante,
  aoAceitarDivergencia, aoAbrirCardapio, aoCancelarPedido, aoEncerrarAtendimento, aoAlterarMeio,
}) {
  const { cardapio } = useCatalogo()
  const { fidelidade } = useAtendimento()
  const {
    confirmarPagamento, marcarRecebidoEntrega, refazerCobranca, escolherMeioPagamento, aplicarCupom, removerCupomDoPedido,
  } = useAcoes()
  // Ponta (a): o chip marcado mora no pedido, para "Enviar comanda" e a
  // barra mandarem o mesmo meio. Nenhum vem marcado: ela escolhe.
  const meioEscolhido = pedido.meio ?? null
  // Diferença de item novo em pedido pago: pergunta o meio de novo, com os
  // mesmos quatro chips (pago na maquininha pode completar no Pix e vice-versa).
  const [recebendo, setRecebendo] = useState(false)
  const [meioDaDiferenca, setMeioDaDiferenca] = useState(false)
  const cobranca = pedido.cobranca ?? null
  const situacao = situacaoDaCobranca(cobranca, agora)
  const total = totalDoPedido(pedido, cardapio)
  const geracao = useEmissao(aoGerar)
  const reenvio = useEmissao(aoReenviar)
  const troca = useEmissao(aoAlterarMeio)
  const alterar = editavel && podeAlterarMeio(pedido) && (
    <AlterarForma cobranca={cobranca} nomeCliente={nomeCliente} aoAlterar={troca.emitir} />
  )

  if (geracao.emitindo || reenvio.emitindo || troca.emitindo) {
    return (
      <Bloco titulo="Cobrança">
        <p className={css.carregando} aria-live="polite">
          <Icone nome="relogio" /> Gerando cobrança…
        </p>
      </Bloco>
    )
  }

  if (!cobranca) {
    // Cancelado sem cobrança nunca chegou a cobrar nada: dizer "pago" aqui
    // seria inventar um fato que não aconteceu.
    if (pedido.estado === 'cancelado') {
      return (
        <Bloco titulo="Cobrança">
          <Vazio titulo="Pedido cancelado" />
        </Bloco>
      )
    }
    // Pedido que já andou sem cobrança veio de antes do Pix no sistema (venda
    // antiga, massa de teste). Oferecer cobrar de novo quem já pagou é o tipo de
    // erro que custa cliente, então aqui só se conta o que aconteceu.
    if (pedido.estado !== 'aguardando') {
      return (
        <Bloco titulo="Cobrança">
          <Vazio titulo="Pago fora da cobrança" />
        </Bloco>
      )
    }
    if (pedido.itens.length === 0 || total === 0) {
      return (
        <Bloco titulo="Cobrança">
          <Vazio
            titulo="Comanda vazia"
            acao={<Botao onClick={aoAbrirCardapio} disabled={!editavel}>Abrir cardápio</Botao>}
          />
        </Bloco>
      )
    }
    const totalLiquido = total - (pedido.valorDesconto ?? 0)
    return (
      <Bloco titulo="Cobrança">
        <p className={css.valorCobranca}>
          <span>{pedido.cupom ? 'Subtotal' : 'Total'}</span>
          <b>{moeda(total)}</b>
        </p>
        {pedido.cupom && (
          <p className={css.valorCobranca}>
            <span>Total com desconto</span>
            <b>{moeda(totalLiquido)}</b>
          </p>
        )}
        {editavel && (
          <CampoCupom
            pedido={pedido}
            total={total}
            cupons={fidelidade.cupons}
            agora={agora}
            aoAplicar={(codigo) => aplicarCupom(conversaId, codigo, agora)}
            aoRemover={() => removerCupomDoPedido(conversaId)}
          />
        )}
        <EscolhaDeMeio
          escolhido={meioEscolhido}
          aoEscolher={(meio) => escolherMeioPagamento(conversaId, meio)}
          pergunta="Como o cliente vai pagar?"
        />
        <Botao
          largo
          variante="primario"
          icone="dollar-sign"
          disabled={!editavel || !meioEscolhido}
          onClick={() => geracao.emitir(meioEscolhido)}
        >
          Gerar cobrança
        </Botao>
      </Bloco>
    )
  }

  // Cancelada conta como fechada mesmo sem `pagaEm` (achado da frente
  // Relógio, 24/09/2026): sem isto, o código copia-e-cola e "Trocar meio" de
  // um Pix morto continuavam na tela depois do cancelamento.
  const emAberto = !cobranca.pagaEm && !cobranca.canceladaEm
  // Defeito b (achado do arquiteto, 23/09/2026): item novo em pedido pago não
  // pode crescer o total calado. Por enquanto só impede o silêncio: destaca a
  // diferença e oferece "Cobrar diferença" (o mesmo caminho que a divergência
  // de pagamento já usa). Cobrança complementar completa é de outra frente.
  // Fonte única de "quanto já foi pago" (dominio/pagamento.js, registro 38):
  // depois de um complemento pago, não mostra de novo a parte original.
  const diferencaPosPagamento = diferencaAposPagamento(pedido, cardapio)
  const nome = nomeDoMeio(cobranca.metodoRecebido ?? cobranca.meio)

  // Maquininha ou vale (sem link): sem anel, sem prazo, uma linha só (decisão
  // 19, seção 4: "Linha 'Receber na entrega · maquininha · R$ 68,00'").
  if (!cobranca.link && !cobranca.pagaEm) {
    return (
      <Bloco titulo="Cobrança">
        <div className={css.cabecaCobranca}>
          <span className={css.rotuloTotal}>Total</span>
          <b className={css.valorForte}>{moeda(cobranca.valor)}</b>
        </div>
        <p className={css.linhaNaEntrega}>
          <Icone nome="credit-card" /> Receber na entrega · {nome} <b>{moeda(cobranca.valor)}</b>
        </p>
        {recebendo && (
          <BaixaAMao valorCobrado={Math.max(0, (pedido.totalApi ?? cobranca.valor) - (pedido.totalPagoApi ?? 0))}
            aoCancelar={() => setRecebendo(false)}
            aoConfirmar={async (valor, metodo) => { await confirmarPagamento(conversaId, valor, metodo); setRecebendo(false) }} />
        )}
        {editavel && !recebendo && (
          <div className={css.acoesCobranca}>
            <Botao
              largo
              variante="primario"
              icone="circle-check"
              onClick={() => pedido.pedidoId ? setRecebendo(true) : marcarRecebidoEntrega(conversaId, true, agora)}
            >
              Recebi
            </Botao>
            <Botao
              largo
              variante="secundario"
              onClick={() => marcarRecebidoEntrega(conversaId, false, agora)}
            >
              Ainda não recebi
            </Botao>
          </div>
        )}
        {editavel && (
          <Botao
            variante="texto"
            className={css.trocarMeio}
            onClick={() => refazerCobranca(conversaId, pedido, cardapio, agora)}
          >
            Refazer cobrança
          </Botao>
        )}
        {alterar}
      </Bloco>
    )
  }

  return (
    <Bloco
      titulo="Cobrança"
      destaque={situacao.chave === 'expirada' || situacao.chave === 'divergente' || diferencaPosPagamento > 0}
    >
      <div className={css.cabecaCobranca}>
        <span className={css.rotuloTotal}>Total</span>
        <b className={css.valorForte}>{moeda(cobranca.valor)}</b>
        <Pilula tom="neutro" fina>{nome}</Pilula>
        {cobranca.tentativa > 1 && <span className={css.tentativa}>{cobranca.tentativa}ª cobrança</span>}
      </div>

      {diferencaPosPagamento > 0 && (
        <div className={css.avisoDiferenca} role="alert">
          <p className={css.corpoBloco}>
            <Icone nome="alerta" /> Item novo depois do pagamento: <b>{moeda(diferencaPosPagamento)}</b> a cobrar.
          </p>
          <Botao
            largo
            variante="secundario"
            disabled={!editavel}
            onClick={() => setMeioDaDiferenca(true)}
          >
            Cobrar diferença
          </Botao>
          {meioDaDiferenca && editavel && (
            <EscolhaDeMeio
              escolhido={null}
              aoEscolher={(meio) => { setMeioDaDiferenca(false); reenvio.emitir(diferencaPosPagamento, meio) }}
              pergunta="Como o cliente vai pagar a diferença?"
            />
          )}
        </div>
      )}

      <RelogioPix
        key={cobranca.id}
        cobranca={cobranca}
        agora={agora}
        editavel={editavel}
        numeroPedido={pedido.numero}
        nomeCliente={nomeCliente}
        aoReenviar={() => reenvio.emitir()}
        aoCancelarPedido={aoCancelarPedido}
        aoEncerrarAtendimento={aoEncerrarAtendimento}
        aoAceitarDivergencia={aoAceitarDivergencia}
        aoCobrarDiferenca={() => reenvio.emitir(cobranca.valor - (cobranca.valorPago ?? 0))}
      />

      <ListaDados itens={[
        { rotulo: 'Recebido', valor: cobranca.valorPago != null ? moeda(cobranca.valorPago) : null },
        {
          rotulo: 'Comprovante',
          valor: cobranca.comprovanteEm ? horaCurta(new Date(cobranca.comprovanteEm).toISOString()) : null,
        },
      ]} />

      {emAberto && cobranca.copiaECola && <CopiaECola codigo={cobranca.copiaECola} />}

      {situacao.chave === 'aguardando' && !cobranca.comprovanteEm && editavel && (
        <Botao largo variante="secundario" icone="imagem" onClick={aoMarcarComprovante}>
          Conferir comprovante
        </Botao>
      )}

      {alterar}
    </Bloco>
  )
}
