#!/usr/bin/env python3
"""Render preview-PNGer av sigma-menyen direkte fra Layout.cs + PixelFont (samme tall og samme pikselfont som pluginen)."""
import re, sys, os
from PIL import Image, ImageDraw, ImageFilter
ROOT = os.path.join(os.path.dirname(__file__), '..', 'src', 'Plugin', 'Ui')
OUT = sys.argv[1] if len(sys.argv) > 1 else '.'
S = 3  # px per layout-enhet

lay = open(os.path.join(ROOT, 'Layout.cs')).read()
L = {k: float(v) for k, v in re.findall(r'(\w+)\s*=\s*(-?[\d.]+)f?\b', lay)}
fd = open(os.path.join(ROOT, 'PixelFontData.cs'), encoding='utf-8').read()
chars = re.search(r'Chars = "((?:[^"\\]|\\.)*)"', fd).group(1).encode('utf-8').decode('unicode_escape').encode('latin-1').decode('utf-8')
data = ''.join(re.findall(r'"([0-9A-F]+)"', fd.split('Data =')[1]))
GLY = {c: bytes.fromhex(data[i*16:(i+1)*16]) for i, c in enumerate(chars)}
pf = open(os.path.join(ROOT, 'PixelFont.cs'), encoding='utf-8').read()
ICONS = {m.group(1): re.findall(r'"([^"]+)"', m.group(2)) for m in re.finditer(r'\["(\w+)"\] = new\[\] \{(.*?)\}', pf, re.S)}
GOR = re.findall(r'"([.BFD]{16})"', pf.split('Gorilla =')[1])

def hx(h): return tuple(int(h[i:i+2], 16) for i in (0, 2, 4)) + (255,)
PANEL, SILVER, WHITE, DARK, DIM = hx('0B0B0D'), hx('C8CCD2'), hx('E9ECEF'), hx('0B0B0D'), hx('5A5E64')
DISF, DIST, GREEN, RED, RECRED, BEVEL = hx('6E7278'), hx('282A2E'), hx('1F8A48'), hx('BE2837'), hx('D22832'), hx('8C9096')

def p(v): return int(round(v * S))

def text(d, x, y, t, cell, col, anchor='mm', maxw=0, ls=1.3):
    lines = t.split('\n'); w = max(len(l) for l in lines) * cell
    if maxw and w > maxw: cell *= maxw / w; w = maxw
    h = cell * (1 + (len(lines) - 1) * ls); px = cell / 8
    y0 = y - h / 2 if anchor[1] == 'm' else y
    for li, line in enumerate(lines):
        lw = len(line) * cell
        x0 = x - lw / 2 if anchor[0] == 'm' else (x if anchor[0] == 'l' else x - lw)
        for ci, ch in enumerate(line):
            g = GLY.get(ch, GLY['?'])
            for r in range(8):
                for c in range(8):
                    if g[r] & (0x80 >> c):
                        gx, gy = x0 + ci * cell + c * px, y0 + li * cell * ls + r * px
                        d.rectangle([p(gx), p(gy), p(gx + px) - 1, p(gy + px) - 1], fill=col)

def bitmap(d, rows, cx, cy, size, cmap):
    n = len(rows); px = size / n; x0, y0 = cx - size / 2, cy - size / 2
    for r, row in enumerate(rows):
        for c, ch in enumerate(row):
            if ch in cmap: d.rectangle([p(x0 + c*px), p(y0 + r*px), p(x0 + (c+1)*px) - 1, p(y0 + (r+1)*px) - 1], fill=cmap[ch])

def rr(d, b, r, fill=None, outline=None, w=0):
    d.rounded_rectangle([p(b[0]), p(b[1]), p(b[2]), p(b[3])], radius=p(r), fill=fill, outline=outline, width=w)

def panel(img, b, r):
    g = Image.new('RGBA', img.size, (0, 0, 0, 0)); gd = ImageDraw.Draw(g)
    rr(gd, (b[0]-4, b[1]-4, b[2]+4, b[3]+4), r+4, fill=(220, 225, 232, 120))
    img.alpha_composite(g.filter(ImageFilter.GaussianBlur(p(5))))
    d = ImageDraw.Draw(img); rr(d, b, r, fill=PANEL, outline=SILVER, w=max(1, p(L['Edge'])))

