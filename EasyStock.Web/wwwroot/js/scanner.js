/* scanner.js — leitor de codigo de barras com fallbacks (Fase 5).
 *
 * Camadas (feature detection):
 *   1) BarcodeDetector nativo (Chrome Android/Desktop, iOS 17+)
 *   2) input USB focado (leitor laser via teclado)
 *
 * Fallback ZXing-js NAO esta incluido — depende de CDN externa, e o ambiente
 * pode bloquear. Pode ser adicionado em uma fase futura via wwwroot/lib/zxing/.
 *
 * API:
 *   const scanner = await EasyScanner.create({ formats: ['ean_13', 'ean_8', 'code_128'] });
 *   if (scanner.supportsCamera) {
 *     await scanner.startCamera(videoEl, code => onScan(code));
 *     scanner.stop();
 *   }
 *
 *   // Modo USB (sempre disponivel): input focado captura entrada de leitor laser.
 *   EasyScanner.attachUsbInput(inputEl, code => onScan(code), {
 *     minLength: 6,            // ignora teclagens curtas (humano)
 *     maxIntervalMs: 30        // chars muito espacados nao sao do leitor
 *   });
 */
(function (global) {
    'use strict';

    if (global.EasyScanner) return;

    const SUPPORTED_FORMATS_DEFAULT = ['ean_13', 'ean_8', 'code_128', 'qr_code', 'upc_a', 'upc_e'];

    function hasNativeBarcodeDetector() {
        return typeof global.BarcodeDetector !== 'undefined';
    }

    async function nativeFormats() {
        if (!hasNativeBarcodeDetector()) return [];
        try { return await global.BarcodeDetector.getSupportedFormats(); }
        catch (_) { return []; }
    }

    async function create(opts) {
        opts = opts || {};
        const requested = opts.formats || SUPPORTED_FORMATS_DEFAULT;
        const supportsCamera = hasNativeBarcodeDetector();
        const supported = supportsCamera ? await nativeFormats() : [];
        const usable = requested.filter(f => supported.indexOf(f) !== -1);

        let detector = null;
        let stream = null;
        let rafId = null;
        let stopped = false;

        if (supportsCamera && usable.length > 0) {
            try { detector = new global.BarcodeDetector({ formats: usable }); }
            catch (_) { detector = null; }
        }

        async function startCamera(videoEl, onScan, errOpts) {
            if (!detector) {
                if (typeof errOpts?.onUnsupported === 'function') errOpts.onUnsupported();
                return false;
            }
            try {
                stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: 'environment' }, audio: false
                });
            } catch (e) {
                console.warn('[EasyScanner] camera bloqueada/indisponivel', e);
                if (typeof errOpts?.onPermissionDenied === 'function') errOpts.onPermissionDenied(e);
                return false;
            }
            videoEl.srcObject = stream;
            videoEl.setAttribute('playsinline', 'true');
            await videoEl.play().catch(() => {});

            stopped = false;
            const seen = new Set();
            const seenTtl = 1500; // ms

            async function loop() {
                if (stopped) return;
                try {
                    const codes = await detector.detect(videoEl);
                    if (codes && codes.length > 0) {
                        for (const c of codes) {
                            const v = c.rawValue || '';
                            if (!v || seen.has(v)) continue;
                            seen.add(v);
                            setTimeout(() => seen.delete(v), seenTtl);
                            try { onScan(v, c.format); } catch (e) { console.error('[EasyScanner]', e); }
                        }
                    }
                } catch (_) { /* frame skip */ }
                rafId = requestAnimationFrame(loop);
            }
            rafId = requestAnimationFrame(loop);
            return true;
        }

        function stop() {
            stopped = true;
            if (rafId) cancelAnimationFrame(rafId);
            rafId = null;
            if (stream) {
                stream.getTracks().forEach(t => t.stop());
                stream = null;
            }
        }

        return {
            supportsCamera: supportsCamera && !!detector,
            supportedFormats: usable,
            startCamera,
            stop
        };
    }

    /** Captura entrada de leitor USB (HID) em um input. Heuristica: rajadas
     *  rapidas (chars com intervalo < maxIntervalMs) terminadas por Enter sao tratadas
     *  como leitura. Rajadas curtas (< minLength) sao ignoradas (humano digitando). */
    function attachUsbInput(inputEl, onScan, opts) {
        opts = opts || {};
        const minLength = opts.minLength || 6;
        const maxIntervalMs = opts.maxIntervalMs || 30;

        let buffer = '';
        let lastTs = 0;
        let timer = null;

        function flush(emit) {
            const v = buffer;
            buffer = '';
            lastTs = 0;
            if (timer) { clearTimeout(timer); timer = null; }
            if (emit && v.length >= minLength) {
                try { onScan(v); } catch (e) { console.error('[EasyScanner]', e); }
            }
        }

        inputEl.addEventListener('keydown', function (ev) {
            const now = performance.now();
            const dt = lastTs ? (now - lastTs) : 0;

            if (ev.key === 'Enter') {
                ev.preventDefault();
                flush(true);
                return;
            }
            // Caractere imprimivel
            if (ev.key && ev.key.length === 1) {
                if (lastTs === 0 || dt < maxIntervalMs) {
                    buffer += ev.key;
                    lastTs = now;
                } else {
                    // Pausa longa: novo input
                    buffer = ev.key;
                    lastTs = now;
                }
                if (timer) clearTimeout(timer);
                timer = setTimeout(() => flush(true), 50);
            }
        });

        return () => flush(false);
    }

    global.EasyScanner = { create, attachUsbInput };
})(window);
