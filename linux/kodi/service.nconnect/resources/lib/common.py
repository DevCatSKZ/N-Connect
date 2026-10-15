# -*- coding: utf-8 -*-
"""Gemeinsames für Dienst (service.py) und Menü (default.py): Pfade, Texte, Einstellungen, Status."""

import json
import os
import platform
import stat
import time

import xbmc
import xbmcaddon
import xbmcvfs

ADDON = xbmcaddon.Addon('service.nconnect')
ADDON_ID = ADDON.getAddonInfo('id')
ADDON_PATH = ADDON.getAddonInfo('path')
DATA_DIR = xbmcvfs.translatePath(ADDON.getAddonInfo('profile'))
ICON = os.path.join(ADDON_PATH, 'resources', 'icon.png')
SETTINGS_JSON = os.path.join(DATA_DIR, 'settings.json')
STATUS_JSON = os.path.join(DATA_DIR, 'status.json')
LOG_FILE = os.path.join(DATA_DIR, 'nconnect.log')

# Prozessor → .NET-Laufzeitkennung des mitgelieferten Programms.
_RIDS = {
    'x86_64': 'linux-x64', 'amd64': 'linux-x64',
    'aarch64': 'linux-arm64', 'arm64': 'linux-arm64',
    'armv7l': 'linux-arm', 'armv8l': 'linux-arm', 'armv7': 'linux-arm',
}


def text(string_id, *args):
    """Übersetzter Text aus strings.po; {0}, {1} … werden ersetzt."""
    value = ADDON.getLocalizedString(string_id)
    return value.format(*args) if args else value


def log(message, level=xbmc.LOGINFO):
    xbmc.log('[N-Connect] ' + message, level)


def machine():
    return platform.machine().lower()


def is_windows():
    """Kodi läuft unter Windows: Dort verbindet die N-Connect-App die Controller selbst und schreibt
    status.json in diesen Datenordner – das Add-on startet keinen Dienst, sondern zeigt den Stand nur an."""
    return os.name == 'nt'


def has_daemon():
    """Der N-Connect-Dienst (BlueZ/uinput) gibt es nur unter echtem Linux. Auf anderen Systemen läuft das Add-on
    im Begleitmodus: Es zeigt eine status.json an, die eine N-Connect-App schreibt. Achtung: platform.system()
    meldet auf Android 'Linux' – darum wird Kodi gezielt gefragt."""
    if xbmc.getCondVisibility('System.Platform.Android'):
        return False
    return platform.system() == 'Linux'


