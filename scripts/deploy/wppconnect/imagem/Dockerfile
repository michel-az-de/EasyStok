# #3228: a ponte 2.10.81 traz o wa-js 4.7.2, que chama UserPrefs.getMeLidUserOrThrow em todo envio. O WhatsApp Web
# tirou essa funcao, e desde 22/11/2026 todo send-message falha com TypeError. O wa-js 4.8.2 cai em getMaybeMeLidUser
# (wppconnect-team/wa-js#3794). Ate a v2.10.89 e a main do wppconnect-server, o yarn.lock resolve 4.7.2, entao a
# imagem oficial nao resolve. Aqui so o pacote do wa-js e trocado; a biblioteca 2.3.6 declara ^4.7.2, faixa que
# aceita a 4.8.2. Base fixada por digest, pacote fixado por sha512 do npm. Quando uma release oficial trouxer
# wa-js >= 4.8.2 no yarn.lock, volte para ela e apague esta pasta.
FROM wppconnect/wppconnect-server:2.10.81@sha256:7c882ee934a5663a107b5f5a0382bfd8b1fb95150cd21ab35cd8ff9cea70d1f8

ARG WA_JS=4.8.2
ARG WA_JS_SHA512=3xMNZt/rCrJKZBmfD428LsaGakYGvq4vNmH+AzKM2uf7x3/XL4tpyqKO/MtB7NFQmh8UlL957tGOu3i5iMOQqw==

RUN set -eu; \
    cd /tmp; \
    npm pack --silent "@wppconnect/wa-js@${WA_JS}" >/dev/null; \
    f="wppconnect-wa-js-${WA_JS}.tgz"; \
    hash="$(node -e "process.stdout.write(require('crypto').createHash('sha512').update(require('fs').readFileSync('${f}')).digest('base64'))")"; \
    if [ "$hash" != "$WA_JS_SHA512" ]; then echo "sha512 do wa-js ${WA_JS} nao confere: ${hash}" >&2; exit 1; fi; \
    d=/usr/src/wpp-server/node_modules/@wppconnect/wa-js; \
    rm -rf "$d"; mkdir -p "$d"; \
    tar -xzf "$f" -C "$d" --strip-components=1 --no-same-owner; \
    rm -f "$f"; \
    v="$(node -p "require('${d}/package.json').version")"; \
    if [ "$v" != "$WA_JS" ]; then echo "wa-js instalado ${v}, esperado ${WA_JS}" >&2; exit 1; fi; \
    if grep -Eq 'getMyUserLid=function\(\)\{return [A-Za-z_$]+\.UserPrefs\.getMeLidUserOrThrow\(\)\}' "$d/dist/wppconnect-wa.js"; then \
      echo "wa-js ${WA_JS} ainda chama getMeLidUserOrThrow sem alternativa" >&2; exit 1; fi
