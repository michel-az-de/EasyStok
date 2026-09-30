// A janela do Cardápio por link (rodada 7, US-021): `window.open('#/cardapio-link/<id>', ...)`
// abre isto SEM `AtendimentoProvider` por perto, mesmo desenho de
// `features/entregas/TelaEntregas.jsx` (usado como modelo) — espelho do
// Balcão pelo mesmo `infra/canalEntreJanelas.js`, nunca uma segunda árvore de
// estado. Sem resposta do Balcão em 1 s, mostra só o aviso (mas em
// linguagem de cliente: quem abre este link nunca viu a palavra "Balcão").
//
// Rodada 12 (#19), feedback da Thatiane (áudio de 26/09/2026): "em vez de
// mandar só um link simples, uma página mais robusta, com mais imagens, onde
// o cliente marca os itens e a quantidade, e o pedido volta pronto". Vitrine
// estilo iFood: foto por prato, categorias, busca, quantidade, observação na
// linha do item (RN-20), sacola com subtotal e "Enviar pedido". O pedido
// volta pela mesma conversa já com a cobrança no meio escolhido: Pix e cartão
// pagam aqui mesmo (decisão do dono de 24/09, mantida no registro 98);
// maquininha e vale, na entrega. Com observação, a conversa sobe para ela
// conferir.
//
// Desktop e celular com o mesmo componente: no largo, vitrine e sacola lado a
// lado; no estreito, a sacola vira passo próprio pela barra de baixo (CSS).
import { useRef, useState } from 'react'
import { useRelogio } from '../../hooks/useRelogio'
import { useEspelhoCardapioLink } from '../../aplicacao/useEspelhoCardapioLink'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import {
  PREFIXO_ROTA, TODAS_AS_CATEGORIAS, avisoDeEndereco, carrinhoComItem, carrinhoComObservacao,
  carrinhoComQuantidade, carrinhoVazio, carrinhoVazioDeItens, filtrarVitrine, meioTemLink, meiosDaPagina,
  pedidoLinkPodeSerConfirmado, podeAdicionarAoCarrinho, proximoNumeroDoPedido, quantidadeNoCarrinho,
  subtotalDoCarrinho, vitrineDoCardapio,
} from '../../dominio/cardapioLink'
import { adicionaisDoItem } from '../../dominio/cardapio'
import { fotoDoPrato } from '../../dominio/arteCardapio'
import { itensDetalhados } from '../../dominio/pedido'
import { nomeDoMeio } from '../../dominio/cobranca'
import { janelaDisponivel, ocupacaoDeHoje, rotuloDaOcupacao } from '../../dominio/entrega'
import { moeda } from '../../dominio/formato'
import { primeiroNome } from '../../dominio/mensagem'
import css from './cardapioLink.module.css'

export function TelaCardapioLink() {
  const { estado, semBalcao, acoes } = useEspelhoCardapioLink()
  const conversaId = window.location.hash.slice(PREFIXO_ROTA.length)

  if (!estado) {
    return semBalcao ? (
      <PaginaAviso titulo="Não consegui abrir seu pedido agora">
        Tente de novo em instantes ou peça um link novo pelo WhatsApp.
      </PaginaAviso>
    ) : null
  }
  return <TelaCardapioLinkPronta estado={estado} conversaId={conversaId} acoes={acoes} />
}

function TelaCardapioLinkPronta({ estado, conversaId, acoes }) {
  const agoraLocal = useRelogio(estado.catalogo.instanteInicial)
  const agora = agoraLocal + (estado.relogio?.deslocamentoMs ?? 0)
  const conversa = estado.conversas.find((c) => c.id === conversaId)

  if (!conversa) {
    return (
      <PaginaAviso titulo="Esse link não é mais válido">
        Peça um cardápio novo pelo WhatsApp da Casa da Baba.
      </PaginaAviso>
    )
  }

  return (
    <Loja conversa={conversa} conversas={estado.conversas} catalogo={estado.catalogo} agora={agora} acoes={acoes} />
  )
}

function PaginaAviso({ titulo, children }) {
  return (
    <div className={css.pagina}>
      <div className={css.avisoCartao}>
        <Icone nome="cardapio" tamanho={32} />
        <h1>{titulo}</h1>
        <p>{children}</p>
      </div>
    </div>
  )
}

