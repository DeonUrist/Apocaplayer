"""Deterministic outline HUD assets and the PNG design preview (no game dependencies)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Models' / 'Hud'
OUT.mkdir(parents=True, exist_ok=True)
SCALE = 4

def icon(name, color):
    im = Image.new('RGBA', (96 * SCALE, 96 * SCALE))
    d = ImageDraw.Draw(im)
    def line(points, width=5):
        d.line([(x*SCALE, y*SCALE) for x,y in points], fill=color, width=width*SCALE, joint='curve')
    def circle(box):
        d.ellipse(tuple(int(v*SCALE) for v in box), outline=color, width=5*SCALE)
    if name == 'ignition':
        circle((19, 19, 47, 47))
        line([(43,43),(76,76)])
        line([(57,57),(66,48)])
        line([(70,70),(79,61)])
    elif name == 'handbrake':
        circle((22,22,74,74))
        d.arc((8*SCALE,17*SCALE,88*SCALE,79*SCALE), 142, 218, fill=color, width=5*SCALE)
        d.arc((8*SCALE,17*SCALE,88*SCALE,79*SCALE), -38, 38, fill=color, width=5*SCALE)
        line([(42,64),(42,33),(53,33),(59,38),(59,46),(53,51),(42,51)])
    else:
        d.rounded_rectangle((12*SCALE,25*SCALE,84*SCALE,72*SCALE), radius=5*SCALE, outline=color, width=4*SCALE)
        circle((23,34,39,50)); circle((57,34,73,50))
        line([(39,42),(57,42)], 4)
        line([(23,72),(30,60),(66,60),(73,72)], 4)
    im = im.resize((96,96), Image.Resampling.LANCZOS)
    im.save(OUT / f'{name}.png')
    return im

key = icon('ignition', '#f0cd82')
brake = icon('handbrake', '#ed9581')
tape = icon('cassette', '#d9e6cf')

# Reviewable raster mockup matches the icons that ship in the mod.
im = Image.new('RGBA', (1200,680), '#151a1b')
d = ImageDraw.Draw(im)
d.polygon([(0,350),(230,230),(430,320),(630,195),(930,300),(1200,210),(1200,680),(0,680)], fill='#262d2b')
d.polygon([(515,680),(570,345),(630,345),(810,680)], fill='#343a34')
def font(size): return ImageFont.truetype('C:/Windows/Fonts/arial.ttf', size)
def text(x,y,t,size=19,c='#eee9dc'): d.text((x,y),t,font=font(size),fill=c)
text(36,28,'APOCAPLAYER · VEHICLE HUD MOCKUP',21,'#d4d8cb')
text(36,65,'Small outline icons · only active conditions appear · volume 0.0–1.0',14,'#929d90')
for x,asset,w in [(936,key,62),(1004,brake,62),(1072,tape,104)]:
    d.rounded_rectangle((x,28,x+w,76),6,fill='#101415')
    im.alpha_composite(asset.resize((48,48),Image.Resampling.LANCZOS),(x+7,28))
text(1134,43,'0.5',19,'#d9e6cf')
for i,t in enumerate(['E – Ignition','X – Headlights On','Z – Cassette Stop','− – Volume Down','+ – Volume Up']): text(32,405+i*29,t)
d.line((590,340,610,340),fill='#dadbcf',width=2); d.line((600,330,600,350),fill='#dadbcf',width=2)
d.rounded_rectangle((36,574,1164,654),6,fill='#101415')
text(56,589,'KEY · Ignition off',15,'#b6beb1'); text(324,589,'(P) · Handbrake engaged',15,'#b6beb1')
text(648,589,'CASSETTE + 0.5 · Music playing',15,'#b6beb1')
text(56,624,'Engine running: E – Ignition Stop · Both hint groups can be hidden in VEHICLE settings',15,'#b6beb1')
im.convert('RGB').save(ROOT / 'design' / 'vehicle-hud.png')
