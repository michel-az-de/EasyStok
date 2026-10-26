// Site e impressos (#2070, só no modo API): cria o site se ainda não existe; depois, subtítulo, cor,
// WhatsApp de pedidos, pedido mínimo, frete grátis e um logo que vale para os impressos e o site.
// Ligar ou tirar o site do ar, endereço próprio, mensagem fora da área de entrega, loja padrão e
// campos fiscais não aparecem aqui, e o PUT devolve o que a tela não mostra exatamente como veio.
import { useId, useRef, useState } from 'react'
import { useEnvio, useLeitura, usePendenciaAvisada } from '../../../aplicacao/formularioDeAjuste'
import { carregarSite, criarSite, enviarLogoDoSite, salvarSite, urlDoLogo } from '../../../aplicacao/siteEImpressos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { CampoReais } from '../../../componentes/CampoReais'
import { lerMoeda } from '../../../dominio/formato'
import {
  EXEMPLO_DE_COR, LIMITES_DO_SITE, TIPOS_DE_LOGO, errosDaCriacao, errosDoSite, formularioDoSite, motivoDoArquivoDeLogo, siteMudou,
  sugerirEnderecoDoSite,
} from '../../../dominio/vitrineDoSite'
import { Erros, FalhaDeLeitura, Pendencia, Retorno } from '../PartesDoAjuste'
import css from '../formularioDeAjuste.module.css'

const ROTULOS_DA_CRIACAO = { tituloPublico: 'Nome do site', slug: 'Endereço do site' }
const ROTULOS_DO_SITE = {
  subtituloPublico: 'Subtítulo', corPrimaria: 'Cor principal', whatsappPedidos: 'WhatsApp de pedidos',
  pedidoMinimoCentavos: 'Pedido mínimo para entrega', freteGratisCentavos: 'Frete grátis acima de',
}

export function AbaSiteEImpressos({ aoMudarPendencia, aoSalvo, compacto = false }) {
  const [carga, recarregar] = useLeitura(carregarSite)
  const [criacaoPendente, setCriacaoPendente] = useState(false)
  const [sitePendente, setSitePendente] = useState(false)
  usePendenciaAvisada(criacaoPendente || sitePendente, aoMudarPendencia)

  if (carga.estado === 'carregando') return <output className={css.descricao}>Carregando o site…</output>
  if (carga.estado === 'erro') return <FalhaDeLeitura mensagem={`O site não carregou: ${carga.mensagem}`} aoTentarDeNovo={recarregar} />
  const { vitrine, configuracao } = carga.dados
  if (!vitrine) {
    return <CriarSite compacto={compacto} aoMudarPendencia={setCriacaoPendente} aoCriado={() => { aoSalvo?.(); return recarregar() }} />
  }
  return <ConfigurarSite compacto={compacto} configuracao={configuracao} aoMudarPendencia={setSitePendente} aoSalvo={aoSalvo} />
}

const CRIACAO_VAZIA = { tituloPublico: '', slug: '' }

