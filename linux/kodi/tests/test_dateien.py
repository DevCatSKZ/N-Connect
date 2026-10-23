# -*- coding: utf-8 -*-
"""Dateien der Add-ons: XML wohlgeformt, Beschreibungen und Sprachdateien vollständig, generate.py aktuell,
Versionen stimmig, Index-Werkzeug des kodi-repo."""
import contextlib
import hashlib
import importlib.util
import io
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

import kodi_stubs

KODI_DIR = kodi_stubs.KODI_DIR
ADDONS = ('service.skzconnect', 'plugin.skzconnect.status', 'script.skzconnect.bundle', 'repository.devcatskz')
# 12 Sprachen wie unter Windows; Portugiesisch zweimal (pt_PT, pt_BR)
LOCALES = ('de_DE', 'en_GB', 'es_ES', 'fr_FR', 'it_IT', 'pt_PT', 'pt_BR', 'nl_NL', 'pl_PL', 'ru_RU', 'ja_JP', 'zh_CN',
           'ko_KR')
PLACEHOLDER = re.compile(r'\{\d\}')


def addon_xml(addon_id):
    return ET.parse(os.path.join(KODI_DIR, addon_id, 'addon.xml')).getroot()


def po_entries(path):
    with open(path, encoding='utf-8') as f:
        return {int(m.group(1)): (m.group(2), m.group(3)) for m in re.finditer(
            r'msgctxt "#(\d+)"\nmsgid "(.*)"\nmsgstr "(.*)"', f.read())}


class AddonXmlTest(unittest.TestCase):
    def test_wohlgeformt(self):
        for addon_id in ADDONS:
            self.assertEqual(addon_xml(addon_id).get('id'), addon_id)
        ET.parse(os.path.join(KODI_DIR, 'service.skzconnect', 'resources', 'settings.xml'))

    def test_beschreibung_in_allen_sprachen(self):
        for addon_id in ('service.skzconnect', 'plugin.skzconnect.status', 'script.skzconnect.bundle'):
            meta = addon_xml(addon_id).find("extension[@point='xbmc.addon.metadata']")
            for tag in ('summary', 'description'):
                found = {e.get('lang'): (e.text or '').strip() for e in meta.findall(tag)}
                self.assertEqual(sorted(found), sorted(LOCALES), '{0} {1}'.format(addon_id, tag))
                self.assertTrue(all(found.values()), '{0} {1}'.format(addon_id, tag))

    def test_anbieter_und_versionen(self):
        versions = {a: addon_xml(a).get('version') for a in ADDONS}
        for addon_id in ADDONS:
            self.assertEqual(addon_xml(addon_id).get('provider-name'), 'devcatskz', addon_id)
        self.assertEqual(versions, {'service.skzconnect': '0.4.6', 'plugin.skzconnect.status': '1.1.3',
                                    'script.skzconnect.bundle': '1.0.4', 'repository.devcatskz': '1.0.4'})
        # Programmversion im csproj = service.skzconnect-Version (das Build-Skript setzt sie beim Veröffentlichen)
        with open(os.path.join(KODI_DIR, '..', 'SkzConnect.Linux', 'SkzConnect.Linux.csproj'), encoding='utf-8') as f:
            self.assertIn('<Version>{0}</Version>'.format(versions['service.skzconnect']), f.read())


class LanguageFilesTest(unittest.TestCase):
    def check_addon(self, addon_id):
        folder = os.path.join(KODI_DIR, addon_id, 'resources', 'language')
        source = po_entries(os.path.join(folder, 'resource.language.en_gb', 'strings.po'))
        self.assertTrue(source)
        languages = sorted(os.listdir(folder))
        self.assertEqual(languages, sorted('resource.language.' + l.lower() for l in LOCALES), addon_id)
        for language in languages:
            entries = po_entries(os.path.join(folder, language, 'strings.po'))
            self.assertEqual(sorted(entries), sorted(source), language)
            if language.endswith('en_gb'):
                continue
            for string_id, (msgid, msgstr) in entries.items():
                self.assertTrue(msgstr, '{0} #{1} leer'.format(language, string_id))
                self.assertEqual(sorted(PLACEHOLDER.findall(msgid)), sorted(PLACEHOLDER.findall(msgstr)),
                                 '{0} #{1} Platzhalter'.format(language, string_id))
        return source

    def used_ids(self, addon_id):
        """Alle String-IDs (30000–32999) im Python-Code und in settings.xml eines Add-ons."""
        ids = set()
        root = os.path.join(KODI_DIR, addon_id)
        for folder, _, files in os.walk(root):
            for name in files:
                if name.endswith('.py'):
                    with open(os.path.join(folder, name), encoding='utf-8') as f:
                        ids |= {int(n) for n in re.findall(r'\b(3[0-2]\d{3})\b', f.read())}
        settings = os.path.join(root, 'resources', 'settings.xml')
        if os.path.isfile(settings):
            with open(settings, encoding='utf-8') as f:
                ids |= {int(n) for n in re.findall(r'(?:label|help|heading)="(3[0-2]\d{3})"', f.read())}
        return ids

    def test_service(self):
        source = self.check_addon('service.skzconnect')
        self.assertEqual(self.used_ids('service.skzconnect') - set(source), set())

    def test_bundle(self):
        source = self.check_addon('script.skzconnect.bundle')
        self.assertEqual(self.used_ids('script.skzconnect.bundle') - set(source), set())

    def test_generate_py_ist_aktuell(self):
        """generate.py in einer Kopie laufen lassen – das Ergebnis muss den eingecheckten Dateien gleichen."""
        temp = tempfile.mkdtemp(prefix='skzconnect-i18n-')
        self.addCleanup(shutil.rmtree, temp, True)
        shutil.copytree(os.path.join(KODI_DIR, 'i18n'), os.path.join(temp, 'i18n'),
                        ignore=shutil.ignore_patterns('__pycache__'))
        for addon_id in ('service.skzconnect', 'script.skzconnect.bundle'):
            shutil.copytree(os.path.join(KODI_DIR, addon_id, 'resources', 'language'),
                            os.path.join(temp, addon_id, 'resources', 'language'))
        subprocess.run([sys.executable, '-B', os.path.join(temp, 'i18n', 'generate.py')], check=True,
                       stdout=subprocess.DEVNULL)
        for addon_id in ('service.skzconnect', 'script.skzconnect.bundle'):
            folder = os.path.join(addon_id, 'resources', 'language')
            for language in os.listdir(os.path.join(temp, folder)):
                path = os.path.join(folder, language, 'strings.po')
                with open(os.path.join(KODI_DIR, path), 'rb') as a, open(os.path.join(temp, path), 'rb') as b:
                    # Zeilenenden egal (Windows-Checkout mit autocrlf)
                    self.assertEqual(a.read().replace(b'\r\n', b'\n'), b.read().replace(b'\r\n', b'\n'), path)


