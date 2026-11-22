"""生成托盘图标：圆形声波标记，停止/运行两种状态。纯标准库，无第三方依赖。"""
import zlib, struct, math, os

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'legacy', 'v1')


def lerp(a, b, t):
    return a + (b - a) * t


def render(size, active):
    S = 4
    n = size
    FG = (255, 255, 255)
    if active:
        c1, c2 = (34, 211, 238), (37, 99, 235)      # 青 -> 蓝
    else:
        c1, c2 = (71, 85, 105), (100, 116, 139)     # 石板灰

    R = 0.485
    CX, CY = 0.315, 0.5
    HALF = math.radians(56)
    if n <= 20:
        # 小尺寸只留两道弧，否则糊成一团
        ARC_W = 0.090
        arcs = (0.190, 0.360)
        dot_r = 0.080
        HALF = math.radians(50)
    else:
        ARC_W = 0.055
        arcs = (0.150, 0.252, 0.354)
        dot_r = 0.050
    min_w = 1.30 / n

    px = bytearray()
    inv = 1.0 / n
    for y in range(n):
        for x in range(n):
            ar = ag = ab = aa = 0.0
            solid = 0
            for sy in range(S):
                for sx in range(S):
                    fx = (x + (sx + 0.5) / S) * inv
                    fy = (y + (sy + 0.5) / S) * inv
                    dx, dy = fx - 0.5, fy - 0.5
                    if math.hypot(dx, dy) > R:
                        continue
                    t = max(0.0, min(1.0, (fx + fy) * 0.5))
                    r = lerp(c1[0], c2[0], t)
                    g = lerp(c1[1], c2[1], t)
                    b = lerp(c1[2], c2[2], t)

                    ex, ey = fx - CX, fy - CY
                    ed = math.hypot(ex, ey)
                    white = ed < dot_r
                    if not white:
                        ang = math.atan2(ey, ex)
                        if -HALF <= ang <= HALF:
                            w = ARC_W if ARC_W > min_w else min_w
                            half = w * 0.5
                            for a in arcs:
                                if abs(ed - a) <= half:
                                    white = True
                                    break
                    if white:
                        r, g, b = FG
                    ar += r; ag += g; ab += b
                    solid += 1
            if solid == 0:
                px += bytes((0, 0, 0, 0))
            else:
                px += bytes((int(ar / solid), int(ag / solid), int(ab / solid),
                             int(255 * solid / (S * S))))
    return bytes(px)


def png(w, h, rgba):
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data +
                struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) +
            chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b''))


def bmp_entry(size, rgba):
    """传统 BMP 条目（32bpp BGRA + AND mask），小尺寸用，兼容性最好。"""
    w = h = size
    stride = w * 4
    px = bytearray()
    for y in range(h - 1, -1, -1):          # BMP 自下而上
        row = rgba[y * stride:(y + 1) * stride]
        for x in range(w):
            i = x * 4
            px += bytes((row[i + 2], row[i + 1], row[i], row[i + 3]))
    mask_row = ((w + 31) // 32) * 4
    mask = bytes(mask_row * h)
    bi_size = len(px) + len(mask)
    hdr = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, bi_size, 0, 0, 0, 0)
    return hdr + bytes(px) + mask


def build_ico(images):
    n = len(images)
    head = struct.pack('<HHH', 0, 1, n)
    entries = b''
    blob = b''
    offset = 6 + 16 * n
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blob += data
    return head + entries + blob


for name, active in (('tray-idle.ico', False), ('tray-active.ico', True)):
    imgs = []
    for s in SIZES:
        raw = render(s, active)
        # 48 及以下用 BMP 条目，更大用 PNG，兼容旧版 .NET 的 Icon 加载
        imgs.append((s, bmp_entry(s, raw) if s <= 48 else png(s, s, raw)))
    path = os.path.join(OUT, name)
    with open(path, 'wb') as f:
        f.write(build_ico(imgs))
    print(name, os.path.getsize(path), 'bytes')

# 预览图：两种状态并排
prev = 160
canvas = 360
buf = bytearray(canvas * prev * 4)
for idx, active in enumerate((False, True)):
    img = render(prev, active)
    ox = 20 + idx * 180
    for y in range(prev):
        for x in range(prev):
            si = (y * prev + x) * 4
            if img[si + 3] == 0:
                continue
            di = (y * canvas + ox + x) * 4
            buf[di:di + 4] = img[si:si + 4]
with open(os.path.join(OUT, 'tray-preview.png'), 'wb') as f:
    f.write(png(canvas, prev, bytes(buf)))
print('tray-preview.png ok')
