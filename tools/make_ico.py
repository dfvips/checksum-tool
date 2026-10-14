#!/usr/bin/env python3
"""把 icon/logo.png 转成多尺寸 ICO，供 Windows 可执行文件与窗口图标使用。

依赖 macOS 的 sips 做高质量缩放（产物已提交到仓库，CI 无需重新生成）。

用法：python3 tools/make_ico.py
"""

import os
import struct
import subprocess
import sys
import tempfile

SIZES = (16, 32, 48, 64, 128, 256)


def resize(png_path: str, size: int, out_path: str) -> None:
    subprocess.run(
        ["sips", "-z", str(size), str(size), png_path, "--out", out_path],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


def build_ico(pngs: list[tuple[int, bytes]]) -> bytes:
    count = len(pngs)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count

    entries = bytearray()
    blob = bytearray()

    for size, data in pngs:
        entries.extend(
            struct.pack(
                "<BBBBHHII",
                0 if size >= 256 else size,
                0 if size >= 256 else size,
                0,
                0,
                1,
                32,
                len(data),
                offset,
            )
        )
        blob.extend(data)
        offset += len(data)

    return header + bytes(entries) + bytes(blob)


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    source = os.path.join(root, "icon", "logo.png")
    target = os.path.join(root, "src", "CheckSumTool", "Assets", "logo.ico")

    if not os.path.exists(source):
        print(f"找不到源文件：{source}", file=sys.stderr)
        return 1

    pngs: list[tuple[int, bytes]] = []

    with tempfile.TemporaryDirectory() as tmp:
        for size in SIZES:
            out = os.path.join(tmp, f"{size}.png")
            print(f"  缩放 {size}x{size} …")
            resize(source, size, out)
            with open(out, "rb") as handle:
                pngs.append((size, handle.read()))

    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "wb") as handle:
        handle.write(build_ico(pngs))

    print(f"已写入 {target} ({os.path.getsize(target)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
