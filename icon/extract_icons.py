from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
SOURCE = Path(r'C:/Users/12712/AppData/Local/Temp/codex-clipboard-ed3dc7a5-2db6-441a-aec6-7dc0a5b76638.png')
OUT = ROOT / 'icons_png'
OUT.mkdir(exist_ok=True)
source = Image.open(SOURCE).convert('RGB')
# Measured card bounds, exclusive right/bottom. Labels lie outside every box.
items = [
 ('game', (28,53,222,234)), ('computer',(257,51,453,234)),
 ('code',(488,51,684,234)), ('study',(719,51,915,234)),
 ('reading',(950,51,1146,234)), ('exam',(1181,51,1378,234)),
 ('fitness',(27,313,221,491)), ('music',(257,312,451,491)),
 ('painting',(487,312,683,491)), ('camera',(718,312,914,491)),
 ('life',(948,312,1144,491)), ('travel',(1180,312,1376,491)),
 ('work',(27,570,220,748)), ('goal',(257,570,451,748)),
 ('health',(487,570,683,748)), ('habit',(718,569,914,748)),
 ('finance',(948,569,1145,748)), ('more',(1180,568,1378,746)),
]

def rounded_distance(w,h,r=34):
    y,x=np.mgrid[:h,:w].astype(float)
    qx=np.abs(x+.5-w/2)-(w/2-r)
    qy=np.abs(y+.5-h/2)-(h/2-r)
    return np.hypot(np.maximum(qx,0),np.maximum(qy,0))+np.minimum(np.maximum(qx,qy),0)-r

def repair_selection(rgb, distance):
    # Only reconstruct the narrow selected outline, using a fitted background
    # surface. All pixels more than 10 px inside the card remain untouched.
    h,w=rgb.shape[:2]
    y,x=np.mgrid[:h,:w].astype(float)
    x=(x-w/2)/w; y=(y-h/2)/h
    features=np.stack([np.ones_like(x),x,y,x*x,x*y,y*y],axis=-1)
    pale=(rgb.min(axis=2)>205)
    samples=(distance < -10)&(distance > -27)&pale
    coefficients=np.linalg.lstsq(features[samples],rgb[samples],rcond=None)[0]
    background=np.clip(features@coefficients,0,255)
    blend=np.clip((-distance-7)/3,0,1)[...,None]
    return background*(1-blend)+rgb*blend

records=[]
for name,box in items:
    rgb=np.array(source.crop(box),dtype=float)
    h,w=rgb.shape[:2]
    distance=rounded_distance(w,h)
    if name=='game':
        rgb=repair_selection(rgb,distance)
    # Mask slightly inside the observed contour to exclude page-background
    # pixels baked into the screenshot's outer antialiasing fringe.
    alpha=np.clip(-distance-.35,0,1)
    rgba=np.dstack([rgb,alpha*255]).round().astype('uint8')
    rgba[alpha==0,:3]=0
    card=Image.fromarray(rgba)
    # A shared source-pixel scale preserves all original icon proportions.
    # 4x staging permits exact centering even for odd-sized crops.
    canvas=Image.new('RGBA',(800,800))
    card=card.resize((w*4,h*4),Image.Resampling.NEAREST)
    canvas.paste(card,((800-w*4)//2,(800-h*4)//2))
    # Premultiplied-alpha resampling avoids white halos on transparent edges.
    result=canvas.convert('RGBa').resize((128,128),Image.Resampling.LANCZOS).convert('RGBA')
    result.save(OUT/f'{name}.png')
    records.append({'file':f'{name}.png','source_box':box,'size':list(result.size),'mode':result.mode})

# Automated checks and a checkerboard contact sheet for visual inspection.
sheet=Image.new('RGB',(6*160,3*164),(245,245,245))
for index,(name,_) in enumerate(items):
    with Image.open(OUT/f'{name}.png') as im:
        assert im.size==(128,128) and im.mode=='RGBA'
        a=np.asarray(im)
        assert all(a[y,x,3]==0 for x,y in [(0,0),(127,0),(0,127),(127,127)])
        assert a[64,64,3]==255
        if name=='game':
            yy,xx=np.mgrid[:128,:128]
            edge=(xx<18)|(xx>109)|(yy<20)|(yy>107)
            orange=(a[:,:,0]>220)&(a[:,:,1]<185)&(a[:,:,2]<135)&(a[:,:,3]>30)
            assert not np.any(orange&edge), 'Selection outline remains'
        tile=Image.new('RGBA',(128,128),'white')
        draw=ImageDraw.Draw(tile)
        for y in range(0,128,8):
            for x in range(0,128,8):
                if (x//8+y//8)%2==0:
                    draw.rectangle((x,y,x+7,y+7),fill=(224,227,232,255))
        tile=Image.alpha_composite(tile,im)
        ox=(index%6)*160+16; oy=(index//6)*164+8
        sheet.paste(tile.convert('RGB'),(ox,oy))
        ImageDraw.Draw(sheet).text((ox,oy+133),name,fill=(40,40,40))
sheet.save(ROOT/'icons_contact_sheet.png')
(ROOT/'extraction_report.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
assert len(list(OUT.glob('*.png')))==18
print(f'Validated 18 RGBA PNGs, all 128 x 128: {OUT}')
