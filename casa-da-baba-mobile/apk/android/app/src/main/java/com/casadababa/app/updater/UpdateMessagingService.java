package com.casadababa.app.updater;

import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

public class UpdateMessagingService extends FirebaseMessagingService {
    @Override public void onNewToken(String token) {
        // The Firebase SDK owns the token. Never put it in logs, the WebView or update manifests.
        UpdatePush.subscribe(this, true);
    }

    @Override public void onMessageReceived(RemoteMessage message) {
        if (!UpdatePolicy.isUpdateAnnouncement(message.getFrom(), message.getData().get("kind"),
                message.getData().get("sequence"))) return;
        UpdatePush.received(this);
        UpdateCheckJob.schedule(this);
    }

    @Override public void onDeletedMessages() { UpdateCheckJob.schedule(this); }
}
