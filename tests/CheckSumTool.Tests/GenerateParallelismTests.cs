using System.ComponentModel;
using System.Collections.ObjectModel;
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
    /// <summary>
    /// 按「状态切换事件」精确统计同时在算的文件数。
    /// 不要用轮询采样：Linux runner 上 SHA3 走原生实现、单个文件算得飞快，
    /// 2ms 采样可能整个窗口只采到 2，而事件计数是精确的——
    /// 启动时 4 个 worker 会同步认领 4 个文件并同时置 Running，
    /// 之后「完成一个、补认领一个」，峰值恒等于并发数。
    /// </summary>
    private static (Action<SourceFileItem> Attach, Func<int> Max) CreateRunningCounter()
    {
        var gate = new object();
        int running = 0;
        int max = 0;

        return (item =>
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(SourceFileItem.State))
                {
                    return;
                }

                bool isRunning = ((SourceFileItem)s!).State == GenerateItemState.Running;
                lock (gate)
                {
                    running += isRunning ? 1 : -1;
                    if (running > max)
                    {
                        max = running;
                    }
                }
            };
        }, () =>
        {
            lock (gate)
            {
                return max;
            }
        });
    }

    [Fact]
    public async Task ConcurrentFileCountMatchesParallelismSetting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crc64tool-parallel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // 20MB × 5 个文件，只勾两个同样慢的算法（macOS 上 SHA3 都是纯托管实现，耗时相当）。
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

            var (attach, maxRunning) = CreateRunningCounter();
            foreach (var item in vm.Files)
            {
                attach(item);
            }

            await vm.StartCommand.ExecuteAsync(null);

            // 并发数 = 4 → 同时在跑的文件数峰值就该是 4。
            // 若按「算法」抢任务（2 个算法就把 4 个 worker 摊成 2 个文件），峰值只会到 2。
            Assert.Equal(4, maxRunning());
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

            var (attach, maxRunning) = CreateRunningCounter();
            foreach (var item in vm.Files)
            {
                attach(item);
            }

            await vm.StartCommand.ExecuteAsync(null);

            Assert.InRange(maxRunning(), 1, 3);
            Assert.All(vm.Files, f => Assert.Equal(GenerateItemState.Done, f.State));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
