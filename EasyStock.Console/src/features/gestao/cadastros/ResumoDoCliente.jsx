import { useFotoCliente } from '../../../aplicacao/useFotoCliente'
import { Avatar } from '../../../componentes/Avatar'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { dadosDeContato, situacaoDaLinha } from '../../../dominio/cadastros'
import { quadroDoCliente } from '../../../dominio/fichaCliente'
import css from './cadastros.module.css'

// #2849: o topo da ficha do cliente, sempre em leitura e sempre na mesma ordem: quem é e a situação, os
// números (com as palavras da Ficha do Atendimento) e os dados de contato. O formulário fica atrás de
// "Editar dados"; quem abre a ficha quer ver a pessoa antes de mexer nela.
export function ResumoDoCliente({ dados, relacionamento, carregando, editando, aoEditar }) {
  const foto = useFotoCliente(dados.fotoUrl ?? dados.detalhe?.fotoUrl)
  const situacao = situacaoDaLinha('clientes', dados, false)
  const quadro = quadroDoCliente(relacionamento)
  const nome = dados.nome?.trim() || 'Cliente sem nome'
  return <section className={css.resumoCliente} aria-label={`Resumo de ${nome}`} data-resumo-do-cliente>
    <header className={css.identidadeCliente}>
      <Avatar nome={dados.nome} foto={foto.url} tamanho="grande" />
      <div>
        <strong>{nome}</strong>
        {situacao && <Pilula tom={situacao.tom}>{situacao.texto}</Pilula>}
      </div>
      {aoEditar && !editando && <Botao icone="lapis" onClick={aoEditar}>Editar dados</Botao>}
    </header>
    {carregando && <p role="status" className={css.legenda}>Carregando os números do cliente…</p>}
    {quadro.length > 0 && <dl className={css.quadroCliente} aria-label="Números do cliente">
      {quadro.map(n => <div key={n.rotulo}><dd>{n.valor}</dd><dt>{n.rotulo}</dt></div>)}
    </dl>}
    {!editando && <dl className={css.contatoCliente} aria-label="Dados de contato">
      {dadosDeContato(dados).map(d => <div key={d.rotulo}><dt>{d.rotulo}</dt><dd className={d.valor ? undefined : css.naoInformado}>{d.valor ?? 'Não informado'}</dd></div>)}
    </dl>}
  </section>
}