def binary_path():
    """Pfad zum N-Connect-Programm für diesen Prozessor (oder None) – setzt das Ausführungsrecht, das beim
    Entpacken des Add-on-Archivs verloren geht."""
    if not has_daemon():
        return None  # anderes System (Windows, Android, macOS) – kein mitgeliefertes Programm dafür
    rid = _RIDS.get(machine())
    if rid is None:
        return None
    for candidate in (os.path.join(ADDON_PATH, 'bin', rid, 'nconnect'), os.path.join(ADDON_PATH, 'bin', 'nconnect')):
        if os.path.isfile(candidate):
            mode = os.stat(candidate).st_mode
            if not mode & stat.S_IXUSR:
                os.chmod(candidate, mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
            return candidate
    return None


def environment():
    """Umgebung für das .NET-Programm: Entpackordner im Add-on-Datenordner, keine Diagnose-Sockets in /tmp."""
    env = dict(os.environ)
    env['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = os.path.join(DATA_DIR, 'cache')
    env['DOTNET_EnableDiagnostics'] = '0'
    env['DOTNET_gcServer'] = '0'
    env['DOTNET_GCConserveMemory'] = '5'
    return env


def ensure_data_dir():
    if not os.path.isdir(DATA_DIR):
        os.makedirs(DATA_DIR)


def install_buttonmaps():
    """Kodi-Belegung für den virtuellen Controller in den Benutzerordner von peripheral.joystick kopieren – nur wenn
    dort noch keine liegt (eine in Kodi angepasste Belegung wird nie überschrieben)."""
    source_root = os.path.join(ADDON_PATH, 'resources', 'buttonmaps')
    target_root = xbmcvfs.translatePath(
        'special://profile/addon_data/peripheral.joystick/resources/buttonmaps/xml/')
    for provider in os.listdir(source_root):
        target_dir = os.path.join(target_root, provider)
        for name in os.listdir(os.path.join(source_root, provider)):
            target = os.path.join(target_dir, name)
            if os.path.exists(target):
                continue
            if not os.path.isdir(target_dir):
                os.makedirs(target_dir)
            with open(os.path.join(source_root, provider, name), 'rb') as src, open(target, 'wb') as dst:
                dst.write(src.read())
            log('Kodi-Belegung installiert: ' + target)


# ---------- Einstellungen: Kodi-Add-on → settings.json (gleiche Datei wie unter Windows/Android) ----------

_LAYOUTS = ['Xbox', 'Switch2']
_GYRO_MODES = ['Off', 'Always', 'WhileAiming']
_INACTIVITY = [0, 5, 10, 15, 30, 60]


def _setting_int(settings, key, default):
    try:
        return settings.getInt(key)
    except Exception:  # noqa: BLE001 – ältere/fehlende Einstellung
        return default


def _setting_bool(settings, key, default):
    try:
        return settings.getBool(key)
    except Exception:  # noqa: BLE001
        return default


def write_settings_json():
    """Überträgt die Add-on-Einstellungen in settings.json. Andere Einträge (z. B. aus Windows übernommene
    Tastenbelegungen) bleiben erhalten. Der Dienst lädt die Datei bei Änderungen selbst neu."""
    ensure_data_dir()
    data = {}
    try:
        with open(SETTINGS_JSON, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
    except (OSError, ValueError):
        data = {}
    s = xbmcaddon.Addon(ADDON_ID).getSettings()
    data['Layout'] = _LAYOUTS[min(max(_setting_int(s, 'layout', 0), 0), 1)]
    data['StickDeadzone'] = round(_setting_int(s, 'stick_deadzone', 6) / 100.0, 3)
    data['CombineJoyCons'] = _setting_bool(s, 'combine_joycons', True)
    data['GyroStick'] = _GYRO_MODES[min(max(_setting_int(s, 'gyro_stick', 0), 0), 2)]
    data['GyroStickFullSpeed'] = float(max(_setting_int(s, 'gyro_speed', 150), 30))
    data['GyroStickInvertY'] = _setting_bool(s, 'gyro_invert_y', False)
    data['RumbleEnabled'] = _setting_bool(s, 'rumble', True)
    data['RumbleStrength'] = round(_setting_int(s, 'rumble_strength', 80) / 100.0, 2)
    data['AutoPair'] = _setting_bool(s, 'auto_pair', True)
    data['GameLeds'] = _setting_bool(s, 'player_leds', True)
    data['ConnectFeedback'] = _setting_bool(s, 'connect_feedback', True)
    data['AutoPairGamepads'] = _setting_bool(s, 'auto_pair_gamepads', True)
    data['BackgroundScan'] = _setting_bool(s, 'background_scan', True)
    data['BatteryWarning'] = _setting_bool(s, 'battery_warning', True)
    data['DsuServer'] = _setting_bool(s, 'dsu_server', True)
    data['DsuNetwork'] = _setting_bool(s, 'dsu_network', False)
    data['InactivityMinutes'] =_INACTIVITY[min(max(_setting_int(s, 'inactivity', 0), 0), len(_INACTIVITY) - 1)]
    temp = SETTINGS_JSON + '.kodi.tmp'
    with open(temp, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    os.replace(temp, SETTINGS_JSON)


# Kodi-Aktionen für Controller-Tasten (Reihenfolge = Optionen 0…10 in settings.xml)
BUTTON_ACTIONS = [
    None, 'TakeScreenshot', 'ActivateWindow(Home)', 'Action(ContextMenu)', 'Action(Info)', 'PlayerControl(Play)',
    'PlayerControl(Stop)', 'Mute', 'ActivateWindow(ShutdownMenu)', 'RunScript(service.nconnect)', 'Action(FullScreen)',
]
BUTTON_SETTINGS = {'Capture': ('capture_action', 1), 'C': ('c_action', 9), 'GL': ('gl_action', 0), 'GR': ('gr_action', 0)}


def button_action(button):
    """Kodi-Befehl für eine Controller-Taste (oder None)."""
    key, default = BUTTON_SETTINGS.get(button, (None, 0))
    if key is None:
        return None
    index = _setting_int(xbmcaddon.Addon(ADDON_ID).getSettings(), key, default)
    return BUTTON_ACTIONS[index] if 0 <= index < len(BUTTON_ACTIONS) else None


def adapter_setting():
    try:
        return xbmcaddon.Addon(ADDON_ID).getSettings().getString('adapter').strip()
    except Exception:  # noqa: BLE001
        return ''


def notifications_enabled():
    return _setting_bool(xbmcaddon.Addon(ADDON_ID).getSettings(), 'notifications', True)


# ---------- Status ----------

def _status_candidates():
    """Alle Orte, an denen eine status.json liegen kann. Unter Android darf die N-Connect-App nicht in
    Kodis Datenordner schreiben – sie legt die Datei im gemeinsamen Dokumente-Ordner ab (bis Android 10
    alternativ in ihrem externen App-Ordner)."""
    yield STATUS_JSON
    if xbmc.getCondVisibility('System.Platform.Android'):
        yield '/sdcard/Documents/N-Connect/status.json'
        yield '/sdcard/Android/data/com.devcatskz.nconnect/files/status.json'


def read_status():
    """Frischeste lesbare status.json; None, wenn nichts läuft (Datei fehlt, veraltet oder „stopped“).
    {"state": "starting"} heißt: das Programm wird gerade (neu) gestartet."""
    status = None
    age = None
    for path in _status_candidates():
        try:
            with open(path, 'r', encoding='utf-8') as f:
                candidate = json.load(f)
            candidate_age = time.time() - os.path.getmtime(path)
        except (OSError, ValueError):
            continue
        if age is None or candidate_age < age:
            status, age = candidate, candidate_age
    if status is None:
        return None
    state = status.get('state')
    if state == 'running' and age <= 15 or state == 'starting' and age <= 60:
        return status
    return None


def mark_starting():
    """Vor dem Start des Programms: das Menü zeigt „startet …“ statt „läuft nicht“."""
    ensure_data_dir()
    temp = STATUS_JSON + '.kodi.tmp'
    with open(temp, 'w', encoding='utf-8') as f:
        json.dump({'state': 'starting'}, f)
    os.replace(temp, STATUS_JSON)


def send_command(command):
    """Befehl an den laufenden Dienst (über Kodis Benachrichtigungen, der Dienst leitet ihn an N-Connect weiter)."""
    xbmc.executebuiltin('NotifyAll({0},{1})'.format(ADDON_ID, command))


def read_log_tail(lines=300):
    try:
        with open(LOG_FILE, 'r', encoding='utf-8', errors='replace') as f:
            return ''.join(f.readlines()[-lines:])
    except OSError:
        return ''


# ---------- Icons je Controller-Art ----------

ICONS_DIR = os.path.join(ADDON_PATH, 'resources', 'icons')
def fingerprint(status):
    """Nur das, was Menü und Widget sichtbar anzeigen (ohne die ständig schwankende Berichtsrate) – ändert sich der
    Wert, lohnt sich ein Neuladen der Anzeige; alle 5 s neu geschriebene status.json allein nicht."""
    if status is None:
        return None
    return (
        status.get('state'), status.get('bluetooth'), bool(status.get('scanning')), bool(status.get('connecting')),
        tuple(sorted(f.get('address', '') for f in status.get('elsewhere', []))),
        tuple((p.get('player'), p.get('name'), p.get('battery'), bool(p.get('charging')),
               tuple((d.get('address'), d.get('kind'), d.get('battery')) for d in p.get('devices', [])))
              for p in status.get('players', [])),
        tuple((g.get('address'), g.get('name'), bool(g.get('connected')), g.get('battery'))
              for g in status.get('gamepads', [])),
        tuple((k.get('address'), k.get('kind'), bool(k.get('connected'))) for k in status.get('known', [])),
    )


# Kurze Namen für Widget-Kacheln – der volle Anzeigename („Nintendo Switch 2 Joy-Con (L)“)
# wird auf dem Home-Bildschirm abgeschnitten.
_KIND_SHORT = {
    'JoyCon2Left': 'Joy-Con 2 (L)', 'JoyCon2Right': 'Joy-Con 2 (R)',
    'JoyCon1Left': 'Joy-Con (L)', 'JoyCon1Right': 'Joy-Con (R)',
    'JoyConPair': 'Joy-Con (L+R)',
    'Pro2': 'Pro Controller 2', 'Pro1': 'Pro Controller', 'WiiUPro': 'Wii U Pro',
    'GameCube2': 'GameCube',
    'NesController': 'NES', 'SnesController': 'SNES',
    'N64Controller': 'N64', 'MegaDrive': 'Mega Drive',
    'WiiRemote': 'Wii Remote',
    'DualShock4': 'DualShock 4', 'DualSense': 'DualSense',
    'XboxController': 'Xbox',
}


def short_name(kind, name=''):
    return _KIND_SHORT.get(kind) or (name or 'Controller')


_KIND_ICONS = {
    'JoyCon2Left': 'joycon_l.png', 'JoyCon2Right': 'joycon_r.png',
    'JoyCon1Left': 'joycon_l.png', 'JoyCon1Right': 'joycon_r.png',
    'JoyConPair': 'joycon_pair.png',
    'Pro2': 'pro.png', 'Pro1': 'pro.png', 'WiiUPro': 'pro.png',
    'GameCube2': 'gamecube.png',
    'DualShock4': 'playstation.png', 'DualSense': 'playstation.png',
    'XboxController': 'xbox.png',
}


def kind_icon(kind, name=''):
    """Icon-Datei zur Controller-Art; Fremd-Gamepads (Xbox, PlayStation …) anhand des Namens."""
    if kind in _KIND_ICONS:
        return os.path.join(ICONS_DIR, _KIND_ICONS[kind])
    lower = (name or '').lower()
    if 'joy-con' in lower:
        return os.path.join(ICONS_DIR, 'joycon_l.png' if '(l' in lower else 'joycon_r.png')
    for key, icon in (('xbox', 'xbox.png'), ('playstation', 'playstation.png'), ('dualshock', 'playstation.png'),
                      ('dualsense', 'playstation.png'), ('gamecube', 'gamecube.png')):
        if key in lower:
            return os.path.join(ICONS_DIR, icon)
    return os.path.join(ICONS_DIR, 'gamepad.png')


def offline_icon(path):
    """Ausgegraute Variante eines Icons (für Controller, die bekannt, aber gerade nicht verbunden sind)."""
    off = path.replace('.png', '_off.png')
    return off if os.path.isfile(off) else path


def player_icon(player):
    """Icon für einen Spieler-Eintrag aus status.json (Joy-Con-Paar hat ein eigenes)."""
    devices = player.get('devices', [])
    if len(devices) > 1:
        return os.path.join(ICONS_DIR, 'joycon_pair.png')
    for d in devices:
        if d.get('kind') in _KIND_ICONS:
            return os.path.join(ICONS_DIR, _KIND_ICONS[d['kind']])
    return kind_icon('', player.get('name'))


def help_text():
    """Anleitung in Kodis Sprache (help_<ISO 639-1>.txt), sonst Englisch."""
    lang = (xbmc.getLanguage(xbmc.ISO_639_1) or 'en')[:2].lower()
    for name in ('help_{0}.txt'.format(lang), 'help_en.txt'):
        try:
            with open(os.path.join(ADDON_PATH, 'resources', name), 'r', encoding='utf-8') as f:
                return f.read()
        except OSError:
            continue
    return ''
