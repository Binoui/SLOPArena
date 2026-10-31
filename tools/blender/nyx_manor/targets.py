"""Nyxxie's junk targets. Original procedural junk and gothic targets, centred on Shared target spheres."""
import argparse,json,sys,math
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path[:0]=[str(HERE),str(HERE.parent)]
from stagekit import geo,blender as kit
import parts as K
TARGET_TRIANGLE_BUDGET=18000

def models():
    bat=K.mesh('Bat'); K.bat(bat,s=.63)
    skel=K.mesh('Skeleton');K.skeleton(skel,(0,-1,0),1.05)
    light=K.mesh('RingLight');K.ring(light,(0,.2,0),.48,K.HOT,.055)
    K.rod(light,(0,-1,0),(0,-.28,0),.035,K.INK)
    for a in (0,math.tau/3,math.tau*2/3):K.rod(light,(0,-.7,0),(.37*math.cos(a),-1,.37*math.sin(a)),.025,K.INK)
    giant=K.mesh('GiantBat'); K.bat(giant,(0,.1,0),1.75)
    flamingo=K.mesh('Flamingo')
    K.ring(flamingo,(0,-.6,0),1.1,K.HOT,.32,'xz',None)
    neck=[(.65,-.45,0),(.95,.1,0),(.85,.8,0),(.5,1.35,0),(.10,1.25,0)]
    geo.tube(flamingo,neck,.20,10,K.PINK)
    K.ball(flamingo,(.10,1.30,0),(.36,.28,.26),K.PINK)
    K.rod(flamingo,(-.1,1.3,-.03),(-.45,1.0,-.03),.13,K.WHITE)
    K.rod(flamingo,(-.42,1.03,-.03),(-.49,.89,-.03),.12,K.INK)
    K.ball(flamingo,(.09,1.39,-.25),(.055,.055,.025),K.INK)
    for side in (-1,1):K.ball(flamingo,(side*.85,-.4,.5),(.26,.20,.50),K.PINK)
    flamingo.verts=[(x,y+.4,z) for x,y,z in flamingo.verts]
    card=K.mesh('Cardboard')
    # Printed portrait panel and a cardboard easel. Image binding belongs to the builder.
    K.box(card,(0,.55,0),(1.45,1.55,.055),(.48,.32,.20))
    geo.oriented(card,[(-.45,-.2,-.035),(.45,-.2,-.035),(.62,-.95,-.035),(-.62,-.95,-.035)],(0,0,-1),K.INK)
    for x in (-.24,.24):K.box(card,(x,-1.12,0),(.20,.42,.06),K.INK)
    K.rod(card,(0,-1.3,.04),(0,-.5,.52),.055,(.34,.23,.12))
    K.box(card,(0,-1.3,.25),(.7,.04,.6),(.34,.23,.12))
    printface=K.mesh('Portrait')
    geo.oriented(printface,[(-.70,-.20,-.032),(.70,-.20,-.032),(.70,1.20,-.032),(-.70,1.20,-.032)],(0,0,-1),(1,1,1),uvs=[(0,0),(1,0),(1,1),(0,1)])
    candle=K.mesh('SkullCandle')
    K.box(candle,(0,-.78,0),(.65,.12,.54),K.STONE)
    K.ball(candle,(0,-.36,0),(.38,.38,.29),(.65,.60,.48))
    for x in (-.14,.14):
        K.ball(candle,(x,-.30,-.25),(.105,.13,.06),K.INK)
    K.box(candle,(0,-.59,-.25),(.32,.07,.06),K.INK)
    K.box(candle,(0,.09,0),(.23,.48,.23),K.WHITE)
    for x,y in ((-.1,.14),(.09,.02)):
        K.rod(candle,(x,.23,-.13),(x,y,-.13),.035,K.WHITE)
    K.ball(candle,(0,.48,0),(.11,.24,.11),(.85,.46,.12),'ManorGlow')
    gargoyle=K.mesh('Gargoyle')
    K.box(gargoyle,(0,-1.08,0),(1.4,.25,1.05),K.STONE)
    K.ball(gargoyle,(0,-.34,.06),(.43,.62,.32),K.STONE)
    K.ball(gargoyle,(0,.39,-.05),(.40,.36,.32),(.39,.33,.46))
    for side in (-1,1):
        K.ball(gargoyle,(side*.16,.46,-.34),(.09,.09,.035),(.62,.39,.17),'ManorGlow')
        K.rod(gargoyle,(side*.25,.62,0),(side*.35,1.0,.08),.12,K.STONE)
        K.rod(gargoyle,(side*.34,-.28,-.02),(side*.55,-.89,-.31),.16,K.STONE)
        K.ball(gargoyle,(side*.48,-.91,-.25),(.24,.11,.33),K.STONE)
        pts=[(side*x,y,z) for x,y,z in ((.25,.12,.16),(.68,.78,.21),(1.35,.88,.22),(1.09,.15,.18),(.85,.23,.16),(.61,-.22,.12))]
        geo.oriented(gargoyle,pts,(0,0,-1),K.STONE)
        geo.oriented(gargoyle,[(x,y,z+.12) for x,y,z in reversed(pts)],(0,0,1),K.STONE)
        geo.tube(gargoyle,pts+[pts[0]],.065,6,(.42,.36,.49))
    K.box(gargoyle,(0,.14,-.36),(.28,.10,.08),K.INK)
    return {m.name:m for m in (bat,skel,light,giant,flamingo,card,printface,candle,gargoyle)}

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--out',required=True);ap.add_argument('--report',required=True)
    args=ap.parse_args(sys.argv[sys.argv.index('--')+1:]);groups=models()
    triangles=sum(m.triangle_count for m in groups.values())
    if triangles>TARGET_TRIANGLE_BUDGET:raise ValueError(triangles)
    kit.reset_scene(K.MATERIALS); objects=kit.build_objects(groups,smooth=(),channels=True)
    out=Path(args.out);out.mkdir(parents=True,exist_ok=True);kit.export_fbx(objects,str(out/'NyxManorTargets.fbx'))
    Path(args.report).write_text(json.dumps({'triangles':triangles,'budget':TARGET_TRIANGLE_BUDGET,'objects':{n:m.triangle_count for n,m in groups.items()},'meshHealth':kit.mesh_health(objects)},indent=2)+'\n')
    print('NYX_MANOR_TARGETS',triangles)
if __name__=='__main__':main()
