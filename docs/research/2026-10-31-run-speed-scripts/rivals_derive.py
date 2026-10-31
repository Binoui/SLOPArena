chars = {  # name: dash, init_dash, init_dash_time, air_max, jump, grav, max_fall, ground_friction, dash_stop_time, walk
 "Etalus":(5.0,5.0,14,4.0,11.0,0.55,6.0,0.30,6,3.0),
 "Kragg":(5.75,6.25,12,4.0,12.0,0.60,11.0,0.50,4,3.0),
 "Zetterburn":(6.5,7.0,14,4.0,10.99,0.50,10.0,0.50,4,3.25),
 "Ranno":(7.5,7.5,8,3.0,11.0,0.45,10.0,0.70,4,4.0),
 "Maypul":(9.0,9.0,10,6.0,10.5,0.50,10.0,1.00,6,4.5),
}
H=74.0; reach={"ftilt":140,"jab":105}; plat=(512,640,832)
def airtime(v,g,cap):
    y=0; vy=-v; f=0
    while True:
        vy=min(vy+g,cap); y+=vy; f+=1
        if y>=0: return f
for n,(d,i,it,a,j,g,mf,gf,st,w) in chars.items():
    at=airtime(j,g,mf)
    print(f"{n:11s} run {d} px/f = {d*60:.0f} px/s  BH/s(H=74) {d*60/H:.2f}  ftilt140 {140/d:.1f}f jab105 {105/d:.1f}f  air/run {a/d:.2f}  airtime {at}f={at/60:.2f}s  plat640 {640/d:.0f}f={640/d/60:.2f}s  plat/H={640/H:.1f}  run-stop-by-friction {d/gf:.0f}f dist {d*d/(2*gf):.0f}px")
