# -*- coding: utf-8 -*-
# plugin.skzconnect.status - Widget-Quelle für Skins (Pfad: plugin://plugin.skzconnect.status/).
# Die Anzeige (verbundene und bekannte Controller mit Icons, Akku, Ausgrauen) liegt im Dienst service.skzconnect
# (plugin.py) – hier nur die Weiterleitung, damit bestehende Widget-Einstellungen weiter funktionieren.
import sys

import xbmcaddon

sys.path.insert(0, xbmcaddon.Addon('service.skzconnect').getAddonInfo('path'))

import plugin  # noqa: E402  (aus service.skzconnect)

plugin.main()
