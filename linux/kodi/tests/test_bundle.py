# -*- coding: utf-8 -*-
"""script.skzconnect.bundle: neueste mitgelieferte ZIP, Installation, Dialoge und Skin-Einträge in Kodis Sprache."""
import json
import os
import shutil
import sys
import unittest
import zipfile
from unittest import mock

import kodi_stubs
from kodi_stubs import STATE

kodi_stubs.reset('script.skzconnect.bundle')
installer = kodi_stubs.load('script.skzconnect.bundle', 'installer.py', 'installer')  # service.py importiert „installer“


def text(string_id, *args):
    return installer.text(string_id, *args)


class BundleTest(unittest.TestCase):
    def setUp(self):
        kodi_stubs.reset('script.skzconnect.bundle')
        self.data = os.path.join(kodi_stubs.ROOT, 'bundle-data')
        self.addons = os.path.join(kodi_stubs.ROOT, 'bundle-addons')
        for folder in (self.data, self.addons):
            shutil.rmtree(folder, ignore_errors=True)
            os.makedirs(folder)
        for name in ('service.skzconnect-0.4.3.zip', 'service.skzconnect-0.4.10.zip', 'plugin.skzconnect.status-1.1.1.zip'):
            addon_id = name.rsplit('-', 1)[0]
            with zipfile.ZipFile(os.path.join(self.data, name), 'w') as z:
                z.writestr(addon_id + '/addon.xml', '<addon id="{0}" version="{1}"/>'.format(addon_id, name))
        for name, value in (('DATA_DIR', self.data), ('ADDONS_DIR', self.addons)):
            patcher = mock.patch.object(installer, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)

    def test_neueste_zip_nach_versionsnummer(self):
        self.assertEqual(os.path.basename(installer.bundled_zip('service.skzconnect')), 'service.skzconnect-0.4.10.zip')
        self.assertEqual(os.path.basename(installer.bundled_zip('plugin.skzconnect.status')),
                         'plugin.skzconnect.status-1.1.1.zip')
        self.assertIsNone(installer.bundled_zip('script.unbekannt'))
        with mock.patch.object(installer, 'DATA_DIR', os.path.join(self.data, 'fehlt')):
            self.assertIsNone(installer.bundled_zip('service.skzconnect'))

    def test_installiert_nur_fehlende(self):
        self.assertEqual(installer.install_addons(), ['service.skzconnect', 'plugin.skzconnect.status'])
        with open(os.path.join(self.addons, 'service.skzconnect', 'addon.xml')) as f:
            self.assertIn('0.4.10', f.read())
        self.assertEqual(installer.install_addons(), [])

    def test_dialoge_in_kodis_sprache(self):
        STATE.language = 'de_de'
        STATE.yesno = True
        installer.run(interactive=True)
        self.assertEqual(STATE.dialogs[0], ('ok', 'Installiert: service.skzconnect, plugin.skzconnect.status\n\n'
                                                  'Kodi neu starten, damit die Add-ons registriert werden.'))
        self.assertEqual(STATE.dialogs[1], ('yesno', 'Kodi jetzt neu starten?'))
        self.assertEqual(STATE.builtins, ['RestartApp'])

    def test_schon_installiert(self):
        installer.install_addons()
        installer.run(interactive=True)
        self.assertEqual(STATE.dialogs, [('ok', text(32002))])
        self.assertEqual(STATE.builtins, [])

    def test_beim_kodi_start_einmal(self):
        flag = os.path.join(kodi_stubs.translate_path('special://profile/addon_data/script.skzconnect.bundle/'),
                            'installed.flag')
        self.addCleanup(lambda: os.path.exists(flag) and os.remove(flag))
        # service.py läuft beim Import (wie in Kodi) und setzt seinen Ordner vor den Suchpfad – danach zurücksetzen
        with mock.patch.object(sys, 'path', list(sys.path)):
            kodi_stubs.load('script.skzconnect.bundle', 'service.py', 'bundle_service')
            self.assertEqual(STATE.dialogs, [('yesno', text(32000, 'service.skzconnect, plugin.skzconnect.status') + '\n' +
                                              text(32004))])
            kodi_stubs.load('script.skzconnect.bundle', 'service.py', 'bundle_service')  # zweiter Start: Merker da
        self.assertEqual(len(STATE.dialogs), 1)
        self.assertTrue(os.path.exists(flag))

    def test_skin_eintrag_uebersetzt(self):
        shortcuts = os.path.join(kodi_stubs.ROOT, 'shortcuts')
        shutil.rmtree(shortcuts, ignore_errors=True)
        os.makedirs(shortcuts)
        STATE.language = 'fr_fr'
        with mock.patch.multiple(installer, SHORTCUTS_DIR=shortcuts,
                                 MENU_XML=os.path.join(shortcuts, 'mainmenu.DATA.xml'),
                                 PROPS_FILE=os.path.join(shortcuts, 'skin.properties')):
            self.assertTrue(installer.integrate_silvo())
            self.assertFalse(installer.integrate_silvo())  # schon eingetragen
            with open(installer.MENU_XML, encoding='utf-8') as f:
                self.assertIn('<label2>Manettes</label2>', f.read())
            with open(installer.PROPS_FILE, encoding='utf-8') as f:
                props = {p[2]: p[3] for p in json.load(f)}
        self.assertEqual(props['widgetName'], 'SKZ Connect - Manettes')
        self.assertIn('SkzConnect.Reload', props['widgetPath'])

    def test_alle_texte_in_allen_sprachen(self):
        for language in ('en_gb', 'de_de', 'es_es', 'fr_fr', 'it_it', 'pt_pt', 'pt_br', 'nl_nl', 'pl_pl', 'ru_ru',
                         'ja_jp', 'zh_cn', 'ko_kr'):
            table = kodi_stubs.strings('script.skzconnect.bundle', language)
            self.assertEqual(sorted(table), list(range(32000, 32006)), language)
            self.assertTrue(all(table.values()), language)


if __name__ == '__main__':
    unittest.main()
