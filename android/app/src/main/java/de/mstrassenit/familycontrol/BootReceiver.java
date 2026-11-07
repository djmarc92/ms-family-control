package de.mstrassenit.familycontrol;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** Nach Neustart / Update / Entsperren: Schutz wieder starten */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent intent) {
        GuardService.ensure(c);
        if (Cfg.get(c).active() && Engine.get(c).webFilterWanted()) FilterVpn.start(c);
    }
}
