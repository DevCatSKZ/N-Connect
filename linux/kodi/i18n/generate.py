# -*- coding: utf-8 -*-
"""
Erzeugt die Kodi-Sprachdateien (strings.po) von service.skzconnect aus resource.language.en_gb (Quelle) und den
Übersetzungen in tr_a.py/tr_b.py, dazu die des Bundle-Installers aus tr_bundle.py. Gleiche Sprachen wie SKZ Connect für
Windows. Aufruf: python linux/kodi/i18n/generate.py
service.skzconnect: Englisch und Deutsch werden von Hand gepflegt; hier werden dort nur fehlende Einträge aus EXTRA
ergänzt. script.skzconnect.bundle: alle Sprachdateien kommen aus tr_bundle.py.
"""
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from tr_a import ES, FR, IT, NL, PT  # noqa: E402
from tr_b import JA, KO, PL, RU, ZH  # noqa: E402
from tr_bundle import BUNDLE  # noqa: E402

LANG_DIR = pathlib.Path(__file__).parent.parent / 'service.skzconnect' / 'resources' / 'language'
BUNDLE_LANG_DIR = pathlib.Path(__file__).parent.parent / 'script.skzconnect.bundle' / 'resources' / 'language'

# Neue Texte, die in allen Sprachen ergänzt werden: ID -> {Sprache: Text}; "en" ist die Quelle (msgid).
EXTRA = {
    30655: {
        'en': 'Connected', 'de': 'Verbunden', 'es': 'Conectado', 'fr': 'Connecté', 'it': 'Connesso',
        'pt': 'Ligado', 'nl': 'Verbonden', 'pl': 'Połączony', 'ru': 'Подключён', 'ja': '接続済み',
        'zh': '已连接', 'ko': '연결됨',
    },
    30656: {
        'en': 'Not connected', 'de': 'Nicht verbunden', 'es': 'No conectado', 'fr': 'Non connecté',
        'it': 'Non connesso', 'pt': 'Não ligado', 'nl': 'Niet verbonden', 'pl': 'Niepołączony',
        'ru': 'Не подключён', 'ja': '未接続', 'zh': '未连接', 'ko': '연결 안 됨',
    },
    30029: {
        'en': 'WiFi is active and shares the antenna with Bluetooth – turn it off if unused (LibreELEC settings > Connections)',
        'de': 'WLAN ist aktiv und teilt sich die Antenne mit Bluetooth – ausschalten, wenn es nicht gebraucht wird (LibreELEC-Einstellungen > Verbindungen)',
        'es': 'El WiFi está activo y comparte la antena con el Bluetooth; desactívalo si no lo usas (Ajustes de LibreELEC > Conexiones)',
        'fr': "Le Wi-Fi est actif et partage l'antenne avec le Bluetooth ; désactivez-le s'il est inutile (Paramètres LibreELEC > Connexions)",
        'it': "Il Wi-Fi è attivo e condivide l'antenna con il Bluetooth; spegnilo se non lo usi (Impostazioni LibreELEC > Connessioni)",
        'pt': 'O Wi-Fi está ativo e partilha a antena com o Bluetooth; desliga-o se não for usado (Definições do LibreELEC > Ligações)',
        'nl': 'Wifi is actief en deelt de antenne met Bluetooth; schakel het uit als je het niet gebruikt (LibreELEC-instellingen > Verbindingen)',
        'pl': 'Wi-Fi jest aktywne i dzieli antenę z Bluetoothem – wyłącz je, jeśli nie jest potrzebne (Ustawienia LibreELEC > Połączenia)',
        'ru': 'Wi-Fi активен и делит антенну с Bluetooth – выключите его, если он не нужен (Настройки LibreELEC > Соединения)',
        'ja': 'Wi-Fi が有効で Bluetooth とアンテナを共用しています – 使わない場合はオフにしてください（LibreELEC 設定 > 接続）',
        'zh': 'Wi-Fi 已启用并与蓝牙共用天线 – 如不使用请关闭（LibreELEC 设置 > 连接）',
        'ko': 'Wi-Fi가 켜져 있어 Bluetooth와 안테나를 공유합니다 – 사용하지 않으면 끄세요 (LibreELEC 설정 > 연결)',
    },
    30027: {
        'en': 'Searching for controllers for 2 minutes …', 'de': 'Suche 2 Minuten nach Controllern …',
        'es': 'Buscando mandos durante 2 minutos …', 'fr': 'Recherche de manettes pendant 2 minutes …',
        'it': 'Ricerca di controller per 2 minuti …', 'pt': 'A procurar comandos durante 2 minutos …',
        'nl': '2 minuten zoeken naar controllers …', 'pl': 'Wyszukiwanie kontrolerów przez 2 minuty …',
        'ru': 'Поиск контроллеров в течение 2 минут …', 'ja': '2 分間コントローラーを探しています …',
        'zh': '正在搜索手柄（2 分钟）…', 'ko': '2분 동안 컨트롤러를 찾는 중 …',
    },
    30334: {
        'en': 'Search for controllers continuously', 'de': 'Ständig nach Controllern suchen',
        'es': 'Buscar mandos continuamente', 'fr': 'Rechercher des manettes en permanence',
        'it': 'Cerca controller di continuo', 'pt': 'Procurar comandos continuamente',
        'nl': 'Continu naar controllers zoeken', 'pl': 'Stale wyszukuj kontrolery',
        'ru': 'Постоянно искать контроллеры', 'ja': '常にコントローラーを探す',
        'zh': '持续搜索手柄', 'ko': '항상 컨트롤러 찾기',
    },
    30335: {
        'en': 'SKZ Connect keeps searching in the background, so any controller can be paired with SYNC at any time and Switch 2 controllers reconnect with a button press. When off, it only searches after "Search for controllers now"; paired Xbox/PlayStation controllers still reconnect on their own.',
        'de': 'SKZ Connect sucht ständig im Hintergrund – so lässt sich jederzeit jeder Controller per SYNC koppeln, und Switch-2-Controller verbinden sich per Tastendruck neu. Aus: Gesucht wird nur nach „Jetzt nach Controllern suchen“; gekoppelte Xbox-/PlayStation-Controller verbinden sich trotzdem selbst.',
        'es': 'SKZ Connect busca continuamente en segundo plano: cualquier mando se puede emparejar con SYNC en cualquier momento y los mandos de Switch 2 se reconectan con un botón. Desactivado: solo busca tras «Buscar mandos ahora»; los mandos de Xbox/PlayStation emparejados se reconectan solos.',
        'fr': "SKZ Connect recherche en permanence en arrière-plan : toute manette peut être associée avec SYNC à tout moment et les manettes Switch 2 se reconnectent d'une pression. Désactivé : recherche uniquement après « Rechercher des manettes maintenant » ; les manettes Xbox/PlayStation associées se reconnectent seules.",
        'it': 'SKZ Connect cerca di continuo in background: qualsiasi controller può essere associato con SYNC in ogni momento e i controller Switch 2 si ricollegano con un tasto. Disattivato: cerca solo dopo «Cerca controller ora»; i controller Xbox/PlayStation associati si ricollegano da soli.',
        'pt': 'O SKZ Connect procura continuamente em segundo plano: qualquer comando pode ser emparelhado com SYNC a qualquer momento e os comandos Switch 2 voltam a ligar-se com um botão. Desativado: só procura após «Procurar comandos agora»; comandos Xbox/PlayStation emparelhados voltam a ligar-se sozinhos.',
        'nl': 'SKZ Connect zoekt continu op de achtergrond: elke controller kan op elk moment met SYNC worden gekoppeld en Switch 2-controllers verbinden met één knop opnieuw. Uit: alleen zoeken na „Nu naar controllers zoeken”; gekoppelde Xbox-/PlayStation-controllers verbinden zichzelf.',
        'pl': 'SKZ Connect stale wyszukuje w tle – każdy kontroler można w dowolnej chwili sparować przez SYNC, a kontrolery Switch 2 łączą się ponownie jednym przyciskiem. Wyłączone: wyszukiwanie tylko po „Wyszukaj kontrolery teraz”; sparowane kontrolery Xbox/PlayStation łączą się same.',
        'ru': 'SKZ Connect постоянно ищет в фоне: любой контроллер можно в любой момент подключить через SYNC, а контроллеры Switch 2 переподключаются нажатием кнопки. Выкл.: поиск только после «Искать контроллеры сейчас»; сопряжённые контроллеры Xbox/PlayStation подключаются сами.',
        'ja': 'SKZ Connect はバックグラウンドで常に探します – いつでも SYNC でコントローラーをペアリングでき、Switch 2 コントローラーはボタンひとつで再接続します。オフ: 「今すぐコントローラーを探す」の後だけ探します。ペアリング済みの Xbox/PlayStation コントローラーは自動で再接続します。',
        'zh': 'SKZ Connect 在后台持续搜索 – 随时可通过 SYNC 配对任何手柄，Switch 2 手柄按一下即可重新连接。关闭：仅在“立即搜索手柄”后搜索；已配对的 Xbox/PlayStation 手柄仍会自动重新连接。',
        'ko': 'SKZ Connect가 백그라운드에서 항상 찾습니다 – 언제든 SYNC로 컨트롤러를 페어링할 수 있고 Switch 2 컨트롤러는 버튼 하나로 다시 연결됩니다. 끄면: "지금 컨트롤러 찾기" 후에만 찾습니다. 페어링된 Xbox/PlayStation 컨트롤러는 스스로 다시 연결됩니다.',
    },
    30632: {
        'en': 'Search for controllers now', 'de': 'Jetzt nach Controllern suchen', 'es': 'Buscar mandos ahora',
        'fr': 'Rechercher des manettes maintenant', 'it': 'Cerca controller ora', 'pt': 'Procurar comandos agora',
        'nl': 'Nu naar controllers zoeken', 'pl': 'Wyszukaj kontrolery teraz', 'ru': 'Искать контроллеры сейчас',
        'ja': '今すぐコントローラーを探す', 'zh': '立即搜索手柄', 'ko': '지금 컨트롤러 찾기',
    },
    30660: {
        'en': 'SKZ Connect app is not running', 'de': 'SKZ-Connect-App läuft nicht', 'es': 'La app SKZ Connect no está en marcha',
        'fr': "L'application SKZ Connect n'est pas lancée", 'it': "L'app SKZ Connect non è in esecuzione",
        'pt': 'A aplicação SKZ Connect não está em execução', 'nl': 'De SKZ-Connect-app draait niet',
        'pl': 'Aplikacja SKZ Connect nie jest uruchomiona', 'ru': 'Приложение SKZ Connect не запущено',
        'ja': 'SKZ Connect アプリが起動していません', 'zh': 'SKZ Connect 应用未在运行', 'ko': 'SKZ Connect 앱이 실행 중이 아닙니다',
    },
    30661: {
        'en': 'Start SKZ Connect on this PC – it reports the status here',
        'de': 'SKZ Connect auf diesem PC starten – die App meldet den Status hierher',
        'es': 'Inicia la app SKZ Connect en este PC; envía el estado aquí',
        'fr': 'Démarrez l’application SKZ Connect sur ce PC – elle transmet l’état ici',
        'it': 'Avvia l’app SKZ Connect su questo PC: riporta qui lo stato',
        'pt': 'Inicia a aplicação SKZ Connect neste PC – ela envia o estado para aqui',
        'nl': 'Start de SKZ-Connect-app op deze pc – die meldt de status hier',
        'pl': 'Uruchom aplikację SKZ Connect na tym komputerze – ona przesyła tu stan',
        'ru': 'Запустите приложение SKZ Connect на этом ПК – оно передаёт сюда состояние',
        'ja': 'この PC で SKZ Connect アプリを起動してください – ここに状態を送ります',
        'zh': '请在此电脑上启动 SKZ Connect 应用——它会将状态发送到这里',
        'ko': '이 PC에서 SKZ Connect 앱을 실행하세요 – 여기로 상태를 보냅니다',
    },
    30633: {
        'en': 'Background search is off – searches for 2 minutes', 'de': 'Hintergrundsuche ist aus – sucht 2 Minuten lang',
        'es': 'La búsqueda en segundo plano está desactivada – busca durante 2 minutos',
        'fr': 'Recherche en arrière-plan désactivée – recherche pendant 2 minutes',
        'it': 'Ricerca in background disattivata – cerca per 2 minuti',
        'pt': 'Procura em segundo plano desativada – procura durante 2 minutos',
        'nl': 'Achtergrondzoeken staat uit – zoekt 2 minuten', 'pl': 'Wyszukiwanie w tle wyłączone – szuka przez 2 minuty',
        'ru': 'Фоновый поиск выключен – поиск 2 минуты', 'ja': 'バックグラウンド検索はオフ – 2 分間探します',
        'zh': '后台搜索已关闭 – 搜索 2 分钟', 'ko': '백그라운드 검색 꺼짐 – 2분 동안 찾습니다',
    },
}

