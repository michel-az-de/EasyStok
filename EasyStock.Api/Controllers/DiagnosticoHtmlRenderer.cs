using EasyStock.Api.BackgroundServices;

namespace EasyStock.Api.Controllers;

// ──────────────────────────────────────────────────────────────────────
// HTML rendering (fallback simples para Accept: text/html)
// ──────────────────────────────────────────────────────────────────────
internal static class DiagnosticoHtmlRenderer
{
    public static string Render(DiagnosticoResult r, IReadOnlyList<HealthSnapshot> snapshots, EnhancedLogsResult? logs, string logsDir)
    {
        var causasHtml = r.CausasProvaveis.Count > 0
            ? "<div class='card alert-card'><h2>&#9888; Causas Provaveis</h2>" +
              string.Join("", r.CausasProvaveis.Select(c =>
                  $"<div class='alert-item'><strong>[{c.Componente}]</strong> {c.Descricao}<br><em>{c.Sugestao}</em></div>")) +
              "</div>"
            : "";

        // Health chart data
        var chartLabels = string.Join(",", snapshots.Select(s => $"\"{s.Timestamp:HH:mm}\""));
        var dbLatencyData = string.Join(",", snapshots.Select(s => s.DbLatencyMs.ToString()));
        var redisLatencyData = string.Join(",", snapshots.Select(s => (s.RedisLatencyMs ?? 0).ToString()));
        var errorData = string.Join(",", snapshots.Select(s => s.ErrorCount.ToString()));
        var dbStatusColors = string.Join(",", snapshots.Select(s =>
            s.DbStatus == "ok" ? "'rgba(22,163,74,0.7)'" : "'rgba(220,38,38,0.7)'"));

        // Logs summary chart data
        var reqByHourLabels = "";
        var reqByHourData = "";
        var errByHourData = "";
        if (logs?.Disponivel == true)
        {
            var hours = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToArray();
            reqByHourLabels = string.Join(",", hours.Select(h => $"\"{h}h\""));
            reqByHourData = string.Join(",", hours.Select(h =>
                logs.Resumo.RequestsByHour.TryGetValue(h, out var v) ? v.ToString() : "0"));
            errByHourData = string.Join(",", hours.Select(h =>
                logs.Resumo.ErrorsByHour.TryGetValue(h, out var v) ? v.ToString() : "0"));
        }

        // Log entries HTML
        var logEntriesHtml = "";
        if (logs?.Disponivel == true && logs.Entradas.Length > 0)
        {
            var rows = logs.Entradas.TakeLast(200).Reverse().Select(e =>
            {
                var levelClass = e.Level switch
                {
                    "ERROR" or "FATAL" => "log-error",
                    "WARN" => "log-warn",
                    "DEBUG" => "log-debug",
                    _ => "log-info"
                };
                var cat = e.Categoria switch
                {
                    "http_request" => $"<span class='log-cat cat-http'>{e.HttpMethod} {e.StatusCode}</span>",
                    "migration" => "<span class='log-cat cat-migration'>MIGRATION</span>",
                    "startup" => "<span class='log-cat cat-startup'>STARTUP</span>",
                    "error" => "<span class='log-cat cat-error'>ERROR</span>",
                    "db_operation" => "<span class='log-cat cat-db'>DB</span>",
                    _ => ""
                };
                var elapsed = e.ElapsedMs.HasValue ? $"<span class='log-elapsed'>{e.ElapsedMs:F0}ms</span>" : "";
                var msg = System.Net.WebUtility.HtmlEncode(e.Message.Length > 300 ? e.Message[..300] + "..." : e.Message);
                var excRaw = e.Exception != null ? System.Net.WebUtility.HtmlEncode(e.Exception.Length > 2000 ? e.Exception[..2000] + "\n...truncado..." : e.Exception) : null;
                var exc = excRaw != null ? $"<div class='log-exception collapsed' onclick='this.classList.toggle(\"collapsed\")'>{excRaw}</div>" : "";
                var ctx = "";
                if (e.Level is "ERROR" or "FATAL")
                {
                    var badges = new System.Text.StringBuilder("<span class='log-ctx'>");
                    if (!string.IsNullOrEmpty(e.CorrelationId))
                        badges.Append($"<span title='{e.CorrelationId}' style='cursor:pointer' onclick='document.getElementById(\"logFilter\").value=\"{e.CorrelationId[..Math.Min(8, e.CorrelationId.Length)]}\";filterLogs()'>CID:{e.CorrelationId[..Math.Min(8, e.CorrelationId.Length)]}</span>");
                    if (!string.IsNullOrEmpty(e.ClientIp))
                        badges.Append($"<span>IP:{e.ClientIp}</span>");
                    if (!string.IsNullOrEmpty(e.UserId))
                        badges.Append($"<span>User:{e.UserId[..Math.Min(8, e.UserId.Length)]}</span>");
                    if (!string.IsNullOrEmpty(e.EmpresaId))
                        badges.Append($"<span>Emp:{e.EmpresaId[..Math.Min(8, e.EmpresaId.Length)]}</span>");
                    badges.Append("</span>");
                    if (badges.Length > "<span class='log-ctx'></span>".Length) ctx = badges.ToString();
                }
                return $"<div class='log-row {levelClass}' data-level='{e.Level}' data-cat='{e.Categoria}'>" +
                       $"<span class='log-time'>{e.Timestamp:HH:mm:ss}</span>" +
                       $"<span class='log-level'>{e.Level}</span>" +
                       $"{cat}{elapsed}{ctx}" +
                       $"<span class='log-msg'>{msg}</span>{exc}</div>";
            });
            logEntriesHtml = string.Join("\n", rows);
        }

        // Log summary stats + total size
        var logStatsHtml = "";
        if (logs?.Disponivel == true)
        {
            var logFilesForSize = Directory.Exists(logsDir)
                ? new DirectoryInfo(logsDir).GetFiles("easystock-*.log")
                : [];
            var totalSizeBytes = logFilesForSize.Sum(f => f.Length);
            var sizeLabel = totalSizeBytes < 1024 * 1024
                ? $"{totalSizeBytes / 1024}KB"
                : $"{totalSizeBytes / (1024.0 * 1024.0):F1}MB";
            var fileCount = logFilesForSize.Length;

            logStatsHtml = $"""
                <div class="stats-grid" style="grid-template-columns:repeat(6,1fr)">
                    <div class="stat-box"><div class="stat-num">{logs.TotalEntries}</div><div class="stat-label">Total Entradas</div></div>
                    <div class="stat-box"><div class="stat-num">{logs.Resumo.TotalRequests}</div><div class="stat-label">Requests HTTP</div></div>
                    <div class="stat-box err"><div class="stat-num">{logs.Resumo.TotalErrors}</div><div class="stat-label">Erros</div></div>
                    <div class="stat-box warn"><div class="stat-num">{logs.Resumo.TotalWarnings}</div><div class="stat-label">Warnings</div></div>
                    <div class="stat-box"><div class="stat-num">{logs.Resumo.AvgResponseTimeMs:F0}ms</div><div class="stat-label">Tempo Medio</div></div>
                    <div class="stat-box"><div class="stat-num">{sizeLabel}</div><div class="stat-label">{fileCount} arquivo(s)</div></div>
                </div>
                """;
        }

        // SLO strip data
        double? sloUptime = snapshots.Count > 0
            ? Math.Round(snapshots.Count(s => s.OverallStatus != "critical") * 100.0 / snapshots.Count, 1)
            : null;
        var httpElapsed = logs?.Disponivel == true
            ? logs.Entradas.Where(e => e.ElapsedMs.HasValue).Select(e => e.ElapsedMs!.Value).OrderBy(v => v).ToList()
            : null;
        var sloAvg = httpElapsed?.Count > 0 ? Math.Round(httpElapsed.Average(), 0) : (double?)null;
        var sloP95 = httpElapsed?.Count > 0 ? Math.Round(httpElapsed[(int)(httpElapsed.Count * 0.95)], 0) : (double?)null;
        var sloErrRate = logs?.Resumo.TotalRequests > 0
            ? Math.Round(100.0 * logs.Resumo.TotalErrors / logs.Resumo.TotalRequests, 2)
            : (double?)null;
        string SloNum(double? v, string suffix, double? warnAbove = null, double? critAbove = null) =>
            v == null ? "<span style='color:#475569'>—</span>"
            : $"<span style='color:{( critAbove.HasValue && v > critAbove ? "#ef4444" : warnAbove.HasValue && v > warnAbove ? "#f59e0b" : "#22c55e")}'>{v}{suffix}</span>";
        var sloHtml = $"""
            <div class='card' style='margin-bottom:1rem'>
                <h2 style='margin-bottom:.75rem'>&#128200; SLO — Periodo Analisado</h2>
                <div class='stats-grid' style='grid-template-columns:repeat(4,1fr)'>
                    <div class='stat-box'><div class='stat-num'>{SloNum(sloUptime, "%", 99, 95)}</div><div class='stat-label'>Uptime ({snapshots.Count} snaps)</div></div>
                    <div class='stat-box'><div class='stat-num'>{SloNum(sloAvg, "ms", 200, 1000)}</div><div class='stat-label'>Resp. Media</div></div>
                    <div class='stat-box'><div class='stat-num'>{SloNum(sloP95, "ms", 500, 2000)}</div><div class='stat-label'>P95</div></div>
                    <div class='stat-box {(sloErrRate > 5 ? "err" : sloErrRate > 1 ? "warn" : "")}'><div class='stat-num'>{SloNum(sloErrRate, "%", 1, 5)}</div><div class='stat-label'>Taxa de Erro</div></div>
                </div>
            </div>
            """;

        // Patterns HTML with ack buttons
        var patternsHtmlAck = "";
        if (logs?.Padroes.Length > 0)
        {
            patternsHtmlAck = "<div class='card'><h2>&#128270; Padroes Detectados</h2><div class='patterns-list'>" +
                string.Join("", logs.Padroes.Select(p =>
                    $"<div class='pattern-item' data-alerta-id='{p.AlertaId}' data-tipo='{System.Net.WebUtility.HtmlEncode(p.Tipo)}'>" +
                    $"{SevBadge(p.Severidade)} <strong>{p.Tipo}</strong> " +
                    $"<span class='pattern-count'>({p.Ocorrencias}x)</span><br>" +
                    $"<span class='pattern-desc'>{System.Net.WebUtility.HtmlEncode(p.Descricao)}</span><br>" +
                    $"<em class='pattern-tip'>{System.Net.WebUtility.HtmlEncode(p.Sugestao)}</em>" +
                    (p.UltimaOcorrencia.HasValue ? $"<br><small style='color:#64748b'>Ultima: {p.UltimaOcorrencia:HH:mm:ss} | Primeira: {p.PrimeiraOcorrencia:HH:mm:ss}</small>" : "") +
                    (!string.IsNullOrEmpty(p.AlertaId) ? $"""
                    <div class='ack-row' style='margin-top:.6rem;display:flex;gap:.4rem;align-items:center;flex-wrap:wrap'>
                        <span style='font-size:.7rem;color:#64748b'>Marcar:</span>
                        <button class='ack-btn' data-id='{p.AlertaId}' data-status='visto' onclick='doAck(this)' style='padding:.2rem .5rem;font-size:.7rem;border:1px solid #334155;background:#1e293b;color:#94a3b8;border-radius:.3rem;cursor:pointer'>&#10003; Visto</button>
                        <button class='ack-btn' data-id='{p.AlertaId}' data-status='em_investigacao' onclick='doAck(this)' style='padding:.2rem .5rem;font-size:.7rem;border:1px solid #78350f;background:#1c1917;color:#f59e0b;border-radius:.3rem;cursor:pointer'>&#128269; Investigando</button>
                        <button class='ack-btn' data-id='{p.AlertaId}' data-status='resolvido' onclick='doAck(this)' style='padding:.2rem .5rem;font-size:.7rem;border:1px solid #052e16;background:#0f172a;color:#22c55e;border-radius:.3rem;cursor:pointer'>&#10003; Resolvido</button>
                        <button onclick='verLogsDoAlerta(this)' data-desc='{System.Net.WebUtility.HtmlEncode(p.Descricao.Length > 40 ? p.Descricao[..40] : p.Descricao)}' style='padding:.2rem .5rem;font-size:.7rem;border:1px solid #1e3a5f;background:#0f172a;color:#60a5fa;border-radius:.3rem;cursor:pointer'>&#128269; Ver logs</button>
                        <span class='ack-status-label' style='font-size:.7rem;color:#64748b;margin-left:.25rem'></span>
                    </div>
                    <div class='investigacao-panel' style='display:none;margin-top:.6rem;border-top:1px solid #334155;padding-top:.5rem'></div>
                    """ : "") +
                    "</div>")) +
                "</div></div>";
        }

        var statusColor = r.Status == "ok" ? "#16a34a" : r.Status == "degraded" ? "#d97706" : "#dc2626";

        return $$"""
        <!DOCTYPE html>
        <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>EasyStock - Central de Diagnostico</title>
        <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.umd.min.js"></script>
        {{RenderStyles(statusColor)}}
        <div class='toast-container' id='toastContainer'></div>
        <div class="container">
        {{RenderHeaderAndTabs(r, logs)}}

        {{RenderOverviewTab(r, causasHtml, sloHtml)}}

        {{RenderHealthTab(snapshots, logs)}}

        {{RenderLogsTab(logs, logStatsHtml, logEntriesHtml)}}

        {{RenderPatternsTab(logs, snapshots, patternsHtmlAck)}}

        <div class="links">
            <a href="/diagnostico">&#8635; Atualizar</a>
            <a href="/swagger">Swagger</a>
            <a href="/health">Health</a>
            <a href="/health/ready">Readiness</a>
            <a href="/api/diagnostico">JSON</a>
            <a href="/api/diagnostico/historico">Historico JSON</a>
            <a href="/api/diagnostico/endpoints">Teste Endpoints</a>
        </div>
        </div>

        <script>
        // _liveEs declarado próximo à função startLiveLogs

        var _timelineEventos=[];
        async function fetchEventos(){
            try{
                const r=await fetch('/api/diagnostico/eventos?hours=2');
                if(!r.ok)return;
                const d=await r.json();
                _timelineEventos=d.eventos||[];
            }catch{}
        }

        function showTab(name){
            document.querySelectorAll('.panel').forEach(p=>p.classList.remove('active'));
            document.querySelectorAll('.tab').forEach(t=>t.classList.remove('active'));
            document.getElementById('tab-'+name).classList.add('active');
            event.target.classList.add('active');
            if(name==='health'){setTimeout(initHealthCharts,50);fetchEventos().then(()=>{if(_dbChart)_dbChart.update();});}
            if(name==='logs'){startLiveLogs();setTimeout(initErrorTimeline,50);reloadLixeiraInfo();}
            else stopLiveLogs();
        }
        document.addEventListener('DOMContentLoaded',function(){
            if(document.getElementById('tab-health')&&document.getElementById('tab-health').classList.contains('active')){
                setTimeout(initHealthCharts,100);
            }
            loadAckStatuses();
        });

        var _liveEs=null; // EventSource ativo
        function startLiveLogs(){
            if(_liveEs)return; // já conectado
            const dot=document.getElementById('liveDot');
            if(dot)dot.style.display='inline-block';
            _liveEs=new EventSource('/api/diagnostico/logs/live');
            _liveEs.addEventListener('log-batch',function(e){
                try{
                    const d=JSON.parse(e.data);
                    if(d.count>0){
                        const console_=document.getElementById('logConsole');
                        if(console_){
                            const tmp=document.createElement('div');
                            tmp.innerHTML=d.rows.join('');
                            Array.from(tmp.children).reverse().forEach(el=>console_.prepend(el));
                            filterLogs();
                        }
                    }
                    const ts=document.getElementById('liveTimestamp');
                    if(ts)ts.textContent='• '+new Date().toLocaleTimeString('pt-BR',{hour:'2-digit',minute:'2-digit',second:'2-digit'});
                }catch{}
            });
            _liveEs.onerror=function(){
                const ts=document.getElementById('liveTimestamp');
                if(ts)ts.textContent='(reconectando...)';
            };
        }
        function stopLiveLogs(){
            if(_liveEs){_liveEs.close();_liveEs=null;}
            const dot=document.getElementById('liveDot');
            if(dot)dot.style.display='none';
        }
        function clearLogs(){
            const c=document.getElementById('logConsole');
            if(c)c.innerHTML='';
        }

        document.getElementById('autoRefresh').addEventListener('change',function(){
            if(this.checked){this._timer=setInterval(()=>location.reload(),300000)} // 5min
            else{clearInterval(this._timer)}
        });

        // Live-refresh health charts every 60s without full reload
        setInterval(function(){
            fetch('/api/diagnostico/historico').then(r=>r.ok?r.json():null).then(d=>{
                if(!d||!d.snapshots||!d.snapshots.length)return;
                var s=d.snapshots;
                cLabels=s.map(x=>new Date(x.timestamp).toLocaleTimeString('pt-BR',{hour:'2-digit',minute:'2-digit'}));
                dbData=s.map(x=>x.dbLatencyMs>=0?x.dbLatencyMs:null);
                redisData=s.map(x=>x.redisLatencyMs!=null?x.redisLatencyMs:null);
                errData=s.map(x=>x.errorCount);
                var last=s[s.length-1];
                var kpiDb=document.getElementById('kpiDb');var kpiErr=document.getElementById('kpiErr');
                var kpiSnap=document.getElementById('kpiSnap');
                if(kpiDb)kpiDb.textContent=last.dbLatencyMs+'ms';
                if(kpiErr)kpiErr.textContent=last.errorCount;
                if(kpiSnap)kpiSnap.textContent=d.total+' snapshots';
                if(document.getElementById('tab-health').classList.contains('active'))initHealthCharts();
            }).catch(()=>{});
        },60000);

        let activeLevel='all';
        function toggleLevel(btn,level){
            activeLevel=level;
            document.querySelectorAll('.log-controls button').forEach(b=>b.classList.remove('active'));
            btn.classList.add('active');
            filterLogs();
        }
        function filterLogs(){
            const q=(document.getElementById('logFilter')?.value||'').toLowerCase();
            document.querySelectorAll('.log-row').forEach(row=>{
                const matchLevel=activeLevel==='all'||row.dataset.level===activeLevel;
                const matchText=!q||row.textContent.toLowerCase().includes(q);
                row.style.display=matchLevel&&matchText?'':'none';
            });
        }

        function showToast(msg,type='info'){
            const c=document.getElementById('toastContainer');if(!c)return;
            const t=document.createElement('div');
            t.className='toast '+type;t.textContent=msg;
            c.appendChild(t);
            requestAnimationFrame(()=>{ requestAnimationFrame(()=>t.classList.add('show')); });
            setTimeout(()=>{t.classList.remove('show');setTimeout(()=>c.removeChild(t),300);},3500);
        }
        async function reloadLixeiraInfo(){
            try{
                const r=await fetch('/api/diagnostico/logs/lixeira');
                if(!r.ok)return;
                const d=await r.json();
                const badge=document.getElementById('lixeiraBadge');
                if(badge) badge.textContent=d.total>0?`Lixeira: ${d.total} arquivo(s)`:'Lixeira vazia';
            }catch{}
        }
        async function zerarMedidores(){
            if(!confirm('Zerar o histórico de health snapshots (gráficos e contadores)?\n\nOs dados atuais serão descartados e a coleta recomeçará do zero.'))return;
            showToast('Zerando medidores...','info');
            try{
                const r=await fetch('/api/diagnostico/historico/zerar',{method:'POST'});
                const d=await r.json();
                if(d.success){
                    showToast(d.mensagem||'Medidores zerados com sucesso.','success');
                    setTimeout(()=>location.reload(),2000);
                } else { showToast('Erro ao zerar medidores.','error'); }
            }catch(e){showToast('Erro: '+e.message,'error');}
        }
        async function moverParaLixeira(){
            if(!confirm('Mover todos os logs para a lixeira?'))return;
            showToast('Movendo logs para lixeira...','info');
            try{
                const r=await fetch('/api/diagnostico/logs/limpar',{method:'POST'});
                const d=await r.json();
                if(d.success||d.arquivosMovidos>=0){
                    clearLogs();
                    showToast(d.mensagem||'Logs movidos para lixeira.','success');
                    await reloadLixeiraInfo();
                } else { showToast(d.mensagem||'Nenhum arquivo movido.','info'); }
            }catch(e){showToast('Erro: '+e.message,'error');}
        }
        async function expurgarLogs(){
            const dias=prompt('Manter logs dos últimos quantos dias? (excluir mais antigos)',3);
            if(!dias||isNaN(dias)||dias<1)return;
            if(!confirm('Excluir permanentemente logs com mais de '+dias+' dia(s)?'))return;
            showToast('Expurgando logs antigos...','info');
            try{
                const r=await fetch('/api/diagnostico/logs/expurgar?diasManter='+dias,{method:'POST'});
                const d=await r.json();
                showToast(d.mensagem||'Expurgo concluído.', d.arquivosExcluidos>0?'success':'info');
                if(d.arquivosExcluidos>0)setTimeout(()=>location.reload(),1500);
            }catch(e){showToast('Erro: '+e.message,'error');}
        }
        async function esvaziarLixeira(){
            if(!confirm('Excluir permanentemente todos os logs da lixeira?'))return;
            showToast('Esvaziando lixeira...','info');
            try{
                const r=await fetch('/api/diagnostico/logs/lixeira/esvaziar',{method:'POST'});
                const d=await r.json();
                showToast(d.mensagem||'Lixeira esvaziada.', d.arquivosExcluidos>0?'success':'info');
                await reloadLixeiraInfo();
            }catch(e){showToast('Erro: '+e.message,'error');}
        }
        async function loadStorageFileList(){
            const sel=document.getElementById('storageFileSelect');
            const st=document.getElementById('storageStatusMsg');
            if(st)st.textContent='Carregando lista...';
            try{
                const r=await fetch('/api/diagnostico/logs/storage');
                const d=await r.json();
                if(!d.disponivel||d.total===0){
                    if(st)st.textContent=d.motivo||'Nenhum arquivo no storage.';
                    return;
                }
                if(sel){
                    sel.innerHTML='<option value="">-- selecione um arquivo --</option>';
                    d.arquivos.forEach(f=>{
                        const opt=document.createElement('option');
                        opt.value=f.storageKey;
                        const dt=new Date(f.dataModificacao).toLocaleDateString('pt-BR');
                        opt.textContent=`${f.nome} (${dt}, ${(f.tamanhoBytes/1024).toFixed(0)}KB)`;
                        sel.appendChild(opt);
                    });
                    sel.style.display='block';
                    const clearBtn=document.getElementById('clearStorageBtn');
                    if(clearBtn)clearBtn.style.display='block';
                }
                if(st)st.textContent=`${d.total} arquivo(s) encontrado(s).`;
            }catch(e){if(st)st.textContent='Erro: '+e.message;}
        }
        async function loadStorageFileContent(){
            const sel=document.getElementById('storageFileSelect');
            const key=sel?.value;
            if(!key)return;
            const st=document.getElementById('storageStatusMsg');
            if(st)st.textContent='Carregando arquivo...';
            showToast('Carregando log do storage...','info');
            try{
                const r=await fetch('/api/diagnostico/logs/storage/conteudo?file='+encodeURIComponent(key));
                const d=await r.json();
                if(d.error){showToast(d.error,'error');return;}
                const console_=document.getElementById('logConsole');
                if(console_){
                    const rows=d.entradas?.slice().reverse().map(e=>{
                        const lc=e.level==='ERROR'||e.level==='FATAL'?'log-error':e.level==='WARN'?'log-warn':'log-info';
                        const msg=(e.message||'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
                        return `<div class="log-row ${lc}" data-level="${e.level||'INFO'}"><span class="log-time">${(e.timestamp||'').substring(11,19)}</span><span class="log-level">${e.level||''}</span><span class="log-msg">${msg}</span></div>`;
                    })||[];
                    console_.innerHTML=rows.join('');
                    const info=document.getElementById('logCountInfo');
                    if(info)info.textContent=`Exibindo ${rows.length} entradas do arquivo arquivado`;
                    const lbl=document.getElementById('logSourceLabel');
                    if(lbl)lbl.textContent='(arquivo do storage — historico)';
                }
                const ind=document.getElementById('storageArchiveIndicator');
                if(ind)ind.style.display='block';
                if(st)st.textContent=`${d.totalEntries} entradas carregadas.`;
                showToast(`${d.totalEntries} entradas carregadas do storage.`,'success');
            }catch(e){showToast('Erro: '+e.message,'error');}
        }
        function clearStorageView(){
            const sel=document.getElementById('storageFileSelect');
            const ind=document.getElementById('storageArchiveIndicator');
            const lbl=document.getElementById('logSourceLabel');
            if(sel){sel.style.display='none';sel.value='';}
            const clearBtn=document.getElementById('clearStorageBtn');
            if(clearBtn)clearBtn.style.display='none';
            if(ind)ind.style.display='none';
            if(lbl)lbl.textContent='(ultimas 48h)';
            const st=document.getElementById('storageStatusMsg');
            if(st)st.textContent='';
            location.reload();
        }
        async function loadQueriesLentas(){
            const el=document.getElementById('queriesLentasResult');
            if(!el)return;
            el.innerHTML="<span style='color:#94a3b8'>Carregando...</span>";
            try{
                const r=await fetch('/api/diagnostico/queries-lentas');
                const d=await r.json();
                if(!d.disponivel){el.innerHTML=`<span style='color:#f59e0b;font-size:.85rem'>${d.motivo||'Extensao nao disponivel'}</span>${d.instrucoesPgStatStatements?`<pre style='font-size:.75rem;color:#94a3b8;margin-top:.5rem;white-space:pre-wrap'>${d.instrucoesPgStatStatements}</pre>`:''}`;return;}
                if(!d.queries||!d.queries.length){el.innerHTML="<span style='color:#22c55e'>Nenhuma query lenta encontrada ✓</span>";return;}
                el.innerHTML="<table style='width:100%;border-collapse:collapse;font-size:.8rem;margin-top:.5rem'><thead><tr style='color:#64748b;border-bottom:1px solid #334155'><th style='text-align:left;padding:.3rem .5rem'>Query</th><th style='padding:.3rem .5rem'>Calls</th><th style='padding:.3rem .5rem'>Avg ms</th><th style='padding:.3rem .5rem'>P95 ms</th><th style='padding:.3rem .5rem'>Rows</th></tr></thead><tbody>"
                    +d.queries.map(q=>`<tr style='border-bottom:1px solid #1e293b'><td style='font-family:monospace;font-size:.75rem;padding:.25rem .5rem;max-width:400px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap' title='${q.query||""}'>${(q.query||"").substring(0,70)}${q.query&&q.query.length>70?"...":""}</td><td style='text-align:right;padding:.25rem .5rem'>${q.calls}</td><td style='text-align:right;padding:.25rem .5rem;color:${q.avgMs>1000?"#ef4444":q.avgMs>200?"#f59e0b":"#22c55e"}'>${(q.avgMs||0).toFixed(1)}</td><td style='text-align:right;padding:.25rem .5rem;color:#94a3b8'>${q.p95Ms?q.p95Ms.toFixed(1):"—"}</td><td style='text-align:right;padding:.25rem .5rem;color:#94a3b8'>${q.avgRows?(q.avgRows).toFixed(0):"—"}</td></tr>`).join("")
                    +"</tbody></table>";
            }catch(e){el.innerHTML=`<span style='color:#ef4444'>Erro: ${e.message}</span>`;}
        }
        async function loadEmpresasHealth(){
            const el=document.getElementById('empresasHealthResult');
            if(!el)return;
            el.innerHTML="<span style='color:#94a3b8'>Verificando...</span>";
            try{
                const r=await fetch('/api/diagnostico/health/empresas');
                const d=await r.json();
                if(!d.empresas||!d.empresas.length){el.innerHTML="<span style='color:#94a3b8'>Nenhuma empresa encontrada.</span>";return;}
                el.innerHTML=`<div style='font-size:.75rem;color:#94a3b8;margin-bottom:.5rem'>Total: ${d.totalAnalisadas} | <span style='color:#22c55e'>OK: ${d.ok}</span>${d.degraded>0?` | <span style='color:#ef4444'>Falha: ${d.degraded}</span>`:""} | ${d.duracaoMs}ms</div>`
                    +"<div style='display:flex;flex-wrap:wrap;gap:.4rem'>"
                    +d.empresas.map(e=>`<div style='border:1px solid ${e.status==="ok"?"#052e16":"#450a0a"};background:${e.status==="ok"?"#020617":"#1c1917"};border-radius:.4rem;padding:.4rem .6rem;font-size:.75rem'><div style='color:#f1f5f9;font-weight:600'>${e.nome}</div><div style='color:${e.status==="ok"?"#22c55e":"#ef4444"}'>${e.status==="ok"?"✓ OK":"✗ "+( e.erro||"Falha")} ${e.latenciaMs?e.latenciaMs+"ms":""}</div></div>`).join("")
                    +"</div>";
            }catch(e){el.innerHTML=`<span style='color:#ef4444'>Erro: ${e.message}</span>`;}
        }
        async function doAck(btn){
            const id=btn.dataset.id, status=btn.dataset.status;
            if(!id)return;
            btn.disabled=true;
            try{
                const r=await fetch('/api/diagnostico/alertas/'+id+'/ack',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({status,observacao:''})});
                if(r.ok){
                    const card=btn.closest('.pattern-item');
                    if(card){
                        card.querySelectorAll('.ack-btn').forEach(b=>{b.style.opacity='.5';b.style.outline='none';});
                        btn.style.opacity='1';btn.style.outline='2px solid currentColor';btn.style.outlineOffset='2px';
                        const lbl=card.querySelector('.ack-status-label');
                        if(lbl)lbl.textContent='✓ '+status.replace('_',' ');
                        if(status==='em_investigacao'){
                            card.classList.add('investigating');
                            expandInvestigacao(card);
                        } else if(status==='resolvido'){
                            card.classList.add('resolved');
                            card.classList.remove('investigating');
                            const panel=card.querySelector('.investigacao-panel');
                            if(panel)panel.style.display='none';
                            showToast('Alerta marcado como resolvido.','success');
                        } else {
                            showToast('Alerta marcado como visto.','info');
                        }
                    }
                }
            }catch{}
            btn.disabled=false;
        }
        function expandInvestigacao(card){
            const panel=card.querySelector('.investigacao-panel');
            if(!panel)return;
            const desc=card.querySelector('.pattern-desc');
            const keyword=(desc?.textContent||'').trim().substring(0,50).toLowerCase();
            const allRows=[...document.querySelectorAll('#logConsole .log-row')];
            const matches=keyword?allRows.filter(r=>r.textContent.toLowerCase().includes(keyword)).slice(0,15):[];
            let html=`<div style='font-size:.72rem;color:#f59e0b;margin-bottom:.4rem;font-weight:500'>&#128269; Investigando — entradas relacionadas:</div>`;
            if(matches.length>0){
                html+=matches.map(r=>{
                    const t=r.querySelector('.log-time')?.textContent||'';
                    const l=r.querySelector('.log-level')?.textContent||'';
                    const m=r.querySelector('.log-msg')?.textContent||'';
                    const lc=l==='ERROR'||l==='FATAL'?'color:#f87171':l==='WARN'?'color:#fbbf24':'color:#cbd5e1';
                    return `<div class='inv-log-entry'><span style='color:#64748b'>${t}</span> <span style='${lc};font-weight:600'>${l}</span> <span style='color:#cbd5e1'>${m.substring(0,120)}</span></div>`;
                }).join('');
            } else {
                html+=`<div style='font-size:.72rem;color:#475569'>Sem entradas visiveis no console para este padrao. Tente carregar mais logs ou ajustar o filtro.</div>`;
            }
            html+=`<div style='margin-top:.5rem'><button onclick='verLogsDoAlerta(this)' data-desc='${(card.querySelector('.pattern-desc')?.textContent||'').replace(/'/g,"&#39;").substring(0,40)}' style='padding:.2rem .5rem;font-size:.7rem;border:1px solid #1e3a5f;background:#0f172a;color:#60a5fa;border-radius:.3rem;cursor:pointer'>&#128269; Ver no console de logs</button></div>`;
            panel.innerHTML=html;
            panel.style.display='block';
            showToast('Investigando — mostrando entradas relacionadas.','info');
        }
        function verLogsDoAlerta(btn){
            const desc=btn.dataset.desc||'';
            if(!desc)return;
            document.querySelectorAll('.panel').forEach(p=>p.classList.remove('active'));
            document.querySelectorAll('.tab').forEach(t=>t.classList.remove('active'));
            const logsPanel=document.getElementById('tab-logs');
            if(logsPanel)logsPanel.classList.add('active');
            document.querySelectorAll('.tab').forEach(t=>{if(t.textContent.includes('Logs'))t.classList.add('active');});
            const filterInput=document.getElementById('logFilter');
            if(filterInput){filterInput.value=desc;filterInput.dispatchEvent(new Event('input'));}
            startLiveLogs();
            showToast('Filtro aplicado: '+desc,'info');
        }
        async function loadAckStatuses(){
            const cards=[...document.querySelectorAll('.pattern-item[data-alerta-id]')];
            if(!cards.length)return;
            const ids=cards.map(c=>c.dataset.alertaId).filter(Boolean).join(',');
            if(!ids)return;
            try{
                const r=await fetch('/api/diagnostico/alertas/acks?ids='+encodeURIComponent(ids));
                if(!r.ok)return;
                const d=await r.json();
                (d.acks||[]).forEach(ack=>{
                    const card=document.querySelector(`.pattern-item[data-alerta-id='${ack.alertaId}']`);
                    if(!card)return;
                    const btn=card.querySelector(`.ack-btn[data-status='${ack.status}']`);
                    if(btn){btn.style.opacity='1';btn.style.outline='2px solid currentColor';btn.style.outlineOffset='2px';}
                    card.querySelectorAll('.ack-btn').forEach(b=>{if(b!==btn)b.style.opacity='.5';});
                    const lbl=card.querySelector('.ack-status-label');
                    if(lbl)lbl.textContent='✓ '+ack.status.replace('_',' ');
                    if(ack.status==='em_investigacao')card.classList.add('investigating');
                    if(ack.status==='resolvido'){card.classList.add('resolved');card.classList.remove('investigating');}
                });
            }catch{}
        }

        // Charts
        {{(snapshots.Count > 0 ? "var cLabels=[" + chartLabels + "];" +
            "var dbData=[" + dbLatencyData + "];" +
            "var redisData=[" + redisLatencyData + "];" +
            "var errData=[" + errorData + "];" : "")}}
        {{(logs?.Disponivel == true ? "var volLabels=[" + reqByHourLabels + "];" +
            "var reqData=[" + reqByHourData + "];" +
            "var errHData=[" + errByHourData + "];" : "")}}
        </script>
        <script>
        var CO={responsive:true,maintainAspectRatio:false,plugins:{legend:{display:false} },
            scales:{x:{ticks:{color:'#64748b',maxTicksLimit:15,font:{size:10} },grid:{color:'#1e293b'} },
                    y:{beginAtZero:true,suggestedMax:50,ticks:{color:'#64748b',font:{size:10},callback:function(v){return v+'ms'} },grid:{color:'#1e293b'} } } };
        var COz={responsive:true,maintainAspectRatio:false,plugins:{legend:{display:false} },
            scales:{x:{ticks:{color:'#64748b',maxTicksLimit:15,font:{size:10} },grid:{color:'#1e293b'} },
                    y:{beginAtZero:true,suggestedMax:1,ticks:{color:'#64748b',font:{size:10},stepSize:1},grid:{color:'#1e293b'} } } };
        var COzL={responsive:true,maintainAspectRatio:false,plugins:{legend:{display:true,labels:{color:'#94a3b8',font:{size:11} } } },
            scales:{x:{ticks:{color:'#64748b',maxTicksLimit:24,font:{size:10} },grid:{color:'#1e293b'} },
                    y:{beginAtZero:true,suggestedMax:5,ticks:{color:'#64748b',font:{size:10} },grid:{color:'#1e293b'} } } };

        var _dbChart=null,_redisChart=null,_errChart=null,_volChart=null,_timelineChart=null;
        function initErrorTimeline(){
            const canvas=document.getElementById('errorTimelineChart');
            if(!canvas)return;
            if(_timelineChart){return;}
            if(typeof volLabels==='undefined'||typeof errHData==='undefined')return;
            const colors=errHData.map(v=>v===0?'rgba(22,163,74,0.6)':v<=2?'rgba(245,158,11,0.7)':'rgba(239,68,68,0.8)');
            const borderColors=errHData.map(v=>v===0?'#16a34a':v<=2?'#f59e0b':'#ef4444');
            _timelineChart=new Chart(canvas,{
                type:'bar',
                data:{labels:volLabels,datasets:[
                    {label:'Erros/hora',data:errHData,backgroundColor:colors,borderColor:borderColors,borderWidth:1,borderRadius:3},
                ]},
                options:{
                    responsive:true,maintainAspectRatio:false,
                    plugins:{legend:{display:false},tooltip:{callbacks:{label:function(ctx){return ctx.parsed.y+' erro(s)';} } } },
                    scales:{
                        x:{ticks:{color:'#475569',maxTicksLimit:12,font:{size:9} },grid:{color:'#1e293b'} },
                        y:{beginAtZero:true,ticks:{color:'#475569',font:{size:9},stepSize:1},grid:{color:'#1e293b'} }
                    },
                    onClick:function(evt,items){
                        if(!items.length)return;
                        const lbl=volLabels[items[0].index];
                        const fi=document.getElementById('logFilter');
                        const info=document.getElementById('timelineFilterInfo');
                        if(fi){fi.value=lbl;fi.dispatchEvent(new Event('input'));}
                        if(info)info.textContent='Filtrando logs da hora: '+lbl+' — clique novamente para limpar';
                    }
                }
            });
        }
        const eventLinesPlugin={
            id:'eventLines',
            afterDraw(chart){
                if(!_timelineEventos||!_timelineEventos.length)return;
                const ctx=chart.ctx, xAxis=chart.scales.x, yAxis=chart.scales.y;
                if(!xAxis||!yAxis)return;
                ctx.save();
                _timelineEventos.forEach(ev=>{
                    const evTime=new Date(ev.timestamp);
                    const evLabel=evTime.toLocaleTimeString('pt-BR',{hour:'2-digit',minute:'2-digit'});
                    const labels=chart.data.labels||[];
                    let bestIdx=-1,bestDiff=Infinity;
                    labels.forEach((l,i)=>{
                        const diff=Math.abs(l.localeCompare(evLabel));
                        if(diff<bestDiff){bestDiff=diff;bestIdx=i;}
                    });
                    if(bestIdx<0)return;
                    const x=xAxis.getPixelForValue(bestIdx);
                    const color=ev.tipo==='deploy'?'rgba(56,189,248,0.8)':ev.tipo==='error_spike'?'rgba(239,68,68,0.8)':'rgba(251,191,36,0.8)';
                    const icon=ev.tipo==='deploy'?'🚀':ev.tipo==='error_spike'?'⚡':'⚠';
                    ctx.strokeStyle=color;ctx.lineWidth=1.5;ctx.setLineDash([4,3]);
                    ctx.beginPath();ctx.moveTo(x,yAxis.top);ctx.lineTo(x,yAxis.bottom);ctx.stroke();
                    ctx.setLineDash([]);ctx.fillStyle=color;ctx.font='11px system-ui';
                    ctx.fillText(icon,x-6,yAxis.top+12);
                });
                ctx.restore();
            }
        };
        Chart.register(eventLinesPlugin);

        function initHealthCharts(){
            if(typeof cLabels==='undefined')return;
            if(_dbChart){_dbChart.data.labels=cLabels;_dbChart.data.datasets[0].data=dbData;_dbChart.update();
                _redisChart.data.labels=cLabels;_redisChart.data.datasets[0].data=redisData;_redisChart.update();
                _errChart.data.labels=cLabels;_errChart.data.datasets[0].data=errData;_errChart.update();return;}
            var dbOpts=JSON.parse(JSON.stringify(CO));
            var maxDb=Math.max(...dbData.filter(v=>v>0));
            if(maxDb>0)dbOpts.scales.y.suggestedMax=Math.ceil(maxDb*1.3);
            _dbChart=new Chart(document.getElementById('dbChart'),{type:'line',data:{labels:cLabels,
                datasets:[{label:'DB Latencia (ms)',data:dbData,borderColor:'#38bdf8',backgroundColor:'rgba(56,189,248,0.1)',
                    fill:true,tension:.3,pointRadius:2,borderWidth:2,spanGaps:true}]},options:dbOpts});
            _redisChart=new Chart(document.getElementById('redisChart'),{type:'line',data:{labels:cLabels,
                datasets:[{label:'Redis (ms)',data:redisData,borderColor:'#a78bfa',backgroundColor:'rgba(167,139,250,0.1)',
                    fill:true,tension:.3,pointRadius:2,borderWidth:2,spanGaps:true}]},options:JSON.parse(JSON.stringify(CO))});
            _errChart=new Chart(document.getElementById('errChart'),{type:'bar',data:{labels:cLabels,
                datasets:[{label:'Erros',data:errData,backgroundColor:'rgba(239,68,68,0.6)',borderColor:'#ef4444',borderWidth:1,borderRadius:2}]},options:COz});
            if(typeof volLabels!=='undefined'&&document.getElementById('volumeChart')){
                _volChart=new Chart(document.getElementById('volumeChart'),{type:'bar',data:{labels:volLabels,
                    datasets:[
                        {label:'Requests',data:reqData,backgroundColor:'rgba(56,189,248,0.5)',borderColor:'#38bdf8',borderWidth:1,borderRadius:2},
                        {label:'Erros',data:errHData,backgroundColor:'rgba(239,68,68,0.7)',borderColor:'#ef4444',borderWidth:1,borderRadius:2}
                    ]},options:COzL});
            }
            if(_timelineEventos&&_timelineEventos.length){
                const leg=document.getElementById('eventosLegenda');
                if(leg){
                    leg.style.display='flex';
                    leg.innerHTML='<span style="font-size:.7rem;color:#64748b;margin-right:.5rem">Eventos:</span>'
                        +_timelineEventos.map(e=>`<span style="font-size:.7rem;margin-right:.75rem">${e.tipo==='deploy'?'🚀':e.tipo==='error_spike'?'⚡':'⚠'} ${e.label||e.tipo} <span style="color:#475569">${new Date(e.timestamp).toLocaleTimeString('pt-BR',{hour:'2-digit',minute:'2-digit'})}</span></span>`).join('');
                }
            }
        }
        </script>
        </body></html>
        """;
    }

