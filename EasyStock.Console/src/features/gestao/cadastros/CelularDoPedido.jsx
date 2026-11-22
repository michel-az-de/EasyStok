import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { erroDoCelular } from '../../../dominio/celularDoPedido'
import css from './pedidos.module.css'

// #2930: Novo pedido de cliente sem telefone no cadastro. O celular é pedido aqui; a API grava no cadastro e abre o pedido.
export function CelularDoPedido({ clienteId, novoPedido }) {
  const [celular, digitar] = useState({ texto: '', erro: null })
  function abrir(evento) {
    evento.preventDefault()
    const erro = erroDoCelular(celular.texto)
    if (erro) { digitar((atual) => ({ ...atual, erro })); return }
    novoPedido.iniciar(clienteId, { celular: celular.texto })
  }
  return <form className={css.celular} onSubmit={abrir} noValidate>
    <CampoTexto rotulo="Celular com DDD (para enviar o resumo)" tipo="tel" inputMode="tel" autoComplete="off" autoFocus placeholder="(11) 98888-7777"
      value={celular.texto} erro={celular.erro} dica="Fica gravado no cadastro do cliente."
      onChange={(e) => digitar({ texto: e.target.value, erro: null })} />
    <Botao tipo="submit" variante="primario" disabled={Boolean(novoPedido.abrindo)}>{novoPedido.abrindo === clienteId ? 'Abrindo…' : 'Abrir pedido'}</Botao>
  </form>
}

// O mesmo campo em janela, para o botão Novo pedido da linha da lista de Clientes.
export function PedirCelularDoPedido({ cliente, novoPedido, aoFechar }) {
  const ocupado = Boolean(novoPedido.abrindo)
  return <Modal titulo={`Novo pedido de ${cliente.nome}`} descricao="O cadastro está sem telefone. O pedido segue pelo WhatsApp da loja, então falta o celular."
    aoFechar={ocupado ? undefined : aoFechar} segurarFechamento={ocupado} focarCampo
    rodape={<Botao disabled={ocupado} onClick={aoFechar}>Cancelar</Botao>}>
    {novoPedido.erro && <p ref={novoPedido.refDoErro} tabIndex={-1} className={css.falta} role="alert">{novoPedido.erro}</p>}
    <CelularDoPedido clienteId={cliente.id} novoPedido={novoPedido} />
  </Modal>
}