EXTRA.update({
    30028: {'en': '{0}: battery low ({1} %) – charge soon', 'de': '{0}: Akku schwach ({1} %) – bald aufladen',
            'es': '{0}: batería baja ({1} %) – cárgalo pronto', 'fr': '{0} : batterie faible ({1} %) – rechargez bientôt',
            'it': '{0}: batteria scarica ({1} %) – ricarica presto', 'pt': '{0}: bateria fraca ({1} %) – carregue em breve',
            'nl': '{0}: batterij bijna leeg ({1} %) – binnenkort opladen', 'pl': '{0}: słaba bateria ({1} %) – wkrótce naładuj',
            'ru': '{0}: низкий заряд ({1} %) – скоро зарядите', 'ja': '{0}: バッテリー残量わずか（{1} %）– 早めに充電してください',
            'zh': '{0}：电量低（{1} %）– 请尽快充电', 'ko': '{0}: 배터리 부족 ({1} %) – 곧 충전하세요'},
    30412: {'en': 'Low battery warning', 'de': 'Warnung bei schwachem Akku', 'es': 'Aviso de batería baja',
            'fr': 'Alerte de batterie faible', 'it': 'Avviso batteria scarica', 'pt': 'Aviso de bateria fraca',
            'nl': 'Waarschuwing bij lage batterij', 'pl': 'Ostrzeżenie o słabej baterii', 'ru': 'Предупреждение о низком заряде',
            'ja': 'バッテリー低下の警告', 'zh': '低电量警告', 'ko': '배터리 부족 경고'},
    30413: {'en': "Shows a message when a controller's battery drops to 20 % and again at 10 %.",
            'de': 'Zeigt eine Meldung, wenn der Akku eines Controllers auf 20 % und noch einmal auf 10 % fällt.',
            'es': 'Muestra un mensaje cuando la batería de un mando baja al 20 % y de nuevo al 10 %.',
            'fr': "Affiche un message lorsque la batterie d'une manette descend à 20 % puis à 10 %.",
            'it': 'Mostra un messaggio quando la batteria di un controller scende al 20 % e di nuovo al 10 %.',
            'pt': 'Mostra uma mensagem quando a bateria de um comando desce para 20 % e novamente para 10 %.',
            'nl': 'Toont een melding wanneer de batterij van een controller naar 20 % en opnieuw naar 10 % zakt.',
            'pl': 'Pokazuje komunikat, gdy bateria kontrolera spadnie do 20 % i ponownie do 10 %.',
            'ru': 'Показывает сообщение, когда заряд контроллера падает до 20 % и ещё раз до 10 %.',
            'ja': 'コントローラーのバッテリーが 20 % と 10 % に下がったときにメッセージを表示します。',
            'zh': '手柄电量降至 20 % 及 10 % 时显示提示。', 'ko': '컨트롤러 배터리가 20 %와 10 %로 떨어지면 메시지를 표시합니다.'},
    30420: {'en': 'Controller buttons in Kodi', 'de': 'Controller-Tasten in Kodi', 'es': 'Botones del mando en Kodi',
            'fr': 'Boutons de la manette dans Kodi', 'it': 'Tasti del controller in Kodi', 'pt': 'Botões do comando no Kodi',
            'nl': 'Controllerknoppen in Kodi', 'pl': 'Przyciski kontrolera w Kodi', 'ru': 'Кнопки контроллера в Kodi',
            'ja': 'Kodi でのコントローラーボタン', 'zh': 'Kodi 中的手柄按键', 'ko': 'Kodi에서 컨트롤러 버튼'},
    30421: {'en': 'Capture button', 'de': 'Aufnahmetaste (Capture)', 'es': 'Botón de captura', 'fr': 'Bouton de capture',
            'it': 'Tasto di acquisizione', 'pt': 'Botão de captura', 'nl': 'Opnameknop', 'pl': 'Przycisk zrzutu',
            'ru': 'Кнопка снимка', 'ja': 'キャプチャーボタン', 'zh': '截图键', 'ko': '캡처 버튼'},
    30422: {'en': 'C button (Switch 2)', 'de': 'C-Taste (Switch 2)', 'es': 'Botón C (Switch 2)', 'fr': 'Bouton C (Switch 2)',
            'it': 'Tasto C (Switch 2)', 'pt': 'Botão C (Switch 2)', 'nl': 'C-knop (Switch 2)', 'pl': 'Przycisk C (Switch 2)',
            'ru': 'Кнопка C (Switch 2)', 'ja': 'C ボタン（Switch 2）', 'zh': 'C 键（Switch 2）', 'ko': 'C 버튼 (Switch 2)'},
    30423: {'en': 'GL back button (Pro Controller 2)', 'de': 'GL-Rückseitentaste (Pro Controller 2)',
            'es': 'Botón trasero GL (Pro Controller 2)', 'fr': 'Bouton arrière GL (Pro Controller 2)',
            'it': 'Tasto posteriore GL (Pro Controller 2)', 'pt': 'Botão traseiro GL (Pro Controller 2)',
            'nl': 'GL-achterknop (Pro Controller 2)', 'pl': 'Tylny przycisk GL (Pro Controller 2)',
            'ru': 'Задняя кнопка GL (Pro Controller 2)', 'ja': '背面 GL ボタン（Pro コントローラー 2）',
            'zh': '背键 GL（Pro 手柄 2）', 'ko': '후면 GL 버튼 (Pro 컨트롤러 2)'},
    30424: {'en': 'GR back button (Pro Controller 2)', 'de': 'GR-Rückseitentaste (Pro Controller 2)',
            'es': 'Botón trasero GR (Pro Controller 2)', 'fr': 'Bouton arrière GR (Pro Controller 2)',
            'it': 'Tasto posteriore GR (Pro Controller 2)', 'pt': 'Botão traseiro GR (Pro Controller 2)',
            'nl': 'GR-achterknop (Pro Controller 2)', 'pl': 'Tylny przycisk GR (Pro Controller 2)',
            'ru': 'Задняя кнопка GR (Pro Controller 2)', 'ja': '背面 GR ボタン（Pro コントローラー 2）',
            'zh': '背键 GR（Pro 手柄 2）', 'ko': '후면 GR 버튼 (Pro 컨트롤러 2)'},
    30425: {'en': 'What the button does in Kodi – in addition to its normal function in games.',
            'de': 'Was die Taste in Kodi auslöst – zusätzlich zu ihrer normalen Funktion in Spielen.',
            'es': 'Qué hace el botón en Kodi, además de su función normal en los juegos.',
            'fr': 'Ce que fait le bouton dans Kodi, en plus de sa fonction normale dans les jeux.',
            'it': 'Cosa fa il tasto in Kodi, oltre alla sua funzione normale nei giochi.',
            'pt': 'O que o botão faz no Kodi, além da sua função normal nos jogos.',
            'nl': 'Wat de knop in Kodi doet – naast zijn normale functie in games.',
            'pl': 'Co przycisk robi w Kodi – oprócz zwykłej funkcji w grach.',
            'ru': 'Что делает кнопка в Kodi – в дополнение к обычной функции в играх.',
            'ja': 'Kodi でのボタンの動作（ゲームでの通常の機能に加えて）。', 'zh': '该按键在 Kodi 中的功能（在游戏中的正常功能之外）。',
            'ko': 'Kodi에서 버튼이 하는 일 – 게임에서의 일반 기능과 별도로.'},
    30430: {'en': 'No action', 'de': 'Keine Aktion', 'es': 'Ninguna acción', 'fr': 'Aucune action', 'it': 'Nessuna azione',
            'pt': 'Nenhuma ação', 'nl': 'Geen actie', 'pl': 'Brak akcji', 'ru': 'Нет действия', 'ja': '何もしない', 'zh': '无操作',
            'ko': '동작 없음'},
    30431: {'en': 'Screenshot', 'de': 'Bildschirmfoto', 'es': 'Captura de pantalla', 'fr': "Capture d'écran", 'it': 'Screenshot',
            'pt': 'Captura de ecrã', 'nl': 'Schermafbeelding', 'pl': 'Zrzut ekranu', 'ru': 'Снимок экрана', 'ja': 'スクリーンショット',
            'zh': '截图', 'ko': '스크린샷'},
    30432: {'en': 'Home screen', 'de': 'Startseite', 'es': 'Pantalla de inicio', 'fr': "Écran d'accueil", 'it': 'Schermata Home',
            'pt': 'Ecrã inicial', 'nl': 'Startscherm', 'pl': 'Ekran główny', 'ru': 'Главный экран', 'ja': 'ホーム画面', 'zh': '主屏幕',
            'ko': '홈 화면'},
    30433: {'en': 'Context menu', 'de': 'Kontextmenü', 'es': 'Menú contextual', 'fr': 'Menu contextuel', 'it': 'Menu contestuale',
            'pt': 'Menu de contexto', 'nl': 'Contextmenu', 'pl': 'Menu kontekstowe', 'ru': 'Контекстное меню', 'ja': 'コンテキストメニュー',
            'zh': '上下文菜单', 'ko': '컨텍스트 메뉴'},
    30434: {'en': 'Information', 'de': 'Informationen', 'es': 'Información', 'fr': 'Informations', 'it': 'Informazioni',
            'pt': 'Informação', 'nl': 'Informatie', 'pl': 'Informacje', 'ru': 'Информация', 'ja': '情報', 'zh': '信息', 'ko': '정보'},
    30435: {'en': 'Play/pause', 'de': 'Wiedergabe/Pause', 'es': 'Reproducir/pausa', 'fr': 'Lecture/pause', 'it': 'Riproduci/pausa',
            'pt': 'Reproduzir/pausa', 'nl': 'Afspelen/pauze', 'pl': 'Odtwórz/pauza', 'ru': 'Воспроизведение/пауза', 'ja': '再生/一時停止',
            'zh': '播放/暂停', 'ko': '재생/일시정지'},
    30436: {'en': 'Stop', 'de': 'Stopp', 'es': 'Detener', 'fr': 'Arrêt', 'it': 'Stop', 'pt': 'Parar', 'nl': 'Stoppen', 'pl': 'Zatrzymaj',
            'ru': 'Стоп', 'ja': '停止', 'zh': '停止', 'ko': '정지'},
    30437: {'en': 'Mute', 'de': 'Stumm', 'es': 'Silenciar', 'fr': 'Muet', 'it': 'Muto', 'pt': 'Sem som', 'nl': 'Dempen', 'pl': 'Wycisz',
            'ru': 'Без звука', 'ja': 'ミュート', 'zh': '静音', 'ko': '음소거'},
    30438: {'en': 'Power menu', 'de': 'Ausschaltmenü', 'es': 'Menú de apagado', 'fr': "Menu d'arrêt", 'it': 'Menu di spegnimento',
            'pt': 'Menu de energia', 'nl': 'Afsluitmenu', 'pl': 'Menu zasilania', 'ru': 'Меню выключения', 'ja': '電源メニュー',
            'zh': '电源菜单', 'ko': '전원 메뉴'},
    30439: {'en': 'SKZ Connect menu', 'de': 'SKZ-Connect-Menü', 'es': 'Menú de SKZ Connect', 'fr': 'Menu SKZ Connect', 'it': 'Menu SKZ Connect',
            'pt': 'Menu do SKZ Connect', 'nl': 'SKZ-Connect-menu', 'pl': 'Menu SKZ Connect', 'ru': 'Меню SKZ Connect', 'ja': 'SKZ Connect メニュー',
            'zh': 'SKZ Connect 菜单', 'ko': 'SKZ Connect 메뉴'},
    30440: {'en': 'Fullscreen', 'de': 'Vollbild', 'es': 'Pantalla completa', 'fr': 'Plein écran', 'it': 'Schermo intero',
            'pt': 'Ecrã inteiro', 'nl': 'Volledig scherm', 'pl': 'Pełny ekran', 'ru': 'Полный экран', 'ja': '全画面', 'zh': '全屏',
            'ko': '전체 화면'},
})

