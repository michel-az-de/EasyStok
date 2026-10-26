package com.casadababa.app;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.view.View;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

public class WorkspaceActivity extends Activity {
    private static final String HOME = "https://app.easystok.online/";
    private static final int PICK_FILE = 2101;
    private static final int RECORD_AUDIO = 2102;
    private WebView web;
    private TextView error;
    private ValueCallback<Uri[]> fileCallback;
    private PermissionRequest microphoneRequest;

    private boolean trustedOrigin(Uri uri) {
        return uri != null && "https".equalsIgnoreCase(uri.getScheme()) &&
            "app.easystok.online".equalsIgnoreCase(uri.getHost()) &&
            uri.getUserInfo() == null && (uri.getPort() == -1 || uri.getPort() == 443);
    }

    private void returnToApp() {
        startActivity(new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT));
    }

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setBackgroundColor(Color.rgb(251, 248, 242));
        LinearLayout actions = new LinearLayout(this);
        Button back = new Button(this);
        back.setText("Voltar ao app");
        back.setOnClickListener(view -> returnToApp());
        Button retry = new Button(this);
        retry.setText("Recarregar");
        retry.setOnClickListener(view -> {
            error.setVisibility(View.GONE);
            web.setVisibility(View.VISIBLE);
            web.reload();
        });
        actions.addView(back, new LinearLayout.LayoutParams(0, -2, 1));
        actions.addView(retry, new LinearLayout.LayoutParams(0, -2, 1));
        layout.addView(actions);
        error = new TextView(this);
        error.setPadding(24, 24, 24, 24);
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
        // Este WebView não recebe a bridge Capacitor nem interfaces JavaScript.
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
        if (savedInstanceState == null || web.restoreState(savedInstanceState) == null) web.loadUrl(HOME);
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
        if (web.canGoBack()) web.goBack(); else returnToApp();
    }

    @Override
    protected void onSaveInstanceState(Bundle state) {
        web.saveState(state);
        super.onSaveInstanceState(state);
    }

    @Override
    protected void onDestroy() {
        if (fileCallback != null) fileCallback.onReceiveValue(null);
        if (microphoneRequest != null) microphoneRequest.deny();
        if (web != null) web.destroy();
        super.onDestroy();
    }
}
