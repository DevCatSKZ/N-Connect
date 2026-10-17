# -*- coding: utf-8 -*-
"""Widget plugin://service.nconnect/: Hinweis passend zum Schreiber der status.json, Controllerliste immer sichtbar."""
import sys
import unittest
from unittest import mock

import kodi_stubs
from kodi_stubs import STATE
from resources.lib import common
from resources.lib.common import text

plugin = kodi_stubs.load('service.nconnect', 'plugin.py', 'nconnect_plugin')

PLAYER = {'player': 1, 'name': 'Pro Controller 2', 'battery': 80, 'charging': False,
          'devices': [{'address': 'AA:BB:CC:DD:EE:01', 'kind': 'Pro2', 'name': 'Pro Controller 2', 'battery': 80}]}
KNOWN = {'address': 'AA:BB:CC:DD:EE:02', 'kind': 'JoyCon2Left', 'name': 'Joy-Con 2 (L)', 'connected': False}


class WidgetTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()

    def listing(self, status, has_daemon=True, kodi='linux'):
        with mock.patch.object(common, 'read_status', return_value=status), \
                mock.patch.object(common, 'has_daemon', return_value=has_daemon), \
                mock.patch.object(common, 'kodi_platform', return_value=kodi), \
                mock.patch.object(sys, 'argv', ['plugin://service.nconnect/', '1', '']):
            plugin.main()
        return [(item.label, item.label2) for item in STATE.items]

    def test_bluetooth_aus_hinweis_vorneweg_controller_trotzdem(self):
        items = self.listing({'state': 'running', 'platform': 'linux', 'bluetooth': 'off', 'players': [PLAYER],
                              'known': [KNOWN]})
        self.assertEqual(items[0], (text(30608), text(30609)))
        self.assertEqual(items[1][0], 'Pro Controller 2')
        self.assertIn('Joy-Con 2 (L)', items[2][0])  # bekannt, nicht verbunden: ausgegraut
        self.assertEqual(items[2][1], text(30656))

    def test_windows_ohne_adapter(self):
        items = self.listing({'state': 'running', 'platform': 'windows', 'bluetooth': 'missing', 'players': [PLAYER]},
                             has_daemon=False, kodi='windows')
        self.assertEqual(items[0], (text(30610), text(30664)))
        self.assertEqual(len(items), 2)

    def test_adapter_ohne_le(self):
        items = self.listing({'state': 'running', 'platform': 'linux', 'bluetooth': 'no_le', 'players': []})
        self.assertEqual(items, [(text(30634), text(30635))])

    def test_startet(self):
        self.assertEqual(self.listing({'state': 'starting'}), [(text(30631), '')])

    def test_begleitmodus_ohne_status(self):
        self.assertEqual(self.listing(None, has_daemon=False, kodi='android'), [(text(30660), text(30668))])

    def test_dienst_laeuft_nicht(self):
        self.assertEqual(self.listing(None), [(text(30612), text(30613))])

    def test_bluetooth_an_ohne_controller(self):
        self.assertEqual(self.listing({'state': 'running', 'platform': 'linux', 'bluetooth': 'on'}),
                         [(text(30604), text(30605))])

    def test_klick_oeffnet_das_menue(self):
        with mock.patch.object(sys, 'argv', ['plugin://service.nconnect/', '1', '?action=menu']):
            plugin.main()
        self.assertEqual(STATE.builtins, ['RunScript(service.nconnect)'])


if __name__ == '__main__':
    unittest.main()
