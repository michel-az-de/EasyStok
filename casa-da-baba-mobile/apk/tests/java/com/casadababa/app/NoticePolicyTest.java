package com.casadababa.app;
public final class NoticePolicyTest {
 public static void main(String[] args) {
  String sender="/topics/"+NoticePolicy.TOPIC;
  check(NoticePolicy.accepts(sender,"app-notice","migration-1","migration-guide","Backup","Confira"));
  check(NoticePolicy.accepts(sender,"app-notice","home-1","home","Aviso","Abra o app"));
  check(!NoticePolicy.accepts("attacker","app-notice","a","home","A","B"));
  check(!NoticePolicy.accepts(sender,"ota-update","a","home","A","B"));
  check(!NoticePolicy.accepts(sender,"app-notice","../a","home","A","B"));
  check(!NoticePolicy.accepts(sender,"app-notice","a","https://evil.test","A","B"));
  check(!NoticePolicy.accepts(sender,"app-notice","a","home","","B"));
  check(!NoticePolicy.accepts(sender,"app-notice","a","home","A","x".repeat(1201)));
  check(!NoticePolicy.accepts(sender,"app-notice",null,"home","A","B"));
  check(!NoticePolicy.screenAllowed(null));
  check(NoticePolicy.shouldRequestPermission(33, false, false));
  check(!NoticePolicy.shouldRequestPermission(32, false, false));
  check(!NoticePolicy.shouldRequestPermission(33, true, false));
  check(!NoticePolicy.shouldRequestPermission(33, false, true));
  check(com.casadababa.app.updater.UpdatePolicy.manifestUrl(4).endsWith("/ota/latest.json"));
  check(com.casadababa.app.updater.UpdatePolicy.manifestUrl(5).endsWith("/ota/native-5/latest.json"));
  System.out.println("PASS: 16 notification policy assertions; Android permission and tray require device validation");
 }
 private static void check(boolean valid) { if(!valid) throw new AssertionError(); }
}