    private static string Badge(string status) => status switch
    {
        "ok" => "<span class='badge ok'>OK</span>",
        "degraded" => "<span class='badge warn'>DEGRADADO</span>",
        "critical" => "<span class='badge crit'>CRITICO</span>",
        "falha" => "<span class='badge crit'>FALHA</span>",
        "nao_configurado" => "<span class='badge na'>N/C</span>",
        _ => $"<span class='badge'>{status}</span>"
    };

    private static string BoolBadge(bool? val) => val switch
    {
        true => "<span class='badge ok'>Sim</span>",
        false => "<span class='badge crit'>Nao</span>",
        null => "<span class='badge na'>N/A</span>"
    };

    private static string SevBadge(string sev) => sev switch
    {
        "critical" => "<span class='badge crit'>CRITICO</span>",
        "warning" => "<span class='badge warn'>ALERTA</span>",
        _ => "<span class='badge ok'>INFO</span>"
    };

    private static string RenderStyles(string statusColor) => $$"""
        <style>
            *{box-sizing:border-box;margin:0;padding:0}
            body{font-family:system-ui,-apple-system,sans-serif;background:#0f172a;color:#e2e8f0;min-height:100vh}
            .container{max-width:1200px;margin:0 auto;padding:1rem}
            header{display:flex;align-items:center;justify-content:space-between;padding:1rem 0;border-bottom:1px solid #1e293b;margin-bottom:1.5rem;flex-wrap:wrap;gap:.5rem}
            header h1{font-size:1.5rem;color:#f8fafc;display:flex;align-items:center;gap:.5rem}
            header h1::before{content:'';display:inline-block;width:10px;height:10px;border-radius:50%;background:{{statusColor}};box-shadow:0 0 8px {{statusColor}}}
            .meta{color:#94a3b8;font-size:.8rem}
            .tabs{display:flex;gap:.25rem;margin-bottom:1.5rem;border-bottom:2px solid #1e293b;overflow-x:auto}
            .tab{padding:.6rem 1.2rem;cursor:pointer;color:#94a3b8;border:none;background:none;font-size:.85rem;font-weight:500;border-bottom:2px solid transparent;margin-bottom:-2px;white-space:nowrap;transition:all .2s}
            .tab:hover{color:#e2e8f0}.tab.active{color:#38bdf8;border-bottom-color:#38bdf8}
            .panel{display:none}.panel.active{display:block}
            .card{background:#1e293b;border:1px solid #334155;border-radius:.75rem;padding:1.25rem;margin-bottom:1rem}
            .card h2{font-size:1rem;color:#f8fafc;margin-bottom:.75rem;display:flex;align-items:center;gap:.5rem}
            .alert-card{border-color:#92400e;background:#1c1917}
            .alert-item{padding:.5rem 0;border-bottom:1px solid #334155;font-size:.85rem}.alert-item:last-child{border:none}
            .alert-item em{color:#94a3b8;font-size:.8rem}
            table{width:100%;border-collapse:collapse;font-size:.875rem}
            td{padding:.4rem .75rem;border-bottom:1px solid #334155}td:first-child{color:#94a3b8;width:40%}
            .grid-2{display:grid;grid-template-columns:1fr 1fr;gap:1rem}
            .grid-3{display:grid;grid-template-columns:1fr 1fr 1fr;gap:1rem}
            @media(max-width:768px){.grid-2,.grid-3{grid-template-columns:1fr} }
            .badge{padding:.15rem .5rem;border-radius:.25rem;font-size:.75rem;font-weight:600;display:inline-block}
            .badge.ok{background:#052e16;color:#22c55e}.badge.crit{background:#450a0a;color:#ef4444}
            .badge.warn{background:#422006;color:#f59e0b}.badge.na{background:#1e293b;color:#64748b}
            .chart-box{background:#0f172a;border-radius:.5rem;padding:1rem;position:relative;height:220px}
            .stats-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:.75rem;margin-bottom:1rem}
            @media(max-width:768px){.stats-grid{grid-template-columns:repeat(3,1fr)} }
            .stat-box{background:#0f172a;border:1px solid #334155;border-radius:.5rem;padding:.75rem;text-align:center}
            .stat-box.err{border-color:#7f1d1d}.stat-box.warn{border-color:#78350f}
            .stat-num{font-size:1.5rem;font-weight:700;color:#f8fafc}.stat-label{font-size:.7rem;color:#94a3b8;margin-top:.25rem}
            .log-console{background:#020617;border:1px solid #1e293b;border-radius:.5rem;max-height:500px;overflow-y:auto;font-family:'Cascadia Code','Fira Code',monospace;font-size:.75rem}
            .log-controls{display:flex;gap:.5rem;margin-bottom:.75rem;flex-wrap:wrap;align-items:center}
            .log-controls input{background:#1e293b;border:1px solid #334155;color:#e2e8f0;padding:.4rem .75rem;border-radius:.375rem;font-size:.8rem;flex:1;min-width:200px}
            .log-controls button{padding:.4rem .75rem;border:1px solid #334155;background:#1e293b;color:#94a3b8;border-radius:.375rem;cursor:pointer;font-size:.75rem;white-space:nowrap}
            .log-controls button.active{background:#1e40af;border-color:#3b82f6;color:#e2e8f0}
            .log-row{padding:.3rem .75rem;border-bottom:1px solid #0f172a;display:flex;gap:.5rem;align-items:baseline;flex-wrap:wrap}
            .log-row:hover{background:#1e293b}
            .log-error{border-left:3px solid #ef4444}.log-warn{border-left:3px solid #f59e0b}
            .log-info{border-left:3px solid transparent}.log-debug{border-left:3px solid #6b7280;opacity:.7}
            .log-time{color:#64748b;min-width:55px}.log-level{min-width:40px;font-weight:600}
            .log-error .log-level{color:#ef4444}.log-warn .log-level{color:#f59e0b}.log-info .log-level{color:#38bdf8}
            .log-cat{padding:.1rem .4rem;border-radius:.2rem;font-size:.65rem;font-weight:600}
            .cat-http{background:#1e3a5f;color:#60a5fa}.cat-migration{background:#3b0764;color:#c084fc}
            .cat-startup{background:#064e3b;color:#34d399}.cat-error{background:#450a0a;color:#f87171}
            .cat-db{background:#422006;color:#fbbf24}
            .log-elapsed{color:#94a3b8;font-size:.7rem}.log-msg{color:#cbd5e1;flex:1;word-break:break-word}
            .log-exception{color:#fca5a5;font-size:.7rem;white-space:pre-wrap;word-break:break-all;width:100%;margin-top:.25rem;max-height:120px;overflow-y:auto;border-radius:.25rem;padding:.35rem .5rem;background:#1a0505;border:1px solid #450a0a;cursor:pointer}
            .log-exception.collapsed{max-height:2.4rem;overflow:hidden;position:relative}
            .log-exception.collapsed::after{content:'... clique para expandir';position:absolute;bottom:0;right:.5rem;background:#1a0505;color:#94a3b8;font-size:.6rem;padding:0 .25rem}
            .log-ctx{display:inline-flex;gap:.3rem;margin-left:.25rem}
            .log-ctx span{font-size:.6rem;padding:.1rem .35rem;border-radius:.2rem;background:#1e293b;color:#64748b;border:1px solid #334155}
            .patterns-list{display:flex;flex-direction:column;gap:.75rem}
            .pattern-item{padding:.75rem;background:#0f172a;border-radius:.5rem;font-size:.85rem;border:1px solid #334155}
            .pattern-count{color:#94a3b8;font-size:.8rem}.pattern-desc{color:#cbd5e1}
            .pattern-tip{color:#94a3b8;font-size:.8rem}
            .links{display:flex;gap:1rem;margin-top:1.5rem;padding-top:1rem;border-top:1px solid #1e293b;flex-wrap:wrap}
            .links a{color:#38bdf8;text-decoration:none;font-size:.85rem}.links a:hover{text-decoration:underline}
            .refresh-bar{display:flex;align-items:center;gap:.5rem;font-size:.8rem;color:#94a3b8}
            .refresh-bar label{cursor:pointer;display:flex;align-items:center;gap:.25rem}
            .section-empty{color:#64748b;text-align:center;padding:2rem;font-size:.85rem}
            .toast-container{position:fixed;bottom:1.5rem;right:1.5rem;display:flex;flex-direction:column;gap:.5rem;z-index:9999;pointer-events:none}
            .toast{padding:.65rem 1rem;border-radius:.5rem;font-size:.8rem;color:#f1f5f9;opacity:0;transform:translateY(8px);transition:all .25s;pointer-events:none;max-width:320px;word-break:break-word}
            .toast.show{opacity:1;transform:translateY(0)}
            .toast.success{background:#052e16;border:1px solid #16a34a;color:#4ade80}
            .toast.error{background:#450a0a;border:1px solid #dc2626;color:#fca5a5}
            .toast.info{background:#1e293b;border:1px solid #334155;color:#94a3b8}
            .investigacao-panel .inv-log-entry{font-family:monospace;font-size:.7rem;color:#cbd5e1;padding:.2rem .3rem;border-bottom:1px solid #1e293b;word-break:break-all}
            .investigacao-panel .inv-log-entry:last-child{border:none}
            .pattern-item.resolved{opacity:.6;border-color:#052e16}
            .pattern-item.investigating{border-color:#78350f;background:#1c1917}
        </style></head><body>
        """;

