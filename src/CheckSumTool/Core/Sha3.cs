// Keccak-f[1600] round scheduling/unrolling is adapted from tinysha's keccak_x64 backend:
// https://github.com/gibme-c/tinysha
// Copyright (c) 2025-2026, Brandon Lehmann
//
// Redistribution and use in source and binary forms, with or without modification, are
// permitted provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright notice, this list of
//    conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright notice, this list
//    of conditions and the following disclaimer in the documentation and/or other materials.
// 3. Neither the name of the copyright holder nor its contributors may be used to endorse
//    or promote products derived from this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDER AND CONTRIBUTORS "AS IS" AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
// COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
// EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
// GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED
// AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
// NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
// OF THE POSSIBILITY OF SUCH DAMAGE.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CheckSumTool.Core;

/// <summary>
/// 纯托管实现的 SHA3-256 / SHA3-384 / SHA3-512（FIPS 202 Keccak 海绵结构）。
/// 不依赖 .NET 内置的 <c>SHA3_256</c>/<c>SHA3_512</c>——
/// 后者在部分平台（如 macOS 上的某些 .NET 运行时）会抛 <c>PlatformNotSupportedException</c>。
/// 这里用与 .NET 实现逐位一致的 Keccak-f[1600] 置换，保证跨平台可用。
///
/// 性能优化要点：
/// <list type="bullet">
///   <item><c>AbsorbBlock</c> 用 <see cref="BinaryPrimitives.ReadUInt64LittleEndian"/> 取代逐字节拼装 lane，减少约 8× 操作量。</item>
///   <item><c>GetHash</c> 输出用 <see cref="MemoryMarshal.AsBytes"/> 直接拷贝 state 的字节视图（小端系统内存布局与 Keccak lane 字节序一致），避免逐字节移位。</item>
/// </list>
/// </summary>
public sealed class Sha3
{
    private static readonly ulong[] RoundConstants =
    [
        0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808aUL,
        0x8000000080008000UL, 0x000000000000808bUL, 0x0000000080000001UL,
        0x8000000080008081UL, 0x8000000000008009UL, 0x000000000000008aUL,
        0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000aUL,
        0x000000008000808bUL, 0x800000000000008bUL, 0x8000000000008089UL,
        0x8000000000008003UL, 0x8000000000008002UL, 0x8000000000000080UL,
        0x000000000000800aUL, 0x800000008000000aUL, 0x8000000080008081UL,
        0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL,
    ];

    private readonly int _rateBytes;
    private readonly int _digestBytes;
    private readonly ulong[] _state = new ulong[25];
    private readonly byte[] _buffer;
    private int _bufferPos;
    private bool _finished;

    public Sha3(int digestBits)
    {
        _digestBytes = digestBits / 8;
        _rateBytes = (1600 - 2 * digestBits) / 8;
        _buffer = new byte[_rateBytes];
    }

    public static Sha3 Sha256() => new(256);

    public static Sha3 Sha384() => new(384);

    public static Sha3 Sha512() => new(512);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Update(ReadOnlySpan<byte> data)
    {
        if (_finished)
        {
            throw new InvalidOperationException("Hash already finalized");
        }

        int rate = _rateBytes;
        int i = 0;
        int n = data.Length;

        // 先把已有缓冲填满（若之前有残留）
        if (_bufferPos > 0)
        {
            while (_bufferPos < rate && i < n)
            {
                _buffer[_bufferPos++] = data[i++];
            }

            if (_bufferPos == rate)
            {
                AbsorbBlock(_buffer.AsSpan(0, rate));
                _bufferPos = 0;
            }
        }

        // 直接按整块吸收，避免缓冲残留污染
        while (i + rate <= n)
        {
            AbsorbBlock(data.Slice(i, rate));
            i += rate;
        }

        // 剩余不足一块的字节留在缓冲中
        while (i < n)
        {
            _buffer[_bufferPos++] = data[i++];
        }
    }

