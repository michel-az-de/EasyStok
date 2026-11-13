package android.content;
import android.content.pm.PackageManager;
import android.content.res.AssetManager;
import java.io.File;
/** Host-test fixture. Real Android behavior is verified by the Android build/device gate. */
public class Context {
    private final File files;
    public Context(File files) { this.files = files; }
    public Context getApplicationContext() { return this; }
    public File getFilesDir() { return files; }
    public String getPackageName() { return "com.casadababa.piloto"; }
    public PackageManager getPackageManager() { return new PackageManager(); }
    public AssetManager getAssets() { return new AssetManager(new File(files, "assets")); }
}
