"""Build cumulative submarine damage decal textures from five RGBA motifs."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parent
MOTIF_DIR = ROOT / "Motifs"
OUTPUT_DIR = ROOT / "Stages"
CANVAS_SIZE = 1024

# Each tuple is: motif name, target width, rotation degrees, center X, center Y.
# The positions intentionally leave each new mark visible instead of replacing
# the previous stage's mark.
PLACEMENTS = (
    ("A", 850, -3, 510, 530),
    ("B", 350, -18, 280, 385),
    ("C", 300, 8, 750, 325),
    ("D", 430, -12, 720, 700),
    ("E", 610, 2, 500, 525),
)


def load_and_place(name: str, width: int, angle: float, center_x: int, center_y: int) -> Image.Image:
    source = Image.open(MOTIF_DIR / f"DamageMotif_{name}.png").convert("RGBA")
    alpha_box = source.getchannel("A").getbbox()
    if alpha_box is None:
        raise RuntimeError(f"Motif {name} contains no visible pixels")

    source = source.crop(alpha_box)
    height = max(1, round(source.height * width / source.width))
    source = source.resize((width, height), Image.Resampling.LANCZOS)
    source = source.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)

    layer = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (0, 0, 0, 0))
    x = round(center_x - source.width / 2)
    y = round(center_y - source.height / 2)
    layer.alpha_composite(source, (x, y))
    return layer


def build_normal_map(albedo: Image.Image) -> Image.Image:
    rgba = np.asarray(albedo, dtype=np.float32) / 255.0
    alpha = rgba[:, :, 3]
    luminance = (
        rgba[:, :, 0] * 0.2126
        + rgba[:, :, 1] * 0.7152
        + rgba[:, :, 2] * 0.0722
    )

    # Dark crack interiors become recesses. Bright exposed-metal rims sit a
    # little higher, which gives the decal readable surface relief in URP.
    height = alpha * (luminance * 0.35 - (1.0 - luminance) * 0.65)
    height_image = Image.fromarray(np.uint8(np.clip(height * 0.5 + 0.5, 0, 1) * 255))
    height = np.asarray(height_image.filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255.0

    gradient_y, gradient_x = np.gradient(height)
    strength = 7.0
    normal_x = -gradient_x * strength
    normal_y = -gradient_y * strength
    normal_z = np.ones_like(normal_x)
    length = np.sqrt(normal_x * normal_x + normal_y * normal_y + normal_z * normal_z)

    normal = np.stack(
        (
            normal_x / length * 0.5 + 0.5,
            normal_y / length * 0.5 + 0.5,
            normal_z / length * 0.5 + 0.5,
        ),
        axis=2,
    )
    normal[alpha <= 0.001] = (0.5, 0.5, 1.0)
    normal_rgba = np.concatenate((normal, np.ones_like(alpha[:, :, None])), axis=2)
    return Image.fromarray(np.uint8(np.clip(normal_rgba, 0, 1) * 255), "RGBA")


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    cumulative = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (0, 0, 0, 0))

    for stage, placement in enumerate(PLACEMENTS, start=1):
        cumulative = Image.alpha_composite(cumulative, load_and_place(*placement))
        albedo_path = OUTPUT_DIR / f"SubmarineDamage_Stage{stage:02d}_Albedo.png"
        normal_path = OUTPUT_DIR / f"SubmarineDamage_Stage{stage:02d}_Normal.png"
        cumulative.save(albedo_path, optimize=True)
        build_normal_map(cumulative).save(normal_path, optimize=True)
        print(f"Wrote {albedo_path.name} and {normal_path.name}")


if __name__ == "__main__":
    main()