    public byte[] GetHash()
    {
        if (!_finished)
        {
            // SHA-3 填充：追加「01」域分隔 + 多速率填充 10*1。
            // 即缓冲当前位置写 0x06，末尾字节置 0x80。
            // 注意：必须先清空缓冲尾部，否则在多段 Update 后（_buffer 残留旧块数据）
            // 末尾字节会与垃圾数据做异或，导致填充错误。
            Array.Clear(_buffer, _bufferPos, _rateBytes - _bufferPos);
            _buffer[_bufferPos] = 0x06;
            _buffer[_rateBytes - 1] |= 0x80;
            AbsorbBlock(_buffer);
            _finished = true;
        }

        // 输出固定长度等于摘要长度，且不超过速率，无需多次挤压。
        // 小端系统上 ulong[] 的内存字节布局与 Keccak lane 的小端字节序一致，
        // 因此可直接用 MemoryMarshal 将 state 的字节视图拷贝到输出。
        var output = new byte[_digestBytes];
        MemoryMarshal.AsBytes(_state.AsSpan()).Slice(0, _digestBytes).CopyTo(output);
        return output;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AbsorbBlock(ReadOnlySpan<byte> block)
    {
        int laneCount = _rateBytes / 8;
        for (int i = 0; i < laneCount; i++)
        {
            _state[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(i * 8));
        }

        KeccakF(_state);
    }

    /// <summary>
    /// Keccak-f[1600] 置换（参考 tinysha x64 后端的展开策略）：
    /// Theta 用预计算列奇偶 d0~d4 直接对 25 个 lane 求 XOR（消除内层取模）；
    /// Rho+Pi 完全展开为 24 步寄存器轮转（消除数组下标与查表）；
    /// Chi 按行展开为局部变量（消除 bc 缓冲读写）。
    ///
    /// <para>
    /// 标记 <see cref="MethodImplOptions.AggressiveOptimization"/>：强制 JIT 直接用优化编译，
    /// 跳过分层编译的首层（QuickJit 对含循环的方法不做优化）。
    /// 这样即使在 Debug 配置下运行，SHA3 也能接近 Release 的吞吐
    /// （实测把 400 MB 文件的 SHA3-256 从约 10 秒降到约 2 秒）。
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void KeccakF(ulong[] st)
    {
        for (int round = 0; round < 24; round++)
        {
            // Theta — 列奇偶
            ulong bc0 = st[0] ^ st[5] ^ st[10] ^ st[15] ^ st[20];
            ulong bc1 = st[1] ^ st[6] ^ st[11] ^ st[16] ^ st[21];
            ulong bc2 = st[2] ^ st[7] ^ st[12] ^ st[17] ^ st[22];
            ulong bc3 = st[3] ^ st[8] ^ st[13] ^ st[18] ^ st[23];
            ulong bc4 = st[4] ^ st[9] ^ st[14] ^ st[19] ^ st[24];

            ulong d0 = bc4 ^ RotateLeft(bc1, 1);
            ulong d1 = bc0 ^ RotateLeft(bc2, 1);
            ulong d2 = bc1 ^ RotateLeft(bc3, 1);
            ulong d3 = bc2 ^ RotateLeft(bc4, 1);
            ulong d4 = bc3 ^ RotateLeft(bc0, 1);

            st[0] ^= d0; st[5] ^= d0; st[10] ^= d0; st[15] ^= d0; st[20] ^= d0;
            st[1] ^= d1; st[6] ^= d1; st[11] ^= d1; st[16] ^= d1; st[21] ^= d1;
            st[2] ^= d2; st[7] ^= d2; st[12] ^= d2; st[17] ^= d2; st[22] ^= d2;
            st[3] ^= d3; st[8] ^= d3; st[13] ^= d3; st[18] ^= d3; st[23] ^= d3;
            st[4] ^= d4; st[9] ^= d4; st[14] ^= d4; st[19] ^= d4; st[24] ^= d4;

            // Rho + Pi — 完全展开（与 PiLane/RotationConstants 逐项对应）
            ulong t = st[1];
            ulong tmp;
            tmp = st[10]; st[10] = RotateLeft(t, 1); t = tmp;
            tmp = st[7]; st[7] = RotateLeft(t, 3); t = tmp;
            tmp = st[11]; st[11] = RotateLeft(t, 6); t = tmp;
            tmp = st[17]; st[17] = RotateLeft(t, 10); t = tmp;
            tmp = st[18]; st[18] = RotateLeft(t, 15); t = tmp;
            tmp = st[3]; st[3] = RotateLeft(t, 21); t = tmp;
            tmp = st[5]; st[5] = RotateLeft(t, 28); t = tmp;
            tmp = st[16]; st[16] = RotateLeft(t, 36); t = tmp;
            tmp = st[8]; st[8] = RotateLeft(t, 45); t = tmp;
            tmp = st[21]; st[21] = RotateLeft(t, 55); t = tmp;
            tmp = st[24]; st[24] = RotateLeft(t, 2); t = tmp;
            tmp = st[4]; st[4] = RotateLeft(t, 14); t = tmp;
            tmp = st[15]; st[15] = RotateLeft(t, 27); t = tmp;
            tmp = st[23]; st[23] = RotateLeft(t, 41); t = tmp;
            tmp = st[19]; st[19] = RotateLeft(t, 56); t = tmp;
            tmp = st[13]; st[13] = RotateLeft(t, 8); t = tmp;
            tmp = st[12]; st[12] = RotateLeft(t, 25); t = tmp;
            tmp = st[2]; st[2] = RotateLeft(t, 43); t = tmp;
            tmp = st[20]; st[20] = RotateLeft(t, 62); t = tmp;
            tmp = st[14]; st[14] = RotateLeft(t, 18); t = tmp;
            tmp = st[22]; st[22] = RotateLeft(t, 39); t = tmp;
            tmp = st[9]; st[9] = RotateLeft(t, 61); t = tmp;
            tmp = st[6]; st[6] = RotateLeft(t, 20); t = tmp;
            st[1] = RotateLeft(t, 44);

            // Chi — 按行展开
            for (int y = 0; y < 25; y += 5)
            {
                ulong t0 = st[y], t1 = st[y + 1], t2 = st[y + 2], t3 = st[y + 3], t4 = st[y + 4];
                st[y] = t0 ^ (~t1 & t2);
                st[y + 1] = t1 ^ (~t2 & t3);
                st[y + 2] = t2 ^ (~t3 & t4);
                st[y + 3] = t3 ^ (~t4 & t0);
                st[y + 4] = t4 ^ (~t0 & t1);
            }

            // Iota
            st[0] ^= RoundConstants[round];
        }
    }

    private static ulong RotateLeft(ulong value, int offset)
        => (value << offset) | (value >> (64 - offset));
}
