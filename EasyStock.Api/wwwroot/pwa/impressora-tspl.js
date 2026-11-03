(function (global) {
  'use strict';
  // #2193: impressoras de etiqueta TSPL (Oasis OIA-8381-B, 203 dpi, até 104 mm)
  // ignoram ESC/POS. O mesmo bitmap da etiqueta térmica vira um BITMAP TSPL.
  //
  // Polaridade do BITMAP: bit 0 = ponto impresso (preto), bit 1 = sem ponto
  // (branco), o inverso do GS v 0. Fonte: TSPL BITMAP X,Y,width(bytes),height,mode,data
  // (hackernoon.com/how-to-print-images-with-tspl-and-javascript). Validar no aparelho.
  const DPI = 203;
  const PONTOS_MM = 8;
  const BRANCO = '#fff';
  const PRETO = '#000';
  const SANS = 'Inter, "Noto Sans", Roboto, Arial, sans-serif';

  function ascii(texto) {
    const out = new Uint8Array(texto.length);
    for (let i = 0; i < texto.length; i++) out[i] = texto.charCodeAt(i) & 0x7F;
    return out;
  }

  function num(v, padrao) {
    const n = Number(v);
    return Number.isFinite(n) && n > 0 ? n : padrao;
  }

  // Pixel preto pelo mesmo limiar do raster() de etiqueta-termica.js.
  function escuro(rgba, i) {
    const luz = (rgba[i] * 299 + rgba[i + 1] * 587 + rgba[i + 2] * 114) / 1000;
    return rgba[i + 3] >= 128 && luz < 128;
  }

  // RGBA -> dados do BITMAP TSPL (linha a linha, MSB primeiro, 0 = preto).
  function dadosBitmap(rgba, largura, altura) {
    const porLinha = Math.ceil(largura / 8);
    const out = new Uint8Array(porLinha * altura).fill(0xFF);
    let k = 0;
    for (let y = 0; y < altura; y++) {
      for (let b = 0; b < porLinha; b++) {
        let byte = 0xFF;
        for (let bit = 0; bit < 8; bit++) {
          const x = b * 8 + bit;
          if (x >= largura) continue;
          if (escuro(rgba, (y * largura + x) * 4)) byte &= ~(0x80 >> bit);
        }
        out[k++] = byte & 0xFF;
      }
    }
    return out;
  }

  // Etiqueta completa: cabeçalho ASCII, BITMAP binário e PRINT.
  function etiqueta(rgba, larguraPx, alturaPx, opts) {
    const o = opts || {};
    const larguraMm = num(o.larguraMm, 104);
    const alturaMm = num(o.alturaMm, Math.ceil(alturaPx / PONTOS_MM) + 2);
    const gapMm = o.gapMm === 0 ? 0 : num(o.gapMm, 2);
    const densidade = num(o.densidade, 8);
    const velocidade = num(o.velocidade, 4);
    const copias = Math.max(1, Math.floor(num(o.copias, 1)));
    const porLinha = Math.ceil(larguraPx / 8);
    const x = Math.max(0, Math.floor((larguraMm * PONTOS_MM - larguraPx) / 2));
    const y = Math.max(0, Math.floor(num(o.y, 0)));
    const gap = o.continuo ? 'GAP 0,0' : `GAP ${gapMm} mm,0 mm`;
    const cabeca = ascii([
      `SIZE ${larguraMm} mm,${alturaMm} mm`,
      gap,
      'DIRECTION 1',
      'REFERENCE 0,0',
      `DENSITY ${densidade}`,
      `SPEED ${velocidade}`,
      'CLS',
      `BITMAP ${x},${y},${porLinha},${alturaPx},0,`
    ].join('\r\n'));
    const dados = dadosBitmap(rgba, larguraPx, alturaPx);
    // Se a etiqueta sair em negativo no aparelho, o perfil liga a inversão.
    if (o.inverter) for (let i = 0; i < dados.length; i++) dados[i] = ~dados[i] & 0xFF;
    const fim = ascii(`\r\nPRINT 1,${copias}\r\n`);
    const out = new Uint8Array(cabeca.length + dados.length + fim.length);
    out.set(cabeca, 0);
    out.set(dados, cabeca.length);
    out.set(fim, cabeca.length + dados.length);
    return out;
  }

  function doCanvas(canvas, opts) {
    const ctx = canvas.getContext('2d');
    const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
    return etiqueta(img.data, canvas.width, canvas.height, opts);
  }

  // Encaixa o desenho (384 px) na etiqueta configurada: escala até caber na
  // largura e na altura úteis, centraliza na vertical. Bobina contínua só
  // limita pela largura e a etiqueta cresce com o desenho.
  function paraEtiqueta(canvas, cfg) {
    const c = cfg || {};
    const doc = c.document || global.document;
    const larguraMm = num(c.larguraMm, 104);
    const margem = 2 * PONTOS_MM;
    const maxL = larguraMm * PONTOS_MM - margem;
    const maxA = c.continuo ? Infinity : num(c.alturaMm, 40) * PONTOS_MM - margem;
    const escala = Math.min(maxL / canvas.width, maxA / canvas.height);
    const largura = Math.max(8, Math.floor(canvas.width * escala));
    const altura = Math.max(1, Math.floor(canvas.height * escala));
    const alvo = doc.createElement('canvas');
    alvo.width = largura;
    alvo.height = altura;
    const ctx = alvo.getContext('2d');
    ctx.imageSmoothingEnabled = escala < 1;
    ctx.fillStyle = BRANCO;
    ctx.fillRect(0, 0, largura, altura);
    ctx.drawImage(canvas, 0, 0, canvas.width, canvas.height, 0, 0, largura, altura);
    const alturaMm = c.continuo ? Math.ceil(altura / PONTOS_MM) + 2 : num(c.alturaMm, 40);
    const y = c.continuo ? PONTOS_MM : Math.max(0, Math.floor((alturaMm * PONTOS_MM - altura) / 2));
    return doCanvas(alvo, Object.assign({}, c, { larguraMm, alturaMm, y }));
  }

  function quebrar(ctx, texto, largura) {
    const palavras = String(texto == null ? '' : texto).split(/\s+/).filter(Boolean);
    const linhas = [];
    let atual = '';
    palavras.forEach(p => {
      const tentativa = atual ? atual + ' ' + p : p;
      if (atual && ctx.measureText(tentativa).width > largura) { linhas.push(atual); atual = p; }
      else atual = tentativa;
    });
    if (atual || !linhas.length) linhas.push(atual);
    return linhas;
  }

  // Texto como imagem: teste de impressão e recibo em TSPL com acento.
  function linhasParaCanvas(titulo, linhas, opts) {
    const o = opts || {};
    const doc = o.document || global.document;
    const largura = num(o.larguraPx, 384);
    const margem = 8;
    const util = largura - margem * 2;
    const rascunho = doc.createElement('canvas');
    rascunho.width = largura;
    rascunho.height = 2400;
    const r = rascunho.getContext('2d');
    r.fillStyle = BRANCO;
    r.fillRect(0, 0, largura, rascunho.height);
    r.fillStyle = PRETO;
    r.textBaseline = 'top';
    r.textAlign = 'left';
    let y = margem;
    if (titulo) {
      r.font = `800 28px ${SANS}`;
      quebrar(r, titulo, util).forEach(l => { r.fillText(l, margem, y); y += 34; });
      y += 6;
    }
    r.font = `500 22px ${SANS}`;
    (linhas || []).forEach(linha => {
      quebrar(r, linha, util).forEach(l => { r.fillText(l, margem, y); y += 28; });
    });
    const altura = Math.min(rascunho.height, Math.ceil(y + margem));
    const canvas = doc.createElement('canvas');
    canvas.width = largura;
    canvas.height = altura;
    canvas.getContext('2d').drawImage(rascunho, 0, 0, largura, altura, 0, 0, largura, altura);
    return canvas;
  }

  global.cdbTspl = { DPI, PONTOS_MM, etiqueta, doCanvas, paraEtiqueta, linhasParaCanvas, dadosBitmap };
})(typeof window !== 'undefined' ? window : globalThis);
