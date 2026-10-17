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
MENU_LABEL2 = 'Controller'
WIDGET_PATH = ('plugin://plugin.nconnect.status/?reload='
               '$INFO[Window(Home).Property(widgetreload)]'
               '$INFO[Window(Home).Property(widgetreload2)]'
               '$INFO[Window(Home).Property(NConnect.Reload)]')
ICON_PATH = os.path.join(ADDONS_DIR, 'service.nconnect', 'resources', 'icon.png')


def widget_props():
    thumb = ICON_PATH.replace('\\', '/')
    thumb = 'image://{0}/'.format(
        thumb.replace('/', '%2f').replace(':', '%3a'))
    return [
        ('widget', 'Addon'),
        ('widgetName', 'N-Connect - Controller'),
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


def shortcut_exists():
    try:
        return 'service.nconnect' in open(MENU_XML, encoding='utf-8').read()
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
        open(MENU_XML, 'w', encoding='utf-8').write(root)
    xml = open(MENU_XML, encoding='utf-8').read()
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
                '\t\t</shortcut>\n').format(MENU_LABEL, MENU_LABEL2, thumb)
        marker = '<defaultID>settings</defaultID>'
        pos = xml.find(marker)
        if pos > 0:
            pos = xml.rfind('<shortcut>', 0, pos)
            xml = xml[:pos] + item + xml[pos:]
        else:
            xml = xml.replace('</shortcuts>', item + '</shortcuts>')
        open(MENU_XML, 'w', encoding='utf-8').write(xml)
        changed = True
        log('Menueeintrag hinzugefuegt')

    # --- Widget-Eigenschaften ---
    try:
        data = json.load(open(PROPS_FILE, encoding='utf-8'))
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
        json.dump(data, open(PROPS_FILE, 'w', encoding='utf-8'), indent=4)
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
            dialog.ok('N-Connect Bundle',
                      'Installiert: {0}\n\nKodi neu starten, damit die '
                      'Addons registriert werden.'.format(', '.join(installed)))
        else:
            msg = 'Alle Addons bereits installiert.'
            if skin_done:
                msg += '\nSkin-Integration aktualisiert.'
            dialog.ok('N-Connect Bundle', msg)
        if installed or skin_done:
            if dialog.yesno('N-Connect Bundle', 'Kodi jetzt neu starten?'):
                xbmc.executebuiltin('RestartApp')
    return installed, skin_done
