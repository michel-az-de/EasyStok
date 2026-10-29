// Etiqueta de produção da térmica de 58 mm (Hipertech WTP05), #2141.
// Um desenho só: o mesmo bitmap de 384 px é a prévia na tela, o PNG salvo e
// a impressão em raster GS v 0. Texto como imagem preserva acento e marca,
// que o modo texto ESC/POS da WTP05 não imprime.
(function (global) {
  'use strict';

  const LARGURA = 384;        // 48 mm úteis a 8 pontos/mm
  const MARGEM = 12;
  const FAIXA = 128;          // linhas por comando GS v 0
  const PRETO = '#000';
  const BRANCO = '#fff';
  const SANS = 'Manrope, Arial, sans-serif';
  const SERIF = 'Fraunces, Georgia, serif';

  function fonte(ctx, peso, tamanho, familia) { ctx.font = `${peso} ${tamanho}px ${familia || SANS}`; }

  function quebrar(ctx, texto, largura) {
    const linhas = [];
    let linha = '';
    for (const palavra of String(texto || '').split(/\s+/).filter(Boolean)) {
      const teste = linha ? linha + ' ' + palavra : palavra;
      if (!linha || ctx.measureText(teste).width <= largura) linha = teste;
      else { linhas.push(linha); linha = palavra; }
    }
    if (linha) linhas.push(linha);
    return linhas;
  }

  // Maior fonte em que o texto cabe inteiro em até maxLinhas, sem cortar letra.
  function ajustar(ctx, texto, largura, tamanhos, peso, maxLinhas) {
    for (const tamanho of tamanhos) {
      fonte(ctx, peso, tamanho);
      const linhas = quebrar(ctx, texto, largura);
      if (linhas.length <= maxLinhas && linhas.every(l => ctx.measureText(l).width <= largura)) return { tamanho, linhas };
    }
    const tamanho = tamanhos[tamanhos.length - 1];
    fonte(ctx, peso, tamanho);
    return { tamanho, linhas: quebrar(ctx, texto, largura) };
  }

  function regua(ctx, y, espessura) {
    ctx.fillStyle = PRETO;
    ctx.fillRect(MARGEM, y, LARGURA - MARGEM * 2, espessura);
  }

  function linhaDado(ctx, y, rotulo, valor) {
    const util = LARGURA - MARGEM * 2;
    ctx.fillStyle = PRETO;
    ctx.textAlign = 'left';
    fonte(ctx, 600, 18);
    ctx.fillText(rotulo, MARGEM, y + 3);
    const espacoValor = util - ctx.measureText(rotulo).width - 12;
    const { tamanho } = ajustar(ctx, valor, espacoValor, [24, 22, 20, 18, 16], 800, 1);
    fonte(ctx, 800, tamanho);
    ctx.textAlign = 'right';
    ctx.fillText(valor, LARGURA - MARGEM, y);
    ctx.textAlign = 'left';
    return 32;
  }

  function desenharQr(ctx, dado, y) {
    if (typeof global.qrcode !== 'function') return null;
    const qr = global.qrcode(0, 'M');
    qr.addData(String(dado));
    qr.make();
    const modulos = qr.getModuleCount();
    const modulo = Math.max(3, Math.floor(184 / (modulos + 8)));
    const lado = modulos * modulo;
    const x = Math.round((LARGURA - lado) / 2);
    const top = y + modulo * 4;
    ctx.fillStyle = BRANCO;
    ctx.fillRect(x - modulo * 4, y, lado + modulo * 8, lado + modulo * 8);
    ctx.fillStyle = PRETO;
    for (let r = 0; r < modulos; r++)
      for (let c = 0; c < modulos; c++)
        if (qr.isDark(r, c)) ctx.fillRect(x + c * modulo, top + r * modulo, modulo, modulo);
    return { x, y: top, modulo, modulos, altura: lado + modulo * 8 };
  }

  // Desenha em ctx (fundo branco já pintado ou não) e devolve a altura usada.
  // opts.formatar = { data(ms), peso(g) }; opts.codigos = 'qr'|'barcode'|'both'|'none';
  // opts.code128(ctx, dado, x, y, w, h) desenha barras; opts.nota é texto livre.
  function desenhar(ctx, et, opts) {
    const o = opts || {};
    const fmt = o.formatar;
    const util = LARGURA - MARGEM * 2;
    ctx.textBaseline = 'top';
    let y = MARGEM;

    ctx.fillStyle = PRETO;
    ctx.textAlign = 'center';
    fonte(ctx, 700, 26, SERIF);
    ctx.fillText(et.empresa || 'Casa da Baba', LARGURA / 2, y);
    y += 36;
    regua(ctx, y, 2);
    y += 12;

    const nome = ajustar(ctx, et.productName, util, [32, 28, 24, 21, 18], 800, 3);
    fonte(ctx, 800, nome.tamanho);
    ctx.textAlign = 'center';
    for (const l of nome.linhas) { ctx.fillText(l, LARGURA / 2, y); y += Math.round(nome.tamanho * 1.18); }
    if (et.unit) {
      fonte(ctx, 600, 20);
      ctx.fillText(et.unit, LARGURA / 2, y + 2);
      y += 28;
    }
    y += 6;
    regua(ctx, y, 1);
    y += 10;

    y += linhaDado(ctx, y, 'Fabricação', fmt.data(et.productionDate));
    y += linhaDado(ctx, y, 'Validade', et.expiresAt ? fmt.data(et.expiresAt) : '—');
    y += linhaDado(ctx, y, 'Lote', String(et.lote || ''));
    if (et.weightG) y += linhaDado(ctx, y, 'Peso', fmt.peso(et.weightG));

    if (o.nota) {
      y += 4;
      regua(ctx, y, 1);
      y += 10;
      const nota = ajustar(ctx, 'Nota: ' + o.nota, util, [20, 18], 700, 2);
      ctx.textAlign = 'left';
      fonte(ctx, 700, nota.tamanho);
      for (const l of nota.linhas) { ctx.fillText(l, MARGEM, y); y += Math.round(nota.tamanho * 1.25); }
    }

    const codigos = o.codigos || 'qr';
    let qr = null;
    if (codigos !== 'none' && et.codigoBarras) {
      y += 8;
      if (codigos === 'qr' || codigos === 'both') {
        qr = desenharQr(ctx, et.codigoBarras, y);
        if (qr) y += qr.altura;
      }
      if ((codigos === 'barcode' || codigos === 'both') && typeof o.code128 === 'function') {
        o.code128(ctx, et.codigoBarras, MARGEM, y + 4, util, 56);
        y += 64;
      }
      ctx.fillStyle = PRETO;
      ctx.textAlign = 'center';
      const codigo = ajustar(ctx, et.codigoBarras, util, [16, 14, 12], 700, 1);
      fonte(ctx, 700, codigo.tamanho);
      ctx.fillText(et.codigoBarras, LARGURA / 2, y + 2);
      y += codigo.tamanho + 8;
    }
    ctx.textAlign = 'left';
    return { altura: Math.ceil(y + MARGEM), qr };
  }

  // RGBA -> comandos GS v 0 (preto = 1), em faixas de FAIXA linhas.
  function raster(rgba, largura, altura) {
    const porLinha = Math.ceil(largura / 8);
    const faixas = Math.ceil(altura / FAIXA);
    const out = new Uint8Array(faixas * 8 + porLinha * altura);
    let k = 0;
    for (let inicio = 0; inicio < altura; inicio += FAIXA) {
      const linhas = Math.min(FAIXA, altura - inicio);
      out.set([0x1D, 0x76, 0x30, 0x00, porLinha & 0xFF, porLinha >> 8, linhas & 0xFF, linhas >> 8], k);
      k += 8;
      for (let yy = inicio; yy < inicio + linhas; yy++) {
        for (let b = 0; b < porLinha; b++) {
          let byte = 0;
          for (let bit = 0; bit < 8; bit++) {
            const xx = b * 8 + bit;
            if (xx >= largura) continue;
            const i = (yy * largura + xx) * 4;
            const luz = (rgba[i] * 299 + rgba[i + 1] * 587 + rgba[i + 2] * 114) / 1000;
            if (rgba[i + 3] >= 128 && luz < 128) byte |= 0x80 >> bit;
          }
          out[k++] = byte;
        }
      }
    }
    return out;
  }

  // Bytes completos de uma etiqueta já desenhada em ctx (largura LARGURA).
  function bytes(ctx, altura) {
    const img = ctx.getImageData(0, 0, LARGURA, altura);
    const corpo = raster(img.data, LARGURA, altura);
    const out = new Uint8Array(2 + corpo.length + 3);
    out.set([0x1B, 0x40], 0);
    out.set(corpo, 2);
    out.set([0x1B, 0x64, 0x04], 2 + corpo.length);
    return out;
  }

  // No navegador: canvas recortado na altura exata, pronto para prévia,
  // PNG e impressão.
  function gerar(et, opts) {
    const rascunho = global.document.createElement('canvas');
    rascunho.width = LARGURA;
    rascunho.height = 1600;
    const r = rascunho.getContext('2d');
    if (!r) throw new Error('canvas indisponível');
    r.fillStyle = BRANCO;
    r.fillRect(0, 0, LARGURA, rascunho.height);
    const { altura } = desenhar(r, et, opts);
    const canvas = global.document.createElement('canvas');
    canvas.width = LARGURA;
    canvas.height = altura;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(rascunho, 0, 0);
    // A prévia mostra os pontos exatos da impressora: o mesmo limiar do raster.
    const img = ctx.getImageData(0, 0, LARGURA, altura);
    const d = img.data;
    for (let i = 0; i < d.length; i += 4) {
      const ponto = d[i + 3] >= 128 && (d[i] * 299 + d[i + 1] * 587 + d[i + 2] * 114) / 1000 < 128 ? 0 : 255;
      d[i] = d[i + 1] = d[i + 2] = ponto;
      d[i + 3] = 255;
    }
    ctx.putImageData(img, 0, 0);
    return { canvas, altura, bytes: () => bytes(ctx, altura) };
  }

  global.cdbEtiquetaTermica = { LARGURA, FAIXA, desenhar, raster, bytes, gerar };
})(typeof window !== 'undefined' ? window : globalThis);
