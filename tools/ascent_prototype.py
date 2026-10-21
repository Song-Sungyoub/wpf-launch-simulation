"""Offline tuning prototype for the C# AscentSimulator (same model). Run: python tools/ascent_prototype.py [json overrides] [-v]
Nuri flight-2 ascent prototype (3-DoF point mass, ECI, spherical Earth + J2-free gravity).
Tunes masses / pitch program against public events: 1st sep 123 s/62 km, fairing 227 s/202 km,
2nd sep 269 s/~265 km, 3rd cutoff ~790 s/700 km circular, inclination ~98.2 deg."""
import math, sys, json

MU = 3.986004418e14; RE = 6378137.0; OMEGA = 7.2921159e-5; G0 = 9.80665

P = dict(
    s1_prop=125.5e3, s1_dry=19.8e3, s1_mdot=1020.0, s1_fsl=4*674.7e3, s1_isp_vac=304.8,
    s2_prop=36.4e3, s2_dry=4.4e3, s2_mdot=254.8, s2_f=788e3,
    s3_prop=11.2e3, s3_dry=1.8e3, s3_mdot=21.55, s3_f=68.7e3,
    fairing=0.75e3, payload=1.5e3,
    kick_t=10.0, kick_end=22.0, kick_deg=0.75,
    coast12=4.0, coast23=4.0, fairing_t=227.0,
    lat=34.431636, lon=127.534411, alt=100.0, az=192.8,
    target_alt=700e3, cd=0.32, inc=98.2, plane_tau=40.0, vz_gain=1.0, vz_tau=30.0, vfrac=0.5, s2_pitchdown=0.0,
)

def vadd(a,b): return [a[0]+b[0],a[1]+b[1],a[2]+b[2]]
def vsub(a,b): return [a[0]-b[0],a[1]-b[1],a[2]-b[2]]
def vmul(a,k): return [a[0]*k,a[1]*k,a[2]*k]
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def norm(a): return math.sqrt(dot(a,a))
def unit(a): n=norm(a); return [a[0]/n,a[1]/n,a[2]/n]
def cross(a,b): return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]

