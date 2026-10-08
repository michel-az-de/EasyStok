// Nota interna como post-it na conversa (#1441, homologação de 07/10: "pensei
// na nota interna como um post-itzinho"). A nota é do cadastro do cliente, não
// da conversa; entra na linha do tempo pela hora em que foi escrita. Nota sem
// hora ISO (massa antiga) não tem lugar certo no fio e fica só na Ficha.
const instanteDa = (nota) => (nota?.criadoEm ? new Date(nota.criadoEm).getTime() : NaN)

export function intercalarNotas(mensagens, notas) {
  const comHora = (notas ?? [])
    .filter((n) => Number.isFinite(instanteDa(n)))
    .sort((a, b) => instanteDa(a) - instanteDa(b))
  const linha = []
  let proxima = 0
  for (const mensagem of mensagens ?? []) {
    const em = new Date(mensagem.em).getTime()
    while (proxima < comHora.length && instanteDa(comHora[proxima]) <= em) {
      linha.push({ tipo: 'nota', nota: comHora[proxima] })
      proxima += 1
    }
    linha.push({ tipo: 'mensagem', mensagem })
  }
  for (; proxima < comHora.length; proxima += 1) linha.push({ tipo: 'nota', nota: comHora[proxima] })
  return linha
}

// Ficha: as mais recentes primeiro (a lista já chega assim da API e do reducer).
export const notasRecentes = (notas, quantas = 3) => (notas ?? []).slice(0, quantas)