    private static string RenderHeaderAndTabs(DiagnosticoResult r, EnhancedLogsResult? logs) => $$"""
        <header>
            <h1>EasyStock - Central de Diagnostico</h1>
            <div>
                <div class="meta">{{r.Timestamp:yyyy-MM-dd HH:mm:ss}} UTC | Uptime: {{r.Uptime}} | {{r.Ambiente}} | v{{r.Versao}}</div>
                <div class="refresh-bar" style="margin-top:.25rem;justify-content:flex-end">
                    <label><input type="checkbox" id="autoRefresh"> Auto-refresh 30s</label>
                    <button class="tab" onclick="location.reload()" style="padding:.2rem .5rem;font-size:.75rem">&#8635; Atualizar</button>
                </div>
            </div>
        </header>

        <div class="tabs">
            <button class="tab active" onclick="showTab('overview')">Visao Geral</button>
            <button class="tab" onclick="showTab('health')">Saude &amp; Graficos</button>
            <button class="tab" onclick="showTab('logs')">Logs 24h</button>
            <button class="tab" onclick="showTab('patterns')">Alertas &amp; Padroes{{(logs?.Padroes.Length > 0 ? $" <span style='background:#450a0a;color:#ef4444;border-radius:9999px;padding:.1rem .45rem;font-size:.65rem;font-weight:700'>{logs.Padroes.Length}</span>" : "")}}</button>
        </div>
        """;

