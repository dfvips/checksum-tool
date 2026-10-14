using Xunit;

// Localizer.Instance 是全局单例，语言相关测试并行执行会互相干扰（曾出现偶发失败），
// 测试量很小，直接关闭并行以保证稳定。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
