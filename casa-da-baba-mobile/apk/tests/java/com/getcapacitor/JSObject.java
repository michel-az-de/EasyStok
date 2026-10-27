package com.getcapacitor;
import org.json.JSONObject;
public class JSObject extends JSONObject {
    @Override public JSObject put(String key, boolean value) { super.put(key, value); return this; }
    @Override public JSObject put(String key, int value) { super.put(key, value); return this; }
    @Override public JSObject put(String key, Object value) { super.put(key, value); return this; }
}
