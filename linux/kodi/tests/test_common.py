# -*- coding: utf-8 -*-
"""service.nconnect/resources/lib/common.py: Programmwahl, Plattform-Hinweise, status.json, PID-Datei."""
import json
import os
import time
import types
import unittest
from unittest import mock

import kodi_stubs
from kodi_stubs import STATE
from resources.lib import common
from resources.lib.common import text

ANDROID = 'System.Platform.Android'


class RuntimeIdsTest(unittest.TestCase):
    """Programm nach der Bitbreite von Kodis Python-Prozess, nicht nach dem Kernel."""

    def ids(self, machine, pointer_size):
        with mock.patch.object(common, 'machine', return_value=machine), \
                mock.patch.object(common.struct, 'calcsize', return_value=pointer_size):
            return common.runtime_ids()

    def test_pc(self):
        self.assertEqual(self.ids('x86_64', 8), ['linux-x64'])
        self.assertEqual(self.ids('amd64', 8), ['linux-x64'])

    def test_arm_64bit_system(self):
        self.assertEqual(self.ids('aarch64', 8), ['linux-arm64', 'linux-arm'])

    def test_64bit_kernel_mit_32bit_system_nimmt_arm(self):
        # z. B. Raspberry Pi OS 32 Bit auf Pi 4/5: Kernel meldet aarch64, das Userland ist 32 Bit
        self.assertEqual(self.ids('aarch64', 4), ['linux-arm', 'linux-arm64'])

    def test_arm_32bit(self):
        self.assertEqual(self.ids('armv7l', 4), ['linux-arm', 'linux-arm64'])

    def test_unbekannter_prozessor(self):
        self.assertEqual(self.ids('mips', 4), [])


class BinaryCandidatesTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()
        self.addon = os.path.join(kodi_stubs.ROOT, 'addon-bin')
        for rid in ('linux-arm64', 'linux-arm'):
            os.makedirs(os.path.join(self.addon, 'bin', rid), exist_ok=True)
            with open(os.path.join(self.addon, 'bin', rid, 'nconnect'), 'wb') as f:
                f.write(b'\x7fELF')

    def candidates(self, has_daemon=True, chmod=None):
        with mock.patch.object(common, 'ADDON_PATH', self.addon), \
                mock.patch.object(common, 'has_daemon', return_value=has_daemon), \
                mock.patch.object(common, 'runtime_ids', return_value=['linux-arm', 'linux-arm64']), \
                mock.patch.object(common.os, 'chmod', chmod or mock.Mock()):
            return common.binary_candidates()

    def test_reihenfolge_wie_runtime_ids_nur_vorhandene(self):
        found = self.candidates()
        self.assertEqual(found, [os.path.join(self.addon, 'bin', 'linux-arm', 'nconnect'),
                                 os.path.join(self.addon, 'bin', 'linux-arm64', 'nconnect')])

    def test_begleitmodus_ohne_programm(self):
        self.assertEqual(self.candidates(has_daemon=False), [])

    def test_chmod_fehler_bricht_nicht_ab(self):
        found = self.candidates(chmod=mock.Mock(side_effect=OSError(30, 'Read-only file system')))
        self.assertEqual(len(found), 2)
        self.assertTrue(any(level == 2 and 'Ausführungsrecht' in message for level, message in STATE.log))


class PlatformTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()

    def kodi_platform(self, os_name, system='Linux', conditions=()):
        STATE.conditions = set(conditions)
        with mock.patch.object(common.os, 'name', os_name), mock.patch('platform.system', return_value=system):
            return common.kodi_platform(), common.has_daemon()

    def test_android_meldet_linux_ist_aber_begleitmodus(self):
        self.assertEqual(self.kodi_platform('posix', 'Linux', [ANDROID]), ('android', False))

    def test_linux_hat_dienst(self):
        self.assertEqual(self.kodi_platform('posix', 'Linux'), ('linux', True))

    def test_windows(self):
        self.assertEqual(self.kodi_platform('nt', 'Windows'), ('windows', False))

    def test_macos(self):
        self.assertEqual(self.kodi_platform('posix', 'Darwin', ['System.Platform.OSX']), ('osx', False))


