"""Build the Naro pad-area ground texture (run explicitly; never called by the app).

Downloads NASA GIBS Landsat WELD (2000, ~30 m) Web-Mercator tiles at level 12 around the pad and resamples
them into one equirectangular JPEG covering LAT/LON bounds below. Only gibs.earthdata.nasa.gov is contacted.
Output: Assets/naro-area.jpg and Assets/naro-area.json (bounds and source).
"""
import concurrent.futures
import io
import json
import math
from pathlib import Path
import urllib.parse
import urllib.request
from PIL import Image

PAD_LAT, PAD_LON = 34.431636, 127.534411
LAT_MIN, LAT_MAX = PAD_LAT - 0.75, PAD_LAT + 0.75
LON_MIN, LON_MAX = PAD_LON - 0.9, PAD_LON + 0.9
ZOOM, SIZE = 12, 4096
LAYER = "Landsat_WELD_CorrectedReflectance_TrueColor_Global_Annual"
OUT = Path(__file__).resolve().parents[1] / "Assets"


def tile_xy(lat, lon):
    n = 2 ** ZOOM
    return (lon + 180) / 360 * n, (1 - math.asinh(math.tan(math.radians(lat))) / math.pi) / 2 * n


def fetch(x, y):
    url = f"https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/{LAYER}/default/2000-12-01/GoogleMapsCompatible_Level12/{ZOOM}/{y}/{x}.jpeg"
    request = urllib.request.Request(url, headers={"User-Agent": "WpfLaunchSimulation-asset-preparation/1.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        if urllib.parse.urlparse(response.url).hostname != "gibs.earthdata.nasa.gov":
            raise ValueError("Unexpected download host")
        if response.headers.get_content_type() != "image/jpeg":
            raise ValueError("Unexpected content type")
        data = response.read(1024 * 1024 + 1)
    with Image.open(io.BytesIO(data)) as image:
        if image.format != "JPEG" or image.size != (256, 256):
            raise ValueError("Expected a 256x256 JPEG tile")
        return (x, y), image.convert("RGB")


def main():
    x0, y0 = tile_xy(LAT_MAX, LON_MIN); x1, y1 = tile_xy(LAT_MIN, LON_MAX)
    xs, ys = range(math.floor(x0), math.floor(x1) + 1), range(math.floor(y0), math.floor(y1) + 1)
    print(f"Downloading {len(xs) * len(ys)} tiles at level {ZOOM}", flush=True)
    mosaic = Image.new("RGB", (len(xs) * 256, len(ys) * 256))
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for (x, y), image in pool.map(lambda t: fetch(*t), [(x, y) for y in ys for x in xs]):
            mosaic.paste(image, ((x - xs.start) * 256, (y - ys.start) * 256))
    # Resample Web Mercator → equirectangular (rows linear in latitude).
    out = Image.new("RGB", (SIZE, SIZE)); src = mosaic.load(); dst = out.load()
    for row in range(SIZE):
        lat = LAT_MAX - (row + 0.5) / SIZE * (LAT_MAX - LAT_MIN)
        _, ty = tile_xy(lat, PAD_LON); py = min(mosaic.height - 1, max(0, int((ty - ys.start) * 256)))
        for col in range(SIZE):
            lon = LON_MIN + (col + 0.5) / SIZE * (LON_MAX - LON_MIN)
            tx, _ = tile_xy(lat, lon); px = min(mosaic.width - 1, max(0, int((tx - xs.start) * 256)))
            dst[col, row] = src[px, py]
    out.save(OUT / "naro-area.jpg", quality=85, optimize=True)
    (OUT / "naro-area.json").write_text(json.dumps({
        "source": f"NASA GIBS {LAYER} (2000-12-01), level {ZOOM}, https://gibs.earthdata.nasa.gov",
        "projection": "equirectangular", "latMin": LAT_MIN, "latMax": LAT_MAX, "lonMin": LON_MIN, "lonMax": LON_MAX,
        "size": SIZE}, indent=2), encoding="utf-8")
    print(f"Wrote naro-area.jpg ({(OUT / 'naro-area.jpg').stat().st_size} bytes)", flush=True)


if __name__ == "__main__":
    main()
