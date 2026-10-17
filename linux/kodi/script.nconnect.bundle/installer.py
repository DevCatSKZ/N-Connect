# -*- coding: utf-8 -*-
"""Installiert die mitgelieferten N-Connect-Add-ons und richtet (falls vorhanden) im Skin Aeon Nox Silvo den
Menüpunkt samt Controller-Widget ein."""
import json
import os
import re
import zipfile

import xbmc
import xbmcaddon
import xbmcgui
import xbmcvfs

ADDON = xbmcaddon.Addon()
ADDON_DIR = xbmcvfs.translatePath(ADDON.getAddonInfo('path'))
DATA_DIR = os.path.join(ADDON_DIR, 'resources', 'data')
ADDONS_DIR = xbmcvfs.translatePath('special://home/addons')
PROFILE_DIR = xbmcvfs.translatePath('special://profile')

# Mitgelieferte Add-ons. Ihre ZIPs (<id>-<version>.zip) legt linux/build-kodi-addon.ps1 beim Bauen jeweils in der
# aktuellen Version nach resources/data – darum hier keine festen Dateinamen.
BUNDLED = ['service.nconnect', 'plugin.nconnect.status']

SKIN_ID = 'skin.aeon.nox.silvo'
SHORTCUTS_DIR = os.path.join(PROFILE_DIR, 'addon_data', 'script.skinshortcuts')
MENU_XML = os.path.join(SHORTCUTS_DIR, 'mainmenu.DATA.xml')
PROPS_FILE = os.path.join(SHORTCUTS_DIR, SKIN_ID + '.properties')

MENU_LABEL = 'N-Connect'
WIDGET_PATH = ('plugin://plugin.nconnect.status/?reload='
               '$INFO[Window(Home).Property(widgetreload)]'
               '$INFO[Window(Home).Property(widgetreload2)]'
               '$INFO[Window(Home).Property(NConnect.Reload)]')
ICON_PATH = os.path.join(ADDONS_DIR, 'service.nconnect', 'resources', 'icon.png')


def text(string_id, *args):
    """Übersetzter Text aus resources/language (Kodis Sprache, sonst Englisch); {0} … werden ersetzt."""
    value = ADDON.getLocalizedString(string_id)
    return value.format(*args) if args else value


def widget_props():
    thumb = ICON_PATH.replace('\\', '/')
    thumb = 'image://{0}/'.format(
        thumb.replace('/', '%2f').replace(':', '%3a'))
    return [
        ('widget', 'Addon'),
        ('widgetName', '{0} - {1}'.format(MENU_LABEL, text(32005))),
        ('widgetType', 'videos'),
        ('widgetTarget', 'videos'),
        ('widgetPath', WIDGET_PATH),
        ('widgetStyle', 'Extended Compact Panel'),
        ('widgetPanelInfo', 'true'),
        ('widgetTitle', 'Panel'),
        ('widgetCase', 'Glass'),
        ('icon', 'DefaultShortcut.png'),
        ('thumb', thumb),
    ]


def log(msg):
    xbmc.log('[N-Connect Bundle] ' + msg, xbmc.LOGINFO)


def _version_key(name, addonid):
    version = name[len(addonid) + 1:-len('.zip')]
    return [int(p) if p.isdigit() else -1 for p in re.split(r'[.\-+~]', version)]


def bundled_zip(addonid):
    """Mitgelieferte ZIP eines Add-ons in resources/data (bei mehreren die neueste) oder None."""
    try:
        names = [n for n in os.listdir(DATA_DIR) if n.startswith(addonid + '-') and n.endswith('.zip')]
    except OSError:
        return None
    if not names:
        return None
    return os.path.join(DATA_DIR, max(names, key=lambda n: _version_key(n, addonid)))


def install_addons():
    """Mitgelieferte Add-on-ZIPs nach special://home/addons entpacken (nur fehlende)."""
    installed = []
    for addonid in BUNDLED:
        target = os.path.join(ADDONS_DIR, addonid)
        if os.path.isdir(target):
            continue
        src = bundled_zip(addonid)
        if src is None:
            log('fehlt: {0} in {1}'.format(addonid, DATA_DIR))
            continue
        with zipfile.ZipFile(src) as z:
            z.extractall(ADDONS_DIR)
        installed.append(addonid)
        log('installiert: {0} ({1})'.format(addonid, os.path.basename(src)))
    return installed


def _read(path):
    with open(path, encoding='utf-8') as f:
        return f.read()


def _write(path, content):
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)


def shortcut_exists():
    try:
        return 'service.nconnect' in _read(MENU_XML)
    except OSError:
        return False


def integrate_silvo():
    """N-Connect-Menüpunkt und Controller-Widget in Aeon Nox Silvo eintragen."""
    if not os.path.isdir(SHORTCUTS_DIR):
        return False
    changed = False

    # --- Menüpunkt in mainmenu.DATA.xml ---
    if not os.path.isfile(MENU_XML):
        root = '<shortcuts>\n</shortcuts>'
        _write(MENU_XML, root)
    xml = _read(MENU_XML)
    if 'service.nconnect' not in xml:
        thumb = ICON_PATH.replace('\\', '/')
        thumb = 'image://{0}/'.format(
            thumb.replace('/', '%2f').replace(':', '%3a'))
        item = ('\t<shortcut>\n'
                '\t\t<defaultID>nconnect</defaultID>\n'
                '\t\t<label>{0}</label>\n'
                '\t\t<label2>{1}</label2>\n'
                '\t\t<icon>DefaultShortcut.png</icon>\n'
                '\t\t<thumb>{2}</thumb>\n'
                '\t\t<action>RunScript(service.nconnect)</action>\n'
                '\t\t</shortcut>\n').format(MENU_LABEL, text(32005), thumb)
        marker = '<defaultID>settings</defaultID>'
        pos = xml.find(marker)
        if pos > 0:
            pos = xml.rfind('<shortcut>', 0, pos)
            xml = xml[:pos] + item + xml[pos:]
        else:
            xml = xml.replace('</shortcuts>', item + '</shortcuts>')
        _write(MENU_XML, xml)
        changed = True
        log('Menueeintrag hinzugefuegt')

    # --- Widget-Eigenschaften ---
    try:
        data = json.loads(_read(PROPS_FILE))
    except (OSError, ValueError):
        data = []
    have = {tuple(e[:3]) for e in data}
    for prop, value in widget_props():
        key = ('mainmenu', 'service.nconnect', prop)
        if key in have:
            continue
        data.append([key[0], key[1], prop, value])
        changed = True
    if changed:
        _write(PROPS_FILE, json.dumps(data, indent=4))
        log('Widget-Properties geschrieben')
    return changed


def run(interactive=True):
    installed = install_addons()
    skin_done = False
    if os.path.isdir(xbmcvfs.translatePath(
            'special://home/addons/' + SKIN_ID)):
        skin_done = integrate_silvo()

    if interactive:
        dialog = xbmcgui.Dialog()
        if installed:
            dialog.ok('N-Connect Bundle', text(32000, ', '.join(installed)) + '\n\n' + text(32001))
        else:
            msg = text(32002)
            if skin_done:
                msg += '\n' + text(32003)
            dialog.ok('N-Connect Bundle', msg)
        if installed or skin_done:
            if dialog.yesno('N-Connect Bundle', text(32004)):
                xbmc.executebuiltin('RestartApp')
    return installed, skin_done