class BluetoothHintTest(unittest.TestCase):
    """Hinweis je Schreiber der status.json (Feld platform) und Bluetooth-Zustand; die Liste erscheint trotzdem."""

    def setUp(self):
        kodi_stubs.reset()

    def hint(self, platform, state, kodi='linux'):
        status = {'state': 'running', 'bluetooth': state}
        if platform is not None:
            status['platform'] = platform
        with mock.patch.object(common, 'kodi_platform', return_value=kodi):
            return common.bluetooth_hint(status)

    def test_kein_hinweis_wenn_bluetooth_an_oder_unbekannt(self):
        for platform in ('linux', 'windows', 'android', None):
            self.assertIsNone(self.hint(platform, 'on'))
            self.assertIsNone(self.hint(platform, None))

    def test_linux(self):
        self.assertEqual(self.hint('linux', 'off'), (text(30608), text(30609)))
        self.assertEqual(self.hint('linux', 'missing'), (text(30610), text(30611)))

    def test_windows(self):
        self.assertEqual(self.hint('windows', 'off'), (text(30608), text(30662)))
        self.assertEqual(self.hint('windows', 'missing'), (text(30610), text(30664)))

    def test_android(self):
        self.assertEqual(self.hint('android', 'off'), (text(30608), text(30663)))
        self.assertEqual(self.hint('android', 'missing'), (text(30610), text(30663)))

    def test_ohne_le_auf_jeder_plattform(self):
        for platform in ('linux', 'windows', 'android', None):
            self.assertEqual(self.hint(platform, 'no_le'), (text(30634), text(30635)))

    def test_ohne_platform_feld_entscheidet_kodis_plattform(self):
        self.assertEqual(self.hint(None, 'off', kodi='windows'), (text(30608), text(30662)))
        self.assertEqual(self.hint(None, 'off', kodi='android'), (text(30608), text(30663)))
        self.assertEqual(self.hint('macos', 'off', kodi='linux'), (text(30608), text(30609)))  # unbekannter Wert

    def test_alle_texte_vorhanden(self):
        for string_id in (30608, 30609, 30610, 30611, 30634, 30635, 30662, 30663, 30664):
            self.assertTrue(text(string_id), string_id)


class CompanionHintTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()

    def test_je_kodi_plattform(self):
        with mock.patch.object(common, 'kodi_platform', return_value='windows'):
            self.assertEqual(common.companion_hint(), (text(30660), text(30661)))
        with mock.patch.object(common, 'kodi_platform', return_value='android'):
            self.assertEqual(common.companion_hint(), (text(30660), text(30668)))
            self.assertIn('0.9.1', text(30668))  # Android-App schreibt status.json erst ab 0.9.1
        with mock.patch.object(common, 'kodi_platform', return_value='osx'):
            title, detail = common.companion_hint()
            self.assertEqual(title, text(30666))
            self.assertIn('macOS', detail)


class ReadStatusTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset()
        common.ensure_data_dir()
        self.addCleanup(lambda: os.path.exists(common.STATUS_JSON) and os.remove(common.STATUS_JSON))

    def write(self, status, age=0.0, path=None):
        path = path or common.STATUS_JSON
        with open(path, 'w', encoding='utf-8') as f:
            f.write(status if isinstance(status, str) else json.dumps(status))
        stamp = time.time() - age
        os.utime(path, (stamp, stamp))
        return path

    def test_keine_datei(self):
        self.assertIsNone(common.read_status())

    def test_laeuft(self):
        self.write({'state': 'running', 'platform': 'linux', 'bluetooth': 'on'}, age=2)
        self.assertEqual(common.read_status()['bluetooth'], 'on')

    def test_veraltet_heisst_laeuft_nicht(self):
        self.write({'state': 'running'}, age=20)  # Schreiber schreibt alle 5 s – 20 s alt = abgestürzt
        self.assertIsNone(common.read_status())

    def test_startet_darf_laenger_dauern(self):
        self.write({'state': 'starting'}, age=40)
        self.assertEqual(common.read_status(), {'state': 'starting'})
        self.write({'state': 'starting'}, age=70)
        self.assertIsNone(common.read_status())

    def test_beendet_und_kaputt(self):
        self.write({'state': 'stopped'})
        self.assertIsNone(common.read_status())
        self.write('{"state": "runn')
        self.assertIsNone(common.read_status())

    def test_mark_starting(self):
        common.mark_starting()
        self.assertEqual(common.read_status(), {'state': 'starting'})

    def test_android_liest_auch_den_dokumente_ordner(self):
        self.assertEqual(list(common._status_candidates()), [common.STATUS_JSON])
        STATE.conditions = {ANDROID}
        candidates = list(common._status_candidates())
        self.assertIn('/sdcard/Documents/N-Connect/status.json', candidates)

    def test_frischeste_datei_gewinnt(self):
        old = self.write({'state': 'running', 'platform': 'linux'}, age=10,
                         path=os.path.join(common.DATA_DIR, 'alt.json'))
        new = self.write({'state': 'running', 'platform': 'android'}, age=1,
                         path=os.path.join(common.DATA_DIR, 'neu.json'))
        self.addCleanup(os.remove, old)
        self.addCleanup(os.remove, new)
        with mock.patch.object(common, '_status_candidates', return_value=iter([old, new])):
            self.assertEqual(common.read_status()['platform'], 'android')


