namespace Mes.Domain.Execution;

// 一次测试运行的总体服务端判定，由逐项结果汇总产生。
public enum TestRunResult
{
    Succeeded,
    Failed,
}
