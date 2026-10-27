package com.casadababa.app;

import android.content.Intent;
import android.net.Uri;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

@CapacitorPlugin(name = "PilotApp")
public class PilotAppPlugin extends Plugin {
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
