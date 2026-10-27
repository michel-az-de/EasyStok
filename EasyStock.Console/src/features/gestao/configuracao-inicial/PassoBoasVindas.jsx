// Boas-vindas dos Primeiros passos (#2070): o que ter em mãos, a dona confirma o próprio nome (`PATCH api/auth/me` só
// com o nome) e vê, só leitura, o que o suporte já ligou. Nenhuma chave de integração se cadastra aqui.
import { useId, useState } from 'react'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useEnvio, usePendenciaAvisada } from '../../../aplicacao/formularioDeAjuste'
import { confirmarMeuNome } from '../../../aplicacao/useConfiguracaoInicialApi'
import { useIntegracoesApi } from '../../../aplicacao/useIntegracoesApi'
import { useStatusWhatsAppApi } from '../../../aplicacao/useStatusWhatsAppApi'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { ligadosPeloSuporte } from '../../../dominio/configuracaoInicial'
import { Pendencia, Retorno } from '../PartesDoAjuste'
import css from './configuracaoInicial.module.css'

const TAMANHO_DO_NOME = 150
const EM_MAOS = [
  'Os dias e horários em que a loja atende e entrega.',
  'O logo da loja, em imagem.',
  'O cardápio com preços, fotos e categorias.',
  'Os bairros ou CEPs que a loja atende e o frete de cada um.',
  'Os e-mails de quem vai trabalhar com você.',
]

export function PassoBoasVindas({ aoMudarPendencia }) {
  const id = useId()
  return (
    <div className={css.bloco}>
      <section className={css.secao} aria-labelledby={`${id}-maos`}>
        <h3 id={`${id}-maos`}>O que ter em mãos</h3>
        <ul className={css.itens}>{EM_MAOS.map((item) => <li key={item}>{item}</li>)}</ul>
      </section>
      <SeuNome aoMudarPendencia={aoMudarPendencia} />
      <LigadoPeloSuporte />
      <p className={css.destaque}>O que você salvar aqui já vale para os clientes.</p>
    </div>
  )
}

function SeuNome({ aoMudarPendencia }) {
  const id = useId()
  const { conta } = useAcessoModulos()
  const [salvo, setSalvo] = useState(() => conta?.nome ?? '')
  const [nome, setNome] = useState(salvo)
  const [situacao, enviar, limpar] = useEnvio()
  const salvando = situacao.estado === 'salvando'
  const pendente = nome.trim() !== salvo.trim()
  const valido = nome.trim().length > 0 && nome.trim().length <= TAMANHO_DO_NOME
  usePendenciaAvisada(pendente, aoMudarPendencia)

  async function salvar(evento) {
    evento.preventDefault()
    if (!pendente || !valido || salvando) return
    const { ok, resultado } = await enviar(() => confirmarMeuNome(nome.trim()))
    if (ok) { setSalvo(resultado); setNome(resultado) }
  }

  return (
    <form className={css.secao} aria-labelledby={`${id}-nome`} onSubmit={salvar}>
      <h3 id={`${id}-nome`}>Seu nome</h3>
      <CampoTexto rotulo="Seu nome" dica="É assim que a equipe vê você no Console." value={nome} maxLength={TAMANHO_DO_NOME} required
        disabled={salvando} onChange={(evento) => { limpar(); setNome(evento.target.value) }} />
      <Pendencia visivel={pendente && !salvando} acao="Salve o nome ou desfaça antes de seguir." />
      <div className={css.acoesDoBloco}>
        <Botao tipo="submit" variante="primario" disabled={!pendente || !valido || salvando}>Salvar nome</Botao>
        {pendente && <Botao variante="texto" disabled={salvando} onClick={() => { limpar(); setNome(salvo) }}>Desfazer</Botao>}
        <Retorno situacao={situacao} salvo="Nome salvo." />
      </div>
    </form>
  )
}

function LigadoPeloSuporte() {
  const id = useId()
  const integracoes = useIntegracoesApi()
  const whatsapp = useStatusWhatsAppApi()
  const itens = ligadosPeloSuporte({
    integracoes: integracoes.lista, erroIntegracoes: integracoes.erro,
    whatsapp: whatsapp.carregando ? undefined : whatsapp.status, erroWhatsApp: whatsapp.erro,
  })
  return (
    <section className={css.secao} aria-labelledby={`${id}-suporte`}>
      <h3 id={`${id}-suporte`}>O que o suporte já ligou</h3>
      <p className={css.apoio}>Só para conferir: quem liga e desliga é o suporte.</p>
      <ul className={css.itens}>
        {itens.map((item) => (
          <li key={item.nome} className={css.ligado}>
            <strong>{item.nome}</strong>
            <Pilula tom={item.ligado ? 'ok' : 'neutro'} fina>{item.situacao}</Pilula>
          </li>
        ))}
      </ul>
    </section>
  )
}
