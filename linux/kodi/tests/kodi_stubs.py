# -*- coding: utf-8 -*-
"""
Nachbau der Kodi-Module (xbmc, xbmcaddon, xbmcgui, xbmcplugin, xbmcvfs) für die Tests der Add-ons – gerade so viel,
wie die Add-ons benutzen. Beim Import werden sie in sys.modules eingetragen. Texte kommen aus den echten strings.po
der Add-ons (Sprache: STATE.language), Datenordner (special://profile, special://home) liegen in einem Temp-Ordner.
Aufrufe, die Kodi sichtbar machen würde (Meldungen, Dialoge, Listeneinträge, Builtins), werden in STATE gesammelt.

Tests ausführen (aus dem Repo-Wurzelordner): py -3 -m unittest discover -s linux/kodi/tests
"""
import atexit
import importlib.util
import os
import re
import shutil
import sys
import tempfile
import types

sys.dont_write_bytecode = True  # keine __pycache__ in den Add-on-Quellordnern

KODI_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # linux/kodi
ROOT = tempfile.mkdtemp(prefix='nconnect-kodi-tests-')
atexit.register(shutil.rmtree, ROOT, True)

STATE = types.SimpleNamespace()


def reset(addon='service.nconnect'):
    """Vor jedem Test: nichts gesammelt, keine Kodi-Bedingung erfüllt, Englisch, Standard-Einstellungen."""
    STATE.addon = addon            # Add-on, das xbmcaddon.Addon() ohne ID meint
    STATE.language = 'en_gb'
    STATE.conditions = set()       # z. B. {'System.Platform.Android'}
    STATE.settings = {}
    STATE.log = []
    STATE.builtins = []
    STATE.notifications = []
    STATE.dialogs = []
    STATE.yesno = False
    STATE.items = []
    STATE.properties = {}
    STATE.abort = False


reset()

_ENTRY = re.compile(r'msgctxt "#(\d+)"\nmsgid "(.*)"\nmsgstr "(.*)"')
_strings_cache = {}


def strings(addon_id, language='en_gb'):
    """ID -> Text aus resources/language/resource.language.<language>/strings.po (en_gb: msgid)."""
    key = (addon_id, language)
    if key not in _strings_cache:
        path = os.path.join(KODI_DIR, addon_id, 'resources', 'language', 'resource.language.' + language, 'strings.po')
        table = {}
        if os.path.isfile(path):
            with open(path, encoding='utf-8') as f:
                for m in _ENTRY.finditer(f.read()):
                    msgid, msgstr = (s.replace('\\"', '"').replace('\\\\', '\\') for s in m.group(2, 3))
                    table[int(m.group(1))] = msgstr if language != 'en_gb' and msgstr else msgid
        _strings_cache[key] = table
    return _strings_cache[key]


def translate_path(path):
    for prefix, folder in (('special://profile/', 'profile'), ('special://userdata/', 'profile'),
                           ('special://home/', 'home')):
        if path.startswith(prefix):
            return os.path.join(ROOT, folder, *path[len(prefix):].split('/'))
    return path


# ---------- xbmc ----------

xbmc = types.ModuleType('xbmc')
xbmc.LOGDEBUG, xbmc.LOGINFO, xbmc.LOGWARNING, xbmc.LOGERROR = 0, 1, 2, 3
xbmc.ISO_639_1 = 0
xbmc.log = lambda message, level=1: STATE.log.append((level, message))
xbmc.getCondVisibility = lambda condition: condition in STATE.conditions
xbmc.executebuiltin = lambda command, wait=False: STATE.builtins.append(command)
xbmc.getLanguage = lambda fmt=None, region=False: STATE.language[:2]
xbmc.getInfoLabel = lambda label: ''


class _Monitor:
    def abortRequested(self):
        return STATE.abort

    def waitForAbort(self, timeout=None):
        return STATE.abort