const SITUACAO_DA_ETAPA = {
  pagamento: { rotulo: 'Aguardando pagamento', tom: 'aviso' },
  pago: { rotulo: 'Pago', tom: 'ok' },
  naEntrega: { rotulo: 'Pedido enviado', tom: 'ok' },
}

function Cabecalho({ etapa }) {
  const situacao = SITUACAO_DA_ETAPA[etapa] ?? { rotulo: 'Cardápio de hoje', tom: 'neutro' }
  return (
    <header className={css.cabecalho}>
      <div className={css.cabecalhoMiolo}>
        <div className={css.marca}>
          <span className={css.marcaIcone}><Icone nome="cardapio" tamanho={22} /></span>
          <div>
            <strong>Casa da Baba</strong>
            <span className={css.marcaSub}>Massa artesanal</span>
          </div>
        </div>
        <Pilula tom={situacao.tom}>{situacao.rotulo}</Pilula>
      </div>
    </header>
  )
}

// Componente central: dono do carrinho e dos passos. O pedido só existe de
// verdade (na conversa, na ficha) depois de "Enviar pedido": RN-23, o pedido
// nasce antes do pagamento. "Simular pagamento" só dá a baixa depois.
function Loja({ conversa, conversas, catalogo, agora, acoes }) {
  const [carrinho, setCarrinho] = useState(carrinhoVazio)
  const [busca, setBusca] = useState('')
  const [categoria, setCategoria] = useState(TODAS_AS_CATEGORIAS)
  const [sacolaAberta, setSacolaAberta] = useState(false)
  const [janelaEscolhida, setJanelaEscolhida] = useState('')
  const [endereco, setEndereco] = useState(conversa.cliente.endereco ?? '')
  const [meioEscolhido, setMeioEscolhido] = useState('')
  const [enviado, setEnviado] = useState(null)
  const [etapa, setEtapa] = useState(null)
  // A página rola por dentro (base.css trava o body), então voltar ao topo
  // ao trocar de passo é no contêiner, não na janela.
  const paginaRef = useRef(null)
  const irAoTopo = () => paginaRef.current?.scrollTo?.(0, 0)

  const vitrine = vitrineDoCardapio(catalogo.cardapio, catalogo.linhas)
  const grupos = filtrarVitrine(vitrine, { busca, categoria })
  const qtdNoCarrinho = (sku) => carrinho.itens.find((l) => l.sku === sku)?.qtd ?? 0
  const obsNoCarrinho = (sku) => carrinho.itens.find((l) => l.sku === sku)?.obs ?? ''
  const ajustar = (sku, delta) => setCarrinho((c) => carrinhoComQuantidade(c, sku, delta))
  const adicionar = (sku) => setCarrinho((c) => carrinhoComItem(c, sku))
  const anotar = (sku, texto) => setCarrinho((c) => carrinhoComObservacao(c, sku, texto))

  const enviarPedido = () => {
    const pedido = {
      id: conversa.id,
      agora,
      numero: proximoNumeroDoPedido(conversas),
      // Observação sem espaço sobrando: "sem cebola " vira "sem cebola".
      itens: carrinho.itens.map((l) => ({ ...l, obs: l.obs.trim() })),
      janela: janelaEscolhida,
      endereco: endereco.trim(),
      meio: meioEscolhido,
    }
    const emissao = acoes.criarPedidoCardapioLink({ ...pedido, cardapio: catalogo.cardapio })
    setEnviado({ ...pedido, emissao })
    setEtapa(meioTemLink(pedido.meio) ? 'pagamento' : 'naEntrega')
    irAoTopo()
  }

  const simularPagamento = () => {
    acoes.confirmarPagamento(conversa.id, agora)
    setEtapa('pago')
    irAoTopo()
  }

  if (enviado) {
    return (
      <div className={css.pagina} ref={paginaRef}>
        <Cabecalho etapa={etapa} />
        <PassoEnviado
          enviado={enviado}
          etapa={etapa}
          catalogo={catalogo}
          nomeCliente={conversa.nome}
          aoSimularPagamento={simularPagamento}
        />
      </div>
    )
  }

  const qtdTotal = quantidadeNoCarrinho(carrinho)
  const subtotal = subtotalDoCarrinho(carrinho, catalogo.cardapio)

  return (
    <div className={css.pagina} ref={paginaRef}>
      <Cabecalho />
      <div className={`${css.loja} ${sacolaAberta ? css.comSacolaAberta : ''}`}>
        <main className={css.vitrine}>
          <p className={css.introducao}>
            Oi, {primeiroNome(conversa.nome)}! Marque os pratos e as quantidades. A Thatiane confere
            seu pedido e confirma por aqui.
          </p>

          <div className={css.ferramentas}>
            <div className={css.busca}>
              <Icone nome="search" />
              <CampoTexto
                rotulo="Buscar no cardápio"
                rotuloOculto
                tipo="search"
                placeholder="Buscar prato"
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
              />
            </div>
            <div role="radiogroup" aria-label="Categorias" className={css.categorias}>
              <Chip papel="escolha" ativo={categoria === TODAS_AS_CATEGORIAS} onClick={() => setCategoria(TODAS_AS_CATEGORIAS)}>
                Todos
              </Chip>
              {vitrine.map((g) => (
                <Chip key={g.chave} papel="escolha" ativo={categoria === g.chave} onClick={() => setCategoria(g.chave)}>
                  {g.rotulo}
                </Chip>
              ))}
            </div>
          </div>

          {vitrine.length === 0 && (
            <Vazio titulo="Sem itens no cardápio hoje">Volte mais tarde ou fale com a gente pelo WhatsApp.</Vazio>
          )}
          {vitrine.length > 0 && grupos.length === 0 && (
            <Vazio titulo="Nada com esse nome hoje">Tente outra palavra ou veja todos os pratos.</Vazio>
          )}

          {grupos.map((grupo) => (
            <section key={grupo.chave} className={css.grupo} aria-labelledby={`grupo-${grupo.chave}`}>
              <h2 id={`grupo-${grupo.chave}`} className={css.rotuloGrupo}>{grupo.rotulo}</h2>
              {grupo.dica && <p className={css.dicaGrupo}>{grupo.dica}</p>}
              <ul className={css.grade}>
                {grupo.itens.map((item) => (
                  <CartaoItem
                    key={item.sku}
                    item={item}
                    qtd={qtdNoCarrinho(item.sku)}
                    obs={obsNoCarrinho(item.sku)}
                    adicionais={adicionaisDoItem(catalogo.cardapio, catalogo.adicionais, item.sku).filter(podeAdicionarAoCarrinho)}
                    aoAdicionar={adicionar}
                    aoAjustar={ajustar}
                    aoAnotar={anotar}
                  />
                ))}
              </ul>
            </section>
          ))}
        </main>

        <aside className={css.sacola} aria-label="Sua sacola">
          <Sacola
            carrinho={carrinho}
            catalogo={catalogo}
            conversas={conversas}
            agora={agora}
            subtotal={subtotal}
            janelaEscolhida={janelaEscolhida}
            aoEscolherJanela={setJanelaEscolhida}
            endereco={endereco}
            aoMudarEndereco={setEndereco}
            meioEscolhido={meioEscolhido}
            aoEscolherMeio={setMeioEscolhido}
            aoAjustar={ajustar}
            aoEnviar={enviarPedido}
            aoVoltar={() => setSacolaAberta(false)}
          />
        </aside>

        {qtdTotal > 0 && !sacolaAberta && (
          <div className={css.barraCarrinho}>
            <span className={css.barraResumo}>
              <Icone nome="shopping-cart" />
              {qtdTotal} {qtdTotal === 1 ? 'item' : 'itens'} · {moeda(subtotal)}
            </span>
            <Botao variante="primario" onClick={() => { setSacolaAberta(true); irAoTopo() }}>
              Ver sacola
            </Botao>
          </div>
        )}
      </div>
    </div>
  )
}

