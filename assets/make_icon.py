from PIL import Image, ImageDraw, ImageFilter
import os
ROOT=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S=1024
def lerp(a,b,t): return tuple(int(a[i]+(b[i]-a[i])*t) for i in range(3))
# gradient tile
grad=Image.new("RGB",(S,S))
gp=grad.load()
c1=(23,32,96); c2=(14,170,164)
for y in range(S):
    for x in range(S):
        t=min(1,max(0,(x*0.35+y*0.65)/S))
        gp[x,y]=lerp(c1,c2,t)
mask=Image.new("L",(S,S),0); ImageDraw.Draw(mask).rounded_rectangle([16,16,S-16,S-16],radius=220,fill=255)
img=Image.new("RGBA",(S,S),(0,0,0,0)); img.paste(grad,(0,0),mask)
# subtle top highlight
hl=Image.new("RGBA",(S,S),(0,0,0,0)); hd=ImageDraw.Draw(hl)
for y in range(16,S//2):
    a=int(26*(1-(y-16)/(S//2-16))**1.5)
    hd.line([(16,y),(S-16,y)],fill=(255,255,255,a))
hl.putalpha(Image.composite(hl.split()[3],Image.new("L",(S,S),0),mask))
img=Image.alpha_composite(img,hl)

# drive, drawn on a layer then rotated
L=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(L)
cx=S//2
bw,bh=330,520; bx0=cx-bw//2; by0=400
# connector
cw,ch=210,190; d.rounded_rectangle([cx-cw//2,by0-ch+20,cx+cw//2,by0+30],radius=26,fill=(214,222,235,255))
d.rounded_rectangle([cx-62,by0-ch+70,cx-18,by0-ch+112],radius=6,fill=(40,52,110,255))
d.rounded_rectangle([cx+18,by0-ch+70,cx+62,by0-ch+112],radius=6,fill=(40,52,110,255))
# body
d.rounded_rectangle([bx0,by0,bx0+bw,by0+bh],radius=70,fill=(255,255,255,255))
# ruler ticks on left edge of body
for i in range(9):
    y=by0+70+i*48
    ln=70 if i%4==0 else 40
    d.rounded_rectangle([bx0+34,y-7,bx0+34+ln,y+7],radius=7,fill=(14,170,164,255))
# check mark
pts=[(cx-20,by0+275),(cx+40,by0+340),(cx+130,by0+190)]
d.line(pts,fill=(23,32,96,255),width=58,joint="curve")
for p in [pts[0],pts[2]]:
    d.ellipse([p[0]-29,p[1]-29,p[0]+29,p[1]+29],fill=(23,32,96,255))
k=1.18; big=L.resize((int(S*k),int(S*k)),Image.LANCZOS); o=(big.size[0]-S)//2
L=big.crop((o,o+20,o+S,o+20+S))
L=L.rotate(-18,resample=Image.BICUBIC,center=(cx,S//2+20))
# shadow
sh=Image.new("RGBA",(S,S),(0,0,0,0)); sa=L.split()[3].point(lambda a:int(a*0.35))
sh.putalpha(sa); shadow=Image.new("RGBA",(S,S),(5,10,40,0)); shadow.putalpha(sa)
shadow=shadow.filter(ImageFilter.GaussianBlur(24))
img.alpha_composite(shadow,(18,30))
img.alpha_composite(L)
img.save(ROOT + "/assets/icon_1024.png")
sizes=[(256,256),(128,128),(64,64),(48,48),(32,32),(24,24),(16,16)]
img.save(ROOT + "/src/app.ico",sizes=sizes,bitmap_format="bmp")
img.resize((128,128),Image.LANCZOS).save(ROOT + "/src/logo.png")
img.resize((256,256),Image.LANCZOS).save(ROOT + "/assets/icon_256.png")
# preview sheet
sheet=Image.new("RGBA",(620,280),(240,240,240,255)); x=10
for s in [256,128,64,48,32,16]:
    sheet.alpha_composite(img.resize((s,s),Image.LANCZOS),(x,140-s//2)); x+=s+12
sheet.save(ROOT + "/assets/icon_preview.png")
