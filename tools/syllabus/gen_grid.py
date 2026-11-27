#!/usr/bin/env python3
"""Asks gpt-image-2 for one text-free grid of lesson pictures from a prompt file; saves the PNG next to it.
  SCRIPT=gen_grid.py bash run_extract.sh prompt_ml-MAL-01.txt [quality] [size]   (1024x1024 for one picture)
"""
import base64, json, os, sys, time, urllib.error, urllib.request
src = sys.argv[1]; quality = sys.argv[2] if len(sys.argv) > 2 else "high"; size = sys.argv[3] if len(sys.argv) > 3 else "1536x1024"
ep = os.environ["AOAI_ENDPOINT"].rstrip("/")
url = f"{ep}/openai/deployments/{os.environ.get('AOAI_IMAGE_DEPLOYMENT', 'gpt-image-2')}/images/generations?api-version=2025-04-01-preview"
body = {"prompt": open(src).read(), "size": size, "quality": quality, "n": 1}
for attempt in range(5):
    try:
        req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json", "api-key": os.environ["AOAI_KEY"]})
        with urllib.request.urlopen(req, timeout=600) as r:
            img = base64.b64decode(json.loads(r.read())["data"][0]["b64_json"])
        break
    except urllib.error.HTTPError as e:
        print("HTTP", e.code, e.read()[:300], flush=True)
        if e.code not in (408, 429, 500, 502, 503, 504): raise
        time.sleep(30)
out = os.path.splitext(src)[0].replace("prompt_", "grid_") + ".png"
open(out, "wb").write(img)
print("saved", out, len(img) // 1024, "KB")