STYLE = {'silver': (SILVER, DARK), 'active': (WHITE, DARK), 'disabled': (DISF, DIST)}
def btn(d, b, label='', font=9, st='silver', tc=None, icon=None, r=3):
    f, t = STYLE[st]; t = tc or t
    rr(d, (b[0], b[1]+1.2, b[2], b[3]+1.2), r, fill=BEVEL); rr(d, b, r, fill=f)
    cx, cy = (b[0]+b[2])/2, (b[1]+b[3])/2
    if icon: bitmap(d, ICONS[icon], cx, cy, 0.64 * min(b[2]-b[0], b[3]-b[1]), {'#': t})
    if label: text(d, cx, cy, label, font, t, maxw=(b[2]-b[0]) * 0.92, ls=2.0 if '\n' in label else 1.3)

def bg(w, h):
    im = Image.new('RGBA', (p(w), p(h)), hx('2B3A2E')); d = ImageDraw.Draw(im)
    for i in range(p(h)): d.line([(0, i), (p(w), i)], fill=(40 + i*30//p(h), 58 + i*20//p(h), 52, 255))
    return im

def strip(img, ox, oy, active):
    h = L['TabCount'] * L['TabStep'] + 12
    x0 = ox; y0 = oy
    panel(img, (x0, y0, x0 + L['StripW'], y0 + h), L['StripR']); d = ImageDraw.Draw(img)
    for i, ic in enumerate(["power", "people", "tag", "sun", "spotify", "camera", "shield"]):
        cy = y0 + 8 + i * L['TabStep'] + L['TabSize'] / 2; cx = x0 + L['StripW'] / 2; s = L['TabSize']
        btn(d, (cx - s/2, cy - s/2, cx + s/2, cy + s/2), st='active' if i == active else 'silver', icon=ic)

def left_lobby(img, ox, oy):
    panel(img, (ox, oy, ox + L['PW'], oy + L['PH']), L['PR']); d = ImageDraw.Draw(img)
    for i, ic in enumerate(["info", "tower", "window", "target", "people"]):
        x = ox + L['TopIconX0'] + i * L['TopIconStep']; y = oy + L['TopIconY']
        btn(d, (x, y, x + L['TopIconW'], y + L['TopIconH']), icon=ic)
    names = ['SEB', 'MONKE42', 'BANANA', 'PBBV', 'GORILLA_K', 'LEMMING', 'JMAN', 'TTTPIG', 'ECHO', 'KIWI']
    for i, n in enumerate(names):
        row, col = divmod(i, 2)
        x0 = ox + (L['BX0'] if col == 0 else L['BX1'] - L['PlayerBtnW']); y0 = oy + L['PlayerRowY0'] + row * L['PlayerRowStep']
        btn(d, (x0, y0, x0 + L['PlayerBtnW'], y0 + L['PlayerBtnH']), n, L['PlayerFont'], 'active' if i == 3 else 'silver')
        if i == 5: d.rectangle([p(x0 + L['PlayerBtnW'] - 8.5), p(y0 + L['PlayerBtnH']/2 - 2.5), p(x0 + L['PlayerBtnW'] - 3.5), p(y0 + L['PlayerBtnH']/2 + 2.5)], fill=RED)
    text(d, ox + L['PW']/2, oy + L['LobbyStatusY'], 'ROOM ABC12 - 10 PLAYERS - 1 FLAGGED', L['LobbyStatusFont'], SILVER, maxw=L['PW'] - 40)
    btn(d, (ox + L['GunX0'], oy + L['GunY0'], ox + L['GunX1'], oy + L['GunY1']), 'SELECT GUN', L['GunFont'], 'active')
    text(d, ox + L['PW']/2, oy + L['TitleY'], 'LOBBY', L['TitleFont'], WHITE, maxw=L['PW'] - 30)
    text(d, ox + L['PW']/2, oy + L['FooterY'], 'GTAG MOD CHECKER - by Seb', L['FooterFont'], DIM)

def player(img, ox, oy):
    x, y = ox + L['QX'], oy + L['QY']
    panel(img, (x, y, x + L['QW'], y + L['QH']), L['PR']); d = ImageDraw.Draw(img)
    ps = L['PortraitSize']; rr(d, (x + L['PortraitX0'], y + L['PortraitY0'], x + L['PortraitX0'] + ps, y + L['PortraitY0'] + ps), 6, fill=SILVER)
    bitmap(d, GOR, x + L['PortraitX0'] + ps/2, y + L['PortraitY0'] + ps/2 + 2, L['PortraitArt'], {'B': hx('C8641E'), 'F': hx('E1E4E8'), 'D': hx('282A2E')})
    tw = L['QW'] - L['InfoX'] - 12
    for i, t in enumerate(['PBBV', 'STEAM - 72 FPS', 'COLOR 9 4 1']):
        text(d, x + L['InfoX'], y + L['InfoY0'] + i * L['InfoStep'], t, L['InfoFont'], WHITE, 'lm', tw)
    text(d, x + L['InfoX'], y + L['JoinedY'], 'Joined', L['InfoFont'], WHITE, 'lm', tw)
    text(d, x + L['InfoX'], y + L['JoinedValY'], '15:24 (6 MIN)', L['InfoFont'], WHITE, 'lm', tw)
    text(d, x + L['InfoX'], y + L['StatusY'], 'STATUS: CLEAN', 6, SILVER, 'lm', tw)
    vy, vb = L['VolY0'], L['VolBtn']
    btn(d, (x + 16, y + vy, x + 16 + vb, y + vy + vb), icon='spk'); btn(d, (x + 46, y + vy, x + 46 + vb, y + vy + vb), icon='spk2')
    text(d, x + L['VolLabelX'], y + vy + 9, 'VOLUME', L['VolLabelFont'], WHITE); text(d, x + L['VolLabelX'], y + vy + 20, '100%', L['VolValueFont'], SILVER)
    btn(d, (x + L['QW'] - 72, y + vy, x + L['QW'] - 46, y + vy + vb), icon='chat'); btn(d, (x + L['QW'] - 42, y + vy, x + L['QW'] - 16, y + vy + vb), st='active', icon='mic')
    btn(d, (x + 16, y + L['ReportY0'], x + L['QW'] - 16, y + L['ReportY1']), 'REPORT', L['ReportFont'], 'active')
    btn(d, (x + 16, y + L['CloneY0'], x + L['QW'] - 16, y + L['CloneY1']), 'CLONE COSMETICS', L['CloneFont'], 'disabled')
    for (x0, x1, cap, c, n) in [(16, L['QW']/2 - 5, 'MODS', GREEN, '2'), (L['QW']/2 + 5, L['QW'] - 16, 'CHEATS', RED, '0')]:
        b = (x + x0, y + L['CountY0'], x + x1, y + L['CountY1']); btn(d, b, '', st='silver')
        cx, cy = (b[0]+b[2])/2, (b[1]+b[3])/2; text(d, cx, cy - 4, n, L['CountFont'], c); text(d, cx, cy + 5, cap, L['CountFont']*0.8, c)
    text(d, x + L['QW']/2, y + L['PFooterY'], 'PROFILE  |  ALLOWED MODS  |  CHEATS', L['PFooterFont'], SILVER)

def camera(img, ox, oy):
    k = L['CamScale']; W, H = L['CW'], L['CH']
    big = Image.new('RGBA', (p(W) + p(30), p(H) + p(30)), (0, 0, 0, 0)); o = 15
    panel(big, (o + L['StemX0'], o + L['StemY0'], o + L['StemX1'], o + H), L['StemR'])
    panel(big, (o, o + L['CBodyY0'], o + W, o + L['CBodyY1']), L['CR']); d = ImageDraw.Draw(big)
    d.ellipse([p(o + W/2 - 8), p(o + 4), p(o + W/2 + 8), p(o + 20)], outline=SILVER, width=p(2))
    for hx0 in (L['HandleX0'], W - L['HandleX0'] - L['HandleW']):
        rr(d, (o + hx0, o + L['HandleY0'], o + hx0 + L['HandleW'], o + L['HandleY1']), 12, fill=SILVER)
        for j in range(5): d.rectangle([p(o + hx0 + L['HandleW']/2 - 12), p(o + 120 + j*10 - .75), p(o + hx0 + L['HandleW']/2 + 12), p(o + 120 + j*10 + .75)], fill=hx('969AA0'))
    for i, ic in enumerate(["rotate", "photo", "grid", "swap", "gear", "dot"]):
        x = o + L['CGridX0'] + (i % 2) * L['CGridDX']; y = o + L['CGridY0'] + (i // 2) * L['CGridDY']
        btn(d, (x, y, x + L['CGridSize'], y + L['CGridSize']), icon=ic)
    rr(d, (o + L['ScreenX0'], o + L['ScreenY0'], o + L['ScreenX1'], o + L['ScreenY1']), L['ScreenR'], fill=WHITE)
    i_ = L['ScreenInset']; sx0, sy0, sx1, sy1 = o + L['ScreenX0'] + i_, o + L['ScreenY0'] + i_, o + L['ScreenX1'] - i_, o + L['ScreenY1'] - i_
    for yy in range(p(sy0), p(sy1)):  # "kamerabilde": himmel -> gress
        t = (yy - p(sy0)) / (p(sy1) - p(sy0)); col = (int(120 + 60*t), int(170 + 10*t), int(220 - 150*t), 255) if t < .6 else (60, 110, 55, 255)
        d.line([(p(sx0), yy), (p(sx1), yy)], fill=col)
    bitmap(d, GOR, (sx0 + sx1)/2, (sy0 + sy1)/2 + 20, 70, {'B': hx('3C6EC8'), 'F': hx('E1E4E8'), 'D': hx('282A2E')})
    b = L['ScreenBtn']
    btn(d, (sx0 + 4, sy0 + 4, sx0 + 4 + b, sy0 + 4 + b), st='active', icon='rotate')
    btn(d, (sx1 - 4 - 2*b - 6, sy0 + 4, sx1 - 4 - b - 6, sy0 + 4 + b), st='active', icon='photo')
    btn(d, (sx1 - 4 - b, sy0 + 4, sx1 - 4, sy0 + 4 + b), st='active', icon='gear')
    text(d, (sx0 + sx1)/2, sy0 + 15, 'THIRD PERSON - FOV 90', 6, WHITE, maxw=sx1 - sx0 - 120)
    text(d, sx0 + 8, sy1 - 12, 'SMOOTH MED - PC SCREEN OFF', 5, WHITE, 'lm', sx1 - sx0 - 90)
    bitmap(d, ICONS['rec'], sx1 - 16, sy1 - 12, 14, {'#': RECRED}); text(d, sx1 - 28, sy1 - 12, 'REC', 6, WHITE, 'rm')
    modes = ["FIRST\nPERSON", "THIRD\nPERSON", "FOLLOW\nMODE", "TRACK\nMONKEY", "LOCK", "GOPRO"]
    total = 6 * L['ModeW'] + 5 * L['ModeGap']; mx = o + W/2 - total/2
    for i, m in enumerate(modes):
        btn(d, (mx, o + L['ModeY0'], mx + L['ModeW'], o + L['ModeY1']), m, L['ModeFont'] if '\n' in m else 7, 'active' if i == 1 else 'silver'); mx += L['ModeW'] + L['ModeGap']
    text(d, o + L['RecTextX'], o + L['RecY0'], 'RECORDING\nOFF', L['RecFont'], WHITE, 'mt', ls=L['RecStep']/L['RecFont'])
    text(d, o + L['RecTextX'], o + L['RecSubY'], 'ON VR', 6, SILVER)
    bitmap(d, ICONS['folder'], o + L['FolderX'], o + L['FolderY'], 20, {'#': SILVER})
    rs = L['RecBtnSize']; d.ellipse([p(o + L['RecBtnX'] - rs/2), p(o + L['RecBtnY'] - rs/2), p(o + L['RecBtnX'] + rs/2), p(o + L['RecBtnY'] + rs/2)], fill=SILVER)
    bitmap(d, ICONS['rec'], o + L['RecBtnX'], o + L['RecBtnY'], rs * 0.64, {'#': RECRED})
    text(d, o + W/2, o + H - 14, 'GTAG MOD CHECKER CAM - by Seb', 5, DIM)
    big = big.resize((int(big.width * k), int(big.height * k)), Image.LANCZOS)
    img.alpha_composite(big, (p(ox) - int(p(o) * k), p(oy) - int(p(o) * k)))

def save(im, name):
    path = os.path.join(OUT, name); im.convert('RGB').save(path); print(path, im.size)

# 1) LOBBY + spillerpanel + fanestripe
W, H = L['QX'] + L['QW'] + 60, L['PH'] + 60
im = bg(W, H); ox, oy = 30, 30
left_lobby(im, ox, oy); strip(im, ox + L['PW'] + L['StripGap'], oy + L['StripY'], 1); player(im, ox, oy)
save(im, 'sigma_lobby_player.png')
# 2) CAMERA (venstre panel og spillerpanel skjult, stripe til høyre for nettbrettet)
cw = L['CW'] * L['CamScale']; W = cw + L['StripGap'] + L['StripW'] + 60; H = L['CH'] * L['CamScale'] + 60
im = bg(W, max(H, L['TabCount'] * L['TabStep'] + 12 + L['StripY'] + 60)); camera(im, 30, 30); strip(im, 30 + cw + L['StripGap'], 30 + L['StripY'], 5)
save(im, 'sigma_camera.png')
