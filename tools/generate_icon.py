from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SCALE = 4
SIZE = 512
canvas = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (0, 0, 0, 0))
d = ImageDraw.Draw(canvas)

def pts(values):
    return [(int(x * SCALE), int(y * SCALE)) for x, y in values]

def line(values, fill, width, joint="curve"):
    d.line(pts(values), fill=fill, width=width * SCALE, joint=joint)

# Rounded dark tile.
d.rounded_rectangle((0, 0, SIZE * SCALE - 1, SIZE * SCALE - 1), radius=112 * SCALE, fill="#101318")

# Box lid and body.
lid = pts([(112, 218), (256, 146), (400, 218), (256, 290)])
d.polygon(lid, fill="#26331f")
line([(112, 218), (256, 146), (400, 218), (256, 290), (112, 218)], "#8bff4d", 22)
line([(112, 218), (112, 372), (256, 444), (400, 372), (400, 218)], "#8bff4d", 22)
line([(256, 290), (256, 444)], "#8bff4d", 22)

# Compression arrows moving data into the box.
for x in (174, 338):
    line([(x, 82), (x, 186)], "#f2f3f5", 24)
    line([(x - 34, 150), (x, 186), (x + 34, 150)], "#f2f3f5", 24)

image = canvas.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
png_path = ROOT / "assets" / "vkorobku-icon.png"
ico_path = ROOT / "src" / "vKOROBKU.App" / "Assets" / "vkorobku.ico"
png_path.parent.mkdir(parents=True, exist_ok=True)
ico_path.parent.mkdir(parents=True, exist_ok=True)
image.save(png_path, optimize=True)
image.save(ico_path, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(png_path)
print(ico_path)
