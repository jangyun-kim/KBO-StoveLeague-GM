# -*- coding: utf-8 -*-
"""
[TASK-KBO-180] 대구 삼성 라이온즈 파크 런타임 텍스처 크롭/후가공.

입력: ReferenceImages/SetImage (Assets 밖, 커밋 제외)
  - 메인 홈.jpg (1248x1972): 로비 배경 - 라이온즈 파크 외야(흥련봉/IDEAL/IMC COFFEE 광고판·전광판)와 대표 선수 3D 모델.
  - 경기 플레이 메인 시뮬레이션 UI.jpg (2448x1848): 홈플레이트 뒤 하이앵글 - 광고판(흥련봉·링티·IDEAL·IMC COFFEE·POWERADE)이
    라팍_1~3 캡처와 같은 라이온즈 파크다. 중계 화면 상단 그라운드(타구 궤적 좌표계)로 쓴다.
출력: Assets/Resources/Broadcast180/*.png (커밋 포함)
베이크된 게임 UI(스코어버그·버튼·선수 카드·탭 등)는 정규화 합성곱 인페인팅으로 지운다 - 그 자리는 런타임 UI가 다시 덮는다.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "ReferenceImages", "SetImage")
OUT = os.path.join(ROOT, "Assets", "Resources", "Broadcast180")
os.makedirs(OUT, exist_ok=True)


def inpaint(img, boxes):
    mask = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(mask)
    for box in boxes:
        d.rectangle(box, fill=255)
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


def save(img, name):
    img.save(os.path.join(OUT, name), optimize=True)
    print(f"  {name}: {img.size[0]}x{img.size[1]}")


print("[CropLionsParkAssets180] 생성:")

# 1) 로비 배경(메인 홈.jpg 1:1 좌표 유지 - CompyaUiKit 1248x1972 좌표계)
home = Image.open(os.path.join(SRC, "메인 홈.jpg")).convert("RGB")
lobby = inpaint(home, [
    (0, 0, 300, 92),          # 상태바
    (8, 106, 496, 222),       # 프로필 헤더
    (8, 222, 1245, 294),      # 재화 바
    (1032, 108, 1245, 212),   # 채팅/우편
    (8, 310, 622, 442),       # LIVE 업데이트 배너
    (1005, 318, 1242, 652),   # 우측 퀵메뉴
    (905, 1372, 1072, 1528),  # 시그니처 획득 아이콘
    (182, 1528, 1068, 1822),  # 하단 벤토 타일
    (0, 1834, 1248, 1972),    # 하단 5탭
    (18, 930, 40, 1045),      # 스크롤 핸들
])
save(lobby, "lobby_lionspark.png")

# 2) 중계 그라운드(라이온즈 파크 하이앵글) - 1248x850으로 맞춘 뒤 위쪽 60px(하늘)을 덧대 1248x910.
#    측정 좌표(1248x910 기준): 홈플레이트 (622, 703), 외야 펜스 중앙 (622, 318), 파울라인 끝 (0, 435) / (1248, 435).
sim = Image.open(os.path.join(SRC, "경기 플레이 메인 시뮬레이션 UI.jpg")).convert("RGB").resize((1248, 850), Image.LANCZOS)
sim = inpaint(sim, [
    (0, 40, 434, 134),        # 좌상단 스코어버그
    (1082, 40, 1248, 98),     # 화면 잠금/배속/일시정지
    (948, 192, 1242, 242),    # "6번 로하스B'20 현재 타석"
    (0, 622, 302, 800),       # 좌하단 타자 카드
    (946, 622, 1248, 800),    # 우하단 투수 카드
    (16, 800, 175, 834),      # 시각/배터리
    (6, 398, 24, 448),        # 스크롤 핸들
    (480, 836, 770, 850),     # 홈 인디케이터
])
ground = Image.new("RGB", (1248, 910))
sky = sim.crop((0, 0, 1248, 20)).resize((1248, 60), Image.BICUBIC)
ground.paste(sky, (0, 0))
ground.paste(sim, (0, 60))
save(ground, "ground_lionspark.png")
