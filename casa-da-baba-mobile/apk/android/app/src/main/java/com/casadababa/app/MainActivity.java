package com.casadababa.app;

import android.os.Bundle;

import com.casadababa.app.printer.ThermalPrinterPlugin;
import com.casadababa.app.backup.PublicBackupPlugin;
import com.casadababa.app.updater.OtaManager;
import com.casadababa.app.updater.PilotUpdaterPlugin;
import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeActivity;
import com.getcapacitor.ServerPath;

public class MainActivity extends BridgeActivity {
    private boolean recreatedActivity;

    @Override
    public void onCreate(Bundle savedInstanceState) {
        recreatedActivity = savedInstanceState != null;
        registerPlugin(PilotAppPlugin.class);
        registerPlugin(ThermalPrinterPlugin.class);
        registerPlugin(PublicBackupPlugin.class);
        registerPlugin(PilotUpdaterPlugin.class);
        super.onCreate(savedInstanceState);
    }

    @Override
    protected void load() {
        String path = OtaManager.get(this).beginBoot(recreatedActivity);
        bridgeBuilder.setServerPath(path == null
            ? new ServerPath(ServerPath.PathType.ASSET_PATH, Bridge.DEFAULT_WEB_ASSET_DIR)
            : new ServerPath(ServerPath.PathType.BASE_PATH, path));
        super.load();
    }
}