EXTRA.update({
    30140: {'en': 'Emulators (motion control)', 'de': 'Emulatoren (Bewegungssteuerung)', 'es': 'Emuladores (control por movimiento)',
            'fr': 'Émulateurs (détection de mouvement)', 'it': 'Emulatori (controllo di movimento)', 'pt': 'Emuladores (controlo por movimento)',
            'nl': 'Emulators (bewegingsbesturing)', 'pl': 'Emulatory (sterowanie ruchem)', 'ru': 'Эмуляторы (управление движением)',
            'ja': 'エミュレーター（モーション操作）', 'zh': '模拟器（体感控制）', 'ko': '에뮬레이터 (모션 컨트롤)'},
    30141: {'en': 'Gyro for emulators (DSU/Cemuhook)', 'de': 'Gyro für Emulatoren (DSU/Cemuhook)', 'es': 'Giroscopio para emuladores (DSU/Cemuhook)',
            'fr': 'Gyroscope pour émulateurs (DSU/Cemuhook)', 'it': 'Giroscopio per emulatori (DSU/Cemuhook)', 'pt': 'Giroscópio para emuladores (DSU/Cemuhook)',
            'nl': 'Gyroscoop voor emulators (DSU/Cemuhook)', 'pl': 'Żyroskop dla emulatorów (DSU/Cemuhook)', 'ru': 'Гироскоп для эмуляторов (DSU/Cemuhook)',
            'ja': 'エミュレーター用ジャイロ（DSU/Cemuhook）', 'zh': '模拟器陀螺仪（DSU/Cemuhook）', 'ko': '에뮬레이터용 자이로 (DSU/Cemuhook)'},
    30142: {'en': 'Emulators such as Dolphin, Cemu or Citra receive motion, gyro and buttons of players 1–4 (port 26760) – for motion controls like on the console.',
            'de': 'Emulatoren wie Dolphin, Cemu oder Citra erhalten Bewegung, Gyro und Tasten der Spieler 1–4 (Port 26760) – für Bewegungssteuerung wie an der Konsole.',
            'es': 'Emuladores como Dolphin, Cemu o Citra reciben el movimiento, el giroscopio y los botones de los jugadores 1–4 (puerto 26760), para controlar con movimiento como en la consola.',
            'fr': 'Les émulateurs comme Dolphin, Cemu ou Citra reçoivent les mouvements, le gyroscope et les boutons des joueurs 1 à 4 (port 26760), pour jouer avec la détection de mouvement comme sur la console.',
            'it': 'Emulatori come Dolphin, Cemu o Citra ricevono movimento, giroscopio e tasti dei giocatori 1–4 (porta 26760), per il controllo di movimento come sulla console.',
            'pt': 'Emuladores como Dolphin, Cemu ou Citra recebem o movimento, o giroscópio e os botões dos jogadores 1–4 (porta 26760), para controlo por movimento como na consola.',
            'nl': 'Emulators zoals Dolphin, Cemu of Citra ontvangen beweging, gyroscoop en knoppen van spelers 1–4 (poort 26760) – voor bewegingsbesturing zoals op de console.',
            'pl': 'Emulatory, takie jak Dolphin, Cemu czy Citra, otrzymują ruch, żyroskop i przyciski graczy 1–4 (port 26760) – do sterowania ruchem jak na konsoli.',
            'ru': 'Эмуляторы, например Dolphin, Cemu или Citra, получают движение, гироскоп и кнопки игроков 1–4 (порт 26760) – для управления движением, как на консоли.',
            'ja': 'Dolphin、Cemu、Citra などのエミュレーターがプレイヤー 1〜4 のモーション・ジャイロ・ボタンを受け取ります（ポート 26760）– 本体と同じようにモーション操作できます。',
            'zh': 'Dolphin、Cemu、Citra 等模拟器可接收玩家 1–4 的动作、陀螺仪和按键（端口 26760）– 像在主机上一样体感操作。',
            'ko': 'Dolphin, Cemu, Citra 같은 에뮬레이터가 플레이어 1–4의 모션, 자이로, 버튼을 받습니다 (포트 26760) – 본체처럼 모션 컨트롤을 쓸 수 있습니다.'},
    30143: {'en': 'Also for other devices on the home network', 'de': 'Auch für andere Geräte im Heimnetz', 'es': 'También para otros dispositivos de la red doméstica',
            'fr': 'Aussi pour les autres appareils du réseau domestique', 'it': 'Anche per altri dispositivi della rete domestica', 'pt': 'Também para outros dispositivos da rede doméstica',
            'nl': 'Ook voor andere apparaten in het thuisnetwerk', 'pl': 'Także dla innych urządzeń w sieci domowej', 'ru': 'Также для других устройств в домашней сети',
            'ja': 'ホームネットワーク上のほかの機器にも', 'zh': '也供家庭网络中的其他设备使用', 'ko': '홈 네트워크의 다른 기기에도'},
    30144: {'en': 'Lets an emulator on another device (e.g. Cemu on a PC) use the motion data of controllers connected here. Only enable on a trusted home network.',
            'de': 'Erlaubt einem Emulator auf einem anderen Gerät (z. B. Cemu auf dem PC), die Bewegungsdaten der hier verbundenen Controller zu nutzen. Nur im vertrauenswürdigen Heimnetz einschalten.',
            'es': 'Permite que un emulador en otro dispositivo (p. ej. Cemu en un PC) use los datos de movimiento de los mandos conectados aquí. Actívalo solo en una red doméstica de confianza.',
            'fr': "Permet à un émulateur sur un autre appareil (p. ex. Cemu sur un PC) d'utiliser les mouvements des manettes connectées ici. À activer uniquement sur un réseau domestique de confiance.",
            'it': 'Consente a un emulatore su un altro dispositivo (ad es. Cemu sul PC) di usare i dati di movimento dei controller collegati qui. Attivalo solo in una rete domestica fidata.',
            'pt': 'Permite que um emulador noutro dispositivo (p. ex. Cemu num PC) use os dados de movimento dos comandos ligados aqui. Ative apenas numa rede doméstica de confiança.',
            'nl': 'Laat een emulator op een ander apparaat (bijv. Cemu op een pc) de bewegingsgegevens van de hier verbonden controllers gebruiken. Alleen inschakelen in een vertrouwd thuisnetwerk.',
            'pl': 'Pozwala emulatorowi na innym urządzeniu (np. Cemu na PC) korzystać z danych ruchu podłączonych tutaj kontrolerów. Włączaj tylko w zaufanej sieci domowej.',
            'ru': 'Позволяет эмулятору на другом устройстве (например, Cemu на ПК) использовать данные движения подключённых здесь контроллеров. Включайте только в доверенной домашней сети.',
            'ja': 'ほかの機器のエミュレーター（例: PC の Cemu）が、ここに接続したコントローラーのモーションデータを使えます。信頼できるホームネットワークでのみ有効にしてください。',
            'zh': '允许其他设备上的模拟器（例如电脑上的 Cemu）使用此处连接的手柄的动作数据。仅在可信的家庭网络中启用。',
            'ko': '다른 기기의 에뮬레이터(예: PC의 Cemu)가 여기 연결된 컨트롤러의 모션 데이터를 사용할 수 있게 합니다. 신뢰할 수 있는 홈 네트워크에서만 켜세요.'},
})

