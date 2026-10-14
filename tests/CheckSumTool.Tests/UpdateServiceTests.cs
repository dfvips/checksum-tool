using CheckSumTool.Services;
using Xunit;

namespace CheckSumTool.Tests;

/// <summary>更新检查的版本比较规则。</summary>
public class UpdateServiceTests
{
    [Theory]
    // 与当前版本一致（含 v 前缀差异）→ 不算更新
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "v1.0.0", false)]
    [InlineData("v1.0.0", "1.0.0+build.7", false)]
    // 语义化版本高低
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("v1.0.0", "1.1.0", false)]
    [InlineData("v1.0.0", "2.0.0", false)]
    // 缺省段按 0 处理
    [InlineData("v1.2", "1.2.0", false)]
    // 空 tag → 不提示更新
    [InlineData("", "1.0.0", false)]
    // 当前版本是构建时间戳（无法与语义化 tag 比较）→ 只要不同就提示有新版本
    [InlineData("v1.0.0", "202610142200", true)]
    public void IsNewer_ComparesVersions(string latestTag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(latestTag, current));
    }

    [Fact]
    public void CurrentVersionIsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(UpdateService.CurrentVersion));
    }

    [Fact]
    public void RepositoryUrlPointsAtProject()
    {
        Assert.StartsWith("https://github.com/", UpdateService.RepositoryUrl, StringComparison.Ordinal);
    }
}
