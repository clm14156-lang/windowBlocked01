from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'icons01' / '_png'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = Path(r'C:/Users/12712/AppData/Local/Temp/codex-clipboard-47dbfe94-46cf-4e9b-b5b0-8b5c669af8c3.png')
source = Image.open(SOURCE).convert('RGB')
assert source.size == (1448,1086)
names = [
 'folder','finance','reading','graduation','study','writing',
 'code','terminal','music','movies','design','painting',
 'medical','first_aid','meditation','work','analytics','kettlebell',
 'fitness','notebook','justice','geography','travel','internet',
 'tools','pets','chemistry','brain','heart','gardening',
]
# Pixel-measured card boundaries; all intervening page pixels are excluded.
columns = [(49,244),(278,476),(509,706),(740,938),(971,1170),(1203,1399)]
rows = [(54,229),(249,427),(443,622),(639,816),(833,1011)]
report=[]
sheet=Image.new('RGB',(960,820),(247,247,247))
for i,name in enumerate(names):
    left,right=columns[i%6]; top,bottom=rows[i//6]
    rgb=np.asarray(source.crop((left,top,right,bottom))).copy()
    h,w=rgb.shape[:2]
    # The cards are slightly wider than tall. Use a common square canvas
    # and one uniform scale for every card, without changing aspect ratios.
    y,x=np.mgrid[:h,:w].astype(float)
    radius=35.0
    qx=np.abs(x+.5-w/2)-(w/2-radius)
    qy=np.abs(y+.5-h/2)-(h/2-radius)
    distance=np.hypot(np.maximum(qx,0),np.maximum(qy,0))+np.minimum(np.maximum(qx,qy),0)-radius
    alpha=np.clip(-distance-.4,0,1)
    rgba=np.dstack([rgb,np.rint(alpha*255).astype('uint8')])
    rgba[alpha==0,:3]=0
    card=Image.fromarray(rgba).resize((w*4,h*4),Image.Resampling.NEAREST)
    canvas=Image.new('RGBA',(800,800))
    canvas.paste(card,((800-w*4)//2,(800-h*4)//2))
    result=canvas.convert('RGBa').resize((128,128),Image.Resampling.LANCZOS).convert('RGBA')
    result.save(OUT/f'{name}.png')
    with Image.open(OUT/f'{name}.png') as check:
        assert check.size==(128,128) and check.mode=='RGBA'
        pixels=np.asarray(check)
        assert all(pixels[y,x,3]==0 for x,y in [(0,0),(127,0),(0,127),(127,127)])
        bounds=check.getchannel('A').getbbox()
        assert abs(bounds[0]-(128-bounds[2]))<=1
        assert abs(bounds[1]-(128-bounds[3]))<=1
        # Saturated icon foreground must retain clear separation from edges.
        chroma=pixels[:,:,:3].max(axis=2).astype(int)-pixels[:,:,:3].min(axis=2)
        fy,fx=np.where((chroma>75)&(pixels[:,:,3]>200))
        assert fx.min()>12 and fx.max()<115 and fy.min()>12 and fy.max()<115
    tile=Image.new('RGBA',(128,128),'white')
    draw=ImageDraw.Draw(tile)
    for yy in range(0,128,8):
        for xx in range(0,128,8):
            if (xx//8+yy//8)%2==0:
                draw.rectangle((xx,yy,xx+7,yy+7),fill=(222,225,230,255))
    tile=Image.alpha_composite(tile,result)
    ox=(i%6)*160+16; oy=(i//6)*164+8
    sheet.paste(tile.convert('RGB'),(ox,oy))
    ImageDraw.Draw(sheet).text((ox,oy+134),name,fill=(40,40,40))
    report.append({'file':name+'.png','crop':[left,top,right,bottom], 'size':[128,128], 'mode':'RGBA','alpha_bounds':bounds})
sheet.save(ROOT/'icons01'/'preview.png')
(ROOT/'icons01'/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(f'Exported and validated {len(report)} icons: {OUT}')
