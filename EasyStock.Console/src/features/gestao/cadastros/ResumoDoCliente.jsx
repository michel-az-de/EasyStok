import { useRef, useState } from 'react'
import { gerarFichaClientePdf } from '../../../aplicacao/fichaClientePdf'
import { enviarFotoCliente, removerFotoCliente } from '../../../aplicacao/fotoCliente'
import { useFotoCliente } from '../../../aplicacao/useFotoCliente'
import { Avatar } from '../../../componentes/Avatar'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { baixarPdf } from '../../../componentes/pdfNoNavegador'
import { dadosDeContato, situacaoDaLinha } from '../../../dominio/cadastros'
import { quadroDoCliente } from '../../../dominio/fichaCliente'
import css from './cadastros.module.css'

const FALHA = { foto: 'Não foi possível alterar a foto. Tente de novo.', pdf: 'Não foi possível baixar a ficha. Tente de novo.' }

// #2849: o topo da ficha do cliente, sempre em leitura e sempre na mesma ordem: quem é e a situação, os
// números (com as palavras da Ficha do Atendimento) e os dados de contato. O formulário fica atrás de
// "Editar dados"; quem abre a ficha quer ver a pessoa antes de mexer nela. Foto e PDF, que só existiam no
// modal do Atendimento, são daqui: valem em Cadastros, no Buscar na Casa e no Atendimento.
export function ResumoDoCliente({ dados, relacionamento, carregando, editando, aoEditar, aoMudarFoto }) {
  // Com o relacionamento lido, a foto é a dele: removida lá, não volta pela cópia do cadastro.
  const fotoUrl = relacionamento ? relacionamento.fotoUrl ?? null : dados.fotoUrl ?? dados.detalhe?.fotoUrl ?? null
  const foto = useFotoCliente(fotoUrl)
  const [ocupado, ocupar] = useState(null)
  const [erro, falhar] = useState(null)
  const arquivo = useRef(null)
  const situacao = situacaoDaLinha('clientes', dados, false)
  const quadro = quadroDoCliente(relacionamento)
  const nome = dados.nome?.trim() || 'Cliente sem nome'

  async function executar(tipo, acao) {
    if (ocupado) return
    ocupar(tipo); falhar(null)
    try { await acao() } catch (falha) { falhar(falha.message || FALHA[tipo]) } finally { ocupar(null) }
  }
  const mudarFoto = (escolhido = null) => executar('foto', async () => {
    if (escolhido) await enviarFotoCliente(dados.id, escolhido)
    else await removerFotoCliente(dados.id)
    aoMudarFoto()
  })
  const baixar = () => executar('pdf', async () => baixarPdf(await gerarFichaClientePdf(dados.id)))

  return <section className={css.resumoCliente} aria-label={`Resumo de ${nome}`} data-resumo-do-cliente>
    <header className={css.identidadeCliente}>
      <Avatar nome={dados.nome} foto={foto.url} tamanho="grande" />
      <div>
        <strong>{nome}</strong>
        {situacao && <Pilula tom={situacao.tom}>{situacao.texto}</Pilula>}
      </div>
      {aoEditar && !editando && <Botao icone="lapis" onClick={aoEditar}>Editar dados</Botao>}
    </header>
    <div className={css.acoesDoResumo}>
      {aoMudarFoto && <>
        <input ref={arquivo} type="file" hidden accept="image/jpeg,image/png,image/webp" aria-label="Foto do cliente"
          onChange={(e) => { const escolhido = e.target.files?.[0]; e.target.value = ''; if (escolhido) mudarFoto(escolhido) }} />
        <Botao variante="texto" icone="camera" disabled={Boolean(ocupado)} onClick={() => arquivo.current?.click()}>{ocupado === 'foto' ? 'Salvando foto…' : fotoUrl ? 'Trocar foto' : 'Adicionar foto'}</Botao>
        {fotoUrl && <Botao variante="texto" disabled={Boolean(ocupado)} onClick={() => mudarFoto()}>Remover foto</Botao>}
      </>}
      <Botao variante="texto" icone="download" disabled={Boolean(ocupado)} onClick={baixar}>{ocupado === 'pdf' ? 'Preparando PDF…' : 'Baixar PDF'}</Botao>
    </div>
    {(erro || foto.erro) && <p role="alert" className={css.erro}>{erro || foto.erro}</p>}
    {carregando && <p role="status" className={css.legenda}>Carregando os números do cliente…</p>}
    {quadro.length > 0 && <dl className={css.quadroCliente} aria-label="Números do cliente">
      {quadro.map(n => <div key={n.rotulo}><dd>{n.valor}</dd><dt>{n.rotulo}</dt></div>)}
    </dl>}
    {!editando && <dl className={css.contatoCliente} aria-label="Dados de contato">
      {dadosDeContato(dados).map(d => <div key={d.rotulo}><dt>{d.rotulo}</dt><dd className={d.valor ? undefined : css.naoInformado}>{d.valor ?? 'Não informado'}</dd></div>)}
    </dl>}
  </section>
}
