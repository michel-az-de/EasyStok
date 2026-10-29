import { useState } from 'react'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Avatar } from '../../componentes/Avatar'
import { Botao } from '../../componentes/Botao'
import { ConsultaFreteCliente } from './ConsultaFreteCliente'
import css from './ficha.module.css'
import notasCss from './NotasDoCliente.module.css'

// #1839: o que se vê sempre no Resumo, sem abrir nada: quem é o cliente (resumo da IA), quanto já
// comprou no total, o que costuma pedir, as tags e a última nota interna como post-it. O resto
// (endereço, histórico, conversas) segue em "Cliente e histórico".
export function CartaoDoCliente({ conversa, fonteApi, aoAbrir, foto = null, aoVerEntrega, aoConsultarFrete, aoVerAoLado, focoFrete }) {
  const { cliente } = conversa
  const fotoCadastro = useFotoCliente(cliente.fotoUrl)
  const [freteAberto, setFreteAberto] = useState(false)
  const lead = conversa.conta === 'lead' || !conversa.clienteId
  const tags = (cliente.tags ?? []).slice(0, 4)
  const email = cliente.email || conversa.contatoInformado?.email
  return (
    <section className={css.cartaoCliente} aria-label={`Cliente ${conversa.nome}`}>
      <button type="button" className={css.cartaoClienteAbrir} onClick={aoAbrir} aria-label={`Abrir ficha de ${conversa.nome}`}>
        <span className={css.identidadeCliente}>
          <Avatar nome={conversa.nome} foto={fotoCadastro.url ?? foto} tamanho="grande" />
          <span className={css.dadosIdentidadeCliente}>
            <strong className={css.nomeClienteInline}>{conversa.nome}</strong>
            {cliente.telefone && <span className={css.contatoClienteInline}>{cliente.telefone}</span>}
            {email && <span className={css.contatoClienteInline}>{email}</span>}
          </span>
        </span>
        {cliente.endereco && <span className={css.enderecoClienteInline}>{cliente.endereco}</span>}
        {lead && <span className={css.numerosCliente}>Contato sem cadastro</span>}
      </button>
      <div className={notasCss.acoesCliente}>
        {aoVerEntrega
          ? <Botao data-foco-reserva={focoFrete} className={css.freteCliente} variante="texto" icone="moto" aria-label="Ver frete e entrega" onClick={aoConsultarFrete ?? aoVerEntrega}>Frete</Botao>
          : fonteApi && conversa.clienteId && cliente.endereco ? <Botao data-foco-reserva={focoFrete} className={css.freteCliente} variante="texto" icone="moto" aria-label="Calcular frete" onClick={aoConsultarFrete ?? (() => setFreteAberto(true))}>Frete</Botao>
            : fonteApi && conversa.clienteId && <Botao variante="texto" icone="map-pin" onClick={aoAbrir}>Adicionar endereço</Botao>}
      </div>
      {tags.length > 0 && (
        <ul className={css.tagsCliente} aria-label="Tags do cliente">
          {tags.map((t) => <li key={t}>{t}</li>)}
        </ul>
      )}
      {freteAberto && <ConsultaFreteCliente conversa={conversa} aoFechar={() => setFreteAberto(false)}
        aoVerAoLado={aoVerAoLado ? (dados) => { setFreteAberto(false); aoVerAoLado(dados) } : undefined}
        aoEditarEndereco={() => { setFreteAberto(false); aoAbrir() }} />}
    </section>
  )
}
