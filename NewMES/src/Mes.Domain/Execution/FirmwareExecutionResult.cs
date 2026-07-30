namespace Mes.Domain.Execution;

// 固件执行的服务端最终判定；设备自报结果不能替代 MES 对冻结证据的比较。
public enum FirmwareExecutionResult
{
    Succeeded,
    Failed,
}
