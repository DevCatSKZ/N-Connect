# -*- coding: utf-8 -*-
"""
plugin://service.nconnect/ – zeigt den aktuellen Controller-Stand als Liste (für das Kodi-Widget im Home-Menü:
Skin-Einstellungen > Home > Widgets > Add-ons > N-Connect). Liest status.json bei jedem Aufruf frisch; der Dienst
stößt bei jeder Änderung Container.Refresh an, die Anzeige ist damit immer aktuell.
Auswahl eines Eintrags öffnet das N-Connect-Menü.
"""

import sys
from urllib.parse import parse_qsl, urlencode

import xbmcgui
import xbmcplugin

from resources.lib import common
from resources.lib.common import text


def add(label, label2='', icon=None, action='menu'):
    url = '{0}?{1}'.format(sys.argv[0], urlencode({'action': action}))
    li = xbmcgui.ListItem(label=label, label2=label2)
    li.setArt({'icon': icon or common.ICON, 'thumb': icon or common.ICON})
    li.setProperty('IsPlayable', 'false')
    xbmcplugin.addDirectoryItem(int(sys.argv[1]), url, li, isFolder=False)


def subtitle_battery(player):
    parts = []
    if player.get('battery', -1) >= 0:
        parts.append(text(30601, player['battery']))
    if player.get('charging'):
        parts.append(text(30602))
    return ' · '.join(parts)


def listing():
    status = common.read_status()
    if status is None:
        add(text(30612), text(30613), 'DefaultIconWarning.png')
    else:
        bluetooth = status.get('bluetooth')
        if bluetooth == 'off':
            add(text(30608), text(30609), 'DefaultIconWarning.png')
        elif bluetooth == 'missing':
            add(text(30610), text(30611), 'DefaultIconWarning.png')
        elif status.get('state') == 'starting':
            add(text(30631))
        else:
            for p in status.get('players', []):
                devices = p.get('devices', [])
                name = common.short_name(devices[0].get('kind'), p.get('name')) if len(devices) == 1 else p.get('name')
                add(text(30600, p.get('player'), name), subtitle_battery(p), common.player_icon(p))
                # Bei Paaren (Joy-Con L+R) jedes Gerät einzeln mit eigenem Icon einrücken.
                if len(devices) > 1:
                    for d in devices:
                        sub = []
                        if d.get('battery', -1) >= 0:
                            sub.append(text(30601, d['battery']))
                        add('  ' + common.short_name(d.get('kind'), d.get('name')), ' · '.join(sub),
                            common.kind_icon(d.get('kind'), d.get('name')))
            for g in status.get('gamepads', []):
                details = [text(30651) if g.get('connected') else text(30652)]
                if g.get('battery', -1) >= 0:
                    details.append(text(30601, g['battery']))
                add(g.get('name'), ' · '.join(details), common.kind_icon('', g.get('name')))
            if status.get('connecting'):
                add(text(30621), '', 'DefaultIconInfo.png')
            for f in status.get('elsewhere', []):
                add(text(30606, f.get('name')), text(30607), 'DefaultIconInfo.png')
            if not status.get('players') and not status.get('gamepads') and bluetooth == 'on':
                add(text(30604), text(30605), common.ICON)
    xbmcplugin.setContent(int(sys.argv[1]), 'files')
    xbmcplugin.endOfDirectory(int(sys.argv[1]), cacheToDisc=False)


def main():
    action = dict(parse_qsl(sys.argv[2].lstrip('?'))).get('action', 'list') if len(sys.argv) > 2 else 'list'
    if action == 'disconnect-all':
        common.send_command('disconnect-all')
        return
    if action != 'list':
        # Einträge im Widget öffnen immer das N-Connect-Menü.
        import xbmc
        xbmc.executebuiltin('RunScript(service.nconnect)')
        return
    listing()


if __name__ == '__main__':
    main()
