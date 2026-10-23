# -*- coding: utf-8 -*-
"""Ergänzt die Anleitungen (help_*.txt) um Wii-Fernbedienung, feste Spielerplätze und Gyro für Emulatoren.
Idempotent: bereits ergänzte Dateien bleiben unverändert. Aufruf: python linux/kodi/i18n/help_additions.py"""
import pathlib

RES = pathlib.Path(__file__).parent.parent / 'service.skzconnect' / 'resources'

WII = {
    'de': '   • Wii-Fernbedienung / Wii U Pro Controller: rote SYNC-Taste (im Batteriefach bzw. hinten) drücken',
    'en': '   • Wii Remote / Wii U Pro Controller: press the red SYNC button (in the battery compartment or on the back)',
    'es': '   • Wiimote / Wii U Pro Controller: pulsa el botón rojo SYNC (en el compartimento de las pilas o detrás)',
    'fr': '   • Wiimote / Manette Pro Wii U : appuyez sur le bouton rouge SYNC (dans le compartiment à piles ou au dos)',
    'it': '   • Wiimote / Controller Pro Wii U: premi il tasto rosso SYNC (nel vano batterie o sul retro)',
    'pt': '   • Wiimote / Comando Pro da Wii U: prima o botão vermelho SYNC (no compartimento das pilhas ou atrás)',
    'nl': '   • Wii-afstandsbediening / Wii U Pro Controller: druk op de rode SYNC-knop (in het batterijvak of achterop)',
    'pl': '   • Wiilot / Wii U Pro Controller: naciśnij czerwony przycisk SYNC (w komorze baterii lub z tyłu)',
    'ru': '   • Пульт Wii / Wii U Pro Controller: нажмите красную кнопку SYNC (в отсеке батарей или сзади)',
    'ja': '   • Wii リモコン / Wii U PRO コントローラー: 赤い SYNC ボタン（電池ぶたの中または背面）を押す',
    'zh': '   • Wii 遥控器 / Wii U Pro 手柄：按红色 SYNC 键（在电池仓内或背面）',
    'ko': '   • Wii 리모컨 / Wii U Pro 컨트롤러: 빨간 SYNC 버튼(배터리 커버 안 또는 뒷면)을 누름',
}

