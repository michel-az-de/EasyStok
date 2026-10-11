import { useEffect, useRef, useState } from 'react'
import { Modal } from '../../componentes/Modal'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { CampoMascarado } from '../../componentes/CampoMascarado'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { lerCategoriasDoPrato, lerEmbalagensDaPorcao, lerFichaDoPrato, salvarFichaDoPrato, salvarFotoDoPrato } from '../../aplicacao/edicaoCardapio'
import { catalogoDeAdicionais } from '../../dominio/cardapio'
import { fotoDoItem } from '../../dominio/vitrineCardapio'
import { lerMoeda, mascaraMoeda, moeda, moedaAltaDemais } from '../../dominio/formato'
import { corpoDasPorcoes, erroDasPorcoes, porcoesMudaram, rascunhoDasPorcoes, resumoDoItem } from '../../dominio/porcoes'
import { EditorPorcoes } from './EditorPorcoes'
import css from './editorItem.module.css'

const VAZIA = { descricao: '', ingredientes: '', alergenos: '', preparo: '', instrucao: '' }
const dataDoIso = (iso) => iso?.slice(0, 10) ?? ''
const camposDe = (d) => ({ descricao: d.descricao, ingredientes: d.ingredientes, alergenos: d.alergenos,
  preparo: d.tempoPreparoMinutos == null ? '' : String(d.tempoPreparoMinutos), instrucao: d.instrucaoFinalizacao })

