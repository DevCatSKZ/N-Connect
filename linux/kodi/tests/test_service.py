# -*- coding: utf-8 -*-
"""service.nconnect/service.py: Programmstart mit Rückfall, Warten beim Beenden, Meldungen zum Bluetooth-Zustand."""
import io
import json
import os
import subprocess
import unittest
from unittest import mock

import kodi_stubs
from kodi_stubs import STATE
from resources.lib import common
from resources.lib.common import text

service = kodi_stubs.load('service.nconnect', 'service.py', 'nconnect_service')


class FakeProcess:
    """Wie subprocess.Popen: läuft, bis terminate/kill kommt."""

    def __init__(self, pid=4321, hangs=False):
        self.pid = pid
        self.stdin = io.StringIO()
        self.stdout = io.StringIO('')
        self.returncode = None
        self.hangs = hangs  # reagiert nicht auf SIGTERM
        self.calls = []

    def poll(self):
        return self.returncode

    def terminate(self):
        self.calls.append('terminate')
        if not self.hangs:
            self.returncode = 0

    def kill(self):
        self.calls.append('kill')
        self.returncode = -9

    def wait(self, timeout=None):
        self.calls.append('wait')
        if self.returncode is None:
            raise subprocess.TimeoutExpired('nconnect', timeout)
        return self.returncode


class StartTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()
        common.ensure_data_dir()
        self.addCleanup(common.clear_pid)
        patcher = mock.patch.multiple(common, stop_leftover=mock.DEFAULT, machine=mock.Mock(return_value='aarch64'))
        patcher.start()
        self.addCleanup(patcher.stop)
        self.svc = service.Service()
        self.svc.load_uinput = mock.Mock()

    def start(self, candidates, popen):
        with mock.patch.object(common, 'binary_candidates', return_value=candidates), \
                mock.patch.object(service.subprocess, 'Popen', popen):
            return self.svc.start()

    def test_rueckfall_auf_die_andere_variante(self):
        process = FakeProcess()
        popen = mock.Mock(side_effect=[OSError(8, 'Exec format error'), process])
        self.assertTrue(self.start(['/bin/linux-arm64/nconnect', '/bin/linux-arm/nconnect'], popen))
        self.assertIs(self.svc.process, process)
        self.assertEqual(popen.call_args_list[1].args[0], ['/bin/linux-arm/nconnect', '--data', common.DATA_DIR, '--kodi'])
        with open(common.PID_FILE) as f:
            self.assertEqual(f.read(), '4321')
        with open(common.STATUS_JSON) as f:
            self.assertEqual(json.load(f), {'state': 'starting'})
        self.assertEqual(STATE.notifications, [])

    def test_adapter_wird_uebergeben(self):
        self.svc.adapter = 'hci1'
        popen = mock.Mock(return_value=FakeProcess())
        self.assertTrue(self.start(['/bin/linux-x64/nconnect'], popen))
        self.assertEqual(popen.call_args.args[0][-2:], ['--adapter', 'hci1'])

    def test_kein_programm_meldung_statt_absturz(self):
        self.assertFalse(self.start([], mock.Mock()))
        self.assertEqual(STATE.notifications, [(text(30022, 'aarch64'), 'error')])

    def test_keine_variante_startet(self):
        popen = mock.Mock(side_effect=OSError(8, 'Exec format error'))
        self.assertFalse(self.start(['/bin/linux-arm64/nconnect', '/bin/linux-arm/nconnect'], popen))
        self.assertEqual(popen.call_count, 2)
        self.assertEqual(STATE.notifications, [(text(30022, 'aarch64'), 'error')])
        self.assertIsNone(self.svc.process)


class StopTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()
        common.ensure_data_dir()
        self.svc = service.Service()

    def test_wartet_auf_das_ende(self):
        process = FakeProcess()
        self.svc.process = process
        common.write_pid(process.pid)
        self.svc.stop()
        self.assertEqual(process.calls, ['terminate', 'wait'])
        self.assertIsNone(self.svc.process)
        self.assertTrue(self.svc.stop_requested)  # Hauptschleife: gewollter Neustart, kein Absturz
        self.assertFalse(os.path.exists(common.PID_FILE))

    def test_haengendes_programm_wird_beendet(self):
        process = FakeProcess(hangs=True)
        self.svc.process = process
        self.svc.stop()
        self.assertEqual(process.calls, ['terminate', 'wait', 'kill', 'wait'])
        self.assertIsNone(self.svc.process)

    def test_ohne_programm(self):
        self.svc.stop()
        self.assertIsNone(self.svc.process)


class EventTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()
        self.svc = service.Service()

    def test_bluetooth_zustand_einmal_melden(self):
        for state in ('no_le', 'no_le', 'off', 'missing', 'on'):
            self.svc.on_event({'event': 'bluetooth', 'state': state})
        self.assertEqual(STATE.notifications, [(text(30019), 'error'), (text(30015), 'error'), (text(30016), 'error')])
        self.assertTrue(text(30019))

    def test_befehle_aus_dem_menue(self):
        self.svc.send = mock.Mock()
        self.svc.onNotification('service.nconnect', 'Other.gamepad-forget-AA:BB:CC:DD:EE:FF', '')
        self.svc.onNotification('service.nconnect', 'Other.disconnect-2', '')
        self.svc.onNotification('anderes.addon', 'Other.disconnect-all', '')
        self.assertEqual([c.args[0] for c in self.svc.send.call_args_list],
                         ['gamepad-forget AA:BB:CC:DD:EE:FF', 'disconnect 2'])


if __name__ == '__main__':
    unittest.main()
