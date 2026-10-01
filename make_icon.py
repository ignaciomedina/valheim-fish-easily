#!/usr/bin/env python3
"""Generate the Fish-Easily Thunderstore icon (256x256 PNG, no dependencies)."""
import struct
import zlib
import math

SIZE = 256
SS = 4  # supersample factor
W = SIZE * SS


def lerp(a, b, t):
    return a + (b - a) * t


def mix(c1, c2, t):
    t = max(0.0, min(1.0, t))
    return tuple(lerp(c1[i], c2[i], t) for i in range(3))


def render():
    top = (8, 20, 34)
    bot = (16, 48, 60)
    fish_hi = (58, 232, 200)
    fish_lo = (14, 122, 124)
    buf = bytearray(W * W * 3)

    # water line and hook geometry
    hook_x = 196 * SS
    hook_top = 30 * SS
    hook_bot = 176 * SS
    hook_r = 20 * SS

    for y in range(W):
        for x in range(W):
            # background gradient
            col = mix(top, bot, y / W)

            # subtle water band near the bottom
            if y > W * 0.78:
                wave = math.sin(x / (18.0 * SS)) * 3 * SS
                if y + wave > W * 0.80:
                    col = mix(col, (20, 96, 108), 0.55)

            # fish body: ellipse
            bx, by = 108 * SS, 128 * SS
            rx, ry = 64 * SS, 40 * SS
            nx = (x - bx) / rx
            ny = (y - by) / ry
            if nx * nx + ny * ny <= 1.0:
                col = mix(fish_hi, fish_lo, (y - (by - ry)) / (2 * ry))

            # tail: triangle pointing left
            tx, ty = 46 * SS, 128 * SS
            if x <= tx and abs(y - ty) <= (tx - x) * 0.72:
                col = mix(fish_lo, (10, 92, 100), 0.35)

            # dorsal fin
            if 92 * SS <= x <= 136 * SS:
                fx = (x - 92 * SS) / (44 * SS)
                fin_top = by - ry - math.sin(fx * math.pi) * 26 * SS
                if fin_top <= y <= by - ry + 6 * SS:
                    col = (26, 176, 164)

            # eye
            ex, ey = 150 * SS, 116 * SS
            er = 9 * SS
            if (x - ex) ** 2 + (y - ey) ** 2 <= er * er:
                col = (240, 250, 252)
            if (x - ex) ** 2 + (y - ey) ** 2 <= (er * 0.45) ** 2:
                col = (8, 20, 30)

            # hook: vertical shank + curl
            shank = abs(x - hook_x) <= 3.0 * SS and hook_top <= y <= hook_bot
            dx, dy = x - (hook_x - hook_r), y - hook_bot
            curl = abs(math.hypot(dx, dy) - hook_r) <= 3.0 * SS and dx <= 0
            if shank or curl:
                col = (226, 232, 240)

            i = (y * W + x) * 3
            buf[i] = int(max(0, min(255, col[0])))
            buf[i + 1] = int(max(0, min(255, col[1])))
            buf[i + 2] = int(max(0, min(255, col[2])))

    # box downsample
    out = bytearray(SIZE * SIZE * 3)
    area = SS * SS
    for y in range(SIZE):
        for x in range(SIZE):
            r = g = b = 0
            for sy in range(SS):
                base = ((y * SS + sy) * W + x * SS) * 3
                for sx in range(SS):
                    r += buf[base + sx * 3]
                    g += buf[base + sx * 3 + 1]
                    b += buf[base + sx * 3 + 2]
            i = (y * SIZE + x) * 3
            out[i] = r // area
            out[i + 1] = g // area
            out[i + 2] = b // area
    return out


def write_png(path, rgb):
    raw = bytearray()
    for y in range(SIZE):
        raw.append(0)
        raw += rgb[y * SIZE * 3:(y + 1) * SIZE * 3]

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


if __name__ == "__main__":
    write_png("icon.png", render())
    print("wrote icon.png")
