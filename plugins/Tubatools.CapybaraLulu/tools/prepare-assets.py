"""下载本轮生成的水豚噜噜素材，裁掉多余透明边、统一尺寸，并合成插件图标。

这些图是以网上噜噜表情包为形象参考、重新生成的原创插画（无第三方水印/署名），
仅用于本机个人美化，不随插件公开分发。重新生成素材后重跑本脚本即可。
"""
import os
import subprocess
from PIL import Image, ImageDraw, ImageFilter

ASSETS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets")
os.makedirs(ASSETS, exist_ok=True)

CHARACTERS = {
    "lulu-smile.png": ("https://aka.doubaocdn.com/s/I7vCS69vb9", 512),
    "lulu-cheer.png": ("https://aka.doubaocdn.com/s/eeSRAVYcKf", 512),
    "lulu-daze.png": ("https://aka.doubaocdn.com/s/QYbIKKzbPj", 512),
    "lulu-sleep.png": ("https://aka.doubaocdn.com/s/qIg1UyhUD8", 640),
    "lulu-wrench.png": ("https://aka.doubaocdn.com/s/qKHHYZWFOm", 512),
    "lulu-laptop.png": ("https://aka.doubaocdn.com/s/1EmK2JPMoh", 512),
    "lulu-head.png": ("https://aka.doubaocdn.com/s/h6oVOSBneu", 384),
}

BACKGROUNDS = {
    "bg-bath.jpg": ("https://aka.doubaocdn.com/s/J3I9pKeZfm", (1920, 1080)),
    "bg-park.jpg": ("https://aka.doubaocdn.com/s/fnw6QJfIRN", (1920, 1080)),
    "bg-desk.jpg": ("https://aka.doubaocdn.com/s/QU5AIn4SVP", (1920, 1080)),
    "bg-night.jpg": ("https://aka.doubaocdn.com/s/NAPlMVtFnO", (1920, 1080)),
}


def download(url, path):
    subprocess.run(["curl.exe", "-sL", "--retry", "3", "-o", path, url], check=True)
    if not os.path.exists(path) or os.path.getsize(path) < 1024:
        raise RuntimeError("下载失败: " + url)


def trim_alpha(image, pad_ratio=0.02):
    alpha = image.getchannel("A")
    box = alpha.getbbox()
    if box is None:
        return image
    left, top, right, bottom = box
    pad = int(max(right - left, bottom - top) * pad_ratio)
    return image.crop((max(0, left - pad), max(0, top - pad),
                       min(image.width, right + pad), min(image.height, bottom + pad)))


def fit(image, longest):
    scale = longest / max(image.width, image.height)
    if scale < 1:
        image = image.resize((round(image.width * scale), round(image.height * scale), ), Image.LANCZOS)
    return image


def process_character(name, url, longest):
    raw = os.path.join(ASSETS, "_raw-" + name)
    download(url, raw)
    image = Image.open(raw).convert("RGBA")
    image = fit(trim_alpha(image), longest)
    image.save(os.path.join(ASSETS, name), optimize=True)
    os.remove(raw)
    print("char", name, image.size)


def process_background(name, url, size):
    raw = os.path.join(ASSETS, "_raw-" + name)
    download(url, raw)
    image = Image.open(raw).convert("RGB")
    image = image.resize(size, Image.LANCZOS)
    image.save(os.path.join(ASSETS, name), quality=88, optimize=True)
    os.remove(raw)
    print("bg", name, image.size)


def make_icon():
    head = Image.open(os.path.join(ASSETS, "lulu-head.png")).convert("RGBA")
    size = 256
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    top, bottom = (255, 238, 214), (255, 196, 128)
    gradient = Image.new("RGBA", (size, size))
    for y in range(size):
        t = y / (size - 1)
        gradient.paste(tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,), (0, y, size, y + 1))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=52, fill=255)
    canvas.paste(gradient, (0, 0), mask)

    # 底部一团柔光，避免图标显得死板
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((28, 150, 228, 268), fill=(255, 255, 255, 90))
    canvas.alpha_composite(glow.filter(ImageFilter.GaussianBlur(18)))

    target = 196
    head = head.resize((round(head.width * target / max(head.width, head.height)),
                        round(head.height * target / max(head.width, head.height))), Image.LANCZOS)
    canvas.alpha_composite(head, ((size - head.width) // 2, (size - head.height) // 2 + 14))
    canvas.save(os.path.join(ASSETS, "icon.png"), optimize=True)
    print("icon", canvas.size)


if __name__ == "__main__":
    for name, (url, longest) in CHARACTERS.items():
        process_character(name, url, longest)
    for name, (url, size) in BACKGROUNDS.items():
        process_background(name, url, size)
    make_icon()
    # 清掉最初试生成的白色边缘残留
    for stale in ("lulu-smile.png.bak",):
        path = os.path.join(ASSETS, stale)
        if os.path.exists(path):
            os.remove(path)
    print("done ->", ASSETS)