xbmc.Monitor = _Monitor

# ---------- xbmcaddon ----------

xbmcaddon = types.ModuleType('xbmcaddon')


class _Settings:
    """Wie Kodis Settings-Objekt: unbekannte Einstellungen werfen (die Add-ons fangen das ab)."""

    def getBool(self, key):
        return bool(STATE.settings[key])

    def getInt(self, key):
        return int(STATE.settings[key])

    def getString(self, key):
        return str(STATE.settings[key])


class _Addon:
    def __init__(self, id=None):
        self._id = id or STATE.addon

    def getAddonInfo(self, key):
        return {'id': self._id, 'path': os.path.join(KODI_DIR, self._id),
                'profile': 'special://profile/addon_data/{0}/'.format(self._id)}.get(key, '')

    def getLocalizedString(self, string_id):
        return strings(self._id, STATE.language).get(string_id) or strings(self._id).get(string_id, '')

    def getSettings(self):
        return _Settings()


xbmcaddon.Addon = _Addon

# ---------- xbmcvfs ----------

xbmcvfs = types.ModuleType('xbmcvfs')
xbmcvfs.translatePath = translate_path


def _mkdirs(path):
    os.makedirs(path, exist_ok=True)
    return True


xbmcvfs.mkdirs = _mkdirs

# ---------- xbmcgui ----------

xbmcgui = types.ModuleType('xbmcgui')
xbmcgui.NOTIFICATION_INFO, xbmcgui.NOTIFICATION_WARNING, xbmcgui.NOTIFICATION_ERROR = 'info', 'warning', 'error'


class _Dialog:
    def notification(self, heading, message, icon='info', time=5000, sound=True):
        STATE.notifications.append((message, icon))

    def ok(self, heading, message):
        STATE.dialogs.append(('ok', message))
        return True

    def yesno(self, heading, message, *args, **kwargs):
        STATE.dialogs.append(('yesno', message))
        return STATE.yesno

    def textviewer(self, heading, text, usemono=False):
        STATE.dialogs.append(('textviewer', text))


class _Window:
    def __init__(self, window_id):
        pass

    def setProperty(self, key, value):
        STATE.properties[key] = value

    def getProperty(self, key):
        return STATE.properties.get(key, '')


class _ListItem:
    def __init__(self, label='', label2='', path='', offscreen=False):
        self.label, self.label2, self.art, self.properties = label, label2, {}, {}

    def setArt(self, art):
        self.art.update(art)

    def setProperty(self, key, value):
        self.properties[key] = value


xbmcgui.Dialog, xbmcgui.Window, xbmcgui.ListItem = _Dialog, _Window, _ListItem

# ---------- xbmcplugin ----------

xbmcplugin = types.ModuleType('xbmcplugin')
xbmcplugin.addDirectoryItem = lambda handle, url, listitem, isFolder=False, totalItems=0: STATE.items.append(listitem)
xbmcplugin.setContent = lambda handle, content: None
xbmcplugin.endOfDirectory = lambda handle, succeeded=True, updateListing=False, cacheToDisc=True: None

sys.modules.update({'xbmc': xbmc, 'xbmcaddon': xbmcaddon, 'xbmcvfs': xbmcvfs, 'xbmcgui': xbmcgui,
                    'xbmcplugin': xbmcplugin})

# service.nconnect als Wurzel für „from resources.lib import common“ (wie in Kodi: der Add-on-Ordner im Suchpfad)
SERVICE_DIR = os.path.join(KODI_DIR, 'service.nconnect')
if SERVICE_DIR not in sys.path:
    sys.path.insert(0, SERVICE_DIR)


def load(addon_id, filename, name):
    """Python-Datei eines Add-ons unter eigenem Modulnamen laden (mehrere Add-ons haben eine service.py)."""
    spec = importlib.util.spec_from_file_location(name, os.path.join(KODI_DIR, addon_id, filename))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module
