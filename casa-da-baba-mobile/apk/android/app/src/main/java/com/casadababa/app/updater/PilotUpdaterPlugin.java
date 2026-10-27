package com.casadababa.app.updater;

import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

@CapacitorPlugin(name = "PilotUpdater")
public class PilotUpdaterPlugin extends Plugin {
    private OtaManager manager() { return OtaManager.get(getContext()); }

    @Override public void load() { checkAutomatically(); }
    @Override protected void handleOnResume() { checkAutomatically(); }

    private void checkAutomatically() {
        UpdatePush.subscribe(getContext(), false);
        manager().check(false, value -> notifyListeners("status", UpdatePush.decorate(getContext(), value)));
    }

    @PluginMethod public void status(PluginCall call) { call.resolve(UpdatePush.decorate(getContext(), manager().status())); }
    @PluginMethod public void check(PluginCall call) {
        UpdatePush.subscribe(getContext(), false);
        manager().check(true, value -> call.resolve(UpdatePush.decorate(getContext(), value)));
    }

    @PluginMethod public void ready(PluginCall call) {
        try { call.resolve(UpdatePush.decorate(getContext(), manager().ready(call.getString("bundleVersion")))); }
        catch (Exception error) { call.reject(error.getMessage(), error); }
    }

    @PluginMethod public void rollback(PluginCall call) {
        try { call.resolve(UpdatePush.decorate(getContext(), manager().rollback())); }
        catch (Exception error) { call.reject(error.getMessage(), error); }
    }
}
