from pathlib import Path
import json
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'icons03'
src=Image.open(OUT/'307e5c24-9f44-4038-95a3-a6ec915ec42e.png').convert('RGBA')
names=['folder','finance','reading','graduation','study','writing',
       'code','terminal','music','movies','design','painting',
       'medical','first_aid','meditation','work','analytics','kettlebell',
       'fitness','notebook','justice','geography','travel','internet',
       'tools','pets','chemistry','brain','heart','gardening']
xs=[0,211,435,659,883,1107,1335]
ys=[0,285,505,735,955,1178]
preview=Image.new('RGB',(960,820),(245,245,245))
report=[]
for i,name in enumerate(names):
    c,r=i%6,i//6
    cell=src.crop((xs[c],ys[r],xs[c+1],ys[r+1]))
    # Ignore nearly invisible alpha specks in the source when locating content;
    # retain a 3 px guard around the meaningful contour for antialiasing.
    bounds=cell.getchannel('A').point(lambda a: 255 if a>5 else 0).getbbox()
    assert bounds is not None
    bounds=(max(0,bounds[0]-3),max(0,bounds[1]-3),min(cell.width,bounds[2]+3),min(cell.height,bounds[3]+3))
    assert bounds[0]>0 and bounds[1]>0 and bounds[2]<cell.width and bounds[3]<cell.height
    icon=cell.crop(bounds)
    scale=96/max(icon.size)
    size=tuple(round(d*scale) for d in icon.size)
    icon=icon.convert('RGBa').resize(size,Image.Resampling.LANCZOS).convert('RGBA')
    result=Image.new('RGBA',(128,128))
    result.paste(icon,((128-size[0])//2,(128-size[1])//2))
    path=OUT/(name+'.png')
    result.save(path)
    with Image.open(path) as im:
        assert im.size==(128,128) and im.mode=='RGBA'
        b=im.getchannel('A').getbbox()
        assert min(b[0],b[1],128-b[2],128-b[3])>=16
        assert abs(b[0]-(128-b[2]))<=5 and abs(b[1]-(128-b[3]))<=5
    tile=Image.new('RGBA',(128,128),'white')
    d=ImageDraw.Draw(tile)
    for y in range(0,128,8):
        for x in range(0,128,8):
            if (x//8+y//8)%2==0:
                d.rectangle((x,y,x+7,y+7),fill=(225,228,233,255))
    tile=Image.alpha_composite(tile,result)
    px=c*160+16; py=r*164+8
    preview.paste(tile.convert('RGB'),(px,py))
    ImageDraw.Draw(preview).text((px,py+134),name,fill=(30,30,30))
    report.append({'file':name+'.png','source_cell':[xs[c],ys[r],xs[c+1],ys[r+1]],'source_local_bounds':bounds,'output_size':[128,128],'alpha_bounds':b})
preview.save(ROOT/'icons03_preview.png')
(ROOT/'icons03_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(f'Validated {len(report)} centered 128x128 RGBA icons in {OUT}')
