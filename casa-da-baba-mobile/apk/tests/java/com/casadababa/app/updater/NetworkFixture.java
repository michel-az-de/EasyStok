package com.casadababa.app.updater;
import java.io.*;
import java.net.*;
import java.security.cert.Certificate;
import java.util.concurrent.ConcurrentHashMap;
import javax.net.ssl.HttpsURLConnection;

/** In-process HTTP fixture. It cannot open external sockets. */
final class NetworkFixture {
    static final ConcurrentHashMap<String, byte[]> responses = new ConcurrentHashMap<>();
    static volatile int status = 200;
    static volatile int packageRequests;
    static void install() {
        URL.setURLStreamHandlerFactory(protocol -> new URLStreamHandler() {
            @Override protected URLConnection openConnection(URL url) {
                if (!protocol.equals("https")) throw new AssertionError("Unexpected protocol");
                return new HttpsURLConnection(url) {
                    public void disconnect() {}
                    public boolean usingProxy() { return false; }
                    public void connect() {}
                    public String getCipherSuite() { return "HOST_TEST"; }
                    public Certificate[] getLocalCertificates() { return null; }
                    public Certificate[] getServerCertificates() { return null; }
                    @Override public int getResponseCode() {
                        if (getInstanceFollowRedirects()) throw new AssertionError("Redirects must be disabled");
                        return status;
                    }
                    @Override public InputStream getInputStream() throws IOException {
                        byte[] bytes = responses.get(url.toString());
                        if (bytes == null) throw new IOException("Unregistered test URL");
                        if (url.toString().endsWith(".zip")) packageRequests++;
                        return new ByteArrayInputStream(bytes);
                    }
                };
            }
        });
    }
}
