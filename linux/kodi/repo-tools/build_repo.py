# -*- coding: utf-8 -*-
"""
Baut den Index eines Kodi-Repositorys (DevCatSKZ/kodi-repo) aus den Add-on-ZIPs im Ordner.

Aufbau:  <repo>/<addon-id>/<addon-id>-<version>.zip   (beliebig viele Add-ons und Versionen)
Erzeugt: addons.xml, addons.xml.md5, je ZIP eine .sha256, je Add-on icon.png (aus dem neuesten ZIP).
Ältere Versionen werden bis auf KEEP Stück entfernt, damit das Repository klein bleibt.

Neues Plugin veröffentlichen:  ZIP nach <repo>/<id>/ kopieren → python tools/build_repo.py → committen.
"""
import hashlib
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

KEEP = 2


def version_key(v):
    return [int(p) if p.isdigit() else p for p in re.split(r'[.\-+~]', v)]


def read_addon_xml(zip_path, addon_id):
    with zipfile.ZipFile(zip_path) as z:
        return z.read('{0}/addon.xml'.format(addon_id)).decode('utf-8')


def extract_icon(zip_path, addon_id, xml_text, target):
    icon = ET.fromstring(xml_text).find('.//assets/icon')
    if icon is None or not icon.text:
        return
    with zipfile.ZipFile(zip_path) as z:
        try:
            target.write_bytes(z.read('{0}/{1}'.format(addon_id, icon.text.strip())))
        except KeyError:
            pass


def main(repo):
    entries = []
    for folder in sorted(p for p in repo.iterdir() if p.is_dir() and not p.name.startswith(('.', 'tools'))):
        zips = []
        for z in folder.glob('{0}-*.zip'.format(folder.name)):
            version = z.stem[len(folder.name) + 1:]
            zips.append((version_key(version), version, z))
        if not zips:
            continue
        zips.sort()
        for _, _, old in zips[:-KEEP]:
            old.unlink()
            pathlib.Path(str(old) + '.sha256').unlink(missing_ok=True)
            print('entfernt:', old.name)
        for _, _, z in zips[-KEEP:]:
            pathlib.Path(str(z) + '.sha256').write_text(hashlib.sha256(z.read_bytes()).hexdigest(), encoding='utf-8')
        _, version, newest = zips[-1]
        xml_text = read_addon_xml(newest, folder.name)
        extract_icon(newest, folder.name, xml_text, folder / 'icon.png')
        entries.append(re.sub(r'^\s*<\?xml[^>]*\?>\s*', '', xml_text).strip())
        print('{0} {1}'.format(folder.name, version))
    addons = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<addons>\n' + '\n'.join(entries) + '\n</addons>\n'
    ET.fromstring(addons.encode('utf-8'))  # muss gültiges XML sein
    (repo / 'addons.xml').write_text(addons, encoding='utf-8', newline='\n')
    (repo / 'addons.xml.md5').write_text(hashlib.md5(addons.encode('utf-8')).hexdigest(), encoding='utf-8')
    print('addons.xml mit {0} Add-ons'.format(len(entries)))


if __name__ == '__main__':
    main(pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parent.parent))
