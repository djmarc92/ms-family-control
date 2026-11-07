package de.mstrassenit.familycontrol;

import android.app.admin.DeviceAdminReceiver;
import android.content.Context;
import android.content.Intent;

/** Geräteadministrator: verhindert das einfache Deinstallieren der App */
public class AdminReceiver extends DeviceAdminReceiver {
    @Override
    public CharSequence onDisableRequested(Context c, Intent intent) {
        if (Cfg.get(c).active()) {
            Engine.get(c).addEvent("tamper", "Versuch, den Geräteadministrator der Familien-App abzuschalten", "");
            GuardService.kick(c);
        }
        return "Damit wird der Kinderschutz abgeschaltet. Deine Eltern werden benachrichtigt.";
    }

    @Override
    public void onDisabled(Context c, Intent intent) {
        if (Cfg.get(c).active()) {
            Engine.get(c).addEvent("uninstall", "Geräteadministrator der Familien-App wurde abgeschaltet", "Die App kann jetzt deinstalliert werden");
            GuardService.kick(c);
        }
    }
}
