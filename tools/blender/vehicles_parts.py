"""Wheels, shared by the fighting vehicles and the cars."""
import math
from meshkit import *


def tyre(name, r, w, tread=True):
    t = Part(name)
    t.lathe((-w / 2, 0, 0), (w / 2, 0, 0), [(0, r * 0.6), (0.03, r * 0.9), (0.14, r), (0.86, r), (0.97, r * 0.9), (1, r * 0.6)], segs=20, kind=PAINT)
    # tread blocks (left off the small wheels of cars, where nobody would see them)
    for i in range(20 if tread else 0):
        a = i * math.tau / 20
        c = (0, math.cos(a) * r, math.sin(a) * r)
        t.box(c, (w * 0.8, 0.04, r * 0.16), PAINT, 0, None, Matrix.Rotation(a - math.pi / 2, 3, "X").transposed())
    return t


def hub(name, r, w, out=-1):
    """Wheel disc; 'out' is the side (-1 = -X) that faces away from the vehicle."""
    h = Part(name)
    h.cyl((-w / 2 * 0.9, 0, 0), (w / 2 * 0.9, 0, 0), r * 0.62, segs=16, kind=FRAME)
    h.cyl((out * w / 2 * 0.9, 0, 0), (out * (w / 2 + 0.05), 0, 0), r * 0.3, segs=10, kind=SOFT)
    h.bolts([(out * w / 2 * 0.9, math.cos(i * math.tau / 6) * r * 0.46, math.sin(i * math.tau / 6) * r * 0.46) for i in range(6)], (out, 0, 0), r=r * 0.06, h=0.03)
    return h