    private static string RenderOverviewTab(DiagnosticoResult r, string causasHtml, string sloHtml) => $$"""
        <!-- OVERVIEW TAB -->
        <div class="panel active" id="tab-overview">
        <div class="overall" style="font-size:1.25rem;margin-bottom:1rem">Status geral: {{Badge(r.Status)}}</div>
        {{causasHtml}}
        {{sloHtml}}
        <div class="grid-2">
            <div class="card"><h2>&#128451; Banco de Dados</h2><table>
                <tr><td>Provider</td><td>{{r.Banco.Provider}}</td></tr>
                <tr><td>Configurado</td><td>{{r.Banco.ProviderConfigurado}}</td></tr>
                <tr><td>Fallback</td><td>{{BoolBadge(r.Banco.Fallback)}}</td></tr>
                <tr><td>Conexao</td><td>{{Badge(r.Banco.Conexao)}}</td></tr>
                <tr><td>Latencia</td><td>{{r.Banco.LatenciaMs}}ms</td></tr>
                <tr><td>Migrations</td><td>{{BoolBadge(r.Banco.MigrationsAplicadas)}}</td></tr>
                {{(r.Banco.Erro != null ? $"<tr><td>Erro</td><td style='color:#fca5a5;font-size:.8rem'>{System.Net.WebUtility.HtmlEncode(r.Banco.Erro)}</td></tr>" : "")}}
            </table></div>
            <div class="card"><h2>&#9889; Redis</h2><table>
                <tr><td>Configurado</td><td>{{BoolBadge(r.Redis.Configurado)}}</td></tr>
                <tr><td>Conexao</td><td>{{Badge(r.Redis.Conexao)}}</td></tr>
                <tr><td>Latencia</td><td>{{(r.Redis.Configurado ? r.Redis.LatenciaMs + "ms" : "N/A")}}</td></tr>
            </table></div>
            <div class="card"><h2>&#9993; SMTP / Email</h2><table>
                <tr><td>Configurado</td><td>{{BoolBadge(r.Smtp.Configurado)}}</td></tr>
                <tr><td>Tipo</td><td>{{r.Smtp.Tipo}}</td></tr>
                <tr><td>Host</td><td>{{r.Smtp.Host ?? "N/A"}}</td></tr>
            </table></div>
            <div class="card"><h2>&#128193; Storage</h2><table>
                <tr><td>Provider</td><td>{{r.Storage.Provider}}</td></tr>
                <tr><td>Configurado</td><td>{{BoolBadge(r.Storage.Configurado)}}</td></tr>
                <tr><td>{{(r.Storage.Provider == "Local" ? "Diretorio existe" : "Conexao")}}</td><td>{{BoolBadge(r.Storage.DiretorioExiste)}}</td></tr>
            </table></div>
            <div class="card"><h2>&#129302; IA (Anthropic)</h2><table>
                <tr><td>Habilitado</td><td>{{BoolBadge(r.Ia.Habilitado)}}</td></tr>
                <tr><td>API Key presente</td><td>{{BoolBadge(r.Ia.ApiKeyPresente)}}</td></tr>
            </table></div>
            <div class="card"><h2>&#128272; Configuracoes</h2><table>
                <tr><td>JWT Secret</td><td>{{BoolBadge(r.Configuracoes.JwtSecretPresente)}} {{(r.Configuracoes.JwtSecretSeguro == true ? Badge("ok") : Badge("falha"))}}</td></tr>
                <tr><td>Connection String</td><td>{{BoolBadge(r.Configuracoes.ConnectionStringPresente)}}</td></tr>
                <tr><td>CORS Origins</td><td style="font-size:.8rem">{{string.Join(", ", r.Configuracoes.CorsOrigins)}}</td></tr>
            </table></div>
        </div>
        <!-- Queries Lentas -->
        <div class='card' style='margin-top:1rem'>
            <h2>&#128269; Queries SQL Lentas (pg_stat_statements) <button onclick='loadQueriesLentas()' style='margin-left:.75rem;padding:.2rem .6rem;font-size:.75rem;background:#1e293b;border:1px solid #334155;color:#38bdf8;border-radius:.3rem;cursor:pointer'>Carregar</button></h2>
            <div id='queriesLentasResult'><span style='color:#475569;font-size:.85rem'>Clique em Carregar para buscar as queries mais lentas do PostgreSQL.</span></div>
        </div>
        <!-- Saude por Empresa -->
        <div class='card' style='margin-top:1rem'>
            <h2>&#128188; Saude por Empresa (sintetico) <button onclick='loadEmpresasHealth()' style='margin-left:.75rem;padding:.2rem .6rem;font-size:.75rem;background:#1e293b;border:1px solid #334155;color:#38bdf8;border-radius:.3rem;cursor:pointer'>Verificar</button></h2>
            <div id='empresasHealthResult'><span style='color:#475569;font-size:.85rem'>Clique em Verificar para testar conectividade por empresa.</span></div>
        </div>
        </div>
        """;

