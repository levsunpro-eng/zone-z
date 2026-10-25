"""Create restrained, repeatable world-surface normal and AO maps.

Run with Python + Pillow + NumPy. Only companion textures and their import
metadata are written; the original albedos are never modified. The companions
are estimates of shallow surface relief, not photogrammetry height data.
"""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import re

import numpy as np
from PIL import Image


SURFACES = {
    # Fine/mid filters (x, y), middle-detail weight, 95th-percentile slope, AO.
    "weathered-concrete": ((0.65, 0.65), (6.0, 6.0), 0.18, 0.26, 0.13),
    "worn-asphalt": ((0.65, 0.65), (7.0, 7.0), 0.26, 0.36, 0.18),
    "dry-park-ground": ((0.90, 0.90), (8.0, 8.0), 0.24, 0.30, 0.17),
    # The photographed timber grain runs vertically. Smooth along it gently
    # while retaining across-grain groove gradients, without invented stripes.
    "weathered-timber": ((0.55, 1.30), (4.0, 9.0), 0.22, 0.32, 0.19),
}


def periodic_component(source: np.ndarray) -> np.ndarray:
    """Remove boundary discontinuity with a periodic/smooth decomposition."""
    boundary = np.zeros_like(source)
    boundary[0, :] = source[-1, :] - source[0, :]
    boundary[-1, :] = source[0, :] - source[-1, :]
    boundary[:, 0] += source[:, -1] - source[:, 0]
    boundary[:, -1] += source[:, 0] - source[:, -1]
    rows, columns = source.shape
    fy = np.fft.fftfreq(rows)
    fx = np.fft.fftfreq(columns)
    laplacian = (
        2.0 * np.cos(2.0 * np.pi * fy[:, None])
        + 2.0 * np.cos(2.0 * np.pi * fx[None, :])
        - 4.0
    )
    laplacian[0, 0] = 1.0
    smooth_spectrum = np.fft.fft2(boundary) / laplacian
    smooth_spectrum[0, 0] = 0.0
    return source - np.fft.ifft2(smooth_spectrum).real


def wrapped_blur(source: np.ndarray, sigma_x: float, sigma_y: float) -> np.ndarray:
    """FFT Gaussian filtering has repeat wrap on both axes, including edges."""
    fy = np.fft.fftfreq(source.shape[0])[:, None]
    fx = np.fft.fftfreq(source.shape[1])[None, :]
    kernel = np.exp(-2.0 * np.pi**2 * (sigma_x**2 * fx**2 + sigma_y**2 * fy**2))
    return np.fft.ifft2(np.fft.fft2(source) * kernel).real


