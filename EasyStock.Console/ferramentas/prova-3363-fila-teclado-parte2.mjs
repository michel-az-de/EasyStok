// #3398: grupos S3 e S4 da prova-3363-fila-teclado num processo próprio, para cada metade caber no teto de 180 s do
// rodar-provas. Usa o Playwright da prova base (PLAYWRIGHT_MODULE); a lista de grupos de cada parte fica nela.
process.argv.splice(2, process.argv.length, 'S3', 'S4')
await import('./prova-3363-fila-teclado.mjs')