// Mesmo editor no Balcão e na Gestão, composto pelo App. O rascunho só fecha após a confirmação.
export function EditorItemCardapio({ item = null, aoFechar, aoSalvar, focoInicial }) {
  const { fonteApi } = useAtendimento()
  const { cardapio, linhas, adicionais } = useCatalogo()
  const acoes = useAcoes()
  const acesso = useAcessoModulos()
  const podeEditar = !fonteApi || acesso.acoes.editarCardapio === true
  const [id, setId] = useState(item?.sku ?? null)
  const [nome, setNome] = useState(item?.nome ?? '')
  const [linha, setLinha] = useState(item?.linha ?? Object.keys(linhas)[0])
  const [porcao, setPorcao] = useState(item?.porcao ?? '')
  const [centavos, setCentavos] = useState(Math.round((item?.preco ?? 0) * 100))
  const [novidade, setNovidade] = useState(dataDoIso(item?.novidadeAte))
  const [comNovidade, setComNovidade] = useState(Boolean(item?.novidadeAte))
  const [secao, setSecao] = useState('')
  const [categorias, setCategorias] = useState([])
  const [embalagens, setEmbalagens] = useState([])
  const [detalhe, setDetalhe] = useState(null)
  const [ficha, setFicha] = useState(VAZIA)
  const [porcoes, setPorcoes] = useState([])
  const [selecionados, setSelecionados] = useState(adicionais?.[item?.sku] ?? [])
  const [carga, setCarga] = useState(fonteApi && item ? 'carregando' : 'ok')
  const [tentativa, setTentativa] = useState(0)
  const [erro, setErro] = useState(null)
  const [salvando, setSalvando] = useState(false)
  const [alterado, setAlterado] = useState(false)
  const [confirmaSaida, setConfirmaSaida] = useState(false)
  const [arquivo, setArquivo] = useState(null)
  const [galeria, setGaleria] = useState(false)
  const [preview, setPreview] = useState(null)
  const [fotoSalva, setFotoSalva] = useState(false)
  const [precisaReler, setPrecisaReler] = useState(false)
  const formulario = useRef(null)
  const erroRef = useRef(null)
  const confirmacaoRef = useRef(null)
  const inputFoto = useRef(null)

  useEffect(() => {
    if (!arquivo) { setPreview(null); return undefined }
    const url = URL.createObjectURL(arquivo)
    setPreview(url)
    return () => URL.revokeObjectURL(url)
  }, [arquivo])
  useEffect(() => { if (focoInicial === 'foto' && carga === 'ok') inputFoto.current?.focus() }, [focoInicial, carga])

  useEffect(() => {
    if (!fonteApi) return undefined
    let vivo = true
    Promise.all([item ? lerFichaDoPrato(item.sku) : null, podeEditar ? lerCategoriasDoPrato() : [], podeEditar ? lerEmbalagensDaPorcao() : []])
      .then(([d, categoriasLidas, embalagensLidas]) => {
        if (!vivo) return
        setCategorias(categoriasLidas)
        setEmbalagens(embalagensLidas)
        if (d) {
          setNome(d.nome); setLinha(d.linha); setPorcao(d.porcao); setCentavos(Math.round(d.preco * 100))
          setDetalhe(d); setFicha(camposDe(d)); setSecao(d.secaoId ?? '')
          setPorcoes(rascunhoDasPorcoes(d.porcoes)); setNovidade(dataDoIso(d.novidadeAte)); setComNovidade(Boolean(d.novidadeAte))
        }
        setCarga('ok'); setErro(null)
      }).catch((e) => { if (vivo) { setCarga('erro'); setErro(e.message) } })
    return () => { vivo = false }
  }, [item, fonteApi, podeEditar, tentativa])

  useEffect(() => { if (erro) erroRef.current?.focus() }, [erro])
  useEffect(() => { if (confirmaSaida) confirmacaoRef.current?.focus() }, [confirmaSaida])
  const fechar = () => { if (salvando) return; if (alterado || arquivo) setConfirmaSaida(true); else aoFechar() }
  const mudarFicha = (campo) => (e) => setFicha((atual) => ({ ...atual, [campo]: e.target.value }))
  const resumo = porcoes.length ? resumoDoItem(porcoes) : null
  const foto = preview ?? detalhe?.foto ?? fotoDoItem(item ?? {})
  const fotosDoPrato = detalhe?.fotos?.length ? detalhe.fotos : detalhe?.foto ? [detalhe.foto] : []
  const extras = catalogoDeAdicionais(cardapio, adicionais, item?.sku ?? null)

  function escolherFoto(e) {
    const escolhida = e.target.files?.[0]
    e.target.value = ''
    if (!escolhida) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(escolhida.type) || escolhida.size > 6 * 1024 * 1024) {
      setErro('Escolha uma foto JPG, PNG ou WebP de até 6 MB.'); return
    }
    setArquivo(escolhida); setGaleria(false); setErro(null); setFotoSalva(false)
  }

  async function gravarFoto(salvoId) {
    await salvarFotoDoPrato(salvoId, arquivo, galeria)
    setArquivo(null); setFotoSalva(true)
    // Só atualiza a fotografia. Uma releitura não deve apagar os campos já digitados.
    try {
      const atualizado = await lerFichaDoPrato(salvoId)
      setDetalhe((d) => ({ ...d, foto: atualizado.foto, fotos: atualizado.fotos }))
    } catch {
      setErro('A foto foi salva, mas não consegui atualizar a prévia. Feche e reabra a ficha para conferir.')
    }
  }

  async function salvarSomenteFoto() {
    setSalvando(true); setErro(null)
    try { await gravarFoto(id); await acoes.recarregarCardapio(); await aoSalvar?.() }
    catch (e) { setErro(e.message) }
    finally { setSalvando(false) }
  }

  async function reconciliar(salvoId = id) {
    const salvo = await lerFichaDoPrato(salvoId)
    setDetalhe(salvo)
    setNome(salvo.nome); setLinha(salvo.linha); setPorcao(salvo.porcao); setCentavos(Math.round(salvo.preco * 100))
    setPorcoes(rascunhoDasPorcoes(salvo.porcoes))
    setFicha(camposDe(salvo)); setSecao(salvo.secaoId ?? '')
    setAlterado(false); setPrecisaReler(false)
  }

  async function tentarReler() {
    setSalvando(true); setErro(null)
    try { await reconciliar(); await acoes.recarregarCardapio(); await aoSalvar?.() }
    catch (e) { setErro(`O prato está salvo. Não foi possível atualizar a ficha: ${e.message}`) }
    finally { setSalvando(false) }
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (salvando || precisaReler || carga !== 'ok' || !podeEditar) return
    setErro(null)
    const erroPorcoes = erroDasPorcoes(porcoes)
    if (erroPorcoes) { setErro(erroPorcoes); return }
    if (!resumo && (!porcao.trim() || centavos <= 0 || moedaAltaDemais(centavos))) { setErro('Informe a porção e um preço válido.'); return }
    const dados = {
      nome: nome.trim(), linha, porcao: resumo?.porcao ?? porcao.trim(), preco: resumo?.preco ?? centavos / 100,
      novidadeAte: comNovidade ? `${novidade}T23:59:59-03:00` : null,
      ...(!fonteApi ? { adicionaisSelecionados: selecionados } : {}),
    }
    const inicial = detalhe ? camposDe(detalhe) : VAZIA
    for (const [campo, contrato] of Object.entries({ descricao: 'descricao', ingredientes: 'ingredientes', alergenos: 'alergenos', instrucao: 'instrucaoFinalizacao' })) {
      if (ficha[campo].trim() !== (inicial[campo] ?? '').trim()) dados[contrato] = ficha[campo].trim()
    }
    if (ficha.preparo !== inicial.preparo && ficha.preparo.trim()) dados.tempoPreparoMinutos = Number(ficha.preparo)
    if (secao !== (detalhe?.secaoId ?? '')) dados.secaoId = secao || null
    if (fonteApi && porcoesMudaram(detalhe?.porcoes ?? [], porcoes)) dados.porcoes = corpoDasPorcoes(porcoes)
    setSalvando(true)
    try {
      if (fonteApi) {
        const salvoId = await salvarFichaDoPrato(id, dados)
        setId(salvoId)
        // Uma porção nova recebe id no servidor. Nunca reenviar id:null após falha de foto.
        setPrecisaReler(true)
        try { await reconciliar(salvoId) }
        catch (e) { throw new Error(`O prato está salvo. Atualize a ficha antes de continuar: ${e.message}`) }
        // O cadastro já existe mesmo se a fotografia falhar depois.
        await acoes.recarregarCardapio()
        await aoSalvar?.()
        if (arquivo) {
          try { await gravarFoto(salvoId) }
          catch (e) { throw new Error(`O prato foi salvo, mas a foto não: ${e.message} Tente salvar a foto novamente.`) }
          await acoes.recarregarCardapio()
          await aoSalvar?.()
        }
      } else if (id) acoes.editarItemCardapio(id, dados)
      else acoes.incluirItemCardapio(dados)
      setAlterado(false)
      if (!fonteApi) await aoSalvar?.()
      aoFechar()
    } catch (e) { setErro(e.message) }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo={item ? 'Editar prato' : 'Novo prato'} aoFechar={fechar} largura="min(980px, calc(100vw - 24px))"
      rodape={<>
        <span className={css.statusRodape}>{salvando ? 'Salvando alterações…' : alterado || arquivo ? 'Alterações ainda não salvas' : 'Ficha do cardápio'}</span>
        <Botao disabled={salvando} onClick={fechar}>Cancelar</Botao>
        <Botao variante="primario" disabled={salvando || carga !== 'ok' || !podeEditar} onClick={() => precisaReler ? tentarReler() : formulario.current?.requestSubmit()}>{salvando ? 'Salvando…' : precisaReler ? 'Atualizar ficha salva' : 'Salvar prato'}</Botao>
      </>}>
      {confirmaSaida && <div className={css.confirmacao} role="alert" ref={confirmacaoRef} tabIndex={-1}>
        <strong>Descartar as alterações deste prato?</strong><p>O que ainda não foi salvo será perdido.</p>
        <Botao onClick={() => setConfirmaSaida(false)}>Continuar editando</Botao><Botao variante="texto" onClick={aoFechar}>Descartar alterações</Botao>
      </div>}
      <div className={css.editor}>
        <aside className={css.apresentacao}>
          <div className={css.foto}>{foto ? <img src={foto} alt={`Foto de ${nome}`} /> : <div className={css.semFoto}><Icone nome="imagem" tamanho={40} /><span>Seu prato começa pela foto</span></div>}</div>
          {fonteApi && podeEditar && <div className={css.fotosEditor}>
            <label className={css.escolherFoto}><Icone nome="camera" tamanho={20} />{foto ? 'Escolher outra foto' : 'Adicionar foto'}
              <input ref={inputFoto} type="file" accept="image/jpeg,image/png,image/webp" disabled={salvando || carga !== 'ok'} onChange={escolherFoto} aria-label="Escolher foto do prato" />
            </label>
            <small className={css.ajuda}>JPG, PNG ou WebP · até 6 MB</small>
            {arquivo && <div className={css.fotoPendente}>
              <span>{arquivo.name}</span>
              {id && <label className={css.opcao}><input type="checkbox" checked={galeria} disabled={salvando || fotosDoPrato.length >= 5} onChange={(e) => setGaleria(e.target.checked)} />Adicionar à galeria</label>}
              {galeria && <small>A capa atual será mantida. Você pode ter até 5 fotos por prato.</small>}
              <div className={css.acoesFoto}>{id && <Botao disabled={salvando} onClick={salvarSomenteFoto}>Salvar foto</Botao>}<Botao variante="texto" disabled={salvando} onClick={() => setArquivo(null)}>Desfazer</Botao></div>
              {!id && <small>A foto será enviada ao salvar o prato.</small>}
            </div>}
            {fotoSalva && <output className={css.sucesso}><Icone nome="check" /> Foto salva. Ela já está no cardápio.</output>}
            {fotosDoPrato.length > 1 && <div className={css.galeria}><span>Galeria · {fotosDoPrato.length} de 5 fotos</span><div>{fotosDoPrato.map((url, i) => <img key={url} src={url} alt={`Ângulo ${i + 1} de ${nome}`} />)}</div></div>}
          </div>}
          <div className={css.previa}><span>{linhas[linha]?.rotulo}</span><strong>{nome || 'Nome do prato'}</strong><b>{Number.isFinite(resumo?.preco ?? centavos / 100) ? moeda(resumo?.preco ?? centavos / 100) : "Defina o preço"}</b><span>{resumo?.porcao ?? porcao}</span></div>
        </aside>
        <form ref={formulario} onSubmit={salvar} onChange={() => setAlterado(true)} className={css.formulario} aria-label="Ficha do prato">
          {carga === 'carregando' && <output>Carregando a ficha do prato…</output>}
          {erro && <div className={css.erro} role="alert" ref={erroRef} tabIndex={-1}>{erro}{carga === 'erro' && <Botao onClick={() => { setCarga('carregando'); setTentativa((t) => t + 1) }}>Tentar novamente</Botao>}</div>}
          <fieldset disabled={salvando || precisaReler || carga !== 'ok' || !podeEditar} className={css.campos}>
            <section className={css.secao}>
              <h3>Apresentação do prato</h3>
              <label className={css.campo}>Nome do prato<input required maxLength={200} value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Ex.: Lasanha à bolonhesa" /></label>
              <label className={css.campo}>Descrição para o cliente<textarea rows={3} maxLength={240} value={ficha.descricao} onChange={mudarFicha('descricao')} placeholder="O que faz este prato especial?" /><small>Até 240 caracteres. Conte o que o cliente vai receber.</small></label>
              <div className={css.dupla}>
                <label className={css.campo}>Linha de produto<select value={linha} onChange={(e) => setLinha(e.target.value)}>{Object.entries(linhas).map(([chave, l]) => <option key={chave} value={chave}>{l.rotulo}</option>)}</select></label>
                {fonteApi && <label className={css.campo}>Categoria no cardápio<select value={secao} onChange={(e) => setSecao(e.target.value)}><option value="">Sem categoria</option>{categorias.map((c) => <option key={c.id} value={c.id}>{c.nome}</option>)}</select></label>}
              </div>
            </section>
            <section className={css.secao}>
              <h3>Porções e valores</h3>
              {porcoes.length === 0 && <div className={css.dupla}>
                <label className={css.campo}>Porção<input required value={porcao} onChange={(e) => setPorcao(e.target.value)} placeholder="Ex.: 800 g · serve 2 pessoas" /></label>
                <CampoMascarado tipo="moeda" rotulo="Preço" valor={mascaraMoeda(centavos)} erro={moedaAltaDemais(centavos) ? 'Valor alto demais' : null} aoMudarDigitos={(d) => { setCentavos(Number(d || '0')); setAlterado(true) }} aoColarTexto={(t) => { setCentavos(lerMoeda(t)); setAlterado(true) }} />
              </div>}
              {fonteApi && <EditorPorcoes linhas={porcoes} embalagens={embalagens} aoMudar={(p) => { setPorcoes(p); setAlterado(true) }} />}
            </section>
            <section className={css.secao}>
              <h3>Informações para atender bem</h3>
              <p className={css.ajuda}>Consulte estes detalhes no Balcão para responder às dúvidas do cliente.</p>
              <label className={css.campo}>Ingredientes<textarea rows={2} maxLength={500} value={ficha.ingredientes} onChange={mudarFicha('ingredientes')} /></label>
              <label className={css.campo}>Alérgenos<input maxLength={200} value={ficha.alergenos} onChange={mudarFicha('alergenos')} placeholder="Ex.: trigo, leite e ovos" /><small>Informe os ingredientes e riscos de contato cruzado confirmados pela cozinha.</small></label>
              <label className={css.campo}>Tempo de preparo, em minutos<input type="number" min={1} max={1440} step={1} required={detalhe?.tempoPreparoMinutos != null} value={ficha.preparo} onChange={mudarFicha('preparo')} placeholder="Usa o tempo padrão da loja" /></label>
              {linha === 'casa' && <label className={css.campo}>Como finalizar em casa<textarea rows={3} maxLength={500} value={ficha.instrucao} onChange={mudarFicha('instrucao')} placeholder="Tempo, temperatura e modo de preparo." /></label>}
            </section>
            <section className={css.secao}>
              <h3>Organização e vínculos</h3>
              <label className={css.opcao}><input type="checkbox" checked={comNovidade} onChange={(e) => setComNovidade(e.target.checked)} />Destacar como novidade</label>
              {comNovidade && <label className={css.campo}>Novidade até<input type="date" required value={novidade} onChange={(e) => setNovidade(e.target.value)} /></label>}
              {fonteApi && <p className={css.vinculo}><MarcaIcone nome="produto" tamanho={24} /><span><strong>{detalhe?.produtoId ? 'Vinculado ao estoque' : 'Item sem vínculo com estoque'}</strong>{detalhe?.produtoId ? 'As porções mantêm seus vínculos de saldo. A receita e os insumos ficam em Produção.' : 'A disponibilidade é controlada manualmente. Este cadastro não cria saldo ou receita.'}</span></p>}
              {!fonteApi && extras.length > 0 && <fieldset className={css.secao}><legend>Adicionais do prato</legend>{extras.map((a) => <label key={a.sku} className={css.opcao}><input type="checkbox" checked={selecionados.includes(a.sku)} onChange={() => setSelecionados((s) => s.includes(a.sku) ? s.filter((v) => v !== a.sku) : [...s, a.sku])} />{a.nome} · {moeda(a.preco)}</label>)}</fieldset>}
              {!item && <p className={css.ajuda}>O prato entra em validação. Depois de salvar, confirme no cardápio para o assistente poder oferecê-lo.</p>}
            </section>
          </fieldset>
        </form>
      </div>
    </Modal>
  )
}
