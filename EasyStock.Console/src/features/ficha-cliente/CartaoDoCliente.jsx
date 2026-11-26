import { useState } from 'react'
import { apresentacaoDoNome, clienteSemNome, nomeDaConversa } from '../../dominio/nomeDoContato'
import { NomeDoContato } from '../../componentes/NomeDoContato'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Avatar } from '../../componentes/Avatar'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useCatalogo } from '../../aplicacao/contextos'
import { ehRestricao, ordenarTags, ROTULO_RESTRICAO, rotuloDaTag } from '../../dominio/cliente'
import { acoesDoCartao, ROTULO_CADASTRAR_CLIENTE } from '../../dominio/cadastroDoLead'
import { hashDoCadastroDoCliente } from '../../dominio/fichaCliente'
import { AcoesDoCliente } from './AcoesDoCliente'
import { ConsultaFreteCliente } from './ConsultaFreteCliente'
import { CorrecaoVinculoCliente } from './CorrecaoVinculoCliente'
import { QuemEDoCliente } from './QuemEDoCliente'
import css from './ficha.module.css'
import notasCss from './NotasDoCliente.module.css'

// #1839: o que se vê sempre no Resumo, sem abrir nada: quem é o cliente (resumo da IA), quanto já
// comprou no total, o que costuma pedir, as tags e a última nota interna como post-it. O resto
// (endereço, histórico, conversas) segue em "Cliente e histórico".
export function CartaoDoCliente({ conversa, fonteApi, aoAbrir, aoCadastrar, foto = null, aoVerEntrega, aoConsultarFrete, aoVerAoLado, focoFrete, mostrarEntrega = true }) {
  const { cliente } = conversa
  const fotoCadastro = useFotoCliente(cliente.fotoUrl)
  const [freteAberto, setFreteAberto] = useState(false)
  const semCadastro = conversa.conta === 'lead' || !conversa.clienteId
  const semPedido = !semCadastro && conversa.relacionamento === 'lead' // #2461
  // #2258 (T4): o mesmo nome do cartão da lista, do cabeçalho e dos botões "Abrir".
  const nome = apresentacaoDoNome(nomeDaConversa(conversa), { comanda: conversa.numeroComanda })
  // #2944 (E1-13): contato sem nome de verdade leva o selo, mesmo com o padrão do lead no lugar do nome.
  const capsula = nome.capsula ?? (clienteSemNome({ nome: conversa.nome, telefone: conversa.contato }) ? { texto: 'Sem nome', tom: 'neutro' } : null)
  const acoes = acoesDoCartao(conversa, { fonteApi })
  const { restricoes } = useCatalogo()
  const tags = ordenarTags(cliente.tags ?? [], restricoes).filter((tag, indice) => indice < 4 || ehRestricao(tag, restricoes))
  return (
    <section className={css.cartaoCliente} aria-label={`Pedido de ${nome.rotulo}`}>
      <button type="button" className={css.cartaoClienteAbrir} onClick={aoAbrir} aria-label={`Abrir ficha de ${nome.rotulo}`}>
        <span className={css.identidadeCliente}>
          <Avatar nome={conversa.nome} foto={fotoCadastro.url ?? foto} tamanho="grande" />
          <span className={css.dadosIdentidadeCliente}>
            <strong className={css.nomeClienteInline}>
              <NomeDoContato completo={nome.completo} curto={nome.curto} capsula={capsula} titulo={nome.titulo} />
            </strong>
          </span>
        </span>
        {semCadastro && <span className={css.numerosCliente}>Contato sem cadastro</span>}
        {semPedido && <span className={css.numerosCliente}>Cadastrado, ainda sem pedido</span>}
      </button>
      {/* #2780: cadastro desativado não some da conversa; o cartão diz e leva ao cadastro para reativar. */}
      {!semCadastro && cliente.inativo && (
        <p className={css.avisoInativo} role="status" data-aviso-inativo>
          <Icone nome="alerta" tamanho={16} />
          <span><strong>Cadastro desativado.</strong> Não aparece nas listas de clientes ativos. <a href={hashDoCadastroDoCliente(conversa.clienteId)}>Abrir o cadastro para reativar</a></span>
        </p>
      )}
      <QuemEDoCliente conversa={conversa} />
      {/* #2232 (F2): o endereço fica como texto; copiar e abrir no mapa estão na fileira de ações (#3265, R1). */}
      {cliente.endereco && (
        <p className={css.enderecoCliente}>
          <Icone nome="map-pin" tamanho={16} />
          <span className={css.enderecoClienteInline}>{cliente.endereco}</span>
        </p>
      )}
      <AcoesDoCliente conversa={conversa} conferir={acoes.vincular && !acoes.cadastrar} />
      {/* #3207: lead tem UM botão principal; vincular a um cadastro que já existe fica como ação discreta. */}
      {acoes.cadastrar && aoCadastrar && <Botao largo variante="primario" icone="user-plus" data-cadastrar-cliente onClick={aoCadastrar}>{ROTULO_CADASTRAR_CLIENTE}</Botao>}
      {acoes.cadastrar && acoes.vincular && <CorrecaoVinculoCliente key={conversa.id} conversa={conversa} rotulo={acoes.rotuloDoVinculo} />}
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