    private static string RenderHealthTab(IReadOnlyList<HealthSnapshot> snapshots, EnhancedLogsResult? logs) => $$"""
        <!-- HEALTH TAB -->
        <div class="panel" id="tab-health">
        {{(snapshots.Count > 0 ? $@"
        <div style='display:flex;justify-content:space-between;align-items:center;margin-bottom:.75rem'>
            <span style='font-size:.8rem;color:#94a3b8'>&#128200; Histórico de saúde — últimas 2h (snapshots a cada 60s)</span>
            <button onclick='zerarMedidores()' style='padding:.35rem .8rem;font-size:.75rem;background:#1e3a2f;border:1px solid #166534;color:#4ade80;border-radius:.4rem;cursor:pointer'>&#9654; Zerar medidores</button>
        </div>
        <div class='stats-grid' style='grid-template-columns:repeat(4,1fr);margin-bottom:1rem'>
            <div class='stat-box'><div class='stat-num' id='kpiDb'>{(snapshots.Count > 0 ? snapshots[^1].DbLatencyMs + "ms" : "—")}</div><div class='stat-label'>DB Latencia (ultimo)</div></div>
            <div class='stat-box {(snapshots.Count > 0 && snapshots[^1].RedisLatencyMs.HasValue ? "" : "")}'><div class='stat-num'>{(snapshots.Count > 0 && snapshots[^1].RedisLatencyMs.HasValue ? snapshots[^1].RedisLatencyMs + "ms" : "N/C")}</div><div class='stat-label'>Redis Latencia (ultimo)</div></div>
            <div class='stat-box {(snapshots.Count > 0 && snapshots[^1].ErrorCount > 0 ? "err" : "")}'><div class='stat-num' id='kpiErr'>{(snapshots.Count > 0 ? snapshots[^1].ErrorCount.ToString() : "—")}</div><div class='stat-label'>Erros (ultimo min)</div></div>
            <div class='stat-box'><div class='stat-num' id='kpiSnap'>{snapshots.Count} snapshots</div><div class='stat-label'>Historico (max 120 = 2h)</div></div>
        </div>
        <div class='card'><h2>&#128200; Latencia do Banco de Dados</h2>
            <div id='eventosLegenda' style='display:none;flex-wrap:wrap;gap:.25rem;margin-bottom:.5rem;padding:.4rem .5rem;background:#0f172a;border-radius:.3rem'></div>
            <div class='chart-box'><canvas id='dbChart'></canvas></div>
            <div style='font-size:.65rem;color:#475569;margin-top:.3rem'>🚀 Deploy &nbsp; ⚡ Error Spike &nbsp; (linhas aparecem quando eventos sao detectados nos logs)</div>
        </div>
        <div class='grid-2'>
            <div class='card'><h2>&#128200; Latencia Redis</h2>
                <div class='chart-box'><canvas id='redisChart'></canvas></div>
            </div>
            <div class='card'><h2>&#128200; Erros por Snapshot (por minuto)</h2>
                <div class='chart-box'><canvas id='errChart'></canvas></div>
            </div>
        </div>
        " : @"<div class='card'><div class='section-empty'>
            Aguardando primeiros snapshots de saude (coletados a cada 60s)...<br>
            <small style='color:#475569;margin-top:.5rem;display:block'>Os graficos aparecao automaticamente apos o primeiro minuto de uptime.</small>
        </div></div>")}}

        {{(logs?.Disponivel == true ? $@"
        <div class='card'><h2>&#128200; Volume de Requests e Erros por Hora (48h)</h2>
            <div class='chart-box' style='height:250px'><canvas id='volumeChart'></canvas></div>
        </div>
        " : "")}}
        </div>
        """;

