import { useEffect, useState } from 'react'
import { useAcoes } from '../../../aplicacao/contextos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { CelularDoPedido } from './CelularDoPedido'
import { telefoneDoE164 } from '../../../dominio/formato'
import { nomeExibido } from '../../../dominio/nomeDoContato'
import { hashDoModulo } from '../../../dominio/modulos'
import css from './pedidos.module.css'

const MINIMO_DE_LETRAS = 3
const ESPERA_DA_DIGITACAO_MS = 300

// #2871: "Novo pedido" de Pedidos. A equipe acha o cliente cadastrado (nome ou telefone, só os ativos) e o pedido é montado numa janela sobre a tela (#2921).
// Quem ainda não tem cadastro entra por Clientes.
export function EscolherClienteDoPedido({ novoPedido, aoFechar }) {
  const { buscarClientesParaChamar } = useAcoes()
  const [termo, digitar] = useState('')
  const [achados, guardar] = useState({ termo: '', lista: [], erro: null })
  const busca = termo.trim()
  const ativa = Boolean(buscarClientesParaChamar) && busca.length >= MINIMO_DE_LETRAS
  const ocupado = Boolean(novoPedido.abrindo)
  // #2930: cliente sem telefone no cadastro ganha o campo de celular na própria linha.
  const [pedeCelularDe, pedirCelular] = useState(null)

  useEffect(() => {
    if (!ativa) return undefined
    let vivo = true
    const espera = setTimeout(() => {
      buscarClientesParaChamar(busca).then(
        (lista) => { if (vivo) guardar({ termo: busca, lista, erro: null }) },
        (e) => { if (vivo) guardar({ termo: busca, lista: [], erro: e.message || 'Não consegui buscar os clientes. Tente de novo.' }) },
      )
    }, ESPERA_DA_DIGITACAO_MS)
    return () => { vivo = false; clearTimeout(espera) }
  }, [ativa, busca, buscarClientesParaChamar])

  // Resposta de outra busca (digitação mais rápida que a resposta) não aparece.
  const atual = ativa && achados.termo === busca
  return <Modal titulo="Novo pedido" descricao="Escolha o cliente. O pedido é montado nesta janela. A cliente recebe o resumo no WhatsApp ao registrar."
    aoFechar={ocupado ? undefined : aoFechar} segurarFechamento={ocupado} focarCampo
    rodape={<Botao disabled={ocupado} onClick={aoFechar}>Cancelar</Botao>}>
    <div className={css.escolha}>
      <CampoTexto rotulo="Cliente" tipo="search" placeholder="Nome ou telefone" value={termo} onChange={(e) => digitar(e.target.value)} />
      {novoPedido.erro && <p ref={novoPedido.refDoErro} tabIndex={-1} className={css.falta} role="alert">{novoPedido.erro}</p>}
      {!ativa ? <p className={css.apoio}>Digite ao menos 3 letras do nome ou números do telefone.</p>
        : !atual ? <p className={css.apoio} role="status">Buscando clientes…</p>
          : achados.erro ? <p className={css.falta} role="alert">{achados.erro}</p>
            : achados.lista.length === 0 ? <p className={css.apoio} role="status">Nenhum cliente ativo com este nome ou telefone. <a href={hashDoModulo('cadastros', 'clientes')}>Cadastre em Clientes</a>.</p>
              : <ul className={css.lista} aria-label="Clientes encontrados">{achados.lista.map((cliente) => (
                <li key={cliente.id}>
                  <span><strong>{nomeExibido(cliente)}</strong><small>{telefoneDoE164(cliente.telefone) || 'Sem telefone no cadastro'}</small></span>
                  {pedeCelularDe !== cliente.id && <Botao variante="primario" disabled={ocupado} aria-label={`Novo pedido de ${nomeExibido(cliente)}`}
                    onClick={() => (cliente.telefone ? novoPedido.iniciar(cliente.id) : pedirCelular(cliente.id))}>
                    {novoPedido.abrindo === cliente.id ? 'Abrindo…' : novoPedido.rotulo(cliente.id)}
                  </Botao>}
                  {pedeCelularDe === cliente.id && <CelularDoPedido clienteId={cliente.id} novoPedido={novoPedido} />}
                </li>
              ))}</ul>}
    </div>
  </Modal>
}
