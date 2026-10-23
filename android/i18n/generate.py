"""Übersetzungstabellen der Android-App erzeugen.

Quelle: alle *.json in diesem Ordner – {"deutscher Text": {"en": …, "es": …, …}}.
Ziel:   NConnect.Android/L.Texts.cs (Dictionaries für L.T/L.F) und Resources/values-xx/strings.xml.
Aufruf: python android/i18n/generate.py [--check]

Bricht ohne zu schreiben ab, wenn ein deutscher Text doppelt vorkommt (in einer oder mehreren Dateien), eine
Übersetzung oder ein Platzhalter fehlt oder ein Text, den der Code an L.T/L.F/Ui.* übergibt, in keiner Tabelle steht.
--check (für die CI) schreibt nie, sondern prüft zusätzlich, ob die erzeugten Dateien dem Stand im Repo entsprechen
(Zeilenenden egal) – Exitcode 1, wenn generate.py nach einer Änderung an den Tabellen nicht ausgeführt wurde.
"""
import json
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).parent
APP = HERE.parent / 'NConnect.Android'
LANGS = ['en', 'es', 'fr', 'it', 'pt', 'nl', 'pl', 'ru', 'ja', 'zh', 'ko']
# Android-Ressourcenordner je Sprache (Chinesisch: vereinfacht)
RES = {'en': 'values-en', 'es': 'values-es', 'fr': 'values-fr', 'it': 'values-it', 'pt': 'values-pt', 'nl': 'values-nl',
       'pl': 'values-pl', 'ru': 'values-ru', 'ja': 'values-ja', 'zh': 'values-zh', 'ko': 'values-ko'}
# Texte aus Resources/values/strings.xml (deutsch) – werden über dieselben Tabellen übersetzt
RES_KEYS = ['touch_service_description', 'touch_service_label', 'setup_title']
# Eigene Methoden, die ihre Text-Argumente an Ui.* weiterreichen (HelpPage.Topic, SetupWizardActivity.Option)
WRAPPERS = {'Topic', 'Option'}
# Texte, die bewusst in allen Sprachen gleich bleiben (Eigennamen, Kürzel)
SAME_EVERYWHERE = {'OK', 'Test', 'Root', 'Shizuku', 'SKZ Connect', 'Pro Controller 2', 'Joy-Con 2 (L)', 'Joy-Con 2 (R)'}
CHECK = '--check' in sys.argv[1:]

errors = []


def unique_keys(pairs, name):
    seen = {}
    for key, value in pairs:
        if key in seen:
            errors.append(f'doppelt in {name}: {key[:60]}')
        seen[key] = value
    return seen


table = {}
origin = {}
for f in sorted(HERE.glob('*.json')):
    data = json.loads(f.read_text(encoding='utf-8'), object_pairs_hook=lambda pairs, n=f.name: unique_keys(pairs, n))
    for german, tr in data.items():
        if german in table and f.name != origin[german]:
            errors.append(f'doppelt in {origin[german]} und {f.name}: {german[:60]}')
        table[german] = tr
        origin.setdefault(german, f.name)

for german, tr in table.items():
    if not isinstance(tr, dict):
        continue
    for lang in LANGS:
        text = tr.get(lang)
        if not text:
            errors.append(f'{lang} fehlt: {german[:60]}')
        elif sorted(re.findall(r'\{\d\}', text)) != sorted(re.findall(r'\{\d\}', german)):
            errors.append(f'{lang} Platzhalter: {german[:60]}')


# ---------- Im Code verwendete Texte ----------
# Kleiner C#-Zerleger: Kommentare, Zeichen- und Zeichenkettenliterale (auch $"…" mit verschachtelten Literalen).

ESCAPES = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '"': '"', "'": "'", '\\': '\\', 'a': '\a', 'b': '\b', 'f': '\f',
           'v': '\v'}
OPS = ['??=', '=>', '??', '?.', '==', '!=', '<=', '>=', '&&', '||', '+=', '-=', '..', '::', '++', '--', '->']


def read_string(src, i):
    """Normales "…"-Literal ab src[i] == '"'; liefert (Text, Index danach)."""
    i += 1
    out = []
    while src[i] != '"':
        if src[i] == '\\':
            c = src[i + 1]
            if c == 'u':
                out.append(chr(int(src[i + 2:i + 6], 16)))
                i += 6
                continue
            out.append(ESCAPES.get(c, c))
            i += 2
            continue
        out.append(src[i])
        i += 1
    return ''.join(out), i + 1


def skip_char(src, i):
    i += 1
    while src[i] != "'":
        i += 2 if src[i] == '\\' else 1
    return i + 1


