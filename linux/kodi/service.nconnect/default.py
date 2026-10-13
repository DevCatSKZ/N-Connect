# -*- coding: utf-8 -*-
"""
N-Connect-Menü (Programme → N-Connect): verbundene Controller mit Akku, Trennen, Anleitung, Diagnose, Protokoll.
Aufruf mit Argument: RunScript(service.nconnect,help|selftest|log|disconnect-all).
"""

import json
import subprocess
import sys

import xbmc
import xbmcgui

from resources.lib import common
from resources.lib.common import text

CHECK_NAMES = {'uinput': 30640, 'bluez': 30641, 'adapter': 30642, 'debugfs': 30643, 'settings': 30644}


def show_help():
    xbmcgui.Dialog().textviewer(text(30615), common.help_text())


def show_log():
    content = common.read_log_tail()
    xbmcgui.Dialog().textviewer(text(30629), content or text(30625), usemono=True)


def run_selftest():
    binary = common.binary_path()
    if binary is None:
        xbmcgui.Dialog().ok(text(30622), text(30022, common.machine()))
        return
    progress = xbmcgui.DialogProgress()
    progress.create(text(30622), text(30645))
    try:
        result = subprocess.run([binary, '--selftest', '--data', common.DATA_DIR], stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, env=common.environment(), timeout=60,
                                universal_newlines=True, encoding='utf-8', errors='replace')
        output = result.stdout
    except (OSError, subprocess.TimeoutExpired) as e:
        output = json.dumps({'check': 'start', 'ok': False, 'required': True, 'detail': str(e)})
    finally:
        progress.close()
    lines, passed = [], True
    for line in output.splitlines():
        try:
            entry = json.loads(line)
        except ValueError:
            lines.append(line)
            continue
        if 'result' in entry:
            passed = entry['result'] == 'ok'
            continue
        name = text(CHECK_NAMES[entry['check']]) if entry.get('check') in CHECK_NAMES else entry.get('check', '?')
        mark = '[COLOR lime][B]OK[/B][/COLOR]' if entry.get('ok') else (
            '[COLOR red][B]--[/B][/COLOR]' if entry.get('required') else '[COLOR orange][B]![/B][/COLOR]')
        lines.append('{0}  [B]{1}[/B][CR]     {2}'.format(mark, name, entry.get('detail', '')))
    header = text(30623) if passed else text(30624)
    xbmcgui.Dialog().textviewer(text(30622), header + '[CR][CR]' + '[CR]'.join(lines))


def player_details(player):
    rows = []
    for d in player.get('devices', []):
        rows.append('[B]{0}[/B]'.format(d.get('name')))
        rows.append('{0}: {1}'.format(text(30628), d.get('address')))
        if d.get('serial'):
            rows.append('{0}: {1}'.format(text(30626), d.get('serial')))
        if d.get('firmware'):
            rows.append('{0}: {1}'.format(text(30627), d.get('firmware')))
        if d.get('battery', -1) >= 0:
            rows.append(text(30601, d.get('battery')))
        rows.append(text(30603, int(d.get('rate', 0))))
        rows.append('')
    xbmcgui.Dialog().textviewer(text(30600, player.get('player'), player.get('name')), '[CR]'.join(rows))


def battery_line(player):
    parts = []
    if player.get('battery', -1) >= 0:
        parts.append(text(30601, player['battery']))
    if player.get('charging'):
        parts.append(text(30602))
    return ' · '.join(parts)


def item(label, label2='', icon=None):
    li = xbmcgui.ListItem(label, label2)
    li.setArt({'icon': icon or common.ICON, 'thumb': icon or common.ICON})
    return li