function CriarSite({ compacto, aoMudarPendencia, aoCriado }) {
  const id = useId()
  const [form, setForm] = useState(CRIACAO_VAZIA)
  const [enderecoEscolhido, setEnderecoEscolhido] = useState(false)
  const [erros, setErros] = useState({})
  const [situacao, enviar, limpar] = useEnvio()
  const salvando = situacao.estado === 'salvando'
  const pendente = Boolean(form.tituloPublico.trim() || form.slug.trim())
  usePendenciaAvisada(pendente, aoMudarPendencia)

  function mudarNome(evento) {
    const tituloPublico = evento.target.value
    limpar(); setErros({})
    setForm((atual) => ({ tituloPublico, slug: enderecoEscolhido ? atual.slug : sugerirEnderecoDoSite(tituloPublico) }))
  }
  function mudarEndereco(evento) {
    const slug = evento.target.value.toLowerCase()
    limpar(); setErros({})
    setEnderecoEscolhido(true)
    setForm((atual) => ({ ...atual, slug }))
  }

  async function criar(evento) {
    evento.preventDefault()
    if (salvando) return
    const encontrados = errosDaCriacao(form)
    setErros(encontrados)
    if (Object.keys(encontrados).length > 0) return
    const { ok } = await enviar(() => criarSite(form))
    if (!ok) return
    setForm(CRIACAO_VAZIA)
    setEnderecoEscolhido(false)
    await aoCriado()
  }

  return (
    <form className={css.secao} aria-labelledby={`${id}-titulo`} onSubmit={criar}>
      <h3 id={`${id}-titulo`}>{compacto ? 'Criar o site' : 'A loja ainda não tem site'}</h3>
      {!compacto && <p className={css.descricao}>Escolha o nome e o endereço. Os dois ficam assim depois de criados.</p>}
      <Pendencia visivel={pendente && !salvando} />
      <fieldset className={css.campos} disabled={salvando}>
        <legend className="sr">Novo site</legend>
        <CampoTexto rotulo={ROTULOS_DA_CRIACAO.tituloPublico} required maxLength={LIMITES_DO_SITE.tituloPublico}
          dica="Aparece no topo do site." value={form.tituloPublico} onChange={mudarNome} />
        <CampoTexto rotulo={ROTULOS_DA_CRIACAO.slug} required maxLength={LIMITES_DO_SITE.slug} autoCapitalize="none" spellCheck={false}
          dica="Parte final do link do site: letras minúsculas, números e hífen." value={form.slug} onChange={mudarEndereco} />
      </fieldset>
      <Erros erros={erros} rotulos={ROTULOS_DA_CRIACAO} />
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || salvando}>{salvando ? 'Criando…' : 'Criar o site'}</Botao>
        <Retorno situacao={situacao} salvo="Site criado." />
      </div>
    </form>
  )
}

function ConfigurarSite({ compacto, configuracao, aoMudarPendencia, aoSalvo }) {
  const id = useId()
  const [gravado, setGravado] = useState(configuracao)
  const [form, setForm] = useState(() => formularioDoSite(configuracao))
  const [erros, setErros] = useState({})
  const [situacao, enviar, limpar] = useEnvio()
  const salvando = situacao.estado === 'salvando'
  const pendente = siteMudou(form, gravado)
  usePendenciaAvisada(pendente, aoMudarPendencia)

  const editar = (campo, valor) => {
    limpar(); setErros({})
    setForm((atual) => ({ ...atual, [campo]: valor }))
  }
  const mudar = (campo) => (evento) => editar(campo, evento.target.value)

  async function salvar(evento) {
    evento.preventDefault()
    if (!pendente || salvando) return
    const encontrados = errosDoSite(form, gravado)
    setErros(encontrados)
    if (Object.keys(encontrados).length > 0) return
    const { ok, resultado } = await enviar(async () => {
      const confirmado = await salvarSite(gravado, form)
      if (!confirmado) throw new Error('O EasyStok não confirmou o site. Tente de novo.')
      return confirmado
    })
    if (!ok) return
    setGravado(resultado)
    setForm(formularioDoSite(resultado))
    aoSalvo?.()
  }

  function desfazer() {
    setForm(formularioDoSite(gravado))
    setErros({})
    limpar()
  }

  return (
    <div className={css.aba}>
      {!compacto && (
        <p className={css.descricao}>
          {gravado.tituloPublico ? `${gravado.tituloPublico} · ` : ''}endereço {gravado.slug}. Nome e endereço ficam como foram criados.
        </p>
      )}
      <LogoDoSite gravado={gravado} aoSalvar={setGravado} />
      <form className={css.secao} aria-labelledby={`${id}-titulo`} onSubmit={salvar}>
        <h3 id={`${id}-titulo`}>Site</h3>
        <Pendencia visivel={pendente && !salvando} />
        <fieldset className={css.campos} disabled={salvando}>
          <legend className="sr">Configuração do site</legend>
          <div className={css.inteiro}>
            <CampoTexto rotulo={ROTULOS_DO_SITE.subtituloPublico} maxLength={LIMITES_DO_SITE.subtituloPublico}
              dica="Frase curta abaixo do nome, no topo do site." value={form.subtituloPublico} onChange={mudar('subtituloPublico')} />
          </div>
          <CampoTexto rotulo={ROTULOS_DO_SITE.corPrimaria} maxLength={LIMITES_DO_SITE.corPrimaria} placeholder={EXEMPLO_DE_COR}
            autoCapitalize="none" spellCheck={false} dica="Código da cor dos destaques. Vazio usa a cor padrão do site."
            value={form.corPrimaria} onChange={mudar('corPrimaria')} />
          <CampoTexto rotulo={ROTULOS_DO_SITE.whatsappPedidos} tipo="tel" inputMode="tel" maxLength={LIMITES_DO_SITE.whatsappPedidos}
            dica="Com DDD." value={form.whatsappPedidos} onChange={mudar('whatsappPedidos')} />
          <CampoReais rotulo={ROTULOS_DO_SITE.pedidoMinimoCentavos} centavos={form.pedidoMinimoCentavos} ler={lerMoeda}
            dica="Vazio ou zero: sem mínimo." aoMudar={(centavos) => editar('pedidoMinimoCentavos', centavos ?? 0)} />
          <CampoReais rotulo={ROTULOS_DO_SITE.freteGratisCentavos} centavos={form.freteGratisCentavos} ler={lerMoeda}
            aoMudar={(centavos) => editar('freteGratisCentavos', centavos)} />
        </fieldset>
        <Erros erros={erros} rotulos={ROTULOS_DO_SITE} />
        <div className={css.acoes}>
          <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || salvando}>Salvar o site</Botao>
          <Botao variante="texto" className={css.toque} disabled={!pendente || salvando} onClick={desfazer}>Desfazer edição</Botao>
          <Retorno situacao={situacao} />
        </div>
      </form>
    </div>
  )
}