def skip_interpolated(src, i, holes):
    """$"…" ab src[i] == '$'; Löcher {…} dürfen selbst Literale enthalten – ihre Lage kommt nach holes."""
    i += 2
    while True:
        c = src[i]
        if c == '\\':
            i += 2
        elif c == '"':
            return i + 1
        elif c == '{' and src[i + 1] == '{':
            i += 2
        elif c == '{':
            depth = 1
            i += 1
            begin = i
            while depth:
                c = src[i]
                if c == '"':
                    i = read_string(src, i)[1]
                elif c == '$' and src[i + 1] == '"':
                    i = skip_interpolated(src, i, [])  # innere Löcher zerlegt der Aufruf für dieses Loch
                elif c == "'":
                    i = skip_char(src, i)
                else:
                    depth += {'{': 1, '}': -1}.get(c, 0)
                    i += 1
            holes.append((begin, i - 1))
        else:
            i += 1


def tokens(src):
    """(Art, Wert, Zeile): 'str' (Text), 'istr' ($"…"), 'id' (Bezeichner/Zahl), 'op'."""
    out = []
    i, n, line = 0, len(src), 1
    while i < n:
        c = src[i]
        start = i
        if c == '\n':
            line += 1
            i += 1
            continue
        if c.isspace():
            i += 1
        elif src.startswith('//', i):
            i = src.find('\n', i)
            i = n if i < 0 else i
        elif src.startswith('/*', i):
            i = src.find('*/', i) + 2
        elif c == '#' and src[:i].rstrip(' \t').endswith('\n'):
            i = src.find('\n', i)
            i = n if i < 0 else i
        elif c == '"':
            text, i = read_string(src, i)
            out.append(('str', text, line))
        elif c == '$' and i + 1 < n and src[i + 1] == '"':
            holes = []
            i = skip_interpolated(src, i, holes)
            out.append(('istr', None, line))
            # Aufrufe in den Löchern ($"{L.T("…")}") ebenfalls prüfen – in Klammern, damit Kommas dort nicht stören.
            for begin, end in holes:
                at = line + src.count('\n', start, begin)
                out.append(('op', '(', at))
                out.extend((kind, value, at + row - 1) for kind, value, row in tokens(src[begin:end]))
                out.append(('op', ')', at))
        elif c == "'":
            i = skip_char(src, i)
            out.append(('id', 'char', line))
        elif c.isalnum() or c == '_' or c == '@':
            while i < n and (src[i].isalnum() or src[i] in '_@'):
                i += 1
            out.append(('id', src[start:i], line))
        else:
            op = next((o for o in OPS if src.startswith(o, i)), c)
            i += len(op)
            out.append(('op', op, line))
        line += src.count('\n', start + 1, i) if i - start > 1 else 0
    return out


OPEN, CLOSE = {'(': ')', '[': ']', '{': '}'}, {')', ']', '}'}


def split_top(toks, sep):
    """Tokens an sep (auf oberster Klammerebene) trennen."""
    parts, depth, cur = [], 0, []
    for t in toks:
        if t[0] == 'op' and t[1] in OPEN:
            depth += 1
        elif t[0] == 'op' and t[1] in CLOSE:
            depth -= 1
        if depth == 0 and t[0] == 'op' and t[1] == sep:
            parts.append(cur)
            cur = []
        else:
            cur.append(t)
    parts.append(cur)
    return parts


def top_index(toks, value, start=0):
    depth = 0
    for k in range(start, len(toks)):
        t = toks[k]
        if t[0] == 'op' and t[1] in OPEN:
            depth += 1
        elif t[0] == 'op' and t[1] in CLOSE:
            depth -= 1
        elif depth == 0 and t[0] == 'op' and t[1] == value:
            return k
    return -1


def values(toks):
    """Texte, die ein Ausdruck liefern kann: "a" + "b", c ? "a" : "b", x ?? "a", x switch { … => "a" }."""
    if not toks:
        return []
    if len(toks) > 2 and toks[0][0] == 'id' and toks[1] == ('op', ':', toks[1][2]):
        toks = toks[2:]  # benanntes Argument
    if toks[0][0] == 'op' and toks[0][1] == '(' and top_index(toks[1:], ')') == len(toks) - 2:
        return values(toks[1:-1])
    q = top_index(toks, '?')
    if q >= 0:
        depth, nested = 0, 0
        for k in range(q + 1, len(toks)):
            kind, value, _ = toks[k]
            if kind == 'op' and value in OPEN:
                depth += 1
            elif kind == 'op' and value in CLOSE:
                depth -= 1
            elif depth == 0 and kind == 'op' and value == '?':
                nested += 1
            elif depth == 0 and kind == 'op' and value == ':':
                if nested == 0:
                    return values(toks[q + 1:k]) + values(toks[k + 1:])
                nested -= 1
        return []
    c = top_index(toks, '??')
    if c >= 0:
        return values(toks[c + 1:])
    for k, t in enumerate(toks):
        if t == ('id', 'switch', t[2]) and k + 1 < len(toks) and toks[k + 1][1] == '{' and toks[-1][1] == '}':
            found = []
            for arm in split_top(toks[k + 2:-1], ','):
                a = top_index(arm, '=>')
                if a >= 0:
                    found += values(arm[a + 1:])
            return found
    parts = split_top(toks, '+')
    if all(len(p) == 1 and p[0][0] == 'str' for p in parts):
        return [(''.join(p[0][1] for p in parts), toks[0][2])]
    return []


