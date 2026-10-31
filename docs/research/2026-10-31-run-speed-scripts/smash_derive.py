import math
def airtime_ult(H, g, vf, init_frac=0.55):
    v = math.sqrt(2 * g * (1 - init_frac) * H)
    y, f, vy = init_frac * H, 4, v
    while True:
        vy = max(vy - g, -vf); y += vy; f += 1
        if y <= 0: return f
def airtime_64(v0, g, tv):
    y, f, vy = 0.0, 0, v0
    while True:
        y += vy; f += 1; vy = max(vy - g, -tv)
        if y <= 0: return f
# game, char, run, init_dash, traction, air, height, reach, airtime_frames, jumpsquat
U = [
 ("Ultimate","Incineroar (slowest)",1.18,1.76,0.114,0.88,20.9,20.0,airtime_ult(31.6,0.126,1.76),3),
 ("Ultimate","Mario",1.76,1.936,0.102,1.208,14.7,13.5,airtime_ult(36.33,0.087,1.5),3),
 ("Ultimate","Marth",1.964,2.255,0.114,1.071,22.3,25.0,airtime_ult(33.66,0.075,1.58),3),
 ("Ultimate","Bowser",1.971,2.255,0.104,1.155,24.9,20.0,airtime_ult(33.0,0.125,1.77),3),
 ("Ultimate","Fox",2.402,2.09,0.115,1.11,15.6,17.6,airtime_ult(35.0,0.23,2.1),3),
 ("Ultimate","Captain Falcon",2.552,1.98,0.105,1.218,21.4,19.2,airtime_ult(37.31,0.12,1.865),3),
 ("Ultimate","Sonic (fastest)",3.85,2.31,0.138,1.208,12.1,18.3,airtime_ult(35.0,0.09,1.65),3),
 ("SSB64","Luigi (slowest, tied Ness)",40,50,0.7,22.5,505,435,airtime_64(77*0.7+30,2.1,42),3),
 ("SSB64","Mario",44,54,1.5,30,437,380,airtime_64(77*0.7+26,2.4,44),3),
 ("SSB64","Fox",60,81,2.0,36,550,620,airtime_64(77*1.0+23,4.0,60),3),
 ("SSB64","Captain Falcon (fastest)",75,80,1.8,31,674,642,airtime_64(77*1.0+24,3.4,66),4),
]
print("| Game | Character | Run u/f | Body height u | BH/s | Reach u | Reach/BH | Frames to cross reach | Air/run | Full-hop airtime | Stop from run (frames, u, BH) |")
print("|---|---|---|---|---|---|---|---|---|---|---|")
for g,c,run,dash,tr,air,h,reach,at,js in U:
    bhs = run*60/h
    fr = reach/run
    trac = tr*1.25 if g=="SSB64" else tr
    stop_f = run/trac; stop_d = run*run/(2*trac)
    print(f"| {g} | {c} | {run:g} | {h:g} | {bhs:.1f} | {reach:g} | {reach/h:.2f} | {fr:.1f} | {air/run:.2f} | {at} f ({at/60:.2f} s) | {stop_f:.0f} f, {stop_d:.0f} u, {stop_d/h:.1f} BH |")
print()
for g,c,run,*_ in U[:7]: print(f"FD 160 u at full run, {c}: {160/run:.0f} f = {160/run/60:.2f} s")
print("Mario-heights across FD:", round(160/14.7,1))
# SLOPArena context
for n,v in [("Wibou",14),("FightGuy",12),("Manki/Bashie/Salamander",11.5),("Bonk/Nyx",10)]:
    print(f"{n}: capsule BH/s {v/1.7:.1f} (Manki/Bashie 1.5 m: {v/1.5:.1f}); visual 1.2-1.6 m: {v/1.6:.1f}-{v/1.2:.1f}; ticks to cross 1.1-1.9 m reach: {1.1/(v/60):.1f}-{1.9/(v/60):.1f}")
# Melee/Brawl/SSB4 with Ultimate heights (estimate)
other = {"Melee":{"Mario":(1.5,0.86),"Fox":(2.2,0.83),"Captain Falcon":(2.3,1.12),"Marth":(1.8,0.9),"Bowser":(1.5,0.8)},
         "Brawl":{"Mario":(1.5,0.94),"Fox":(2.08,0.89),"Captain Falcon":(2.18,1.18),"Marth":(1.7,1.034),"Bowser":(1.527,1.034),"Sonic":(3.5,1.1092)},
         "SSB4":{"Mario":(1.6,1.15),"Fox":(2.184,0.96),"Captain Falcon":(2.32,1.1),"Marth":(1.785,1.02),"Bowser":(1.792,1.0),"Sonic":(3.5,1.15)}}
H={"Mario":14.7,"Fox":15.6,"Captain Falcon":21.4,"Marth":22.3,"Bowser":24.9,"Sonic":12.1}
R={"Mario":13.5,"Fox":17.6,"Captain Falcon":19.2,"Marth":25.0,"Bowser":20.0,"Sonic":18.3}
for gname,d in other.items():
    for c,(run,air) in d.items():
        print(f"{gname} {c}: run {run}, BH/s {run*60/H[c]:.1f}, frames to cross Ult reach {R[c]/run:.1f}, air/run {air/run:.2f}")
