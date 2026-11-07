"""Renders tools/icon/icon.svg to src/Tamarin.App/Assets/tamarin.ico (16..256 px). Needs Playwright + Pillow."""
import io, pathlib
from playwright.sync_api import sync_playwright
from PIL import Image

root = pathlib.Path(__file__).resolve().parents[2]
svg = (root / "tools/icon/icon.svg").read_text()
out = root / "src/Tamarin.App/Assets/tamarin.ico"
out.parent.mkdir(parents=True, exist_ok=True)

with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 256, "height": 256})
    pg.set_content(f"<html><body style='margin:0;background:transparent'>{svg}</body></html>")
    png = pg.screenshot(omit_background=True)
    b.close()

img = Image.open(io.BytesIO(png)).convert("RGBA")
img.save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("wrote", out, out.stat().st_size, "bytes")