# 0.4.4: Bluetooth-Hinweise je Plattform (Vertrag der status.json: on | off | missing | no_le), Begleitmodus,
# Selbsttest „Bluetooth LE“.
EXTRA.update({
    30019: {'en': 'Bluetooth adapter without LE – Switch 2 controllers cannot connect, other controllers still work',
            'de': 'Bluetooth-Adapter ohne LE – Switch-2-Controller können sich nicht verbinden, andere Controller schon',
            'es': 'Adaptador Bluetooth sin LE – los mandos de Switch 2 no se pueden conectar, los demás mandos sí',
            'fr': "Adaptateur Bluetooth sans LE – les manettes Switch 2 ne peuvent pas se connecter, les autres manettes oui",
            'it': 'Adattatore Bluetooth senza LE – i controller Switch 2 non possono collegarsi, gli altri controller sì',
            'pt': 'Adaptador Bluetooth sem LE – os comandos Switch 2 não se podem ligar, os outros comandos sim',
            'nl': 'Bluetooth-adapter zonder LE – Switch 2-controllers kunnen niet verbinden, andere controllers wel',
            'pl': 'Adapter Bluetooth bez LE – kontrolery Switch 2 nie mogą się połączyć, inne kontrolery tak',
            'ru': 'Адаптер Bluetooth без LE – контроллеры Switch 2 не могут подключиться, остальные работают',
            'ja': 'LE 非対応の Bluetooth アダプターです – Switch 2 コントローラーは接続できませんが、ほかのコントローラーは使えます',
            'zh': '蓝牙适配器不支持 LE – Switch 2 手柄无法连接，其他手柄仍可使用',
            'ko': 'LE를 지원하지 않는 블루투스 어댑터 – Switch 2 컨트롤러는 연결할 수 없지만 다른 컨트롤러는 됩니다'},
    30520: {'en': 'These settings only apply on LibreELEC/Linux', 'de': 'Diese Einstellungen gelten nur unter LibreELEC/Linux',
            'es': 'Estos ajustes solo se aplican en LibreELEC/Linux', 'fr': "Ces paramètres ne s'appliquent que sous LibreELEC/Linux",
            'it': 'Queste impostazioni valgono solo su LibreELEC/Linux', 'pt': 'Estas definições só se aplicam no LibreELEC/Linux',
            'nl': 'Deze instellingen gelden alleen op LibreELEC/Linux', 'pl': 'Te ustawienia działają tylko w LibreELEC/Linux',
            'ru': 'Эти настройки действуют только в LibreELEC/Linux', 'ja': 'これらの設定は LibreELEC/Linux でのみ有効です',
            'zh': '这些设置仅在 LibreELEC/Linux 上生效', 'ko': '이 설정은 LibreELEC/Linux에서만 적용됩니다'},
    30521: {'en': 'On Windows and Android the SKZ Connect app connects the controllers – change these settings in the app. On macOS the add-on only displays the status. Select to open the guide.',
            'de': 'Unter Windows und Android verbindet die SKZ-Connect-App die Controller – diese Einstellungen dort in der App ändern. Unter macOS zeigt das Add-on nur den Status an. Auswählen öffnet die Anleitung.',
            'es': 'En Windows y Android, la app SKZ Connect conecta los mandos: cambia estos ajustes en la app. En macOS el add-on solo muestra el estado. Selecciónalo para abrir la guía.',
            'fr': "Sous Windows et Android, l'application SKZ Connect connecte les manettes : modifiez ces paramètres dans l'application. Sous macOS, l'add-on affiche seulement l'état. Sélectionnez pour ouvrir le guide.",
            'it': "Su Windows e Android è l'app SKZ Connect a collegare i controller: modifica queste impostazioni nell'app. Su macOS l'add-on mostra solo lo stato. Seleziona per aprire la guida.",
            'pt': 'No Windows e no Android é a aplicação SKZ Connect que liga os comandos – altere estas definições na aplicação. No macOS o add-on só mostra o estado. Selecione para abrir o guia.',
            'nl': 'Op Windows en Android verbindt de SKZ-Connect-app de controllers – wijzig deze instellingen in de app. Op macOS toont de add-on alleen de status. Selecteer om de handleiding te openen.',
            'pl': 'W Windows i Androidzie kontrolery łączy aplikacja SKZ Connect – zmień te ustawienia w aplikacji. W macOS dodatek tylko pokazuje stan. Wybierz, aby otworzyć instrukcję.',
            'ru': 'В Windows и Android контроллеры подключает приложение SKZ Connect – меняйте эти настройки в приложении. В macOS дополнение только показывает состояние. Выберите, чтобы открыть инструкцию.',
            'ja': 'Windows と Android では SKZ Connect アプリがコントローラーを接続します – これらの設定はアプリで変更してください。macOS ではアドオンは状態を表示するだけです。選ぶとガイドを開きます。',
            'zh': '在 Windows 和 Android 上由 SKZ Connect 应用连接手柄 – 请在应用中更改这些设置。在 macOS 上此插件仅显示状态。选择以打开指南。',
            'ko': 'Windows와 Android에서는 SKZ Connect 앱이 컨트롤러를 연결합니다 – 이 설정은 앱에서 바꾸세요. macOS에서는 애드온이 상태만 표시합니다. 선택하면 안내를 엽니다.'},
    30634: {'en': 'Bluetooth without LE', 'de': 'Bluetooth ohne LE', 'es': 'Bluetooth sin LE', 'fr': 'Bluetooth sans LE',
            'it': 'Bluetooth senza LE', 'pt': 'Bluetooth sem LE', 'nl': 'Bluetooth zonder LE', 'pl': 'Bluetooth bez LE',
            'ru': 'Bluetooth без LE', 'ja': 'Bluetooth LE 非対応', 'zh': '蓝牙不支持 LE', 'ko': '블루투스 LE 미지원'},
    30635: {'en': 'Switch 2 controllers need a Bluetooth adapter with LE (4.0 or newer) – other controllers keep working',
            'de': 'Switch-2-Controller brauchen einen Bluetooth-Adapter mit LE (ab 4.0) – andere Controller laufen weiter',
            'es': 'Los mandos de Switch 2 necesitan un adaptador Bluetooth con LE (4.0 o superior) – los demás mandos siguen funcionando',
            'fr': 'Les manettes Switch 2 nécessitent un adaptateur Bluetooth avec LE (4.0 ou plus récent) – les autres manettes continuent de fonctionner',
            'it': 'I controller Switch 2 richiedono un adattatore Bluetooth con LE (4.0 o successivo) – gli altri controller continuano a funzionare',
            'pt': 'Os comandos Switch 2 precisam de um adaptador Bluetooth com LE (4.0 ou mais recente) – os outros comandos continuam a funcionar',
            'nl': 'Switch 2-controllers hebben een Bluetooth-adapter met LE nodig (4.0 of nieuwer) – andere controllers blijven werken',
            'pl': 'Kontrolery Switch 2 wymagają adaptera Bluetooth z LE (4.0 lub nowszy) – inne kontrolery działają dalej',
            'ru': 'Контроллерам Switch 2 нужен адаптер Bluetooth с LE (4.0 или новее) – остальные контроллеры работают дальше',
            'ja': 'Switch 2 コントローラーには LE 対応（4.0 以降）の Bluetooth アダプターが必要です – ほかのコントローラーはそのまま使えます',
            'zh': 'Switch 2 手柄需要支持 LE 的蓝牙适配器（4.0 或更高）– 其他手柄仍可正常使用',
            'ko': 'Switch 2 컨트롤러에는 LE를 지원하는 블루투스 어댑터(4.0 이상)가 필요합니다 – 다른 컨트롤러는 계속 작동합니다'},
    30646: {'en': 'Bluetooth LE (Switch 2)', 'de': 'Bluetooth LE (Switch 2)', 'es': 'Bluetooth LE (Switch 2)',
            'fr': 'Bluetooth LE (Switch 2)', 'it': 'Bluetooth LE (Switch 2)', 'pt': 'Bluetooth LE (Switch 2)',
            'nl': 'Bluetooth LE (Switch 2)', 'pl': 'Bluetooth LE (Switch 2)', 'ru': 'Bluetooth LE (Switch 2)',
            'ja': 'Bluetooth LE（Switch 2）', 'zh': '蓝牙 LE（Switch 2）', 'ko': '블루투스 LE (Switch 2)'},
    30662: {'en': 'Turn on Bluetooth in the Windows settings', 'de': 'Bluetooth in den Windows-Einstellungen einschalten',
            'es': 'Activa el Bluetooth en la configuración de Windows', 'fr': 'Activez le Bluetooth dans les paramètres Windows',
            'it': 'Attiva il Bluetooth nelle impostazioni di Windows', 'pt': 'Ative o Bluetooth nas definições do Windows',
            'nl': 'Zet Bluetooth aan in de Windows-instellingen', 'pl': 'Włącz Bluetooth w ustawieniach systemu Windows',
            'ru': 'Включите Bluetooth в параметрах Windows', 'ja': 'Windows の設定で Bluetooth をオンにしてください',
            'zh': '请在 Windows 设置中开启蓝牙', 'ko': 'Windows 설정에서 블루투스를 켜세요'},
    30663: {'en': 'Turn on Bluetooth in Android', 'de': 'Bluetooth in Android einschalten', 'es': 'Activa el Bluetooth en Android',
            'fr': 'Activez le Bluetooth dans Android', 'it': 'Attiva il Bluetooth in Android', 'pt': 'Ative o Bluetooth no Android',
            'nl': 'Zet Bluetooth aan in Android', 'pl': 'Włącz Bluetooth w Androidzie', 'ru': 'Включите Bluetooth в Android',
            'ja': 'Android で Bluetooth をオンにしてください', 'zh': '请在 Android 中开启蓝牙', 'ko': 'Android에서 블루투스를 켜세요'},
    30664: {'en': 'Plug in a Bluetooth adapter or turn on Bluetooth in the Windows settings',
            'de': 'Bluetooth-Adapter anschließen oder Bluetooth in den Windows-Einstellungen einschalten',
            'es': 'Conecta un adaptador Bluetooth o activa el Bluetooth en la configuración de Windows',
            'fr': 'Branchez un adaptateur Bluetooth ou activez le Bluetooth dans les paramètres Windows',
            'it': 'Collega un adattatore Bluetooth o attiva il Bluetooth nelle impostazioni di Windows',
            'pt': 'Ligue um adaptador Bluetooth ou ative o Bluetooth nas definições do Windows',
            'nl': 'Sluit een Bluetooth-adapter aan of zet Bluetooth aan in de Windows-instellingen',
            'pl': 'Podłącz adapter Bluetooth lub włącz Bluetooth w ustawieniach systemu Windows',
            'ru': 'Подключите адаптер Bluetooth или включите Bluetooth в параметрах Windows',
            'ja': 'Bluetooth アダプターを接続するか、Windows の設定で Bluetooth をオンにしてください',
            'zh': '插入蓝牙适配器，或在 Windows 设置中开启蓝牙', 'ko': '블루투스 어댑터를 연결하거나 Windows 설정에서 블루투스를 켜세요'},
    30666: {'en': 'No SKZ Connect app for this system', 'de': 'Keine SKZ-Connect-App für dieses System',
            'es': 'No hay app SKZ Connect para este sistema', 'fr': "Pas d'application SKZ Connect pour ce système",
            'it': 'Nessuna app SKZ Connect per questo sistema', 'pt': 'Não há aplicação SKZ Connect para este sistema',
            'nl': 'Geen SKZ-Connect-app voor dit systeem', 'pl': 'Brak aplikacji SKZ Connect dla tego systemu',
            'ru': 'Нет приложения SKZ Connect для этой системы', 'ja': 'このシステム用の SKZ Connect アプリはありません',
            'zh': '此系统没有 SKZ Connect 应用', 'ko': '이 시스템용 SKZ Connect 앱이 없습니다'},
    30667: {'en': 'Display only – SKZ Connect is not available for {0}', 'de': 'Nur Anzeige – SKZ Connect gibt es für {0} nicht',
            'es': 'Solo visualización – SKZ Connect no está disponible para {0}', 'fr': "Affichage seul – SKZ Connect n'existe pas pour {0}",
            'it': 'Solo visualizzazione – SKZ Connect non è disponibile per {0}', 'pt': 'Só visualização – o SKZ Connect não está disponível para {0}',
            'nl': 'Alleen weergave – SKZ Connect is niet beschikbaar voor {0}', 'pl': 'Tylko podgląd – SKZ Connect nie jest dostępny dla {0}',
            'ru': 'Только просмотр – SKZ Connect для {0} нет', 'ja': '表示のみ – SKZ Connect は {0} に対応していません',
            'zh': '仅显示 – SKZ Connect 不支持 {0}', 'ko': '표시 전용 – SKZ Connect는 {0}용이 없습니다'},
    30668: {'en': 'Start SKZ Connect for Android (0.9.1 or newer) on this device – it reports the status here',
            'de': 'SKZ Connect für Android (ab 0.9.1) auf diesem Gerät starten – die App meldet den Status hierher',
            'es': 'Inicia SKZ Connect para Android (0.9.1 o posterior) en este dispositivo; envía el estado aquí',
            'fr': 'Démarrez l’application SKZ Connect pour Android (0.9.1 ou plus récente) sur cet appareil – elle transmet l’état ici',
            'it': 'Avvia l’app SKZ Connect per Android (0.9.1 o successiva) su questo dispositivo: riporta qui lo stato',
            'pt': 'Inicia a aplicação SKZ Connect para Android (0.9.1 ou mais recente) neste dispositivo – ela envia o estado para aqui',
            'nl': 'Start de SKZ-Connect-app voor Android (0.9.1 of nieuwer) op dit apparaat – die meldt de status hier',
            'pl': 'Uruchom aplikację SKZ Connect dla Androida (0.9.1 lub nowszą) na tym urządzeniu – ona przesyła tu stan',
            'ru': 'Запустите приложение SKZ Connect для Android (0.9.1 или новее) на этом устройстве – оно передаёт сюда состояние',
            'ja': 'この端末で SKZ Connect for Android（0.9.1 以降）を起動してください – ここに状態を送ります',
            'zh': '请在此设备上启动 SKZ Connect for Android（0.9.1 或更高版本）——它会将状态发送到这里',
            'ko': '이 기기에서 SKZ Connect for Android(0.9.1 이상)를 실행하세요 – 여기로 상태를 보냅니다'},
})

