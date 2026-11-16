import sys
from pathlib import Path
from PIL import Image

source, output = map(Path, sys.argv[1:3])
paths = sorted(source.glob('*.png'))
if len(paths) != 200:
    raise RuntimeError(f'Expected 200 frames, got {len(paths)}')
frames = []
for path in paths:
    with Image.open(path) as image:
        frames.append(image.convert('RGB').quantize(colors=256))
output.parent.mkdir(parents=True, exist_ok=True)
frames[0].save(output, save_all=True, append_images=frames[1:], duration=100, loop=0, optimize=True)
with Image.open(output) as gif:
    elapsed = 0
    for index in range(gif.n_frames):
        gif.seek(index)
        elapsed += gif.info['duration']
    print(f'{output}: {gif.size}, {gif.n_frames} frames, {elapsed / 1000:.2f}s, {output.stat().st_size:,} bytes')
