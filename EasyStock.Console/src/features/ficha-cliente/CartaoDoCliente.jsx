import { useState } from 'react'
import { useResumoDoCliente } from '../../aplicacao/useResumoDoCliente'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Avatar } from '../../componentes/Avatar'
import { Botao } from '../../componentes/Botao'
import { ConsultaFreteCliente } from './ConsultaFreteCliente'
import { NotasDoCliente } from './NotasDoCliente'
import { moeda } from '../../dominio/formato'
import css from './ficha.module.css'
import notasCss from './NotasDoCliente.module.css'

// #1839: o que se vê sempre no Resumo, sem abrir nada: quem é o cliente (resumo da IA), quanto já
// comprou no total, o que costuma pedir, as tags e a última nota interna como post-it. O resto
// (endereço, histórico, conversas) segue em "Cliente e histórico".
export function CartaoDoCliente({ conversa, fonteApi, aoAbrir, foto = null, aoVerEntrega, aoCriarNota, aoConsultarFrete, aoVerAoLado, focoFrete }) {
  const { cliente } = conversa
  const fotoCadastro = useFotoCliente(cliente.fotoUrl)
  const [freteAberto, setFreteAberto] = useState(false)
  const lead = conversa.conta === 'lead' || !conversa.clienteId
  const pedidos = cliente.pedidos ?? 0
  // `totalGasto` só existe depois que o dossiê chegou; antes disso os números ainda não valem.
  const dossieLido = cliente.totalGasto !== undefined
  const notasDoResumo = (cliente.notas ?? []).map((n) => `${n.id}:${n.criadoEm}`).join(',')
  const resumo = useResumoDoCliente(conversa.id, `${pedidos}|${notasDoResumo}`, fonteApi && !lead && dossieLido)
  const numeros = pedidos > 0
    ? [`${pedidos} ${pedidos === 1 ? 'pedido' : 'pedidos'} no total`,
      cliente.totalGasto > 0 ? `${moeda(cliente.totalGasto)} em compras` : null,
      cliente.favorito ? `costuma pedir ${cliente.favorito}` : null].filter(Boolean).join(' · ')
    : lead ? 'Contato sem cadastro' : dossieLido || !fonteApi ? 'Ainda não comprou com a casa' : null
  // #1920: a IA devolve a frase com ponto final ("Ainda não comprou com a casa."); comparar sem a
  // pontuação do fim para o cartão não repetir a mesma frase em duas linhas.
  const mesmaFrase = (a, b) => Boolean(a && b) && a.trim().replace(/[.!]+$/, '') === b.trim().replace(/[.!]+$/, '')
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
        {resumo.texto && !mesmaFrase(resumo.texto, numeros) && <span className={css.resumoIa}>{resumo.texto}</span>}
        {resumo.carregando && <span className={css.resumoIaCarregando}>Lendo o histórico do cliente…</span>}
        {numeros && <span className={css.numerosCliente}>{numeros}</span>}
      </button>
      <div className={notasCss.acoesCliente}>
        {aoCriarNota && <Botao variante="texto" icone="plus" aria-label="Adicionar nota" onClick={aoCriarNota}
          disabled={fonteApi && !conversa.clienteId} title={fonteApi && !conversa.clienteId ? 'Cadastre o cliente para adicionar uma nota.' : undefined}>Nota</Botao>}
        {aoVerEntrega
          ? <Botao data-foco-reserva={focoFrete} className={css.freteCliente} variante="texto" icone="moto" aria-label="Ver frete e entrega" onClick={aoConsultarFrete ?? aoVerEntrega}>Frete</Botao>
          : fonteApi && conversa.clienteId && cliente.endereco ? <Botao data-foco-reserva={focoFrete} className={css.freteCliente} variante="texto" icone="moto" onClick={aoConsultarFrete ?? (() => setFreteAberto(true))}>Calcular frete</Botao>
            : fonteApi && conversa.clienteId && <Botao variante="texto" icone="map-pin" onClick={aoAbrir}>Adicionar endereço</Botao>}
      </div>
      {tags.length > 0 && (
        <ul className={css.tagsCliente} aria-label="Tags do cliente">
          {tags.map((t) => <li key={t}>{t}</li>)}
        </ul>
      )}
      <NotasDoCliente key={conversa.id} conversaId={conversa.id} clienteId={conversa.clienteId ?? conversa.id}
        clienteNome={conversa.nome} fonteApi={fonteApi && Boolean(conversa.clienteId)} notas={cliente.notas ?? []} />
      {freteAberto && <ConsultaFreteCliente conversa={conversa} aoFechar={() => setFreteAberto(false)}
        aoVerAoLado={aoVerAoLado ? (dados) => { setFreteAberto(false); aoVerAoLado(dados) } : undefined}
        aoEditarEndereco={() => { setFreteAberto(false); aoAbrir() }} />}
    </section>
  )
}
