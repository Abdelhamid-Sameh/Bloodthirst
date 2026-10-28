"""Blood bar art for Bloodthirst: SVG layers rendered to transparent PNGs at 2x.
Every main layer shares one 660x170 design canvas so they stack with no offsets in Unity."""
import math, os, re, sys
from playwright.sync_api import sync_playwright

OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
SVG_DIR = os.path.join(OUT, "svg")
os.makedirs(SVG_DIR, exist_ok=True)
W, H, SCALE = 660, 170, 2

# ---------- geometry (design units, y down) ----------
CX, CY, R = 84, 86, 60            # blood moon
S = (98, 86)                      # shoulder: wing pivot
E = (196, 54)                     # elbow
WR = (338, 38)                    # wrist
TIP = (640, 46)                   # wing tip
F2, F3, F4 = (566, 86), (470, 100), (372, 108)
BODY = (104, 114)                 # trailing edge meets the body
ARM1_C, ARM2_C, LEAD_C = (140, 62), (262, 40), (492, 28)

def P(p): return f"{p[0]:.2f},{p[1]:.2f}"

def scallop(a, b, depth):
    """Control point pulling the trailing edge up between two finger tips."""
    mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    nx, ny = dy / L, -dx / L
    if ny > 0: nx, ny = -nx, -ny
    return (mx + nx * L * depth, my + ny * L * depth)

def membrane_path():
    d = f"M{P(S)} Q{P(ARM1_C)} {P(E)} Q{P(ARM2_C)} {P(WR)} Q{P(LEAD_C)} {P(TIP)} "
    pts = [TIP, F2, F3, F4, BODY]
    for a, b in zip(pts, pts[1:]):
        d += f"Q{P(scallop(a, b, 0.12 if b == BODY else 0.2))} {P(b)} "
    return d + "Z"
MEM = membrane_path()

def quad_pt(a, c, b, t):
    return ((1-t)**2*a[0] + 2*(1-t)*t*c[0] + t*t*b[0], (1-t)**2*a[1] + 2*(1-t)*t*c[1] + t*t*b[1])
def curve(a, c, b, n=24): return [quad_pt(a, c, b, i/n) for i in range(n+1)]
def finger(a, b, bend): return curve(a, ((a[0]+b[0])/2, (a[1]+b[1])/2 - bend), b)

def tapered(points, w0, w1):
    """Polygon for a bone along a polyline, width tapering w0 -> w1."""
    n = len(points); left, right = [], []
    for i, p in enumerate(points):
        a, b = points[max(0, i-1)], points[min(n-1, i+1)]
        dx, dy = b[0]-a[0], b[1]-a[1]; L = math.hypot(dx, dy) or 1
        nx, ny = -dy/L, dx/L; w = (w0 + (w1-w0) * i/(n-1)) / 2
        left.append((p[0]+nx*w, p[1]+ny*w)); right.append((p[0]-nx*w, p[1]-ny*w))
    return "M" + " L".join(P(p) for p in left + right[::-1]) + " Z"

BONES = [
    tapered(curve(S, ARM1_C, E), 10, 8),
    tapered(curve(E, ARM2_C, WR), 8, 6.5),
    tapered(curve(WR, LEAD_C, TIP), 6, 1.6),
    tapered(finger(WR, F2, 14), 4.6, 1.2),
    tapered(finger(WR, F3, 10), 4.6, 1.2),
    tapered(finger(WR, F4, 6), 4.6, 1.3),
]

def mirror_path(d):
    """Mirror a path of absolute x,y pairs about the moon's centre line."""
    return re.sub(r"(-?\d+\.?\d*),(-?\d+\.?\d*)", lambda m: f"{2*CX - float(m.group(1)):.2f},{m.group(2)}", d)

