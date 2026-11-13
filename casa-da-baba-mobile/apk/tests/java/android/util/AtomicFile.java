package android.util;
import java.io.*;
import java.nio.file.*;
/** Fault-injection fixture; does not claim to emulate Android filesystem durability. */
public class AtomicFile {
    public static boolean failNextWrite;
    private final File base;
    private final File staging;
    public AtomicFile(File base) { this.base = base; staging = new File(base + ".new"); }
    public InputStream openRead() throws IOException { return new FileInputStream(base); }
    public FileOutputStream startWrite() throws IOException {
        if (failNextWrite) { failNextWrite = false; throw new IOException("Injected persistence failure"); }
        return new FileOutputStream(staging);
    }
    public void finishWrite(FileOutputStream stream) throws IOException {
        stream.getFD().sync(); stream.close();
        Files.move(staging.toPath(), base.toPath(), StandardCopyOption.REPLACE_EXISTING);
    }
    public void failWrite(FileOutputStream stream) {
        try { stream.close(); } catch (IOException ignored) {}
        staging.delete();
    }
}