function CartaoItem({ item, qtd, obs, adicionais, aoAdicionar, aoAjustar, aoAnotar }) {
  const indisponivel = Boolean(item.motivoIndisponivel)
  const podeSomarMais = qtd < item.estoque
  return (
    <li className={`${css.cartao} ${indisponivel ? css.cartaoIndisponivel : ''} ${qtd > 0 ? css.cartaoMarcado : ''}`}>
      <img className={css.foto} src={fotoDoPrato(item)} alt={`Ilustração do prato ${item.nome}`} loading="lazy" />
      <div className={css.cartaoTexto}>
        <strong className={css.cartaoNome}>{item.nome}</strong>
        <span className={css.cartaoPorcao}>{item.porcao}</span>
        <span className={css.cartaoPreco}>{moeda(item.preco)}</span>
        {indisponivel && <Pilula tom="neutro" fina>{item.motivoIndisponivel}</Pilula>}
      </div>
      <div className={css.cartaoAcao}>
        {indisponivel && (
          <Botao variante="secundario" disabled aria-label={`${item.nome}: ${item.motivoIndisponivel}`}>
            Indisponível
          </Botao>
        )}
        {!indisponivel && qtd === 0 && (
          <Botao variante="secundario" icone="mais" aria-label={`Adicionar ${item.nome}`} onClick={() => aoAdicionar(item.sku)}>
            Adicionar
          </Botao>
        )}
        {!indisponivel && qtd > 0 && (
          <span className={css.contador}>
            <button type="button" aria-label={`Tirar uma unidade de ${item.nome}`} onClick={() => aoAjustar(item.sku, -1)}>
              <Icone nome="menos" />
            </button>
            <b aria-live="polite">{qtd}</b>
            <button
              type="button"
              aria-label={`Mais uma unidade de ${item.nome}`}
              disabled={!podeSomarMais}
              onClick={() => aoAjustar(item.sku, 1)}
            >
              <Icone nome="mais" />
            </button>
          </span>
        )}
      </div>
      {!indisponivel && qtd > 0 && (
        <div className={css.cartaoObs}>
          <CampoTexto
            rotulo={`Observação para ${item.nome}`}
            rotuloOculto
            placeholder="Alguma observação? Ex.: sem cebola"
            maxLength={80}
            value={obs}
            onChange={(e) => aoAnotar(item.sku, e.target.value)}
          />
        </div>
      )}
      {/* Integração 50: prato esgotado não oferece o extra dele. */}
      {adicionais.length > 0 && !indisponivel && (
        <ul className={css.adicionaisLista}>
          {adicionais.map((ad) => (
            <li key={ad.sku}>
              <Chip papel="filtro" icone="mais" onClick={() => aoAdicionar(ad.sku)}>
                {ad.nome} · {moeda(ad.preco)}
              </Chip>
            </li>
          ))}
        </ul>
      )}
    </li>
  )
}