# Bat body facing us: right half as (end, control-or-None) segments from the ear dip down to the crotch.
RIGHT_HALF = [
    ((97, 31), (90, 52)),      # inner ear up to tip
    ((104, 60), (107, 44)),    # outer ear down
    ((97, 79), (104, 70)),     # cheek to jaw
    ((103, 90), (101, 84)),    # neck to shoulder
    ((97, 118), (104, 106)),   # flank
    ((102, 129), None), ((97, 126), None), ((95, 131), None), ((91, 124), None),  # clawed foot
    ((84, 122), (88, 121)),
]
def body_path():
    start = (84, 60)
    d = f"M{P(start)}"
    for end, c in RIGHT_HALF:
        d += f" Q{P(c)} {P(end)}" if c else f" L{P(end)}"
    ends = [start] + [e for e, _ in RIGHT_HALF]
    m = lambda p: (2*CX - p[0], p[1])
    for i in range(len(RIGHT_HALF) - 1, -1, -1):   # walk back up the mirrored half
        c = RIGHT_HALF[i][1]; to = m(ends[i])
        d += f" Q{P(m(c))} {P(to)}" if c else f" L{P(to)}"
    return d + " Z"
BODY_D = body_path()

# Folded wing hanging on the bat's left side; mirrored for the right while the bar is closed.
FOLDED_L = ("M70,90 Q58,76 44,62 L38,47 L48,58 Q52,62 46,66 "
            "Q36,92 28,132 Q34,124 38,138 Q44,126 50,140 Q55,128 60,136 "
            "Q66,122 72,118 Q68,104 70,90 Z")
FOLDED_BONES_L = "M46,66 Q36,92 28,132 M46,66 Q42,96 38,138 M46,66 Q48,98 50,140 M46,66 Q54,100 60,136"

def svg(body, defs="", w=W, h=H):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w*SCALE}" height="{h*SCALE}" viewBox="0 0 {w} {h}">'
            f'<defs>{defs}</defs>{body}</svg>')

SIL, RIM = "#0a0205", "#8e1520"   # silhouette black, red rim light
GLOW_F = '<filter id="glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="2.2"/></filter>'
SOFT_F = '<filter id="soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="0.9"/></filter>'
SHADOW_F = ('<filter id="shadow" x="-10%" y="-30%" width="120%" height="160%">'
            '<feGaussianBlur in="SourceAlpha" stdDeviation="3"/><feOffset dy="2"/>'
            '<feComponentTransfer><feFuncA type="linear" slope="0.7"/></feComponentTransfer>'
            '<feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>')

def rimmed(d):
    return f'<path d="{d}" fill="{RIM}" transform="translate(0,-1.3)"/><path d="{d}" fill="{SIL}"/>'

def folded(d, bones):
    return ('<g filter="url(#shadow)">' + rimmed(d) +
            f'<path d="{bones}" fill="none" stroke="#2a0a0e" stroke-width="1.2"/></g>')

layers = {}
layers["BloodBar_Moon"] = svg(
    f'<circle cx="{CX}" cy="{CY}" r="{R+20}" fill="url(#halo)"/>'
    f'<circle cx="{CX}" cy="{CY}" r="{R}" fill="url(#moon)"/>'
    '<g filter="url(#soft)" fill="#5c0309" opacity="0.32">'
    f'<circle cx="{CX+26}" cy="{CY-30}" r="9"/><circle cx="{CX-34}" cy="{CY+18}" r="12"/>'
    f'<circle cx="{CX+30}" cy="{CY+28}" r="7"/><circle cx="{CX-18}" cy="{CY-38}" r="5"/>'
    f'<circle cx="{CX+44}" cy="{CY+2}" r="4"/></g>'
    f'<circle cx="{CX}" cy="{CY}" r="{R}" fill="url(#terminator)"/>'
    f'<circle cx="{CX}" cy="{CY}" r="{R-0.8}" fill="none" stroke="#ffb08a" stroke-opacity="0.35" stroke-width="1.2"/>',
    defs=SOFT_F +
    '<radialGradient id="moon" cx="0.38" cy="0.34" r="0.75">'
    '<stop offset="0" stop-color="#ffc08a"/><stop offset="0.35" stop-color="#f2502e"/>'
    '<stop offset="0.75" stop-color="#a3121a"/><stop offset="1" stop-color="#55040b"/></radialGradient>'
    '<radialGradient id="terminator" cx="0.75" cy="0.8" r="0.7">'
    '<stop offset="0" stop-color="#1a0004" stop-opacity="0.55"/><stop offset="1" stop-color="#1a0004" stop-opacity="0"/></radialGradient>'
    '<radialGradient id="halo" cx="0.5" cy="0.5" r="0.5">'
    f'<stop offset="{R/(R+20):.3f}" stop-color="#ff3b26" stop-opacity="0.55"/>'
    '<stop offset="0.86" stop-color="#ff2a1a" stop-opacity="0.18"/>'
    '<stop offset="1" stop-color="#ff2a1a" stop-opacity="0"/></radialGradient>')

