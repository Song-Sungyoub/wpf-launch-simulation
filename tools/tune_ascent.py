"""Random search over the approximated (unpublished) ascent parameters against the published flight-2 events.
Run: python tools/tune_ascent.py [iterations]. Liftoff mass stays at the published 201.5 t (total dry mass fixed)."""
import json, random, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import ascent_prototype as proto

TARGET = dict(s1=(123, 62), fairing=(227, 202), s2=(269, 265), cutoff=(790, 700))
DRY_TOTAL = 26.0e3


def score(r):
    ev = {name: (t, h) for name, t, h, *_ in r["events"]}
    if "s1_sep" not in ev or "s2_sep" not in ev or "fairing" not in ev:
        return 1e9
    cut = ev.get("s3_cutoff") or ev.get("s3_burnout")
    loss = ((ev["s1_sep"][1] - 62) / 2) ** 2 + ((ev["fairing"][1] - 202) / 5) ** 2 + ((ev["s2_sep"][1] - 265) / 8) ** 2
    loss += ((cut[0] - 790) / 15) ** 2
    if r["perigee"] < 685: loss += ((685 - r["perigee"]) / 3) ** 2
    if r["apogee"] > 735: loss += ((r["apogee"] - 735) / 3) ** 2
    loss += ((r["inc"] - 98.2) / 0.05) ** 2
    if r["maxg"] > 4.3: loss += ((r["maxg"] - 4.3) / 0.02) ** 2  # design limit reported after flight 1
    sp = r.get("splash", {})
    loss += ((sp.get("s1", 0) - 413) / 25) ** 2 + ((sp.get("fairing", 0) - 1514) / 80) ** 2 + ((sp.get("s2", 0) - 2800) / 150) ** 2
    return loss


def sample(rng, base=None, scale=1.0):
    def pick(key, lo, hi):
        if base is None: return rng.uniform(lo, hi)
        return min(hi, max(lo, base[key] + rng.gauss(0, (hi - lo) * 0.08 * scale)))
    p = dict(kick_deg=pick("kick_deg", 0.3, 1.2), kick_end=pick("kick_end", 14, 40), s2_pitchdown=pick("s2_pitchdown", 0, 40), s1_loft=pick("s1_loft", 0, 6),
             s1_dry=pick("s1_dry", 14e3, 22e3), s2_dry=pick("s2_dry", 3.4e3, 7.5e3), cd=pick("cd", 0.25, 0.4))
    p["s3_dry"] = DRY_TOTAL - p["s1_dry"] - p["s2_dry"]
    return p if 1.3e3 <= p["s3_dry"] <= 4.0e3 else sample(rng, base, scale)


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 400
    rng = random.Random(7); best, best_p, best_r = 1e18, None, None
    for i in range(n):
        p = sample(rng, best_p if best_p and i > n // 3 else None, scale=1.0 if i < 2 * n // 3 else 0.4)
        cfg = dict(proto.P); cfg.update(p)
        r = proto.run(cfg); s = score(r)
        if s < best:
            best, best_p, best_r = s, p, r
            print(f"{i:4d} loss {s:8.2f} {json.dumps({k: round(v, 3) for k, v in p.items()})}", flush=True)
            print("     ", json.dumps(r), flush=True)
    print(json.dumps({"best": best, "params": best_p, "result": best_r}))


if __name__ == "__main__":
    main()