    private static string RenderLogsTab(EnhancedLogsResult? logs, string logStatsHtml, string logEntriesHtml) => $$"""
        <!-- LOGS TAB -->
        <div class="panel" id="tab-logs">
        {{(logs?.Disponivel == true ? $@"
        {logStatsHtml}
        <!-- Timeline visual de erros -->
        <div class='card' style='padding:.75rem 1rem;margin-bottom:.75rem'>
            <div style='display:flex;justify-content:space-between;align-items:center;margin-bottom:.4rem'>
                <h3 style='font-size:.8rem;color:#94a3b8;font-weight:500'>&#128200; Timeline de Erros &amp; Requests — ultimas 48h (por hora)</h3>
                <span style='font-size:.7rem;color:#475569'>Clique para filtrar por hora</span>
            </div>
            <div style='position:relative;height:80px'><canvas id='errorTimelineChart'></canvas></div>
            <div id='timelineFilterInfo' style='font-size:.7rem;color:#f59e0b;margin-top:.3rem;min-height:1rem'></div>
        </div>
        <!-- Storage de logs -->
        <div class='card' style='padding:.75rem 1rem;margin-bottom:.75rem'>
            <div style='display:flex;align-items:center;gap:.75rem;flex-wrap:wrap'>
                <span style='font-size:.8rem;color:#94a3b8;font-weight:500'>&#128230; Logs Arquivados (Storage)</span>
                <button onclick='loadStorageFileList()' style='padding:.3rem .65rem;font-size:.75rem;background:#1e293b;border:1px solid #334155;color:#38bdf8;border-radius:.35rem;cursor:pointer'>Listar arquivos</button>
                <select id='storageFileSelect' style='display:none;background:#1e293b;border:1px solid #334155;color:#e2e8f0;padding:.3rem .5rem;border-radius:.35rem;font-size:.75rem;max-width:260px' onchange='loadStorageFileContent()'><option value=''>-- selecione --</option></select>
                <button id='clearStorageBtn' onclick='clearStorageView()' style='display:none;padding:.3rem .65rem;font-size:.75rem;background:#450a0a;border:1px solid #7f1d1d;color:#fca5a5;border-radius:.35rem;cursor:pointer'>&#10005; Limpar</button>
                <span id='storageStatusMsg' style='font-size:.75rem;color:#94a3b8'></span>
            </div>
            <div id='storageArchiveIndicator' style='display:none;margin-top:.4rem;font-size:.75rem;color:#f59e0b;background:#422006;border:1px solid #78350f;border-radius:.3rem;padding:.3rem .6rem'>
                &#128197; Exibindo logs de arquivo arquivado no storage — dados historicos
            </div>
        </div>
        <div class='card'>
            <h2>&#128466; Console de Logs <span id='logSourceLabel' style='font-size:.75rem;font-weight:400;color:#94a3b8'>(ultimas 48h)</span>
                <span id='liveDot' style='display:none;width:8px;height:8px;border-radius:50%;background:#22c55e;margin-left:6px;vertical-align:middle'></span>
                <span id='liveTimestamp' style='font-size:.7rem;font-weight:400;color:#475569;margin-left:.5rem'></span>
            </h2>
            <div class='log-controls'>
                <input type='text' id='logFilter' placeholder='Filtrar mensagens...' oninput='filterLogs()'>
                <button class='active' onclick='toggleLevel(this,""all"")'>Todos</button>
                <button onclick='toggleLevel(this,""ERROR"")'>Erros</button>
                <button onclick='toggleLevel(this,""WARN"")'>Warnings</button>
                <button onclick='toggleLevel(this,""INFO"")'>Info</button>
                <a href='/api/diagnostico/logs/exportar' download style='padding:.4rem .75rem;border:1px solid #334155;background:#1e293b;color:#94a3b8;border-radius:.375rem;font-size:.75rem;text-decoration:none;white-space:nowrap;cursor:pointer'>&#11015; Exportar</a>
                <button onclick='moverParaLixeira()' style='margin-left:auto;background:#92400e;color:#fef3c7;border-color:#b45309'>&#128465; Mover p/ lixeira</button>
                <button onclick='esvaziarLixeira()' style='background:#450a0a;color:#fca5a5;border-color:#7f1d1d'>&#128465; Esvaziar lixeira</button>
                <button onclick='expurgarLogs()' style='background:#1e3a5f;color:#60a5fa;border-color:#2563eb'>&#128465; Expurgar antigos</button>
                <span id='lixeiraBadge' style='font-size:.72rem;color:#94a3b8;white-space:nowrap'></span>
            </div>
            <div class='log-console' id='logConsole'>
                {logEntriesHtml}
            </div>
            <div style='margin-top:.5rem;font-size:.75rem;color:#64748b' id='logCountInfo'>Mostrando ate 200 entradas mais recentes de {logs.TotalEntries} total</div>
        </div>
        " : "<div class='section-empty'>Logs nao disponiveis neste ambiente.</div>")}}
        </div>
        """;