// O que ainda falta para "Enviar pedido", escrito, para o botão nunca ficar
// desabilitado sem motivo.
function faltasDoEnvio({ carrinho, janelaEscolhida, avisoEndereco, meioEscolhido }) {
  return [
    carrinhoVazioDeItens(carrinho) && 'marque um prato',
    !janelaEscolhida && 'escolha o horário',
    avisoEndereco && 'confira o endereço',
    !meioEscolhido && 'escolha o pagamento',
  ].filter(Boolean)
}

function Sacola({
  carrinho, catalogo, conversas, agora, subtotal, janelaEscolhida, aoEscolherJanela, endereco, aoMudarEndereco,
  meioEscolhido, aoEscolherMeio, aoAjustar, aoEnviar, aoVoltar,
}) {
  const itens = itensDetalhados(carrinho, catalogo.cardapio)
  // Mesma ocupação que a Ficha usa (SeletorJanela, dominio/entrega.js): conta
  // pedido de verdade nas conversas e já aplica o corte de 60 min antes da
  // faixa (RN-22, respiro na entrega).
  const ocupacoes = ocupacaoDeHoje(catalogo.janelas, conversas, agora, null, catalogo.cardapio)
  const opcoesJanela = [
    { valor: '', rotulo: 'Escolha um horário', desabilitada: true },
    ...ocupacoes.map((o) => ({
      valor: o.id, desabilitada: !janelaDisponivel(o), rotulo: `${o.faixa} · ${rotuloDaOcupacao(o)}`,
    })),
  ]
  const avisoEndereco = avisoDeEndereco(endereco, catalogo.prefixosCepAtendidos)
  const podeEnviar = pedidoLinkPodeSerConfirmado({
    carrinho, janelaId: janelaEscolhida, endereco, meio: meioEscolhido,
    prefixosCepAtendidos: catalogo.prefixosCepAtendidos,
  })
  const faltas = faltasDoEnvio({ carrinho, janelaEscolhida, avisoEndereco, meioEscolhido })
  const { principais, naEntrega } = meiosDaPagina(catalogo.meiosDePagamento)

  return (
    <div className={css.sacolaCorpo}>
      <Botao variante="texto" className={css.voltar} onClick={aoVoltar}>
        <Icone nome="arrow-left" /> Voltar ao cardápio
      </Botao>

      <section className={css.blocoCheckout}>
        <h2><Icone nome="shopping-cart" /> Sua sacola</h2>
        {itens.length === 0 ? (
          <p className={css.sacolaVazia}>Marque os pratos no cardápio. Eles aparecem aqui.</p>
        ) : (
          <ul className={css.listaResumo}>
            {itens.map((linha) => (
              <li key={linha.sku} className={css.linhaResumo}>
                <span className={css.linhaResumoNome}>
                  {linha.produto?.nome}
                  {linha.obs.trim() && <em className={css.linhaResumoObs}>{linha.obs.trim()}</em>}
                </span>
                <span className={css.contador}>
                  <button
                    type="button"
                    aria-label={`Tirar uma unidade de ${linha.produto?.nome}`}
                    onClick={() => aoAjustar(linha.sku, -1)}
                  >
                    <Icone nome="menos" />
                  </button>
                  <b>{linha.qtd}</b>
                  <button
                    type="button"
                    aria-label={`Mais uma unidade de ${linha.produto?.nome}`}
                    disabled={linha.qtd >= (linha.produto?.estoque ?? 0)}
                    onClick={() => aoAjustar(linha.sku, 1)}
                  >
                    <Icone nome="mais" />
                  </button>
                </span>
                <span className={css.linhaResumoPreco}>{moeda((linha.produto?.preco ?? 0) * linha.qtd)}</span>
              </li>
            ))}
          </ul>
        )}
        <p className={css.total}>
          <span>Subtotal</span>
          <b>{moeda(subtotal)}</b>
        </p>
      </section>

      <section className={css.blocoCheckout}>
        <h2><Icone nome="relogio" /> Horário de entrega</h2>
        <CampoSelecao
          rotulo="Horário de entrega"
          rotuloOculto
          opcoes={opcoesJanela}
          value={janelaEscolhida}
          onChange={(e) => aoEscolherJanela(e.target.value)}
        />
      </section>

      <section className={css.blocoCheckout}>
        <h2><Icone nome="map-pin" /> Endereço de entrega</h2>
        <CampoTexto
          rotulo="Endereço completo, com CEP"
          rotuloOculto
          placeholder="Rua, número, bairro, CEP"
          value={endereco}
          onChange={(e) => aoMudarEndereco(e.target.value)}
        />
        {avisoEndereco && endereco.trim() && <p className={css.avisoCampo} role="alert">{avisoEndereco}</p>}
      </section>

      <section className={css.blocoCheckout}>
        <h2><Icone nome="credit-card" /> Pagamento</h2>
        <GrupoDeMeios rotulo="Pagar agora" meios={principais} escolhido={meioEscolhido} aoEscolher={aoEscolherMeio} />
        <GrupoDeMeios rotulo="Na entrega" meios={naEntrega} escolhido={meioEscolhido} aoEscolher={aoEscolherMeio} discreto />
      </section>

      <Botao largo variante="primario" icone="enviar" disabled={!podeEnviar} onClick={aoEnviar}>
        Enviar pedido · {moeda(subtotal)}
      </Botao>
      {!podeEnviar && faltas.length > 0 && (
        <p className={css.faltas}>Para enviar: {faltas.join(', ')}.</p>
      )}
    </div>
  )
}

