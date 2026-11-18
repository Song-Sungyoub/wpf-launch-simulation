"""Build the flight-region globe texture (run explicitly; never called by the app).

Input: NASA Blue Marble Next Generation, June 2004, topography and bathymetry, 500 m tile D1 (90-180E, 0-90N):
https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-topography-bathymetry/june/world.topo.bathy.200406.3x21600x21600.D1.jpg
Download it first, then: python prepare_region_texture.py <path to D1.jpg>
Output: Assets/flight-region.jpg and Assets/flight-region.json (bounds and source).
"""
import json
import sys
from pathlib import Path
from PIL import Image

LAT_MIN, LAT_MAX = 0.0, 45.0
LON_MIN, LON_MAX = 105.0, 150.0
TILE_LON0, TILE_LAT1, PX_PER_DEG = 90.0, 90.0, 240   # tile D1: 21600 px over 90 degrees
SIZE = 4096
OUT = Path(__file__).resolve().parents[1] / "Assets"


def main(source):
    Image.MAX_IMAGE_PIXELS = None
    tile = Image.open(source).convert("RGB")
    box = (round((LON_MIN - TILE_LON0) * PX_PER_DEG), round((TILE_LAT1 - LAT_MAX) * PX_PER_DEG),
           round((LON_MAX - TILE_LON0) * PX_PER_DEG), round((TILE_LAT1 - LAT_MIN) * PX_PER_DEG))
    tile.crop(box).resize((SIZE, SIZE), Image.LANCZOS).save(OUT / "flight-region.jpg", quality=88, optimize=True)
    (OUT / "flight-region.json").write_text(json.dumps({
        "source": "NASA Blue Marble Next Generation, June 2004, topography and bathymetry, tile D1 (NASA Earth Observatory)",
        "projection": "equirectangular",
        "latMin": LAT_MIN, "latMax": LAT_MAX, "lonMin": LON_MIN, "lonMax": LON_MAX, "size": SIZE,
    }, indent=2), encoding="utf-8")
    print(f"Wrote flight-region.jpg ({(OUT / 'flight-region.jpg').stat().st_size} bytes)")


if __name__ == "__main__":
    main(sys.argv[1])