function LogoDoSite({ gravado, aoSalvar }) {
  const id = useId()
  const arquivoRef = useRef(null)
  const [situacao, setSituacao] = useState({ estado: 'ocioso', mensagem: null })
  const enviando = situacao.estado === 'enviando'

  async function escolher(evento) {
    const arquivo = evento.target.files?.[0]
    // O mesmo arquivo pode ser escolhido de novo depois de um erro.
    evento.target.value = ''
    if (!arquivo || enviando) return
    const motivo = motivoDoArquivoDeLogo(arquivo)
    if (motivo) { setSituacao({ estado: 'erro', mensagem: motivo }); return }
    setSituacao({ estado: 'enviando', mensagem: null })
    try {
      aoSalvar(await enviarLogoDoSite(gravado, arquivo))
      setSituacao({ estado: 'salvo', mensagem: null })
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro.message })
    }
  }

  const imagem = urlDoLogo(gravado.logoUrl)
  return (
    <section className={css.secao} aria-labelledby={`${id}-logo`}>
      <h3 id={`${id}-logo`}>Logo</h3>
      <p className={css.descricao}>PNG ou JPG até 5 MB. O mesmo logo vai para os impressos e para o site. Na térmica só sai em preto.</p>
      <div className={css.logo}>
        {imagem
          ? <img className={css.previa} src={imagem} alt={`Logo de ${gravado.tituloPublico || 'a loja'}`} />
          : <p className={css.semPrevia}>O logo aparece aqui depois do envio.</p>}
        <div className={css.acoes}>
          <input ref={arquivoRef} type="file" accept={TIPOS_DE_LOGO.join(',')} className="sr" aria-label="Arquivo do logo" tabIndex={-1} onChange={escolher} />
          <Botao icone="imagem" className={css.toque} disabled={enviando} onClick={() => arquivoRef.current?.click()}>
            {enviando ? 'Enviando…' : imagem ? 'Trocar logo' : 'Enviar logo'}
          </Botao>
        </div>
      </div>
      {situacao.estado === 'salvo' && <output className={css.retorno}>Logo salvo nos impressos e no site.</output>}
      {situacao.estado === 'erro' && <p className={css.erro} role="alert">{situacao.mensagem}</p>}
    </section>
  )
}
