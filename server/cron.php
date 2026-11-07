<?php
declare(strict_types=1);
/*
 * Cronjob (jede Minute): Offline-Warnungen, Aufräumen.
 *   * * * * * php /pfad/zu/server/cron.php >/dev/null 2>&1
 */
if (PHP_SAPI !== 'cli') {
    http_response_code(403);
    exit('CLI only');
}
require __DIR__ . '/lib/bootstrap.php';
if (!installed()) {
    exit(0);
}
maintenance(true);
// Filterlisten einmal täglich aktualisieren (läuft nur im Cron, nicht bei Seitenaufrufen)
if (time() - (int)setting('filters_last', 0) > 3600) {
    set_setting('filters_last', time());
    filter_update_all();
}
