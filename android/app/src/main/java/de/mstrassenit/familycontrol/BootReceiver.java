package de.mstrassenit.familycontrol;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** Nach Neustart / Update: Schutz starten · Ein-/Ausschalten protokollieren */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent intent) {
        String a = intent == null ? "" : String.valueOf(intent.getAction());
        boolean active = Cfg.get(c).active();
        if (active && (a.equals(Intent.ACTION_SHUTDOWN) || a.equals("android.intent.action.QUICKBOOT_POWEROFF"))) {
            Engine.get(c).power("off", "heruntergefahren");
            return;
        }
        if (active && (a.equals(Intent.ACTION_BOOT_COMPLETED) || a.equals("android.intent.action.QUICKBOOT_POWERON"))) {
            Engine.get(c).power("on", "eingeschaltet");
            GuardService.kick(c);
        }
        GuardService.ensure(c);
        if (active && Engine.get(c).webFilterWanted()) FilterVpn.start(c);
    }
}
