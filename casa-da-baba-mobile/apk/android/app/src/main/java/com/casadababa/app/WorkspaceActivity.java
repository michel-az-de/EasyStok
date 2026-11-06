package com.casadababa.app;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.Configuration;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.webkit.WebBackForwardList;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.LinearLayout;
import android.widget.TextView;

public class WorkspaceActivity extends Activity {
    private static final String HOME = "https://app.easystok.online/#/m/atendimento/balcao";
    private static final int PICK_FILE = 2101;
    private static final int RECORD_AUDIO = 2102;
    private WebView web;
    private TextView error;
    private ValueCallback<Uri[]> fileCallback;
    private PermissionRequest microphoneRequest;
    private boolean sessionBridgeReady;
    private final NativeSession.Listener sessionListener = (authenticated, version) -> WorkspaceSessionBridge.signal(web, authenticated, version);

    private boolean trustedOrigin(Uri uri) {
        return uri != null && "https".equalsIgnoreCase(uri.getScheme()) &&
            "app.easystok.online".equalsIgnoreCase(uri.getHost()) &&
            uri.getUserInfo() == null && (uri.getPort() == -1 || uri.getPort() == 443);
    }

    private void returnToApp() {
        finish();
        startActivity(new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT));
    }

    // Só volta no histórico quando a entrada anterior é uma página real do app.
    private boolean hasRealPreviousPage() {
        if (!web.canGoBack()) return false;
        WebBackForwardList list = web.copyBackForwardList();
        int index = list.getCurrentIndex() - 1;
        if (index < 0) return false;
        String url = list.getItemAtIndex(index).getUrl();
        return url != null && !url.isEmpty() && !"about:blank".equals(url) && trustedOrigin(Uri.parse(url));
    }

    private int dp(int value) {
        return Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, getResources().getDisplayMetrics()));
    }

    private TextView barAction(String label, String description, int ink) {
        TextView action = new TextView(this);
        action.setText(label);
        action.setContentDescription(description);
        action.setTextColor(ink);
        action.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        action.setGravity(Gravity.CENTER_VERTICAL);
        action.setMinWidth(dp(48));
        action.setPadding(dp(16), 0, dp(16), 0);
        action.setClickable(true);
        action.setFocusable(true);
        TypedValue ripple = new TypedValue();
        getTheme().resolveAttribute(android.R.attr.selectableItemBackground, ripple, true);
        action.setBackgroundResource(ripple.resourceId);
        return action;
    }

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        boolean night = (getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
        int paper = night ? Color.rgb(36, 34, 29) : Color.rgb(251, 246, 236);
        int ink = night ? Color.rgb(240, 233, 222) : Color.rgb(53, 45, 37);
        layout.setBackgroundColor(paper);
        getWindow().setStatusBarColor(paper);
        // Barra fina da marca: Início à esquerda, título e recarregar à direita (56dp, 48dp de toque).
        LinearLayout actions = new LinearLayout(this);
        actions.setGravity(Gravity.CENTER_VERTICAL);
        actions.setBackgroundColor(paper);
        TextView back = barAction("\u2039  Início", "Voltar ao início", ink);
        back.setOnClickListener(view -> returnToApp());
        TextView title = new TextView(this);
        title.setText("Atendimento");
        title.setTextColor(ink);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17);
        title.setTypeface(title.getTypeface(), android.graphics.Typeface.BOLD);
        title.setGravity(Gravity.CENTER);
        TextView retry = barAction("\u21bb", "Recarregar", ink);
        retry.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        retry.setOnClickListener(view -> {
            if (!sessionBridgeReady) {
                sessionBridgeReady = WorkspaceSessionBridge.install(this, web);
                if (!sessionBridgeReady) { showWebViewUpdate(); return; }
            }
            error.setVisibility(View.GONE);
            web.setVisibility(View.VISIBLE);
            if (web.getUrl() == null) web.loadUrl(HOME); else web.reload();
        });
        actions.addView(back, new LinearLayout.LayoutParams(-2, -1));
        actions.addView(title, new LinearLayout.LayoutParams(0, -1, 1));
        actions.addView(retry, new LinearLayout.LayoutParams(-2, -1));
        layout.addView(actions, new LinearLayout.LayoutParams(-1, dp(56)));
        error = new TextView(this);
        error.setPadding(dp(16), dp(16), dp(16), dp(16));
        error.setTextColor(ink);
        error.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        error.setText("O atendimento precisa de conexão. Confira a internet e toque em Recarregar.");
        error.setVisibility(View.GONE);
        layout.addView(error);
        web = new WebView(this);
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setGeolocationEnabled(false);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        // Sem bridge Capacitor/interfaces Java. Somente comandos de sessão com origem e frame conferidos.
        sessionBridgeReady = WorkspaceSessionBridge.install(this, web);
        NativeSession.get(this).addListener(sessionListener);
        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                if (!"https".equalsIgnoreCase(uri.getScheme()) || uri.getUserInfo() != null ||
                    (uri.getPort() != -1 && uri.getPort() != 443)) return true;
                String host = uri.getHost();
                if ("app.easystok.online".equalsIgnoreCase(host) ||
                    "api.easystok.online".equalsIgnoreCase(host)) return false;
                if (request.isForMainFrame() && request.hasGesture()) {
                    try { startActivity(new Intent(Intent.ACTION_VIEW, uri)); }
                    catch (Exception ignored) { error.setVisibility(View.VISIBLE); }
                }
                return true;
            }
            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError failure) {
                if (request.isForMainFrame()) {
                    web.setVisibility(View.GONE);
                    error.setVisibility(View.VISIBLE);
                }
            }
        });
        web.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (!trustedOrigin(Uri.parse(view.getUrl() == null ? "" : view.getUrl()))) return false;
                if (fileCallback != null) fileCallback.onReceiveValue(null);
                fileCallback = callback;
                try { startActivityForResult(params.createIntent(), PICK_FILE); }
                catch (Exception unavailable) {
                    fileCallback.onReceiveValue(null);
                    fileCallback = null;
                }
                return true;
            }
            @Override
            public void onPermissionRequest(PermissionRequest request) {
                runOnUiThread(() -> {
                    String[] resources = request.getResources();
                    if (!trustedOrigin(request.getOrigin()) || resources.length != 1 ||
                        !PermissionRequest.RESOURCE_AUDIO_CAPTURE.equals(resources[0])) {
                        request.deny();
                        return;
                    }
                    if (microphoneRequest != null) microphoneRequest.deny();
                    microphoneRequest = request;
                    if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
                        request.grant(new String[] {PermissionRequest.RESOURCE_AUDIO_CAPTURE});
                        microphoneRequest = null;
                    } else {
                        requestPermissions(new String[] {Manifest.permission.RECORD_AUDIO}, RECORD_AUDIO);
                    }
                });
            }
            @Override
            public void onPermissionRequestCanceled(PermissionRequest request) {
                if (microphoneRequest == request) microphoneRequest = null;
            }
        });
        layout.addView(web, new LinearLayout.LayoutParams(-1, 0, 1));
        setContentView(layout);
        if (!sessionBridgeReady) showWebViewUpdate();
        else if (savedInstanceState == null || web.restoreState(savedInstanceState) == null) web.loadUrl(HOME);
    }

    private void showWebViewUpdate() {
        error.setText("Atualize o Android System WebView na Play Store e toque em Recarregar. Seus dados continuam no aplicativo.");
        error.setVisibility(View.VISIBLE);
        web.setVisibility(View.GONE);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == PICK_FILE && fileCallback != null) {
            fileCallback.onReceiveValue(WebChromeClient.FileChooserParams.parseResult(resultCode, data));
            fileCallback = null;
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(requestCode, permissions, results);
        if (requestCode == RECORD_AUDIO && microphoneRequest != null) {
            if (results.length == 1 && results[0] == PackageManager.PERMISSION_GRANTED &&
                trustedOrigin(microphoneRequest.getOrigin()) &&
                trustedOrigin(Uri.parse(web.getUrl() == null ? "" : web.getUrl()))) {
                microphoneRequest.grant(new String[] {PermissionRequest.RESOURCE_AUDIO_CAPTURE});
            } else microphoneRequest.deny();
            microphoneRequest = null;
        }
    }

    @Override
    public void onBackPressed() {
        if (hasRealPreviousPage()) web.goBack(); else returnToApp();
    }

    @Override
    protected void onSaveInstanceState(Bundle state) {
        web.saveState(state);
        super.onSaveInstanceState(state);
    }

    @Override
    protected void onDestroy() {
        NativeSession.get(this).removeListener(sessionListener);
        if (fileCallback != null) fileCallback.onReceiveValue(null);
        if (microphoneRequest != null) microphoneRequest.deny();
        if (web != null) web.destroy();
        super.onDestroy();
    }
}