TARGETS = [
    ('es_es', 'es', ES), ('fr_fr', 'fr', FR), ('it_it', 'it', IT), ('pt_pt', 'pt', PT), ('pt_br', 'pt', PT),
    ('nl_nl', 'nl', NL), ('pl_pl', 'pl', PL), ('ru_ru', 'ru', RU), ('ja_jp', 'ja', JA), ('zh_cn', 'zh', ZH),
    ('ko_kr', 'ko', KO),
]

ENTRY = re.compile(r'msgctxt "#(\d+)"\nmsgid "(.*)"\nmsgstr "(.*)"')


def esc(s):
    return s.replace('\\', '\\\\').replace('"', '\\"')


def entry(i, msgid, msgstr):
    return 'msgctxt "#{0}"\nmsgid "{1}"\nmsgstr "{2}"\n'.format(i, esc(msgid), esc(msgstr))


def append_missing(path, lang):
    """Fehlende EXTRA-Einträge an eine von Hand gepflegte Datei (en/de) anhängen."""
    text = path.read_text(encoding='utf-8')
    have = {int(m.group(1)) for m in ENTRY.finditer(text)}
    add = [entry(i, t['en'], '' if lang == 'en' else t[lang]) for i, t in sorted(EXTRA.items()) if i not in have]
    if add:
        text = text.rstrip('\n') + '\n\n' + '\n'.join(add)
        path.write_text(text, encoding='utf-8', newline='\n')