    private static string RenderPatternsTab(EnhancedLogsResult? logs, IReadOnlyList<HealthSnapshot> snapshots, string patternsHtmlAck) => $$"""
        <!-- PATTERNS TAB -->
        <div class="panel" id="tab-patterns">
        {{(patternsHtmlAck.Length > 0 ? patternsHtmlAck : "<div class='card' style='border-color:#052e16'><div class='section-empty' style='color:#16a34a'>&#10003; Nenhum padrao anomalo detectado nas ultimas 48h.</div></div>")}}

        {{(logs?.Disponivel == true ? $@"
        <div class='grid-2'>
        " + (logs.Resumo.ErrorsByEndpoint.Count > 0 ?
            "<div class='card'><h2>&#128680; Top Endpoints com Erros</h2><table>" +
            string.Join("", logs.Resumo.ErrorsByEndpoint.OrderByDescending(kv => kv.Value).Take(10).Select(kv =>
                $"<tr><td style='font-family:monospace;font-size:.8rem'>{System.Net.WebUtility.HtmlEncode(kv.Key)}</td><td><span class='badge crit'>{kv.Value}x</span></td></tr>")) +
            "</table></div>"
            : "<div class='card'><h2>&#128680; Erros por Endpoint</h2><div class='section-empty'>Sem erros registrados nos endpoints.</div></div>")
        + (logs.Resumo.TotalRequests > 0 && logs.Resumo.AvgResponseTimeMs > 0 ?
            $"<div class='card'><h2>&#9201; Performance (48h)</h2><table>" +
            $"<tr><td>Total Requests</td><td><strong>{logs.Resumo.TotalRequests}</strong></td></tr>" +
            $"<tr><td>Tempo Medio Resposta</td><td><strong style='color:{(logs.Resumo.AvgResponseTimeMs < 200 ? "#22c55e" : logs.Resumo.AvgResponseTimeMs < 1000 ? "#f59e0b" : "#ef4444")}'>{logs.Resumo.AvgResponseTimeMs:F0}ms</strong></td></tr>" +
            $"<tr><td>Taxa de Erro</td><td><strong style='color:{(logs.Resumo.TotalErrors == 0 ? "#22c55e" : "#ef4444")}'>{(logs.Resumo.TotalRequests > 0 ? (100.0 * logs.Resumo.TotalErrors / logs.Resumo.TotalRequests):0):F1}%</strong></td></tr>" +
            $"<tr><td>Warnings</td><td><strong>{logs.Resumo.TotalWarnings}</strong></td></tr>" +
            "</table></div>"
            : "")
        + @"</div>" : "")}}

        <!-- Snapshot health summary -->
        {{(snapshots.Count > 0 ? $@"
        <div class='card'><h2>&#128308; Saude do Banco — Historico de Status</h2>
        <div style='display:flex;flex-wrap:wrap;gap:3px;margin-top:.5rem'>
        {string.Join("", snapshots.TakeLast(60).Select(s => {
            var color = s.DbStatus == "ok" ? "#16a34a" : "#dc2626";
            var title = $"{s.Timestamp:HH:mm} — DB:{s.DbStatus} {s.DbLatencyMs}ms Erros:{s.ErrorCount}";
            return $"<span title='{System.Net.WebUtility.HtmlEncode(title)}' style='display:inline-block;width:10px;height:20px;border-radius:2px;background:{color};cursor:help'></span>";
        }))}
        </div>
        <div style='font-size:.7rem;color:#64748b;margin-top:.4rem'>Cada bloco = 1 minuto. Verde=OK, Vermelho=Falha. Ultimos {Math.Min(snapshots.Count, 60)} minutos.</div>
        </div>" : "")}}
        </div>
        """;
}