class BuildRepoTest(unittest.TestCase):
    """tools/build_repo.py im kodi-repo: Index, Prüfsummen, Verzeichnislisten, nur die zwei neuesten Versionen."""

    def setUp(self):
        spec = importlib.util.spec_from_file_location('build_repo', os.path.join(KODI_DIR, 'repo-tools', 'build_repo.py'))
        self.build_repo = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.build_repo)
        self.repo = tempfile.mkdtemp(prefix='skzconnect-kodi-repo-')
        self.addCleanup(shutil.rmtree, self.repo, True)

    def add_zip(self, addon_id, version):
        folder = os.path.join(self.repo, addon_id)
        os.makedirs(folder, exist_ok=True)
        with zipfile.ZipFile(os.path.join(folder, '{0}-{1}.zip'.format(addon_id, version)), 'w') as z:
            z.writestr(addon_id + '/addon.xml', '<?xml version="1.0" encoding="UTF-8"?>\n<addon id="{0}" version="{1}">'
                       '<extension point="xbmc.addon.metadata"><assets><icon>icon.png</icon></assets></extension>'
                       '</addon>'.format(addon_id, version))
            z.writestr(addon_id + '/icon.png', b'PNG' + version.encode())

    def test_index(self):
        for version in ('0.4.2', '0.4.10', '0.4.3'):
            self.add_zip('service.skzconnect', version)
        self.add_zip('plugin.skzconnect.status', '1.1.1')
        os.makedirs(os.path.join(self.repo, 'downloads'))
        with open(os.path.join(self.repo, 'repository.devcatskz.zip'), 'wb') as f:
            f.write(b'zip')
        with contextlib.redirect_stdout(io.StringIO()):
            self.build_repo.main(pathlib.Path(self.repo))

        service = os.path.join(self.repo, 'service.skzconnect')
        self.assertEqual(sorted(n for n in os.listdir(service) if n.endswith('.zip')),
                         ['service.skzconnect-0.4.10.zip', 'service.skzconnect-0.4.3.zip'])  # 0.4.2 entfernt (KEEP = 2)
        self.assertFalse(os.path.exists(os.path.join(service, 'service.skzconnect-0.4.2.zip.sha256')))
        with open(os.path.join(service, 'service.skzconnect-0.4.10.zip'), 'rb') as z, \
                open(os.path.join(service, 'service.skzconnect-0.4.10.zip.sha256'), encoding='utf-8') as s:
            self.assertEqual(s.read(), hashlib.sha256(z.read()).hexdigest())
        with open(os.path.join(service, 'icon.png'), 'rb') as f:
            self.assertEqual(f.read(), b'PNG0.4.10')  # Symbol aus der neuesten Version

        with open(os.path.join(self.repo, 'addons.xml'), 'rb') as f:
            addons = f.read()
        with open(os.path.join(self.repo, 'addons.xml.md5'), encoding='utf-8') as f:
            self.assertEqual(f.read(), hashlib.md5(addons).hexdigest())
        versions = {a.get('id'): a.get('version') for a in ET.fromstring(addons)}
        self.assertEqual(versions, {'service.skzconnect': '0.4.10', 'plugin.skzconnect.status': '1.1.1'})

        # Verzeichnislisten für Kodis Dateimanager-Quelle: <a href="X">X</a>, neueste zuerst
        with open(os.path.join(service, 'index.html'), encoding='utf-8') as f:
            links = re.findall(r'<a href="([^"]+)">([^<]+)</a>', f.read())
        self.assertEqual(links, [('service.skzconnect-0.4.10.zip',) * 2, ('service.skzconnect-0.4.3.zip',) * 2])
        with open(os.path.join(self.repo, 'index.html'), encoding='utf-8') as f:
            root_links = [href for href, _ in re.findall(r'<a href="([^"]+)">([^<]+)</a>', f.read())]
        self.assertEqual(root_links, ['repository.devcatskz.zip', 'plugin.skzconnect.status/', 'service.skzconnect/'])


if __name__ == '__main__':
    unittest.main()