def header(name, addon_id, folder):
    return [
        '# Kodi Media Center language file', '# Addon Name: ' + name, '# Addon id: ' + addon_id,
        '# Addon Provider: devcatskz', '# Generated by linux/kodi/i18n/generate.py – do not edit by hand.',
        'msgid ""', 'msgstr ""', '"Project-Id-Version: {0}\\n"'.format(addon_id),
        '"Report-Msgid-Bugs-To: https://github.com/DevCatSKZ/SKZ-Connect/issues\\n"', '"MIME-Version: 1.0\\n"',
        '"Content-Type: text/plain; charset=UTF-8\\n"', '"Content-Transfer-Encoding: 8bit\\n"',
        '"Language: {0}\\n"'.format(folder), '',
    ]


def write_bundle():
    """Alle Sprachdateien von script.skzconnect.bundle (en_gb mit leerem msgstr = Quelle)."""
    for folder, code in [('en_gb', 'en'), ('de_de', 'de')] + [(f, c) for f, c, _ in TARGETS]:
        lines = header('SKZ Connect Bundle Installer', 'script.skzconnect.bundle', folder)
        lines += [entry(i, t['en'], '' if code == 'en' else t[code]) for i, t in sorted(BUNDLE.items())]
        out = BUNDLE_LANG_DIR / ('resource.language.' + folder)
        out.mkdir(parents=True, exist_ok=True)
        (out / 'strings.po').write_text('\n'.join(lines), encoding='utf-8', newline='\n')
    print('script.skzconnect.bundle: {0} Texte in {1} Sprachdateien'.format(len(BUNDLE), len(TARGETS) + 2))


def main():
    en_path = LANG_DIR / 'resource.language.en_gb' / 'strings.po'
    append_missing(en_path, 'en')
    append_missing(LANG_DIR / 'resource.language.de_de' / 'strings.po', 'de')
    source = [(int(m.group(1)), m.group(2)) for m in ENTRY.finditer(en_path.read_text(encoding='utf-8'))]
    for folder, code, table in TARGETS:
        lines = header('SKZ Connect', 'service.skzconnect', folder)
        missing = []
        for i, msgid in source:
            msgid = msgid.replace('\\"', '"').replace('\\\\', '\\')
            translated = table.get(i) or EXTRA.get(i, {}).get(code)
            if translated is None:
                missing.append(i)
                translated = ''
            lines.append(entry(i, msgid, translated))
        out = LANG_DIR / ('resource.language.' + folder)
        out.mkdir(exist_ok=True)
        (out / 'strings.po').write_text('\n'.join(lines), encoding='utf-8', newline='\n')
        print('{0}: {1} Texte{2}'.format(folder, len(source), ', fehlend: {0}'.format(missing) if missing else ''))
    write_bundle()


if __name__ == '__main__':
    main()