def run(p, verbose=False):
    lat=math.radians(p['lat']); lon=math.radians(p['lon'])
    r=[(RE+p['alt'])*math.cos(lat)*math.cos(lon),(RE+p['alt'])*math.cos(lat)*math.sin(lon),(RE+p['alt'])*math.sin(lat)]
    w=[0,0,OMEGA]; v=cross(w,r)
    up0=unit(r); east=unit(cross([0,0,1],up0)); north=cross(up0,east)
    az=math.radians(p['az']); hdg=vadd(vmul(north,math.cos(az)),vmul(east,math.sin(az)))
    beta=math.pi+math.asin(-math.cos(math.radians(p['inc']))/math.cos(lat)) if False else math.radians(180)-math.asin(math.cos(math.radians(p['inc']))/math.cos(lat))
    ihdg=vadd(vmul(north,math.cos(beta)),vmul(east,math.sin(beta)))
    nplane=unit(cross(r,ihdg))
    m1=p['s1_prop']+p['s1_dry']; m2=p['s2_prop']+p['s2_dry']; m3=p['s3_prop']+p['s3_dry']
    mass=m1+m2+m3+p['fairing']+p['payload']
    stage=1; prop=p['s1_prop']; t=0.0; dt=0.05
    events=[]; fairing_on=True; coast_until=None; maxq=(0,0,0); maxg=0; done=False; sonic=None
    t_ign3=None
    hist=[]
    def alt_of(r): return norm(r)-RE
    while t<1200 and not done:
        h=alt_of(r); up=unit(r)
        vrel=vsub(v,cross(w,r)); vr=norm(vrel)
        rho=1.225*math.exp(-h/8500.0) if h<150e3 else 0.0
        q=0.5*rho*vr*vr
        if q>maxq[0]: maxq=(q,t,h)
        a_sound=340.0 if h<11e3 else 295.0
        if sonic is None and vr>a_sound and t>5: sonic=(t,h)
        # thrust
        thrusting = coast_until is None and prop>0
        F=0; mdot=0
        if thrusting:
            if stage==1:
                pr=math.exp(-h/7200.0); fvac=p['s1_mdot']*p['s1_isp_vac']*G0
                F=fvac-(fvac-p['s1_fsl'])*pr; mdot=p['s1_mdot']
            elif stage==2: F=p['s2_f']; mdot=p['s2_mdot']
            else: F=p['s3_f']; mdot=p['s3_mdot']
        # attitude
        vi=norm(v); vert=dot(v,up); horizv=vsub(v,vmul(up,vert)); vh=norm(horizv)
        if t<p['kick_t']: d=up
        elif t<p['kick_end']:
            ang=math.radians(p['kick_deg'])*(t-p['kick_t'])/(p['kick_end']-p['kick_t'])
            d=unit(vadd(vmul(up,math.cos(ang)),vmul(hdg,math.sin(ang))))
        elif stage<3:
            d=unit(vrel) if h<60e3 else unit(v)   # gravity turn (atmospheric: relative velocity)
            if stage==2:
                # stage-2 steering: pitch the thrust below the velocity vector toward the horizontal
                hd=unit(vsub(d,vmul(up,dot(d,up)))); el=math.asin(max(-1,min(1,dot(d,up))))
                el=max(el-math.radians(p['s2_pitchdown']),math.radians(-5))
                d=unit(vadd(vmul(hd,math.cos(el)),vmul(up,math.sin(el))))
        else:
            # stage 3 guidance: drive vertical speed to reach target altitude as horizontal speed reaches circular
            hdir=unit(horizv); rr=norm(r)
            vcirc=math.sqrt(MU/(RE+p['target_alt']))
            acc=F/mass if F>0 else 0
            tgo=max(20.0,(vcirc-vh)/max(acc,1e-3))
            g=MU/rr/rr; cent=vh*vh/rr
            # ballistic-apex steering: vertical speed that just reaches the target altitude under net gravity
            geff=max(g-cent,0.05); dh=p['target_alt']-h
            vz_need=math.copysign(math.sqrt(2*geff*abs(dh)),dh)*p['vz_gain']
            a_v=(vz_need-vert)/p['vz_tau']
            if vh>vcirc*0.97: a_v+=g-cent   # near circular speed: hold altitude
            a_v=max(-acc*p['vfrac'],min(acc*p['vfrac'],a_v))
            a_h=math.sqrt(max(acc*acc-a_v*a_v,0))
            d=unit(vadd(vmul(up,a_v),vmul(hdir,a_h))) if acc>0 else up
        if t>=p['kick_t']:
            # hold the target orbital plane: remove out-of-plane thrust and steer out-of-plane velocity to zero
            d=unit(vsub(d,vmul(nplane,dot(d,nplane)+dot(v,nplane)/p['plane_tau']/max(F/mass,1e-3))))
        # dynamics
        rr=norm(r); g=vmul(r,-MU/rr**3)
        dragA=math.pi*(3.5/2)**2 if stage==1 else math.pi*(2.6/2)**2
        drag=vmul(unit(vrel),-q*p['cd']*dragA/mass) if vr>0 else [0,0,0]
        a=vadd(vadd(g,drag),vmul(d,F/mass))
        if t>0 or F/mass>norm(g):
            v=vadd(v,vmul(a,dt)); r=vadd(r,vmul(v,dt))
        maxg=max(maxg,(F/mass)/G0 if F>0 else 0)
        prop-=mdot*dt; mass-=mdot*dt
        t+=dt
        if int(t/1)!=int((t-dt)/1): hist.append((round(t),round(h/1000,1),round(vi/1000,3)))
        if fairing_on and t>=p['fairing_t']:
            fairing_on=False; mass-=p['fairing']; events.append(('fairing',t,h,norm(v),dot(v,up)))
        if coast_until is not None and t>=coast_until:
            coast_until=None
            if stage==3: t_ign3=t
        if thrusting and prop<=0:
            if stage==1:
                events.append(('s1_sep',t,h,norm(v),dot(v,up))); mass-=p['s1_dry']; stage=2; prop=p['s2_prop']; coast_until=t+p['coast12']
            elif stage==2:
                events.append(('s2_sep',t,h,norm(v),dot(v,up))); mass-=p['s2_dry']; stage=3; prop=p['s3_prop']; coast_until=t+p['coast23']
            else:
                events.append(('s3_burnout',t,h,norm(v),dot(v,up))); done=True
        if stage==3 and coast_until is None and prop>0:
            rr=norm(r); vh=norm(vsub(v,vmul(up,dot(v,up))))
            # cutoff when orbit perigee reaches target-ish (circular insertion)
            E=dot(v,v)/2-MU/rr; aa=-MU/(2*E); hvec=cross(r,v); e=math.sqrt(max(0,1-dot(hvec,hvec)/(MU*aa)))
            if aa*(1-e)-RE>p['target_alt']-15e3 and h>p['target_alt']-20e3:
                events.append(('s3_cutoff',t,h,norm(v),dot(v,up))); done=True
    rr=norm(r); E=dot(v,v)/2-MU/rr; aa=-MU/(2*E); hvec=cross(r,v)
    e=math.sqrt(max(0,1-dot(hvec,hvec)/(MU*aa))); inc=math.degrees(math.acos(hvec[2]/norm(hvec)))
    res=dict(events=[(n,round(tt,1),round(hh/1000,1),round(vv/1000,2),round(vz/1000,2)) for n,tt,hh,vv,vz in events], perigee=round((aa*(1-e)-RE)/1000,1),
             apogee=round((aa*(1+e)-RE)/1000,1), inc=round(inc,2), v=round(norm(v)/1000,3), prop_left=round(prop),
             maxq=(round(maxq[0]/1000,1),round(maxq[1],1),round(maxq[2]/1000,1)), maxg=round(maxg,2), sonic=sonic and (round(sonic[0],1),round(sonic[1]/1000,1)))
    if verbose: res['hist']=[x for x in hist if x[0]%60==0]
    return res

if __name__=='__main__':
    over=json.loads(sys.argv[1]) if len(sys.argv)>1 else {}
    P.update(over)
    print(json.dumps(run(P, verbose='-v' in sys.argv), ensure_ascii=False))
