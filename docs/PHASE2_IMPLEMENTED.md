# Phase 2 implementation

Phase 2 turns an image file or detached `ImageFrame` into a deterministic, palette-aware frame that the later planner can consume.

## Pipeline

```text
source stream/file
  -> ImageDecoder (PNG/JPEG/WEBP/BMP and ImageSharp codecs)
  -> alpha policy (preserve, composite, or reject)
  -> linear-light premultiplied resize
  -> adaptive or fixed palette
  -> Lab/Delta-E nearest-color mapping
  -> optional ordered/Floyd–Steinberg/Atkinson dithering
  -> QuantizedImage (palette, indices, rendered frame)
```

## Implemented components

- `ImageDecoder` keeps source resolution until an explicit target size is requested and applies EXIF orientation by default.
- `AlphaPolicy` makes transparency behavior explicit. The default preserves source alpha; profiles can composite on a known whiteboard background or reject transparency.
- `ImageResampler` supports nearest, bilinear, bicubic, and Lanczos3 filters. Lanczos3 is the default and uses linear-light, premultiplied-alpha sampling.
- `ColorMath` converts sRGB to linear RGB and CIE Lab and exposes CIE76 and CIEDE2000 Delta-E metrics.
- `AdaptivePaletteBuilder` uses deterministic median-cut boxes with linear-light averaging and bounded sampling.
- `PaletteQuantizer` supports fixed/adaptive palettes, alpha preservation, and three deterministic dithering modes.
- `ImageProcessingPipeline` composes the stages for files, streams, or already decoded frames.

## Verification

The imaging test suite covers Lab/Delta-E reference values, linear compositing, Lanczos golden pixels, transparent-edge alpha preservation, deterministic palette bounds, fixed-palette quantization, dithering dimensions, PNG alpha decoding, and pipeline composition.

## Deferred to later phases

This phase does not select drawing strokes, detect a game canvas, drive mouse input, or decide a target-specific palette. Those responsibilities remain in the planner, profile, adapter, and Windows automation layers.
