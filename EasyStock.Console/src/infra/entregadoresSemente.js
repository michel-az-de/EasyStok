// Entregadores de exemplo (rodada 12, issue #17). Nomes e placas inventados.
// Vira cadastro de verdade quando existir backend; com a integração da 99, da
// Lalamove ou do iFood Entregas, quem é de aplicativo chega pelo chamado, no
// mesmo formato (`dominio/despacho.js: montarEntregador`).
//
// Três "José" de propósito: é o caso que a Thatiane contou (três entregadores
// com o mesmo nome no mesmo dia), e só a placa e a empresa separam um do outro.
export const ENTREGADORES_CADASTRADOS = [
  { tipo: 'motoboy', nome: 'José Carlos', veiculo: 'moto', placa: 'FQR3C21', empresa: 'propria' },
  { tipo: 'plataforma', nome: 'José Ribamar', veiculo: 'moto', placa: 'EZT8H40', empresa: '99' },
  { tipo: 'plataforma', nome: 'José Antônio', veiculo: 'carro', placa: 'GHD2A17', empresa: 'lalamove' },
  { tipo: 'plataforma', nome: 'Carlos Mendes', veiculo: 'moto', placa: 'FJK2B41', empresa: 'lalamove' },
  { tipo: 'plataforma', nome: 'Marcos Vinícius', veiculo: 'moto', placa: 'DRS5E09', empresa: 'ifood' },
]

// Quem levou cada pedido antigo da semente: escolha fixa pelo número do
// pedido (sempre o mesmo entregador para o mesmo pedido, sem sorteio a cada
// carga). Um em cada seis a própria dona levou. Só pedido entregue ganha
// entregador: pedido que não saiu não tem quem levou.
const LEVOU_PROPRIA = { tipo: 'propria' }
const ESCALA = [...ENTREGADORES_CADASTRADOS, LEVOU_PROPRIA]

const indiceDoNumero = (numero) => [...String(numero)].reduce((soma, letra) => soma * 31 + letra.charCodeAt(0), 0)

export const comEntregadores = (historico) => historico.map((pedido) => (
  pedido.estado === 'entregue' && pedido.entregador === undefined
    ? { ...pedido, entregador: ESCALA[Math.abs(indiceDoNumero(pedido.numero)) % ESCALA.length] }
    : pedido
))
