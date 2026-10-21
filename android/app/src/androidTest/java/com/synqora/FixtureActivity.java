package com.synqora;

import android.app.*;
import android.content.Intent;
import android.os.Bundle;

/** Standalone test APK process: use framework Java only (target APK supplies Kotlin to instrumentation). */
public class FixtureActivity extends Activity {
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        NotificationManager manager = getSystemService(NotificationManager.class);
        if ("fixture.clean".equals(getIntent().getAction())) manager.cancel(9);
        else {
            getFileStreamPath("fixture-reply.txt").delete();
            manager.createNotificationChannel(new NotificationChannel("fixture", "Test replies", NotificationManager.IMPORTANCE_LOW));
            PendingIntent reply = PendingIntent.getBroadcast(this, 0, new Intent(this, FixtureReplyReceiver.class), PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE);
            Notification.Action action = new Notification.Action.Builder(null, "Reply", reply).addRemoteInput(new RemoteInput.Builder("fixture-reply").setLabel("Reply").build()).build();
            manager.notify(9, new Notification.Builder(this, "fixture").setSmallIcon(android.R.drawable.ic_dialog_info).setContentTitle("Synqora reply fixture").setContentText("Synthetic test message").addAction(action).build());
        }
        finish();
    }
}
