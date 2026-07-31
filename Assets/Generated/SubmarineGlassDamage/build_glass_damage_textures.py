"""Validate progressive glass-damage albedos and build their normal maps."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parent
STAGE_DIR = ROOT / "Stages"
CANVAS_SIZE = 1024
STAGE_COUNT = 5
MIN_EDGE_MARGIN = 48
VISIBLE_ALPHA_THRESHOLD = 16


def stage_path(stage: int, texture_type: str) -> Path:
    return STAGE_DIR / f"SubmarineGlassDamage_Stage{stage:02d}_{texture_type}.png"


def load_albedo(stage: int) -> Image.Image:
    path = stage_path(stage, "Albedo")
    image = Image.open(path)

    if image.mode != "RGBA":
        raise RuntimeError(f"{path.name} must be RGBA, got {image.mode}")
    if image.size != (CANVAS_SIZE, CANVAS_SIZE):
        raise RuntimeError(
            f"{path.name} must be {CANVAS_SIZE}x{CANVAS_SIZE}, got {image.size}"
        )

    return image


def validate_progression(albedos: list[Image.Image]) -> None:
    previous_visible_pixels = 0

    for stage, albedo in enumerate(albedos, start=1):
        rgba = np.asarray(albedo, dtype=np.uint8)
        alpha = rgba[:, :, 3]
        visible = alpha >= VISIBLE_ALPHA_THRESHOLD
        visible_pixels = int(np.count_nonzero(visible))

        alpha_box = albedo.getchannel("A").getbbox()
        if alpha_box is None:
            raise RuntimeError(f"Stage {stage:02d} contains no visible fracture")

        left, top, right, bottom = alpha_box
        margins = (left, top, CANVAS_SIZE - right, CANVAS_SIZE - bottom)
        if min(margins) < MIN_EDGE_MARGIN:
            raise RuntimeError(
                f"Stage {stage:02d} fracture is too close to an edge: margins={margins}"
            )

        strong_green = (
            visible
            & (rgba[:, :, 1] > 160)
            & (rgba[:, :, 1] > rgba[:, :, 0].astype(np.uint16) * 2)
            & (rgba[:, :, 1] > rgba[:, :, 2].astype(np.uint16) * 2)
        )
        green_pixels = int(np.count_nonzero(strong_green))
        if green_pixels > 32:
            raise RuntimeError(
                f"Stage {stage:02d} contains {green_pixels} likely chroma-key pixels"
            )

        if stage > 1 and visible_pixels <= previous_visible_pixels:
            raise RuntimeError(
                f"Stage {stage:02d} must contain more fracture pixels than "
                f"Stage {stage - 1:02d}"
            )

        print(
            f"Validated Stage {stage:02d}: visible={visible_pixels}, "
            f"margins={margins}, green={green_pixels}"
        )
        previous_visible_pixels = visible_pixels


def build_normal_map(albedo: Image.Image) -> Image.Image:
    rgba = np.asarray(albedo, dtype=np.float32) / 255.0
    alpha = rgba[:, :, 3]
    luminance = (
        rgba[:, :, 0] * 0.2126
        + rgba[:, :, 1] * 0.7152
        + rgba[:, :, 2] * 0.0722
    )

    # Bright fracture lines and shard edges catch light above the pane while
    # dark center chips and openings recess slightly into it.
    height = alpha * (luminance * 0.7 - (1.0 - luminance) * 0.45)
    height_image = Image.fromarray(
        np.uint8(np.clip(height * 0.5 + 0.5, 0, 1) * 255)
    )
    height = np.asarray(
        height_image.filter(ImageFilter.GaussianBlur(0.9)), dtype=np.float32
    ) / 255.0

    gradient_y, gradient_x = np.gradient(height)
    strength = 5.0
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
    albedos = [load_albedo(stage) for stage in range(1, STAGE_COUNT + 1)]
    validate_progression(albedos)

    for stage, albedo in enumerate(albedos, start=1):
        output = stage_path(stage, "Normal")
        build_normal_map(albedo).save(output, optimize=True)
        print(f"Wrote {output.name}")


if __name__ == "__main__":
    main()
