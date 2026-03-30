using System.Runtime.CompilerServices;

// 统一维护 Execution 层对测试项目开放的 internal 可见性。
// 仅用于测试访问，不用于扩展生产环境可见性边界。
[assembly: InternalsVisibleTo("Zeye.NarrowBeltSorter.Core.Tests")]