def used_texts(path):
    toks = tokens(path.read_text(encoding='utf-8'))
    found = []
    for k, t in enumerate(toks):
        call = None
        if t[0] == 'id' and t[1] in ('L', 'Ui') and k + 3 < len(toks) and toks[k + 1][1] == '.' \
                and toks[k + 2][0] == 'id' and toks[k + 3][1] == '(':
            if t[1] == 'Ui' or toks[k + 2][1] in ('T', 'F'):
                call = k + 3
        elif t[0] == 'id' and t[1] in WRAPPERS and k + 1 < len(toks) and toks[k + 1][1] == '(' \
                and not (k > 0 and toks[k - 1][1] == '.'):
            call = k + 1
        if call is None:
            continue
        depth, end = 0, call
        for end in range(call, len(toks)):
            if toks[end][0] == 'op' and toks[end][1] in OPEN:
                depth += 1
            elif toks[end][0] == 'op' and toks[end][1] in CLOSE:
                depth -= 1
                if depth == 0:
                    break
        for arg in split_top(toks[call + 1:end], ','):
            found += values(arg)
    return found


for cs_file in sorted(APP.rglob('*.cs')):
    rel = cs_file.relative_to(APP)
    if rel.parts[0] in ('obj', 'bin') or cs_file.name == 'L.Texts.cs':
        continue
    for text, line in used_texts(cs_file):
        if text in table or text in SAME_EVERYWHERE or not re.search(r'[^\W\d_]', text):
            continue
        errors.append(f'nicht übersetzt ({rel.as_posix()}:{line}): {text[:60]!r}')

if errors:
    print('\n'.join(errors))
    sys.exit(1)


def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n') + '"'


outputs = {}  # Pfad → Inhalt; erst am Ende geschrieben (bzw. mit --check nur verglichen)
out = ['// <auto-generated> Erzeugt von android/i18n/generate.py – nicht von Hand bearbeiten. </auto-generated>',
       'namespace NConnect.Android;', '', 'public static partial class L', '{']
for lang in LANGS:
    out.append(f'    private static readonly Dictionary<string, string> {lang.capitalize()} = new(StringComparer.Ordinal)')
    out.append('    {')
    for german in sorted(table):
        out.append(f'        [{cs(german)}] = {cs(table[german][lang])},')
    out.append('    };')
    out.append('')
out[-1] = '}'
outputs[APP / 'L.Texts.cs'] = ('\n'.join(out) + '\n').encode('utf-8')

# strings.xml je Sprache
base = (APP / 'Resources/values/strings.xml').read_text(encoding='utf-8')
german_res = dict(re.findall(r'<string name="(\w+)">(.*?)</string>', base, re.S))


def xml(s):
    return s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace("'", "\\'").replace('"', '\\"')


for lang in LANGS:
    lines = ['<?xml version="1.0" encoding="utf-8"?>', '<!-- Erzeugt von android/i18n/generate.py -->', '<resources>']
    for key in RES_KEYS:
        german = german_res[key].replace("\\'", "'")
        lines.append(f'    <string name="{key}">{xml(table[german][lang])}</string>')
    lines.append('</resources>')
    outputs[APP / 'Resources' / RES[lang] / 'strings.xml'] = ('\n'.join(lines) + '\n').encode('utf-8')

if CHECK:
    stale = [p for p, content in outputs.items()
             if not p.exists() or p.read_bytes().replace(b'\r\n', b'\n') != content]
    if stale:
        print('veraltet – python android/i18n/generate.py ausführen:')
        print('\n'.join(f'  {p.relative_to(APP).as_posix()}' for p in stale))
        sys.exit(1)
    print(len(table), 'Texte,', len(LANGS), 'Sprachen – erzeugte Dateien aktuell')
    sys.exit(0)

for p, content in outputs.items():
    p.parent.mkdir(exist_ok=True)
    p.write_bytes(content)
print(len(table), 'Texte,', len(LANGS), 'Sprachen')
