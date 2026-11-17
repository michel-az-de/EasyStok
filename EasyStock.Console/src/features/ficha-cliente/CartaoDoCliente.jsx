import { useEffect, useState } from 'react'
import { apresentacaoDoNome } from '../../dominio/nomeDoContato'
import { NomeDoContato } from '../../componentes/NomeDoContato'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Avatar } from '../../componentes/Avatar'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useCatalogo } from '../../aplicacao/contextos'
import { ehRestricao, ordenarTags, ROTULO_RESTRICAO, rotuloDaTag } from '../../dominio/cliente'
import { hashDoModulo } from '../../dominio/modulos'
import { hashDoCadastroDoCliente } from '../../dominio/fichaCliente'
import { ConsultaFreteCliente } from './ConsultaFreteCliente'
import { CorrecaoVinculoCliente } from './CorrecaoVinculoCliente'
import { copiarTexto, linkDoMapa } from './apresentacaoDaFicha'
import css from './ficha.module.css'
import notasCss from './NotasDoCliente.module.css'

// #2232 (F2): o endereço do cartão tem ação: copiar (com retorno visível) e abrir no mapa em nova aba.
// Fora do botão que abre o cadastro, porque botão dentro de botão não é HTML válido.
function EnderecoDoCliente({ endereco }) {
  const [retorno, setRetorno] = useState(null) // 'copiado' | 'falhou'
  useEffect(() => {
    if (!retorno) return undefined
    const id = setTimeout(() => setRetorno(null), 2400)
    return () => clearTimeout(id)
  }, [retorno])
  const copiar = async () => setRetorno(await copiarTexto(endereco) ? 'copiado' : 'falhou')
  return (
    <div className={css.enderecoCliente}>
      <Icone nome="map-pin" tamanho={16} />
      <span className={css.enderecoClienteInline}>{endereco}</span>
      <span className={css.acoesEndereco}>
        <Botao variante="texto" icone="copy" onClick={copiar}>Copiar</Botao>
        <a className={css.abrirNoMapa} href={linkDoMapa(endereco)} target="_blank" rel="noopener noreferrer">
          <Icone nome="external-link" tamanho={16} />Abrir no mapa
        </a>
      </span>
      <output className={css.retornoEndereco} aria-live="polite">
        {retorno === 'copiado' ? 'Copiado' : retorno === 'falhou' ? 'Não foi possível copiar' : ''}
      </output>
    </div>
  )
}

// #1839: o que se vê sempre no Resumo, sem abrir nada: quem é o cliente (resumo da IA), quanto já
// comprou no total, o que costuma pedir, as tags e a última nota interna como post-it. O resto
// (endereço, histórico, conversas) segue em "Cliente e histórico".
export function CartaoDoCliente({ conversa, fonteApi, aoAbrir, foto = null, aoVerEntrega, aoConsultarFrete, aoVerAoLado, focoFrete, mostrarEntrega = true }) {
  const { cliente } = conversa
  const fotoCadastro = useFotoCliente(cliente.fotoUrl)
  const [freteAberto, setFreteAberto] = useState(false)
  const semCadastro = conversa.conta === 'lead' || !conversa.clienteId
  const semPedido = !semCadastro && conversa.relacionamento === 'lead' // #2461
  // #2258 (T4): o mesmo nome do cartão da lista, do cabeçalho e dos botões "Abrir".
  const nome = apresentacaoDoNome(conversa.nome, { comanda: conversa.numeroComanda })
  const { restricoes } = useCatalogo()
  const tags = ordenarTags(cliente.tags ?? [], restricoes).filter((tag, indice) => indice < 4 || ehRestricao(tag, restricoes))
  return (
    <section className={css.cartaoCliente} aria-label={`Cliente ${nome.rotulo}`}>
      <button type="button" className={css.cartaoClienteAbrir} onClick={aoAbrir} aria-label={`Abrir ficha de ${nome.rotulo}`}>
        <span className={css.identidadeCliente}>
          <Avatar nome={conversa.nome} foto={fotoCadastro.url ?? foto} tamanho="grande" />
          <span className={css.dadosIdentidadeCliente}>
            <strong className={css.nomeClienteInline}>
              <NomeDoContato completo={nome.completo} curto={nome.curto} capsula={nome.capsula} titulo={nome.titulo} />
            </strong>
          </span>
        </span>
        {semCadastro && <span className={css.numerosCliente}>Contato sem cadastro</span>}
        {semPedido && <span className={css.numerosCliente}>Cadastrado, ainda sem pedido</span>}
      </button>
      {/* #2780: cadastro desativado não some da conversa; o cartão diz e leva ao cadastro para reativar. */}
      {!semCadastro && cliente.inativo && (
        <p className={css.avisoInativo} data-aviso-inativo>
          <Icone nome="alerta" tamanho={16} />
          <span><strong>Cadastro desativado.</strong> Não aparece nas listas de clientes ativos. <a href={hashDoCadastroDoCliente(conversa.clienteId)}>Abrir o cadastro para reativar</a></span>
        </p>
      )}
      {cliente.endereco && <EnderecoDoCliente endereco={cliente.endereco} />}
      {fonteApi && <CorrecaoVinculoCliente key={conversa.id} conversa={conversa} />}
      {fonteApi && !conversa.clienteId && <Botao variante="texto" onClick={() => { window.location.hash = hashDoModulo('cadastros', 'clientes') }}>Conferir cadastros</Botao>}
      {mostrarEntrega && <div className={notasCss.acoesCliente}>
        {aoVerEntrega
          ? <Botao data-foco-reserva={focoFrete} variante="texto" icone="moto" aria-label="Ver frete e entrega" onClick={aoConsultarFrete ?? aoVerEntrega}>Frete</Botao>
          : fonteApi && conversa.clienteId && cliente.endereco ? <Botao data-foco-reserva={focoFrete} variante="texto" icone="moto" aria-label="Consultar frete" onClick={aoConsultarFrete ?? (() => setFreteAberto(true))}>Frete</Botao>
            : fonteApi && conversa.clienteId && <Botao variante="texto" icone="map-pin" onClick={aoAbrir}>Adicionar endereço</Botao>}
      </div>}
      {tags.length > 0 && (
        // #2734: o cartão mostra só o resumo; a lista completa ("Tags do cliente") fica na Ficha, logo abaixo.
        <ul className={css.tagsCliente} aria-label="Resumo das tags do cliente">
          {tags.map((t) => <li key={t} className={ehRestricao(t, restricoes) ? css.tagRestricao : undefined}>
            {ehRestricao(t, restricoes) && <Icone nome="alerta" tamanho={16} rotulo={ROTULO_RESTRICAO} />}{rotuloDaTag(t)}</li>)}
        </ul>
      )}
      {freteAberto && <ConsultaFreteCliente conversa={conversa} aoFechar={() => setFreteAberto(false)}
        aoVerAoLado={aoVerAoLado ? (dados) => { setFreteAberto(false); aoVerAoLado(dados) } : undefined}
        aoEditarEndereco={() => { setFreteAberto(false); aoAbrir() }} />}
    </section>
  )
}