def wrapped_sobel(height: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Central Sobel derivatives; every lookup wraps across the texture seam."""
    dx = np.zeros_like(height)
    dy = np.zeros_like(height)
    for shift, weight in ((-1, 1.0), (0, 2.0), (1, 1.0)):
        row = np.roll(height, shift, axis=0)
        column = np.roll(height, shift, axis=1)
        dx += weight * (np.roll(row, -1, axis=1) - np.roll(row, 1, axis=1))
        dy += weight * (np.roll(column, -1, axis=0) - np.roll(column, 1, axis=0))
    return dx / 8.0, dy / 8.0


def make_relief(rgb: np.ndarray, settings: tuple) -> tuple[np.ndarray, np.ndarray]:
    fine_sigma, mid_sigma, mid_weight, desired_slope, ao_depth = settings
    # Work in luminance without an aggressive gamma curve that would overstate
    # every dark color spot as a deep pit. Broad albedo variation is removed.
    luminance = rgb @ np.array([0.2126, 0.7152, 0.0722])
    periodic = periodic_component(luminance)
    fine = wrapped_blur(periodic, *fine_sigma)
    middle = wrapped_blur(periodic, *mid_sigma)
    broad = wrapped_blur(periodic, 26.0, 26.0)
    height = (1.0 - mid_weight) * (fine - middle) + mid_weight * (middle - broad)
    dx, dy = wrapped_sobel(height)
    magnitude = np.hypot(dx, dy)
    strength = desired_slope / max(float(np.quantile(magnitude, 0.95)), 1.0e-6)
    dx *= strength
    dy *= strength
    # Limit isolated luminance outliers to shallow relief (under 29 degrees).
    cap = np.maximum(1.0, np.hypot(dx, dy) / 0.55)
    dx /= cap
    dy /= cap
    # Unity expects an OpenGL tangent normal with positive Y. Image rows grow
    # downwards while texture V grows upwards, so the row derivative is +Y.
    normal = np.stack((-dx, dy, np.ones_like(height)), axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    normal_pixels = np.rint((normal * 0.5 + 0.5) * 255.0).clip(0, 255).astype(np.uint8)

    # Local concavity only. AO should shade fine cavities, never stamp the
    # source photograph's broad color marks into a second baked shadow layer.
    cavity = (
        0.65 * np.maximum(wrapped_blur(height, 2.0, 2.0) - height, 0.0)
        + 0.35 * np.maximum(wrapped_blur(height, 6.0, 6.0) - height, 0.0)
    )
    cavity_scale = max(float(np.quantile(cavity, 0.97)), 1.0e-6)
    occlusion = 1.0 - np.clip(cavity / cavity_scale * ao_depth, 0.0, ao_depth * 1.2)
    # Standard.shader samples occlusion from G. RGB copies also support other
    # conventional AO readers and avoid importer single-channel assumptions.
    ao_pixels = np.repeat(np.rint(occlusion[..., None] * 255.0).astype(np.uint8), 3, axis=2)
    return normal_pixels, ao_pixels


def write_meta(destination: Path, template: str, normal: bool) -> None:
    guid = hashlib.md5(("zonez-world-relief-v1/" + destination.name).encode("utf-8")).hexdigest()
    changes = {
        "guid": guid,
        "sRGBTexture": "0",
        "textureType": "1" if normal else "0",
        "convertToNormalMap": "0",
        "flipGreenChannel": "0",
        "maxTextureSize": "1024",
        "textureCompression": "1",
        "compressionQuality": "70",
        "userData": "ZoneZ shallow world relief v1; Unity positive-Y normals" if normal else "ZoneZ shallow world relief v1; linear RGB ambient occlusion",
    }
    for field, value in changes.items():
        template = re.sub(rf"^([ \t]*{re.escape(field)}:)[ \t]*[^\r\n]*", rf"\g<1> {value}", template, flags=re.MULTILINE)
    destination.with_suffix(destination.suffix + ".meta").write_text(template, encoding="utf-8", newline="\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--size", type=int, choices=(512, 1024), default=1024)
    parser.add_argument("--textures", type=Path, default=Path(__file__).resolve().parents[1] / "UnityProject/Assets/ZoneZ/Resources/Textures")
    options = parser.parse_args()
    for name, settings in SURFACES.items():
        source = options.textures / (name + ".png")
        with Image.open(source) as image:
            rgb = np.asarray(image.convert("RGB").resize((options.size, options.size), Image.Resampling.LANCZOS), dtype=np.float64) / 255.0
        template = source.with_suffix(".png.meta").read_text(encoding="utf-8")
        normal_pixels, ao_pixels = make_relief(rgb, settings)
        for suffix, pixels in (("normal", normal_pixels), ("ao", ao_pixels)):
            destination = options.textures / f"{name}-{suffix}.png"
            Image.fromarray(pixels).save(destination, optimize=True)
            write_meta(destination, template, normal=suffix == "normal")
        normal = normal_pixels.astype(np.float64) / 255.0 * 2.0 - 1.0
        angles = np.degrees(np.arctan2(np.hypot(normal[..., 0], normal[..., 1]), normal[..., 2]))
        print(f"{name}: {options.size}px; normal angle p95={np.quantile(angles, .95):.1f}deg; AO min/mean={ao_pixels.min()/255:.3f}/{ao_pixels.mean()/255:.3f}")


if __name__ == "__main__":
    main()
