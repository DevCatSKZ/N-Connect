# -*- coding: utf-8 -*-
"""Läuft einmal beim Kodi-Start: installiert fehlende mitgelieferte Add-ons und endet dann."""
import os
import sys

import xbmcaddon
import xbmcvfs
import xbmcgui

ADDON_PATH = xbmcvfs.translatePath(
    xbmcaddon.Addon().getAddonInfo('path'))
sys.path.insert(0, ADDON_PATH)

import installer  # noqa: E402

FLAG = os.path.join(xbmcvfs.translatePath(
    xbmcaddon.Addon().getAddonInfo('profile')), 'installed.flag')


def main():
    if os.path.exists(FLAG):
        return
    installed, skin_done = installer.run(interactive=False)
    xbmcvfs.mkdirs(os.path.dirname(FLAG))
    open(FLAG, 'w').write('done')
    if installed:
        dialog = xbmcgui.Dialog()
        if dialog.yesno('N-Connect Bundle',
                        'Addons installiert: {0}\n'
                        'Kodi jetzt neu starten?'.format(
                            ', '.join(installed))):
            import xbmc
            xbmc.executebuiltin('RestartApp')


main()
