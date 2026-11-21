package com.casadababa.app;

import android.app.Activity;
import android.net.Uri;
import android.webkit.WebView;
import androidx.webkit.WebViewCompat;
import androidx.webkit.WebViewFeature;
import java.util.Collections;
import org.json.JSONObject;

final class WorkspaceSessionBridge {
    private static final String NAME = "EasyStokSession";
    private static final String ORIGIN = "https://app.easystok.online";

    static boolean trusted(Uri uri) {
        return uri != null && "https".equalsIgnoreCase(uri.getScheme())
            && "app.easystok.online".equalsIgnoreCase(uri.getHost()) && uri.getUserInfo() == null
            && (uri.getPort() == -1 || uri.getPort() == 443);
    }

    static boolean install(Activity activity, WebView web) {
        if (!WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)) return false;
        NativeSession session = NativeSession.get(activity);
        WebViewCompat.addWebMessageListener(web, NAME, Collections.singleton(ORIGIN),
            (view, message, sourceOrigin, isMainFrame, reply) -> {
                if (!isMainFrame || !trusted(sourceOrigin) || !trusted(Uri.parse(view.getUrl() == null ? "" : view.getUrl()))) return;
                try {
                    JSONObject request = new JSONObject(message.getData());
                    String id = request.optString("id");
                    if (!id.matches("[A-Za-z0-9_-]{1,96}")) return;
                    NativeSession.Callback callback = (data, failure) -> {
                        try {
                            if (activity.isDestroyed() || !trusted(Uri.parse(web.getUrl() == null ? "" : web.getUrl()))) return;
                            JSONObject response = new JSONObject().put("id", id);
                            if (failure == null) response.put("dados", data);
                            else response.put("erro", new JSONObject().put("codigo", failure.code).put("mensagem", failure.getMessage()).put("status", failure.status));
                            reply.postMessage(response.toString());
                        } catch (Exception unavailable) { /* navegação/Activity encerrada não transfere a sessão */ }
                    };
                    switch (request.optString("acao")) {
                        case "obter": session.obtain(request.optString("tokenRejeitado", null), callback); break;
                        case "entrar": session.signIn(activity, new JSONObject(), callback); break;
                        case "sair": session.signOut(callback); break;
                        default: callback.complete(null, new SessionHttpClient.Failure(400, "ACAO_INVALIDA", "Ação de conta inválida."));
                    }
                } catch (Exception invalid) { /* sem credenciais em URL, logs ou bridge genérica */ }
            });
        return true;
    }

    static void signal(WebView web, boolean authenticated, long version) {
        if (web == null) return;
        try {
            if (trusted(Uri.parse(web.getUrl() == null ? "" : web.getUrl())))
                web.evaluateJavascript("window.dispatchEvent(new CustomEvent('easystok:native-session-changed',{detail:{autenticado:"
                    + (authenticated ? "true" : "false") + ",versao:" + version + "}}));", null);
        } catch (Exception unavailable) { }
    }
}