layers["BloodBar_Bat"] = svg(
    folded(FOLDED_L, FOLDED_BONES_L) + '<g filter="url(#shadow)">' + rimmed(BODY_D) +
    '<g fill="#f4e6dc"><path d="M80.3,79.5 L82.6,79.5 L81.4,84 Z"/><path d="M87.7,79.5 L85.4,79.5 L86.6,84 Z"/></g></g>',
    defs=SHADOW_F)

layers["BloodBar_Eyes"] = svg(
    '<g filter="url(#glow)" fill="#ff4a14"><ellipse cx="77.5" cy="68.5" rx="5" ry="3.4"/><ellipse cx="90.5" cy="68.5" rx="5" ry="3.4"/></g>'
    '<g fill="#ffd77a"><ellipse cx="77.5" cy="68.5" rx="3" ry="1.7" transform="rotate(14 77.5 68.5)"/>'
    '<ellipse cx="90.5" cy="68.5" rx="3" ry="1.7" transform="rotate(-14 90.5 68.5)"/></g>',
    defs=GLOW_F)

layers["BloodBar_FoldedWing"] = svg(folded(mirror_path(FOLDED_L), mirror_path(FOLDED_BONES_L)), defs=SHADOW_F)

VEINS = ["M150,74 Q170,92 160,118", "M240,56 Q250,84 236,122", "M300,48 Q330,70 318,124",
         "M410,50 Q430,70 420,112", "M520,52 Q530,70 548,92", "M200,90 Q230,96 250,114",
         "M360,62 Q390,80 400,118", "M460,64 Q470,84 488,108"]
def veins(stroke, opacity, width=1.1):
    return "".join(f'<path d="{v}" fill="none" stroke="{stroke}" stroke-opacity="{opacity}" stroke-width="{width}"/>' for v in VEINS)
MEMCLIP = f'<clipPath id="memclip"><path d="{MEM}"/></clipPath>'

layers["BloodBar_Membrane"] = svg(
    f'<g filter="url(#shadow)"><path d="{MEM}" fill="url(#glass)"/></g>'
    f'<g clip-path="url(#memclip)">{veins("#b0202c", 0.22)}</g>',
    defs=SHADOW_F + MEMCLIP +
    '<linearGradient id="glass" x1="0" y1="0" x2="0" y2="1">'
    '<stop offset="0.2" stop-color="#2a070c" stop-opacity="0.92"/>'
    '<stop offset="0.8" stop-color="#10020a" stop-opacity="0.9"/></linearGradient>')