EXTRA = {
    'de': '[B]Spielerplätze und Gyro für Emulatoren[/B]\nJeder Controller bekommt beim nächsten Verbinden wieder denselben Spielerplatz. Emulatoren wie Dolphin, Cemu oder Citra erhalten über DSU/Cemuhook (Port 26760) die Bewegungsdaten – im Emulator als Bewegungsquelle „DSU“ bzw. „Cemuhook“ mit 127.0.0.1 einstellen. Einstellungen > Emulatoren.\n\n',
    'en': '[B]Player slots and gyro for emulators[/B]\nEvery controller gets the same player slot again the next time it connects. Emulators such as Dolphin, Cemu or Citra receive the motion data via DSU/Cemuhook (port 26760) – choose "DSU" or "Cemuhook" with 127.0.0.1 as motion source in the emulator. Settings > Emulators.\n\n',
    'es': '[B]Puestos de jugador y giroscopio para emuladores[/B]\nCada mando recupera su puesto de jugador al volver a conectarse. Emuladores como Dolphin, Cemu o Citra reciben el movimiento por DSU/Cemuhook (puerto 26760): elige «DSU» o «Cemuhook» con 127.0.0.1 como fuente de movimiento. Ajustes > Emuladores.\n\n',
    'fr': '[B]Places des joueurs et gyroscope pour émulateurs[/B]\nChaque manette retrouve sa place de joueur à la reconnexion. Les émulateurs comme Dolphin, Cemu ou Citra reçoivent les mouvements via DSU/Cemuhook (port 26760) : choisissez « DSU » ou « Cemuhook » avec 127.0.0.1 comme source de mouvement. Paramètres > Émulateurs.\n\n',
    'it': '[B]Posti dei giocatori e giroscopio per emulatori[/B]\nOgni controller ritrova lo stesso posto di giocatore alla riconnessione. Emulatori come Dolphin, Cemu o Citra ricevono il movimento tramite DSU/Cemuhook (porta 26760): scegli «DSU» o «Cemuhook» con 127.0.0.1 come sorgente di movimento. Impostazioni > Emulatori.\n\n',
    'pt': '[B]Lugares dos jogadores e giroscópio para emuladores[/B]\nCada comando recupera o mesmo lugar de jogador ao voltar a ligar-se. Emuladores como Dolphin, Cemu ou Citra recebem o movimento por DSU/Cemuhook (porta 26760): escolha «DSU» ou «Cemuhook» com 127.0.0.1 como fonte de movimento. Definições > Emuladores.\n\n',
    'nl': '[B]Spelersplaatsen en gyroscoop voor emulators[/B]\nElke controller krijgt bij opnieuw verbinden weer dezelfde spelersplaats. Emulators zoals Dolphin, Cemu of Citra ontvangen de bewegingsgegevens via DSU/Cemuhook (poort 26760) – kies in de emulator „DSU” of „Cemuhook” met 127.0.0.1 als bewegingsbron. Instellingen > Emulators.\n\n',
    'pl': '[B]Miejsca graczy i żyroskop dla emulatorów[/B]\nKażdy kontroler po ponownym połączeniu dostaje to samo miejsce gracza. Emulatory, takie jak Dolphin, Cemu czy Citra, otrzymują dane ruchu przez DSU/Cemuhook (port 26760) – wybierz w emulatorze źródło ruchu „DSU” lub „Cemuhook” z adresem 127.0.0.1. Ustawienia > Emulatory.\n\n',
    'ru': '[B]Места игроков и гироскоп для эмуляторов[/B]\nПри повторном подключении каждый контроллер получает то же место игрока. Эмуляторы, например Dolphin, Cemu или Citra, получают данные движения через DSU/Cemuhook (порт 26760) – выберите в эмуляторе источник движения «DSU» или «Cemuhook» с адресом 127.0.0.1. Настройки > Эмуляторы.\n\n',
    'ja': '[B]プレイヤー番号とエミュレーター用ジャイロ[/B]\n再接続しても各コントローラーは同じプレイヤー番号になります。Dolphin、Cemu、Citra などのエミュレーターは DSU/Cemuhook（ポート 26760）でモーションデータを受け取ります – エミュレーターでモーション入力に「DSU」または「Cemuhook」（127.0.0.1）を選んでください。設定 > エミュレーター。\n\n',
    'zh': '[B]玩家位置和模拟器陀螺仪[/B]\n每个手柄重新连接时都会回到相同的玩家位置。Dolphin、Cemu、Citra 等模拟器通过 DSU/Cemuhook（端口 26760）接收动作数据 – 在模拟器中选择“DSU”或“Cemuhook”（127.0.0.1）作为体感来源。设置 > 模拟器。\n\n',
    'ko': '[B]플레이어 자리와 에뮬레이터용 자이로[/B]\n각 컨트롤러는 다시 연결해도 같은 플레이어 자리를 받습니다. Dolphin, Cemu, Citra 같은 에뮬레이터는 DSU/Cemuhook(포트 26760)으로 모션 데이터를 받습니다 – 에뮬레이터에서 모션 소스로 "DSU" 또는 "Cemuhook"(127.0.0.1)을 선택하세요. 설정 > 에뮬레이터.\n\n',
}

for lang, wii in WII.items():
    f = RES / 'help_{0}.txt'.format(lang)
    t = f.read_text(encoding='utf-8')
    if 'Wii' in t:
        continue
    lines = t.split('\n')
    i = next(n for n, line in enumerate(lines) if line.strip().startswith('• 8BitDo'))
    lines.insert(i + 1, wii)
    t = '\n'.join(lines)
    j = next(n for n, line in enumerate(t.split('\n')) if line.startswith('[B]') and 'RetroArch' in line)
    parts = t.split('\n')
    t = '\n'.join(parts[:j]) + '\n' + EXTRA[lang] + '\n'.join(parts[j:])
    f.write_text(t, encoding='utf-8', newline='\n')
    print(lang, 'ergänzt')
