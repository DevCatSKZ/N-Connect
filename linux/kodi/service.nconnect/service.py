# -*- coding: utf-8 -*-
"""
N-Connect-Dienst für Kodi: startet das N-Connect-Programm (BlueZ → virtueller Xbox-360-Controller), überträgt die
Einstellungen, zeigt Meldungen beim Verbinden/Trennen und startet das Programm neu, falls es unerwartet endet.
"""

import json
import subprocess
import threading
import time

import xbmc
import xbmcgui

from resources.lib import common
from resources.lib.common import text

RESTART_DELAYS = [3, 5, 10, 30, 60]

# Ereignisse, die den sichtbaren Controller-Stand ändern → Home-Widget (plugin://service.nconnect/) neu laden.
WIDGET_EVENTS = {'connected', 'disconnected', 'paired', 'failed', 'elsewhere', 'battery-low', 'bluetooth',
                 'wifi-hint', 'gamepad-paired', 'gamepad-connected', 'gamepad-disconnected', 'gamepad-pair-failed'}


WIDGET_PATHS = ('plugin://service.nconnect', 'plugin://plugin.nconnect.status')
RELOAD_PROPERTY = 'NConnect.Reload'


class Service(xbmc.Monitor):
    def __init__(self):
        super().__init__()
        self.process = None
        self.lock = threading.Lock()
        self.adapter = common.adapter_setting()
        self.failures = 0
        self.last_bluetooth = None
        self.stopping = False
        self._last_widget_refresh = 0.0
        self._fingerprint = None
        self._dirty = False
        self._focus_pending = False
        self._reload_counter = int(time.time())

    def refresh_widget(self):
        """Eine sichtbare Änderung ist eingetreten: Widget soll neu laden (siehe flush_widget)."""
        self._dirty = True
        self.flush_widget()

    def flush_widget(self):
        """Widget-Anzeige aktualisieren, höchstens alle 1,5 s; eine gedrosselte Änderung bleibt vorgemerkt.

        1. Home-Eigenschaft NConnect.Reload ändern: Skins mit ?reload=$INFO[Window(Home).Property(NConnect.Reload)]
           im Widget-Pfad laden die Liste damit selbst neu – auch ohne Fokus, ohne andere Listen anzufassen.
        2. Container.Refresh trifft immer den gerade fokussierten Container – ein blindes Aufrufen lud früher alle
           paar Sekunden Hauptmenü und andere Listen neu (Home-Bildschirm blieb schwarz). Daher nur, wenn dort
           gerade eine N-Connect-Liste fokussiert ist (für Skins mit festem Pfad); sonst nachholen, sobald sie es ist.
        """
        now = time.time()
        if self._dirty and now - self._last_widget_refresh >= 1.5:
            self._last_widget_refresh = now
            self._dirty = False
            self._reload_counter += 1
            xbmcgui.Window(10000).setProperty(RELOAD_PROPERTY, str(self._reload_counter))
            self._focus_pending = True
        if self._focus_pending and xbmc.getInfoLabel('Container.FolderPath').startswith(WIDGET_PATHS):
            self._focus_pending = False
            xbmc.executebuiltin('Container.Refresh')

    def watch_status(self):
        """Sichtbare Änderung im Status (Controller, Akku, Verbindung, Bluetooth) → Anzeige neu laden. Fängt auch
        Änderungen ab, zu denen kein Ereignis kam; reine Raten-Schwankungen der alle 5 s geschriebenen status.json
        lösen nichts aus."""
        fingerprint = common.fingerprint(common.read_status())
        if fingerprint != self._fingerprint:
            self._fingerprint = fingerprint
            self.refresh_widget()
        else:
            self.flush_widget()

    # ---------- Programm starten/stoppen ----------

    def start(self):
        binary = common.binary_path()
        if binary is None:
            common.log('kein Programm für Prozessor ' + common.machine(), xbmc.LOGERROR)
            self.notify(text(30022, common.machine()), error=True)
            return False
        common.ensure_data_dir()
        try:
            common.mark_starting()
        except OSError:
            pass
        self.load_uinput()
        args = [binary, '--data', common.DATA_DIR]
        if self.adapter:
            args += ['--adapter', self.adapter]
        common.log('starte ' + ' '.join(args))
        with self.lock:
            self.process = subprocess.Popen(
                args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                env=common.environment(), bufsize=1, universal_newlines=True, encoding='utf-8', errors='replace')
        threading.Thread(target=self.read_events, args=(self.process,), name='nconnect-events', daemon=True).start()
        return True

    def stop(self):
        with self.lock:
            process, self.process = self.process, None
        if process is None or process.poll() is not None:
            return
        process.terminate()  # SIGTERM: trennt die Controller sauber und entfernt die virtuellen Controller
        try:
            process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()

    @staticmethod
    def load_uinput():
        """uinput ist bei LibreELEC meist fest eingebaut; als Modul wird es hier geladen."""
        try:
            subprocess.call(['modprobe', 'uinput'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except OSError:
            pass

    def send(self, command):
        with self.lock:
            process = self.process
        if process is None or process.poll() is not None:
            return
        try:
            process.stdin.write(command + '\n')
            process.stdin.flush()
        except (OSError, ValueError):
            pass

    # ---------- Ereignisse des Programms ----------

    def read_events(self, process):
        for line in process.stdout:
            line = line.strip()
            if not line.startswith('{'):
                continue
            try:
                self.on_event(json.loads(line))
            except ValueError:
                continue
            except Exception as e:  # noqa: BLE001 – Meldungen dürfen den Dienst nie beenden
                common.log('Ereignis: {0}'.format(e), xbmc.LOGWARNING)

    def on_event(self, ev):
        name = ev.get('event')
        if name == 'connected':
            self.failures = 0
            self.notify(text(30010, ev.get('player'), ev.get('name')))
        elif name == 'paired':
            self.notify(text(30011, ev.get('player')))
        elif name == 'disconnected' and ev.get('player'):
            self.notify(text(30012, ev.get('player'), ev.get('name')))
        elif name == 'elsewhere':
            self.notify(text(30013, ev.get('name')), duration=8000)
        elif name == 'failed':
            self.notify(text(30014, ev.get('name')), error=True)
        elif name == 'gamepad-paired':
            self.notify(text(30023, ev.get('name')))
        elif name == 'gamepad-connected':
            self.notify(text(30024, ev.get('name')))
        elif name == 'gamepad-disconnected':
            self.notify(text(30025, ev.get('name')))
        elif name == 'gamepad-pair-failed':
            self.notify(text(30026, ev.get('name')), error=True)
        elif name == 'battery-low':
            # Wichtige Meldung: auch bei abgeschalteten Verbindungsmeldungen anzeigen (eigene Einstellung im Dienst).
            xbmcgui.Dialog().notification('N-Connect', text(30028, ev.get('name'), ev.get('battery')),
                                          xbmcgui.NOTIFICATION_WARNING, 8000, sound=True)
        elif name == 'button':
            action = common.button_action(ev.get('button'))
            if action:
                xbmc.executebuiltin(action)
        elif name == 'wifi-hint':
            # Funkkoexistenz: WLAN frisst die Verbindung – wichtig genug, um immer zu melden.
            xbmcgui.Dialog().notification('N-Connect', text(30029), xbmcgui.NOTIFICATION_WARNING, 10000)
        elif name == 'error':
            self.notify(text(30017), error=True)
        elif name == 'bluetooth':
            state = ev.get('state')
            if state != self.last_bluetooth:
                self.last_bluetooth = state
                if state == 'off':
                    self.notify(text(30015), duration=8000, error=True)
                elif state == 'missing':
                    self.notify(text(30016), duration=8000, error=True)
        if name in WIDGET_EVENTS:
            self.refresh_widget()

    def notify(self, message, duration=4000, error=False):
        if not error and not common.notifications_enabled():
            return
        xbmcgui.Dialog().notification(
            'N-Connect', message, xbmcgui.NOTIFICATION_ERROR if error else common.ICON, duration, sound=False)

    # ---------- Kodi-Rückrufe ----------

    def onSettingsChanged(self):
        try:
            common.write_settings_json()
        except OSError as e:
            common.log('settings.json: {0}'.format(e), xbmc.LOGERROR)
        adapter = common.adapter_setting()
        if adapter != self.adapter:
            self.adapter = adapter
            self.stop()  # die Hauptschleife startet mit dem neuen Adapter neu

    def onNotification(self, sender, method, data):
        if sender != common.ADDON_ID or not method.startswith('Other.'):
            return
        command = method[len('Other.'):]
        if command == 'disconnect-all':
            self.send('disconnect-all')
        elif command.startswith('gamepad-disconnect-') or command.startswith('gamepad-forget-'):
            action, address = command.rsplit('-', 1)
            self.send('{0} {1}'.format(action, address))
        elif command.startswith('disconnect-'):
            self.send('disconnect ' + command[len('disconnect-'):])
        elif command == 'restart':
            self.stop()
        elif command == 'status':
            self.send('status')
        elif command == 'scan':
            self.send('scan')
            self.notify(text(30027))

    # ---------- Hauptschleife ----------

    def run_companion(self):
        """Windows: Die N-Connect-App verbindet die Controller und schreibt status.json selbst. Dieser Dienst
        sorgt nur dafür, dass Widgets bei sichtbaren Änderungen neu laden."""
        common.log('Windows: Begleitmodus – Status kommt von der N-Connect-App')
        while not self.abortRequested():
            self.watch_status()
            if self.waitForAbort(1):
                break

    def run(self):
        if common.is_windows():
            self.run_companion()
            return
        try:
            common.write_settings_json()
        except OSError as e:
            common.log('settings.json: {0}'.format(e), xbmc.LOGERROR)
        try:
            common.install_buttonmaps()
        except OSError as e:
            common.log('Kodi-Belegung: {0}'.format(e), xbmc.LOGWARNING)
        while not self.abortRequested():
            if self.process is None and not self.start():
                return
            started = time.time()
            while not self.abortRequested():
                with self.lock:
                    process = self.process
                if process is None or process.poll() is not None:
                    break
                self.watch_status()
                if self.waitForAbort(1):
                    break
            if self.abortRequested():
                break
            with self.lock:
                process, self.process = self.process, None
            if process is None:
                continue  # gewollter Neustart (Adapter geändert / „restart“)
            # Unerwartet beendet: mit wachsender Pause neu starten; wer länger lief, fängt wieder bei 3 s an.
            if time.time() - started > 120:
                self.failures = 0
            delay = RESTART_DELAYS[min(self.failures, len(RESTART_DELAYS) - 1)]
            self.failures += 1
            common.log('N-Connect beendet (Code {0}), Neustart in {1} s'.format(process.returncode, delay), xbmc.LOGWARNING)
            if self.failures == 3:
                self.notify(text(30018), error=True)
            if self.waitForAbort(delay):
                break
        self.stop()


if __name__ == '__main__':
    Service().run()
