# -*- coding: utf-8 -*-
# plugin.nconnect.status - Widget-Quelle für Skins (Pfad: plugin://plugin.nconnect.status/).
# Die Anzeige (verbundene und bekannte Controller mit Icons, Akku, Ausgrauen) liegt im Dienst service.nconnect
# (plugin.py) – hier nur die Weiterleitung, damit bestehende Widget-Einstellungen weiter funktionieren.
import sys

import xbmcaddon

sys.path.insert(0, xbmcaddon.Addon('service.nconnect').getAddonInfo('path'))

import plugin  # noqa: E402  (aus service.nconnect)

plugin.main()