// Pix e cartão em destaque, pagos aqui; maquininha e vale numa segunda
// linha, menor (mesma hierarquia da PR #18 na Ficha).
function GrupoDeMeios({ rotulo, meios, escolhido, aoEscolher, discreto = false }) {
  if (meios.length === 0) return null
  return (
    <div className={css.grupoMeios}>
      <p className={discreto ? css.rotuloMeiosDiscreto : css.rotuloMeios}>{rotulo}</p>
      <div role="radiogroup" aria-label={rotulo} className={css.opcoesPagamento}>
        {meios.map((meio) => (
          <Chip
            key={meio.id}
            papel="escolha"
            className={discreto ? css.meioDiscreto : css.meioDestaque}
            icone={meio.id === 'pix' ? 'dollar-sign' : 'credit-card'}
            ativo={escolhido === meio.id}
            onClick={() => aoEscolher(meio.id)}
          >
            {meio.rotulo}
          </Chip>
        ))}
      </div>
    </div>
  )
}

const TITULO_DA_ETAPA = {
  pagamento: (numero) => `Pedido ${numero} feito!`,
  pago: (numero, nome) => `Pago! Obrigada, ${nome}.`,
  naEntrega: (numero, nome) => `Pedido ${numero} enviado, ${nome}!`,
}

