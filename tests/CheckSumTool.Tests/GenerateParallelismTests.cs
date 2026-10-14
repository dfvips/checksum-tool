using CheckSumTool.Core;
using CheckSumTool.Services;
using CheckSumTool.ViewModels;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>
/// 生成面板的并发调度：并发数指的是「同时在算的文件数」，不是「同时在跑的算法数」。
/// 每个 worker 认领一个文件，把它的所有已选格式算完再换下一个；
/// 文件都被人认领后才去帮忙别人的剩余格式（所以「1 个文件 + 多个算法」仍然并行）。
/// </summary>
public class GenerateParallelismTests
{
    [Fact]
    public async Task ConcurrentFileCountMatchesParallelismSetting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-parallel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // 20MB × 5 个文件，只勾两个同样慢的算法（macOS 上 SHA3 都是纯托管实现，耗时相当），
        // 单个文件要算 ~0.25s，采样线程才抓得到「同时在跑的文件数」。
        const int FileCount = 5;
        const int FileSize = 20 * 1024 * 1024;
        var chunk = new byte[1024 * 1024];
        Random.Shared.NextBytes(chunk);

        try
        {
            for (int i = 0; i < FileCount; i++)
            {
                await using var stream = File.Create(Path.Combine(dir, $"f{i}.bin"));
                for (int written = 0; written < FileSize; written += chunk.Length)
                {
                    await stream.WriteAsync(chunk);
                }
            }

            var vm = new GenerateViewModel(new AppSettings { Parallelism = 4 }, (_, _) => { });
            vm.AddFiles(Enumerable.Range(0, FileCount).Select(i => Path.Combine(dir, $"f{i}.bin")).ToArray());
            vm.OutputFormats.Single(f => f.Extension == ".crc64").IsSelected = false;
            vm.OutputFormats.Single(f => f.Extension == ".sha3-384").IsSelected = true;
            vm.OutputFormats.Single(f => f.Extension == ".sha3-512").IsSelected = true;

            // 采样：任意时刻「计算中」的文件数
            int maxRunningFiles = 0;
            using var samplerCts = new CancellationTokenSource();
            Task sampler = Task.Run(async () =>
            {
                while (!samplerCts.IsCancellationRequested)
                {
                    int running = vm.Files.Count(f => f.State == GenerateItemState.Running);
                    maxRunningFiles = Math.Max(maxRunningFiles, running);
                    await Task.Delay(2);
                }
            });

            await vm.StartCommand.ExecuteAsync(null);
            await samplerCts.CancelAsync();
            await sampler;

            // 并发数 = 4 → 同时在跑的文件数就该是 4。
            // 若按「算法」抢任务（2 个算法就把 4 个 worker 摊成 2 个文件），这里只会到 2。
            Assert.Equal(4, maxRunningFiles);
            Assert.All(vm.Files, f => Assert.Equal(GenerateItemState.Done, f.State));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SimultaneousFilesNeverExceedParallelismSetting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-parallel2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        const int FileCount = 5;
        const int FileSize = 8 * 1024 * 1024;
        var chunk = new byte[1024 * 1024];
        Random.Shared.NextBytes(chunk);

        try
        {
            for (int i = 0; i < FileCount; i++)
            {
                await using var stream = File.Create(Path.Combine(dir, $"f{i}.bin"));
                for (int written = 0; written < FileSize; written += chunk.Length)
                {
                    await stream.WriteAsync(chunk);
                }
            }

            var vm = new GenerateViewModel(new AppSettings { Parallelism = 3 }, (_, _) => { });
            vm.AddFiles(Enumerable.Range(0, FileCount).Select(i => Path.Combine(dir, $"f{i}.bin")).ToArray());
            vm.OutputFormats.Single(f => f.Extension == ".md5").IsSelected = true;

            int maxRunningFiles = 0;
            using var samplerCts = new CancellationTokenSource();
            Task sampler = Task.Run(async () =>
            {
                while (!samplerCts.IsCancellationRequested)
                {
                    int running = vm.Files.Count(f => f.State == GenerateItemState.Running);
                    maxRunningFiles = Math.Max(maxRunningFiles, running);
                    await Task.Delay(2);
                }
            });

            await vm.StartCommand.ExecuteAsync(null);
            await samplerCts.CancelAsync();
            await sampler;

            Assert.InRange(maxRunningFiles, 1, 3);
            Assert.All(vm.Files, f => Assert.Equal(GenerateItemState.Done, f.State));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