class PidFileTest(unittest.TestCase):
    """Rest einer abgestürzten Kodi-Sitzung: nur ein echtes N-Connect mit diesem Datenordner wird beendet."""

    def setUp(self):
        kodi_stubs.reset()
        common.ensure_data_dir()
        self.addCleanup(common.clear_pid)
        self.signal = types.SimpleNamespace(SIGTERM=15, SIGKILL=9)  # SIGKILL fehlt unter Windows

    def cmdline(self, *args):
        data = '\0'.join(args).encode('utf-8') + b'\0'
        return mock.patch.object(common, 'open', mock.mock_open(read_data=data), create=True)

    def test_pid_datei(self):
        common.write_pid(4321)
        with open(common.PID_FILE) as f:
            self.assertEqual(f.read(), '4321')
        common.clear_pid()
        self.assertFalse(os.path.exists(common.PID_FILE))
        common.clear_pid()  # fehlende Datei ist kein Fehler

    def test_is_nconnect_prueft_programm_und_datenordner(self):
        with self.cmdline('/storage/.kodi/addons/service.nconnect/bin/linux-arm64/nconnect', '--data', common.DATA_DIR,
                          '--kodi'):
            self.assertTrue(common._is_nconnect(1234))
        with self.cmdline('/usr/bin/kodi.bin', '--data', common.DATA_DIR):
            self.assertFalse(common._is_nconnect(1234))  # PID inzwischen an ein anderes Programm vergeben
        with self.cmdline('/opt/nconnect/nconnect', '--data', '/var/lib/nconnect'):
            self.assertFalse(common._is_nconnect(1234))  # anderes N-Connect (z. B. systemd) – nicht unseres
        with mock.patch.object(common, 'open', mock.Mock(side_effect=OSError(2, 'No such file')), create=True):
            self.assertFalse(common._is_nconnect(1234))  # läuft nicht mehr

    def stop_leftover(self, alive):
        kill = mock.Mock()
        with mock.patch.object(common, '_is_nconnect', side_effect=alive), \
                mock.patch.object(common.os, 'kill', kill), \
                mock.patch.object(common, 'signal', self.signal), \
                mock.patch.object(common.time, 'sleep'):
            common.stop_leftover()
        return [c.args for c in kill.call_args_list]

    def test_rest_endet_nach_sigterm(self):
        common.write_pid(4321)
        self.assertEqual(self.stop_leftover([True, True, False]), [(4321, 15)])
        self.assertFalse(os.path.exists(common.PID_FILE))

    def test_rest_haengt_sigkill(self):
        common.write_pid(4321)
        self.assertEqual(self.stop_leftover(lambda pid: True), [(4321, 15), (4321, 9)])
        self.assertFalse(os.path.exists(common.PID_FILE))

    def test_fremde_pid_wird_nicht_beendet(self):
        common.write_pid(4321)
        self.assertEqual(self.stop_leftover([False]), [])
        self.assertFalse(os.path.exists(common.PID_FILE))

    def test_keine_oder_kaputte_pid_datei(self):
        self.assertEqual(self.stop_leftover([]), [])
        with open(common.PID_FILE, 'w') as f:
            f.write('kaputt')
        self.assertEqual(self.stop_leftover([]), [])


if __name__ == '__main__':
    unittest.main()
