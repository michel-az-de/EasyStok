// PWA Caixa NFC-e — fluxo: adicionar itens, emitir NFC-e via API, mostrar DANFE.
// Auth: JWT em localStorage (key "easystok_jwt"). Caso ausente, redireciona para login.

(function () {
    "use strict";

    const STORAGE_TOKEN_KEY = "easystok_jwt";
    const STORAGE_EMITENTE_KEY = "easystok_caixa_emitente";

    // ── Estado ───────────────────────────────────────────────────────────────

    const state = {
        itens: [],
        emitente: null,
        // IdempotencyKey gerado uma vez por venda (regenera apenas em "Nova venda").
        // Evita criar 2 NFC-e se usuário clicar Emitir 2x ou recarregar página antes
        // do servidor responder.
        idempotencyKey: null,
    };

    function novaIdempotencyKey() {
        return (crypto.randomUUID && crypto.randomUUID().replace(/-/g, "")) ||
            (Date.now().toString(36) + Math.random().toString(36).slice(2));
    }

    // ── Helpers DOM ──────────────────────────────────────────────────────────

    function $(id) { return document.getElementById(id); }
    function fmtBRL(v) {
        return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
    }

    function showStatus(msg, classe) {
        const el = $("statusMsg");
        if (!el) return;
        el.className = "status-msg " + (classe || "status-info");
        el.textContent = msg;
    }

    function hideStatus() {
        const el = $("statusMsg");
        if (!el) return;
        el.className = "status-msg";
        el.textContent = "";
    }

    // ── Carregar emitente ────────────────────────────────────────────────────

    function carregarEmitente() {
        const raw = localStorage.getItem(STORAGE_EMITENTE_KEY);
        if (raw) {
            try { state.emitente = JSON.parse(raw); return; }
            catch { /* fall-through */ }
        }
        state.emitente = null;
    }

    async function buscarEmitente() {
        const jwt = localStorage.getItem(STORAGE_TOKEN_KEY);
        if (!jwt) throw new Error("Faça login.");

        const resp = await fetch("/api/configuracao-fiscal", {
            headers: { "Authorization": "Bearer " + jwt },
        });
        if (!resp.ok) throw new Error("Falha buscando config fiscal.");

        const json = await resp.json();
        const data = json.data ?? json;
        if (!data?.configurado) throw new Error("Tenant sem config fiscal habilitada.");

        // Dados do emitente vêm do próprio /api/configuracao-fiscal (cnpj/razão/fantasia).
        state.emitente = {
            cnpj: data.cnpj || "",
            razaoSocial: data.razaoSocial || "(razao social)",
            nomeFantasia: data.nomeFantasia,
            inscricaoEstadual: data.inscricaoEstadual,
            ambiente: data.ambiente,
        };
        localStorage.setItem(STORAGE_EMITENTE_KEY, JSON.stringify(state.emitente));

        if (data.ambiente?.toLowerCase().includes("prod")) {
            $("ambienteIndicator").textContent = "Produção";
            $("ambienteIndicator").className = "badge badge-producao";
        }
    }

    // ── Itens ────────────────────────────────────────────────────────────────

    function adicionarItem() {
        const nome = $("inputNome").value.trim();
        const qtd = parseFloat($("inputQtd").value);
        const preco = parseFloat($("inputPreco").value);
        const unidade = $("inputUnidade").value.trim() || "UN";
        const ncm = $("inputNcm").value.trim() || null;
        const cfop = $("inputCfop").value.trim() || null;

        if (!nome) return showStatus("Nome do produto obrigatório.", "status-erro");
        if (!(qtd > 0)) return showStatus("Quantidade deve ser > 0.", "status-erro");
        if (!(preco > 0)) return showStatus("Preço unitário deve ser > 0.", "status-erro");

        state.itens.push({
            nome,
            quantidade: qtd,
            precoUnitario: preco,
            unidade,
            ncm: ncm && ncm.length === 8 ? ncm : null,
            cfop: cfop && cfop.length === 4 ? cfop : null,
        });

        renderItens();
        $("inputNome").value = "";
        $("inputQtd").value = "1";
        $("inputPreco").value = "";
        $("inputNome").focus();
        hideStatus();
    }

    function removerItem(idx) {
        state.itens.splice(idx, 1);
        renderItens();
    }

    function renderItens() {
        const ul = $("listaItens");
        ul.innerHTML = "";
        let total = 0;

        state.itens.forEach((it, idx) => {
            const subtotal = it.quantidade * it.precoUnitario;
            total += subtotal;

            const li = document.createElement("li");
            li.innerHTML = `
                <span class="item-nome">${escapeHtml(it.nome)}</span>
                <span class="item-info">${it.quantidade} × ${fmtBRL(it.precoUnitario)} = <strong>${fmtBRL(subtotal)}</strong></span>
                <button class="item-remover" data-idx="${idx}">×</button>
            `;
            ul.appendChild(li);
        });

        const elTotal = $("totalNota");
        if (elTotal) elTotal.textContent = fmtBRL(total);
        const elEmitir = $("btnEmitir");
        if (elEmitir) elEmitir.disabled = state.itens.length === 0;
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c =>
            ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[c]));
    }

    // ── Emitir ───────────────────────────────────────────────────────────────

    async function emitirNfce() {
        if (state.itens.length === 0) return showStatus("Adicione ao menos 1 item.", "status-erro");
        if (!state.emitente) {
            try { await buscarEmitente(); }
            catch (e) { return showStatus(e.message, "status-erro"); }
        }

        const jwt = localStorage.getItem(STORAGE_TOKEN_KEY);
        if (!jwt) return showStatus("Sessão expirada. Entre novamente para continuar.", "status-erro");

        const totalNota = state.itens.reduce((s, it) => s + it.quantidade * it.precoUnitario, 0);
        const cpfDest = $("inputCpfDestinatario").value.replace(/\D/g, "");
        const nomeDest = $("inputNomeDestinatario").value.trim();

        if (cpfDest && cpfDest.length !== 11 && cpfDest.length !== 14) {
            return showStatus("Documento do consumidor deve ter 11 (CPF) ou 14 (CNPJ) dígitos.", "status-erro");
        }

        if (!state.idempotencyKey) state.idempotencyKey = novaIdempotencyKey();

        const payload = {
            pedidoId: crypto.randomUUID
                ? crypto.randomUUID()
                : "00000000-0000-0000-0000-" + Date.now().toString(16).padStart(12, "0"),
            idempotencyKey: state.idempotencyKey,
            totalNota: parseFloat(totalNota.toFixed(2)),
            emitente: {
                cnpj: (state.emitente.cnpj || "").replace(/\D/g, ""),
                razaoSocial: state.emitente.razaoSocial,
                nomeFantasia: state.emitente.nomeFantasia,
                inscricaoEstadual: state.emitente.inscricaoEstadual,
            },
            destinatario: (cpfDest || nomeDest) ? {
                cpfCnpj: cpfDest || null,
                nome: nomeDest || null,
            } : null,
            itens: state.itens.map(it => ({
                nomeSnapshot: it.nome,
                quantidade: it.quantidade,
                precoUnitario: it.precoUnitario,
                unidade: it.unidade,
                ncm: it.ncm,
                cfop: it.cfop,
                origemMercadoria: 0,
            })),
        };

        showStatus("Emitindo NFC-e...", "status-info");
        setBtnEmitirLoading(true);

        try {
            const resp = await fetch("/api/notas-fiscais/emitir", {
                method: "POST",
                headers: {
                    "Authorization": "Bearer " + jwt,
                    "Content-Type": "application/json",
                },
                body: JSON.stringify(payload),
            });

            if (!resp.ok) {
                const errBody = await safeJson(resp);
                const detalhe = errBody?.error?.message;
                const codigo = errBody?.error?.code;
                if (resp.status === 401) throw new Error("Sessão expirada. Entre novamente.");
                if (resp.status === 403) throw new Error("Sem permissão para emitir nota fiscal.");
                if (resp.status === 422) throw new Error(detalhe || "NFC-e rejeitada pela SEFAZ. Verifique os dados.");
                if (resp.status === 429) throw new Error("Muitas emissões em sequência. Aguarde 1 minuto.");
                if (resp.status === 503) throw new Error(detalhe || "Gateway fiscal indisponível. Tente em alguns segundos.");
                if (resp.status >= 500) throw new Error("Servidor indisponível. Tente novamente em alguns segundos.");
                throw new Error(detalhe || "Não foi possível emitir a NFC-e. Verifique os dados e tente novamente.");
            }

            const json = await resp.json();
            const nfe = json.data ?? json;

            if (nfe.status === "Autorizada" || nfe.status === 2 /*StatusNfe.Autorizada*/) {
                showStatus(`NFC-e autorizada — chave ${nfe.chaveAcesso}`, "status-ok");
                mostrarDanfe(nfe, totalNota);
            } else if (nfe.status === "FalhaTransiente" || nfe.status === 6) {
                showStatus("NFC-e em contingência — job vai reprocessar.", "status-info");
            } else {
                showStatus(`NFC-e ${nfe.status}: ${nfe.motivoRejeicao ?? "(sem detalhe)"}`, "status-erro");
            }
        } catch (e) {
            showStatus(e.message, "status-erro");
        } finally {
            setBtnEmitirLoading(false);
        }
    }

    function setBtnEmitirLoading(on) {
        const btn = $("btnEmitir");
        if (!btn) return;
        const label = btn.querySelector(".btn__label");
        if (on) {
            btn.dataset.originalLabel = label ? label.textContent : btn.textContent;
            if (label) label.textContent = "Emitindo NFC-e...";
            btn.classList.add("is-loading");
            btn.disabled = true;
            btn.setAttribute("aria-busy", "true");
        } else {
            if (label && btn.dataset.originalLabel) label.textContent = btn.dataset.originalLabel;
            btn.classList.remove("is-loading");
            btn.disabled = state.itens.length === 0;
            btn.removeAttribute("aria-busy");
        }
    }

    async function safeJson(resp) {
        try { return await resp.json(); } catch { return null; }
    }

    // ── DANFE ────────────────────────────────────────────────────────────────

    function mostrarDanfe(nfe, totalNota) {
        const emit = state.emitente || {};
        $("danfeEmitenteRazao").textContent = emit.razaoSocial ?? "";
        $("danfeEmitenteCnpj").textContent = emit.cnpj ?? "";
        $("danfeEmitenteIE").textContent = emit.inscricaoEstadual ?? "(isento)";

        const tbody = $("danfeItensBody");
        tbody.innerHTML = "";
        state.itens.forEach((it, idx) => {
            const tr = document.createElement("tr");
            tr.innerHTML = `
                <td>${idx + 1}</td>
                <td>${escapeHtml(it.nome)}</td>
                <td>${it.quantidade}</td>
                <td>${fmtBRL(it.precoUnitario)}</td>
                <td>${fmtBRL(it.quantidade * it.precoUnitario)}</td>
            `;
            tbody.appendChild(tr);
        });

        $("danfeTotal").textContent = fmtBRL(totalNota);
        $("danfeChave").textContent = formatarChave(nfe.chaveAcesso || "");
        $("danfeProtocolo").textContent = nfe.protocoloAutorizacao || "(pendente)";
        $("danfeDataAuth").textContent = nfe.dataAutorizacao
            ? new Date(nfe.dataAutorizacao).toLocaleString("pt-BR")
            : "(em processamento)";

        const cpfDest = $("inputCpfDestinatario").value.trim();
        const nomeDest = $("inputNomeDestinatario").value.trim();
        if (cpfDest || nomeDest) {
            $("danfeConsumidorBox").hidden = false;
            $("danfeConsumidor").textContent = (nomeDest || "—") + (cpfDest ? ` — CPF ${cpfDest}` : "");
        } else {
            $("danfeConsumidorBox").hidden = true;
        }

        // QR code — usa URL pública da SEFAZ se DanfeUrl não disponível
        const qrDiv = $("danfeQrCode");
        qrDiv.innerHTML = "";
        const urlQr = nfe.danfeUrl || `https://www.nfce.fazenda.sp.gov.br/qrcode?p=${nfe.chaveAcesso || ""}`;
        try {
            new QRCode(qrDiv, {
                text: urlQr,
                width: 160,
                height: 160,
                correctLevel: QRCode.CorrectLevel.M,
            });
        } catch (e) {
            qrDiv.textContent = urlQr;
        }

        $("modalDanfe").hidden = false;
    }

    function formatarChave(chave) {
        if (!chave || chave.length !== 44) return chave;
        return chave.match(/.{1,4}/g).join(" ");
    }

    // ── Eventos ──────────────────────────────────────────────────────────────

    document.addEventListener("DOMContentLoaded", () => {
        carregarEmitente();

        $("btnAddItem").addEventListener("click", adicionarItem);
        $("btnEmitir").addEventListener("click", emitirNfce);
        $("listaItens").addEventListener("click", (e) => {
            const btn = e.target.closest(".item-remover");
            if (btn) removerItem(parseInt(btn.dataset.idx, 10));
        });

        $("btnImprimirDanfe").addEventListener("click", () => window.print());
        $("btnFecharModal").addEventListener("click", () => $("modalDanfe").hidden = true);
        $("btnNovaVenda").addEventListener("click", () => {
            state.itens = [];
            state.idempotencyKey = null;
            renderItens();
            $("modalDanfe").hidden = true;
            $("inputCpfDestinatario").value = "";
            $("inputNomeDestinatario").value = "";
            hideStatus();
        });

        // Enter no preço adiciona item
        $("inputPreco").addEventListener("keydown", (e) => {
            if (e.key === "Enter") { e.preventDefault(); adicionarItem(); }
        });

        renderItens();
    });
})();
