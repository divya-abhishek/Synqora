package com.synqora;
import android.app.RemoteInput;
import android.content.*;
import android.os.Bundle;
import java.io.*;
import java.nio.charset.StandardCharsets;

public class FixtureReplyReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        Bundle values = RemoteInput.getResultsFromIntent(intent);
        CharSequence text = values == null ? "" : values.getCharSequence("fixture-reply", "");
        try (FileOutputStream output = context.openFileOutput("fixture-reply.txt", 0)) { output.write(text.toString().getBytes(StandardCharsets.UTF_8)); }
        catch (IOException e) { throw new IllegalStateException(e); }
    }
}
