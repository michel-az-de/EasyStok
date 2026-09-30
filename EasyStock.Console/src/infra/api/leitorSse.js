// Leitor de quadros SSE para o `fetch` em stream (S18). O EventSource do navegador
// não manda o header Authorization, e o SSE de operação exige o JWT nele. Recebe o
// texto em pedaços quaisquer e chama `aoEvento({ evento, dados })` a cada quadro
// completo; comentário (`: heartbeat`) e quadro sem `data` são ignorados.
export function criarLeitorSse(aoEvento) {
  let resto = ''
  return (pedaco) => {
    resto += pedaco.replace(/\r\n?/g, '\n')
    let fim
    while ((fim = resto.indexOf('\n\n')) >= 0) {
      const quadro = resto.slice(0, fim)
      resto = resto.slice(fim + 2)
      let evento = 'message'
      const dados = []
      for (const linha of quadro.split('\n')) {
        if (linha.startsWith('event:')) evento = linha.slice(6).trim()
        else if (linha.startsWith('data:')) dados.push(linha.slice(5).replace(/^ /, ''))
      }
      if (dados.length === 0) continue
      let valor
      try { valor = JSON.parse(dados.join('\n')) } catch { valor = dados.join('\n') }
      aoEvento({ evento, dados: valor })
    }
  }
}
