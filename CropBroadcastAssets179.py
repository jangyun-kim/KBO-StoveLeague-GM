# -*- coding: utf-8 -*-
"""
[TASK-KBO-179] 경기 화면 컴프야V26 1:1 중계형 UI용 런타임 텍스처 크롭/후가공 스크립트.

입력: ReferenceImages/SetImage/*.jpg (Assets 밖, .gitignore - 원본 캡처는 커밋/빌드 제외)
출력: Assets/Resources/Broadcast179/*.png (CompyaMatchView가 Resources.Load<Texture2D>로 읽는다 - 커밋 포함)

좌표는 전부 원본 픽셀 기준이다(세로 캡처 1248x1972, 가로 캡처 2448x1848). 재실행하면 같은 결과가 나온다.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "ReferenceImages", "SetImage")
OUT = os.path.join(ROOT, "Assets", "Resources", "Broadcast179")
os.makedirs(OUT, exist_ok=True)


def load(name):
    return Image.open(os.path.join(SRC, name)).convert("RGB")


def save(img, name):
    img.save(os.path.join(OUT, name), optimize=True)
    print(f"  {name}: {img.size[0]}x{img.size[1]}")


def side_strip_background(img, left_w, right_w, blur=18, size=None, top=0, bottom=None):
    """가운데 UI가 덮인 레퍼런스에서 좌/우 가장자리 띠만 살리고, 가운데는 행마다 두 띠 안쪽 끝 색을 선형 보간해 채운다
    (배경 그라데이션/조명 톤을 원본 그대로 보존 - 가운데는 어차피 패널이 다시 덮는다)."""
    a = np.asarray(img, dtype=np.float32)
    h, w, _ = a.shape
    bottom = h if bottom is None else bottom
    a = a[top:bottom]
    h = a.shape[0]
    out = a.copy()
    left_edge = a[:, left_w - 8:left_w].mean(axis=1)            # (h, 3)
    right_edge = a[:, w - right_w:w - right_w + 8].mean(axis=1)
    span = w - right_w - left_w
    t = np.linspace(0.0, 1.0, span, dtype=np.float32)[None, :, None]
    out[:, left_w:w - right_w] = left_edge[:, None, :] * (1 - t) + right_edge[:, None, :] * t
    res = Image.fromarray(out.clip(0, 255).astype(np.uint8))
    res = res.filter(ImageFilter.GaussianBlur(blur))
    # 가장자리 띠는 블러 전 원본을 다시 얹어 질감 유지(경계만 부드럽게).
    orig = Image.fromarray(a.astype(np.uint8))
    mask = Image.new("L", res.size, 0)
    m = np.zeros((h, w), dtype=np.uint8)
    m[:, :left_w - 16] = 255
    m[:, w - right_w + 16:] = 255
    mask = Image.fromarray(m).filter(ImageFilter.GaussianBlur(10))
    res.paste(orig, (0, 0), mask)
    if size:
        res = res.resize(size, Image.LANCZOS)
    return res


def inpaint(img, mask):
    """정규화 합성곱 인페인팅 - mask(255) 영역을 주변 유효 픽셀의 가우시안 가중 평균으로 채운다(큰 반경 -> 작은 반경)."""
    mask = mask.filter(ImageFilter.MaxFilter(9))
    g = np.asarray(img, dtype=np.float32)
    valid = 1.0 - np.asarray(mask, dtype=np.float32)[..., None] / 255.0
    fill = None
    for radius in (90, 40, 16):
        num = np.asarray(Image.fromarray((g * valid).clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)
        den = np.asarray(Image.fromarray((valid[..., 0] * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)[..., None] / 255.0
        layer = num / np.maximum(den, 1e-3)
        fill = layer if fill is None else np.where(den > 0.25, layer, fill)
    return Image.fromarray((g * valid + fill * (1.0 - valid)).clip(0, 255).astype(np.uint8))


def patch(dst, src, box):
    dst.paste(src.crop(box), box[:2])


def logo_alpha(img, box, bg_sample):
    """흰/연회색 행 배경 위 로고를 크롭해 "크롭 테두리와 연결된 배경색 영역"만 투명하게 만든다 - 로고 안쪽의 흰 면(키움
    원형, 롯데 테두리 등)은 배경과 연결돼 있지 않아 그대로 남는다. 가장자리는 배경색 거리로 안티앨리어싱한다."""
    from PIL import ImageDraw
    crop = img.crop(box)
    a = np.asarray(crop, dtype=np.float32)
    bg = np.asarray(bg_sample, dtype=np.float32)
    dist = np.sqrt(((a - bg) ** 2).sum(axis=2))
    bg_like = Image.fromarray(np.where(dist < 28, 255, 0).astype(np.uint8)).copy()  # fromarray는 읽기 전용 버퍼라 복사 후 칠한다
    w, h = bg_like.size
    for seed in [(x, 0) for x in range(0, w, 6)] + [(x, h - 1) for x in range(0, w, 6)] +                 [(0, y) for y in range(0, h, 6)] + [(w - 1, y) for y in range(0, h, 6)]:
        if bg_like.getpixel(seed) == 255:
            ImageDraw.floodfill(bg_like, seed, 128)
    outside = np.asarray(bg_like) == 128
    soft = np.clip((dist - 8.0) * 9.0, 0, 255)
    alpha = np.where(outside, 0, 255).astype(np.float32)
    # 바깥 영역과 맞닿은 1~2px 경계만 거리 기반 부드러운 알파
    edge = np.asarray(Image.fromarray(outside.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(5))) > 0
    alpha = np.where(edge & ~outside, np.minimum(alpha, soft), alpha)
    res = Image.fromarray(np.dstack([a, alpha]).astype(np.uint8), "RGBA")
    bbox = res.getbbox()
    return res.crop(bbox) if bbox else res


print("[CropBroadcastAssets179] 생성:")

# 1) 상단 하이앵글(부감도) 그라운드 - "경기 하이라이트 플레이_1"(1248x1972)을 베이스로 y 0~850(점수 바 직전)까지 쓴다.
#    좌표를 원본 그대로 유지해(리사이즈/좌우 크롭 없음) CompyaMatchView가 레퍼런스와 같은 정규화 좌표로 스코어버그·로고를
#    덮으면 정확히 겹치게 했다. 베이크된 UI(스코어버그+반복과제 바, "삼진 P30" 라벨, 상태바, 삼성/키움 컬러 사선 띠와 로고)는
#    "_2"(같은 카메라, 라벨 없음)에서 덮거나 정규화 합성곱 인페인팅으로 지운다(지운 자리는 어차피 런타임 UI가 다시 덮는다).
hl1 = load("경기 하이라이트 플레이_1.jpg")
hl2 = load("경기 하이라이트 플레이_2.jpg")
ground = hl1.copy()
patch(ground, hl2, (0, 305, 345, 385))      # "삼진 P30" 라벨 -> _2의 같은 영역(관중석)
ground = ground.crop((0, 0, 1248, 850))
mask = Image.new("L", ground.size, 0)
d = ImageDraw.Draw(mask)
d.rectangle((150, 98, 1178, 366), fill=255)                    # 스코어버그 + 반복과제 진행 바
d.rectangle((30, 20, 200, 95), fill=255)                       # 상태바 시각
d.rectangle((1080, 25, 1248, 95), fill=255)                    # 와이파이/배터리
d.polygon([(0, 630), (40, 630), (165, 850), (0, 850)], fill=255)        # 원정 컬러 사선 띠
d.rectangle((0, 775, 250, 850), fill=255)                      # 원정 로고
d.polygon([(1248, 600), (1205, 600), (1085, 850), (1248, 850)], fill=255)  # 홈 컬러 사선 띠
d.rectangle((1005, 770, 1248, 850), fill=255)                  # 홈 로고
save(inpaint(ground, mask), "ground_highangle.png")

# 2) 직접 플레이 타석 뷰(대구 삼성 라이온즈 파크 - 외야 사자상/전광판 포함) - "라팍_3"(2448x1848, 야간) 가운데를 세로로
#    잘라낸다. 좌상단 스코어버그·스킬 아이콘, 우측 번트/작전 버튼, 하단 자막은 범위 밖이고, 범위 안의 투구수 박스만 인페인팅한다.
#    베이크된 스트라이크 존 프레임(원본 x 1053~1396, y 796~1212)은 CompyaMatchView가 같은 정규화 위치에 존을 다시 그린다.
BATTER_VIEW_BOX = (685, 367, 1861, 1628)
lp = load("라팍_3.jpg").crop(BATTER_VIEW_BOX)
bmask = Image.new("L", lp.size, 0)
ImageDraw.Draw(bmask).rectangle((1322 - 685, 520 - 367, 1560 - 685, 665 - 367), fill=255)  # 투구수(64) 박스
lp = inpaint(lp, bmask)
save(lp, "batter_view.png")
print(f"  strike zone (normalized in batter_view): x {(1053-685)/1176:.3f}~{(1396-685)/1176:.3f}, "
      f"y(top-down) {(796-367)/1261:.3f}~{(1212-367)/1261:.3f}")

# 3) 배경 4종 - 가장자리 띠 + 보간(원본 톤 보존)
save(side_strip_background(load("경기 플레이 유형 선택.jpg"), 150, 150, top=120, bottom=1890, size=(1080, 1920)), "bg_select_type.png")
save(side_strip_background(load("경기 하이라이트 플레이 중 직접 플레이 선택.jpg"), 140, 140, size=(1080, 1920)), "bg_highlight_navy.png")
save(side_strip_background(load("경기 종료 후 결과_1.jpg"), 120, 120, top=300, size=(1080, 1920)), "bg_result_gray.png")

# 4) 이닝 종료 중간 화면(ROUND) 배경 - 로고/점수가 없는 왼쪽 타일 띠 + 좌우 반전(갈매기 무늬로 이음)
inn = load("경기 플레이 중 한 이닝 종료 후 중간 UI.jpg")
left = inn.crop((0, 0, 520, 1848))
tiles = Image.new("RGB", (1040, 1848))
tiles.paste(left, (0, 0))
tiles.paste(left.transpose(Image.FLIP_LEFT_RIGHT), (520, 0))
save(tiles.resize((1080, 1920), Image.LANCZOS), "bg_round_tiles.png")

# 5) 경기 유형 선택 카드 사진 3종(빠른 진행 / 하이라이트 / 풀 플레이)
sel = load("경기 플레이 유형 선택.jpg")
save(sel.crop((176, 590, 460, 960)), "type_quick.png")
save(sel.crop((482, 532, 770, 905)), "type_highlight.png")
save(sel.crop((788, 541, 1072, 960)), "type_full.png")

# 6) 10개 구단 로고 - "경기 종료 후 결과_2"(라운드 결과)의 AWAY/HOME 로고, 행 배경색 기준 알파 추출
res2 = load("경기 종료 후 결과_2.jpg")
rows = [(585, "Samsung", "Kiwoom"), (785, "Hanwha", "KIA"), (980, "SSG", "LG"), (1175, "NC", "KT"), (1372, "Doosan", "Lotte")]
for cy, away, home in rows:
    bg = res2.getpixel((215, cy))
    save(logo_alpha(res2, (232, cy - 66, 392, cy + 48), bg), f"logo_{away}.png")
    save(logo_alpha(res2, (858, cy - 66, 1012, cy + 48), bg), f"logo_{home}.png")
