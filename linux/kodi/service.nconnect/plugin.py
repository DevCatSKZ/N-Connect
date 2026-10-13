# -*- coding: utf-8 -*-
"""
plugin://service.nconnect/ – zeigt den aktuellen Controller-Stand als Liste (für das Kodi-Widget im Home-Menü:
Skin-Einstellungen > Home > Widgets > Add-ons > N-Connect). Liest status.json bei jedem Aufruf frisch.

Aktualisierung: Der Dienst setzt bei jeder sichtbaren Änderung die Home-Eigenschaft NConnect.Reload. Skins, die
den Widget-Pfad mit  ?reload=$INFO[Window(Home).Property(NConnect.Reload)]  anlegen, laden das Widget dann von
selbst neu – auch wenn es nicht fokussiert ist. Bei anderen Skins lädt der Dienst die Liste neu, sobald sie fokussiert ist.

Angezeigt werden verbundene Controller (farbig) und bekannte, aber gerade nicht verbundene (ausgegraut).
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


def add_offline(name, kind):
    """Bekannter Controller ohne Verbindung: grauer Name, ausgegrautes Icon."""
    add('[COLOR gray]{0}[/COLOR]'.format(name), text(30656), common.offline_icon(common.kind_icon(kind, name)))


def battery_text(source):
    parts = []
    if source.get('battery', -1) >= 0:
        parts.append(text(30601, source['battery']))
    if source.get('charging'):
        parts.append(text(30602))
    return ' · '.join(parts) or text(30655)


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
            players = status.get('players', [])
            connected = {d.get('address', '').lower() for p in players for d in p.get('devices', [])}
            for p in players:
                devices = p.get('devices', [])
                name = common.short_name(devices[0].get('kind'), p.get('name')) if len(devices) == 1 else p.get('name')
                # Kachel-Beschriftung kurz halten (lange Namen werden abgeschnitten); Spielernummer und Akku in Zeile 2
                add(name, '{0} · {1}'.format(text(30600, p.get('player'), '').rstrip(': '), battery_text(p)),
                    common.player_icon(p))
                # Bei Paaren (Joy-Con L+R) jedes Gerät einzeln mit eigenem Icon.
                if len(devices) > 1:
                    for d in devices:
                        add(common.short_name(d.get('kind'), d.get('name')), battery_text(d),
                            common.kind_icon(d.get('kind'), d.get('name')))
            for g in status.get('gamepads', []):
                if g.get('connected'):
                    add(g.get('name'), battery_text(g), common.kind_icon('', g.get('name')))
            if status.get('connecting'):
                add(text(30621), '', 'DefaultIconInfo.png')
            # Bekannt, aber gerade nicht verbunden – ausgegraut
            for k in status.get('known', []):
                if k.get('address', '').lower() not in connected and not k.get('connected'):
                    add_offline(common.short_name(k.get('kind'), k.get('name')), k.get('kind'))
            for g in status.get('gamepads', []):
                if not g.get('connected'):
                    add_offline(g.get('name'), '')
            for f in status.get('elsewhere', []):
                add(text(30606, f.get('name')), text(30607), 'DefaultIconInfo.png')
            if not players and not status.get('gamepads') and not status.get('known') and bluetooth == 'on':
                add(text(30604), text(30605), common.ICON)
    common.log('Widget-Liste geladen')
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