def menu():
    dialog = xbmcgui.Dialog()
    while True:
        status = common.read_status()
        entries = []  # (ListItem, Aktion)
        if status is not None and status.get('state') == 'starting':
            entries.append((item(text(30631)), ('refresh', None)))
            status = None
        elif status is None:
            entries.append((item(text(30612), text(30613), 'DefaultIconWarning.png'), ('restart', None)))
        elif status.get('bluetooth') == 'off':
            entries.append((item(text(30608), text(30609), 'DefaultIconWarning.png'), ('libreelec', None)))
        elif status.get('bluetooth') == 'missing':
            entries.append((item(text(30610), text(30611), 'DefaultIconWarning.png'), ('libreelec', None)))
        elif status.get('bluetooth') != 'on':
            entries.append((item(text(30630)), ('refresh', None)))
        if status is not None:
            players = status.get('players', [])
            for p in players:
                entries.append((item(text(30600, p.get('player'), p.get('name')), battery_line(p),
                                     common.player_icon(p)), ('player', p)))
            for g in status.get('gamepads', []):
                details = [text(30651) if g.get('connected') else text(30652)]
                if g.get('battery', -1) >= 0:
                    details.append(text(30601, g['battery']))
                entries.append((item(g.get('name'), ' · '.join(details),
                                     common.kind_icon('', g.get('name'))), ('gamepad', g)))
            connected = {d.get('address', '').lower() for p in players for d in p.get('devices', [])}
            for k in status.get('known', []):
                if k.get('address', '').lower() not in connected and not k.get('connected'):
                    name = common.short_name(k.get('kind'), k.get('name'))
                    entries.append((item('[COLOR gray]{0}[/COLOR]'.format(name), text(30656),
                                         common.offline_icon(common.kind_icon(k.get('kind'), name))), ('help', None)))
            for address in status.get('connecting', []):
                entries.append((item(text(30621), address), ('refresh', None)))
            for f in status.get('elsewhere', []):
                entries.append((item(text(30606, f.get('name')), text(30607), 'DefaultIconInfo.png'), ('help', None)))
            if not players and not status.get('gamepads') and not status.get('known') and status.get('bluetooth') == 'on':
                entries.append((item(text(30604), text(30605), 'DefaultIconInfo.png'), ('help', None)))
            if status.get('bluetooth') == 'on' and not status.get('backgroundScan', True):
                entries.append((item(text(30632), text(30633)), ('scan', None)))
            if players:
                entries.append((item(text(30614)), ('disconnect-all', None)))
        entries.append((item(text(30615)), ('help', None)))
        entries.append((item(text(30616)), ('selftest', None)))
        entries.append((item(text(30617)), ('log', None)))
        entries.append((item(text(30618)), ('settings', None)))

        choice = dialog.select('N-Connect', [e[0] for e in entries], useDetails=True)
        if choice < 0:
            return
        action, data = entries[choice][1]
        if action == 'player':
            sub = dialog.contextmenu([text(30619), text(30620)])
            if sub == 0:
                common.send_command('disconnect-{0}'.format(data.get('player')))
                xbmc.sleep(800)
            elif sub == 1:
                player_details(data)
        elif action == 'gamepad':
            options = ([text(30619)] if data.get('connected') else []) + [text(30653)]
            sub = dialog.contextmenu(options)
            if sub >= 0 and options[sub] == text(30619):
                common.send_command('gamepad-disconnect-{0}'.format(data.get('address')))
                xbmc.sleep(1500)
            elif sub >= 0 and dialog.yesno(text(30653), text(30654, data.get('name'))):
                common.send_command('gamepad-forget-{0}'.format(data.get('address')))
                xbmc.sleep(1500)
        elif action == 'scan':
            common.send_command('scan')
            xbmc.sleep(800)
        elif action == 'disconnect-all':
            common.send_command('disconnect-all')
            xbmc.sleep(800)
        elif action == 'restart':
            common.send_command('restart')
            xbmc.sleep(2000)
        elif action == 'libreelec':
            xbmc.executebuiltin('RunAddon(service.libreelec.settings)')
            return
        elif action == 'help':
            show_help()
        elif action == 'selftest':
            run_selftest()
        elif action == 'log':
            show_log()
        elif action == 'settings':
            common.ADDON.openSettings()
        elif action == 'refresh':
            xbmc.sleep(1000)


if __name__ == '__main__':
    arg = sys.argv[1] if len(sys.argv) > 1 else ''
    if arg == 'help':
        show_help()
    elif arg == 'selftest':
        run_selftest()
    elif arg == 'log':
        show_log()
    elif arg == 'disconnect-all':
        common.send_command('disconnect-all')
    else:
        menu()
