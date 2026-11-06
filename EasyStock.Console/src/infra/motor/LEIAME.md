# Motor da etiqueta (cópia gerada)

Não edite estes arquivos aqui. A fonte é `EasyStock.Api/wwwroot/pwa/`. Depois de mudar o motor lá, rode:

```
node tools/contratos/sincronizar-motor-console.js
```

O teste `tests/pwa/impressao-v3-motor-console.test.js` reprova se a cópia ficar diferente (#2247, impressão v3, G1 item 7).
O deploy do Console empacota só `EasyStock.Console`, por isso o motor vem copiado em vez de importado.

`qrcode.js` é o qrcode-generator 1.4.4 em ESM (veio da etiqueta antiga, #2081); `motorEtiqueta.js` o põe no global que o motor lê.
