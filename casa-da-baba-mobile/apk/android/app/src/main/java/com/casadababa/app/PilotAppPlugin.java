package com.casadababa.app;

import android.content.Intent;
import android.net.Uri;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.JSObject;
import com.getcapacitor.annotation.CapacitorPlugin;
import org.json.JSONObject;

@CapacitorPlugin(name = "PilotApp")
public class PilotAppPlugin extends Plugin {
    private final NativeSession.Listener sessionListener = (authenticated, version) -> notifyListeners("sessionChanged", new JSObject().put("autenticado", authenticated).put("versao", version));
    private final NoticeManager.Listener noticeListener = value -> notifyListeners("noticeStatus", value);
    @Override public void load() {
        NativeSession.get(getContext()).addListener(sessionListener);
        try { NoticeManager.get(getContext()).setListener(noticeListener); }
        catch (Exception ignored) { /* noticeStatus returns a safe diagnostic. */ }
    }
    @Override protected void handleOnResume() {
        try { NoticeManager.get(getContext()).refresh((state, ok) -> notifyListeners("noticeStatus", state), false); }
        catch (Exception ignored) { }
    }
    @Override protected void handleOnDestroy() {
        NativeSession.get(getContext()).removeListener(sessionListener);
        try { NoticeManager.get(getContext()).removeListener(noticeListener); }
        catch (Exception ignored) { }
    }
    @PluginMethod public void noticeStatus(PluginCall call) {
        try { NoticeManager.get(getContext()).statusAsync((state, ok) -> call.resolve(state)); }
        catch (Exception failure) { call.reject("Não foi possível abrir o histórico de avisos. Os dados foram preservados."); }
    }
    @PluginMethod public void noticeRefresh(PluginCall call) {
        try { NoticeManager.get(getContext()).refresh((state, ok) -> call.resolve(state)); }
        catch (Exception failure) { call.reject("Não foi possível verificar os avisos. Os dados foram preservados."); }
    }
    @PluginMethod public void noticeOpen(PluginCall call) {
        String id = call.getString("id");
        if (id == null || id.length() > 64) { call.reject("Aviso inválido."); return; }
        try { NoticeManager.get(getContext()).openAsync(id, (state, ok) -> call.resolve(state)); }
        catch (Exception failure) { call.reject("Não foi possível confirmar a abertura do aviso."); }
    }
    @PluginMethod
    public void notifications(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            if (Boolean.TRUE.equals(call.getBoolean("configure", false)) && AppNotifications.enabled(getActivity())) AppNotifications.openSettings(getActivity());
            else AppNotifications.offerPermission(getActivity(), true);
            call.resolve();
        });
    }

    @PluginMethod
    public void migrationGuide(PluginCall call) {
        getActivity().runOnUiThread(() -> { AppNotifications.showGuide(getActivity()); call.resolve(); });
    }

    @PluginMethod
    public void authStatus(PluginCall call) { NativeSession.get(getContext()).status(sessionReply(call)); }

    @PluginMethod
    public void authLogin(PluginCall call) { NativeSession.get(getContext()).signIn(getActivity(), call.getData(), sessionReply(call)); }

    @PluginMethod
    public void authPair(PluginCall call) { NativeSession.get(getContext()).pairing(call.getData(), sessionReply(call)); }

    @PluginMethod
    public void authLogout(PluginCall call) { NativeSession.get(getContext()).signOut(sessionReply(call)); }

    private NativeSession.Callback sessionReply(PluginCall call) {
        return (data, failure) -> {
            if (failure != null) {
                JSObject details = new JSObject();
                try { if (failure.details.has("lojas")) details.put("lojas", failure.details.getJSONArray("lojas")); } catch (Exception ignored) { }
                call.reject(failure.getMessage(), failure.code, null, details);
                return;
            }
            try {
                JSONObject safe = new JSONObject(data.toString());
                safe.remove("token"); safe.remove("refreshToken");
                call.resolve(new JSObject(safe.toString()));
            } catch (Exception invalid) { call.reject("Não foi possível confirmar a conta.", "SESSAO_INVALIDA"); }
        };
    }

    @PluginMethod
    public void openWorkspace(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            try {
                getActivity().startActivity(new Intent(getContext(), WorkspaceActivity.class).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT));
                call.resolve();
            } catch (Exception error) {
                call.reject("Não foi possível abrir o atendimento.", error);
            }
        });
    }

    @PluginMethod
    public void openDownload(PluginCall call) {
        try {
            Intent browser = new Intent(Intent.ACTION_VIEW,
                Uri.parse("https://easystok.online/downloads/casa-da-baba/"));
            getActivity().startActivity(browser);
            call.resolve();
        } catch (Exception error) {
            call.reject("Não foi possível abrir a página de atualização.", error);
        }
    }
}