function PassoEnviado({ enviado, etapa, catalogo, nomeCliente, aoSimularPagamento }) {
  const { numero, itens, meio, janela, endereco, emissao } = enviado
  const linhas = itensDetalhados({ itens }, catalogo.cardapio)
  const faixa = catalogo.janelas.find((j) => j.id === janela)?.faixa
  const rotuloDoMeio = catalogo.meiosDePagamento.find((m) => m.id === meio)?.rotulo ?? nomeDoMeio(meio)
  const total = moeda(subtotalDoCarrinho({ itens }, catalogo.cardapio))
  return (
    <div className={css.corpo}>
      <section className={`${css.blocoCheckout} ${css.blocoCentro}`}>
        <span className={etapa === 'pagamento' ? css.seloAguardando : css.seloEnviado}>
          <Icone nome={etapa === 'pagamento' ? 'hourglass' : 'circle-check'} tamanho={32} />
        </span>
        <h2>{TITULO_DA_ETAPA[etapa](numero, primeiroNome(nomeCliente))}</h2>
        {etapa === 'pagamento' && (
          <>
            <p>Total {total} no {nomeDoMeio(meio)}.</p>
            <div className={css.pagamentoMock}>
              <p className={css.legendaMock}>{meio === 'pix' ? 'Pix copia e cola' : 'Link de pagamento'}</p>
              <code className={css.codigoMock}>{meio === 'pix' ? emissao.copiaECola : emissao.link}</code>
            </div>
            <Botao largo variante="primario" icone="circle-check" onClick={aoSimularPagamento}>
              Simular pagamento
            </Botao>
            <p className={css.notaFechar}>Pode fechar e pagar depois: a Thatiane já vê seu pedido por aqui.</p>
          </>
        )}
        {etapa === 'pago' && <p>Pedido {numero}, {total} no {nomeDoMeio(meio)}. Já avisamos a Thatiane.</p>}
        {etapa === 'naEntrega' && <p>Você paga na entrega, por {nomeDoMeio(meio)}. Já avisamos a Thatiane.</p>}
      </section>
      <section className={css.blocoCheckout}>
        <h2>Seu pedido</h2>
        <ul className={css.listaResumo}>
          {linhas.map((l) => (
            <li key={l.sku} className={css.linhaResumo}>
              <span className={css.linhaResumoNome}>
                {l.qtd}× {l.produto?.nome}
                {l.obs && <em className={css.linhaResumoObs}>{l.obs}</em>}
              </span>
              <span className={css.linhaResumoPreco}>{moeda((l.produto?.preco ?? 0) * l.qtd)}</span>
            </li>
          ))}
        </ul>
        <p className={css.total}>
          <span>Total</span>
          <b>{total}</b>
        </p>
        <dl className={css.detalhes}>
          <dt>Horário</dt><dd>{faixa ?? janela}</dd>
          <dt>Endereço</dt><dd>{endereco}</dd>
          <dt>Pagamento</dt><dd>{rotuloDoMeio}</dd>
        </dl>
      </section>
      <p className={css.notaFechar}>Qualquer coisa, é só chamar pelo mesmo canal.</p>
    </div>
  )
}