GLOSS = f"M110,86 Q150,66 200,60 Q268,48 340,46 Q488,38 620,50"
layers["BloodBar_Fill"] = svg(
    f'<path d="{MEM}" fill="url(#blood)"/>'
    f'<g clip-path="url(#memclip)">{veins("#2a0004", 0.45, 1.4)}'
    f'<path d="{GLOSS}" fill="none" stroke="#ffe0d0" stroke-opacity="0.45" stroke-width="3" filter="url(#soft)"/></g>',
    defs=SOFT_F + MEMCLIP +
    '<linearGradient id="blood" gradientUnits="userSpaceOnUse" x1="0" y1="34" x2="0" y2="128">'
    '<stop offset="0" stop-color="#ff5240"/><stop offset="0.3" stop-color="#d0121b"/>'
    '<stop offset="0.75" stop-color="#7a030c"/><stop offset="1" stop-color="#3c0006"/></linearGradient>')

layers["BloodBar_Ghost"] = svg(f'<path d="{MEM}" fill="#ffffff"/>')

thumb = f"M{P((WR[0]-4, WR[1]))} Q{P((WR[0]-6, WR[1]-12))} {P((WR[0]+4, WR[1]-17))} Q{P((WR[0]+1, WR[1]-9))} {P((WR[0]+5, WR[1]-1))} Z"
tipclaw = f"M{P((TIP[0]-6, TIP[1]-2.5))} Q{P((TIP[0]+8, TIP[1]-6))} {P((TIP[0]+14, TIP[1]+2))} Q{P((TIP[0]+5, TIP[1]-1))} {P((TIP[0]-6, TIP[1]+1.5))} Z"
layers["BloodBar_Bones"] = svg(
    f'<path d="{MEM}" fill="none" stroke="{SIL}" stroke-width="3.2" stroke-linejoin="round"/>'
    + "".join(rimmed(b) for b in BONES) +
    f'<circle cx="{WR[0]}" cy="{WR[1]}" r="6.5" fill="{RIM}"/><circle cx="{WR[0]}" cy="{WR[1]+0.8}" r="6" fill="{SIL}"/>'
    f'<circle cx="{E[0]}" cy="{E[1]}" r="5.5" fill="{SIL}"/>'
    f'<path d="{thumb}" fill="#e8d8cc"/><path d="{tipclaw}" fill="#e8d8cc"/>')

extras = {
    "BloodBar_EdgeGlow": (24, H, '<rect width="24" height="170" fill="url(#g)"/>',
        '<linearGradient id="g" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity="0"/>'
        '<stop offset="0.5" stop-color="#fff" stop-opacity="1"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>'),
    "BloodBar_Drop": (16, 22, '<path d="M8,1 C10,7 14,11 14,15 A6,6 0 0 1 2,15 C2,11 6,7 8,1 Z" fill="#fff"/>', ""),
    "BloodBar_Vignette": (480, 270, '<rect width="480" height="270" fill="url(#v)"/>',
        '<radialGradient id="v" cx="0.5" cy="0.5" r="0.72"><stop offset="0.55" stop-color="#fff" stop-opacity="0"/>'
        '<stop offset="0.85" stop-color="#fff" stop-opacity="0.55"/><stop offset="1" stop-color="#fff" stop-opacity="1"/></radialGradient>'),
}

with sync_playwright() as pw:
    browser = pw.chromium.launch()
    page = browser.new_page()
    def render(name, markup, w, h):
        with open(os.path.join(SVG_DIR, name + ".svg"), "w") as f: f.write(markup)
        page.set_viewport_size({"width": w*SCALE, "height": h*SCALE})
        page.set_content(f'<html><body style="margin:0;background:transparent">{markup}</body></html>')
        page.screenshot(path=os.path.join(OUT, name + ".png"), omit_background=True,
                        clip={"x": 0, "y": 0, "width": w*SCALE, "height": h*SCALE})
    for n, m in layers.items(): render(n, m, W, H)
    for n, (w, h, body, defs) in extras.items(): render(n, svg(body, defs, w, h), w, h)
    browser.close()

print(f"canvas={W}x{H} slotXMin={S[0]} slotXMax={TIP[0]} shoulder={S} moon=({CX},{CY})")
