// #3392: itens D a H da prova-3386-r9 num processo próprio, para cada metade caber no teto de 180 s do rodar-provas.
// Usa o Playwright da r9 (PLAYWRIGHT_MODULE); a lista de itens de cada parte fica em prova-3386-r9.mjs.
process.argv.splice(2, process.argv.length, 'D', 'E', 'F', 'G', 'H')
await import('./prova-3386-r9.mjs')
