namespace Mes.Domain.Execution;

// 除成品 SN 外可纳入谱系的受控标识类型。
public enum ControlledIdentifierType
{
    SerialNumber,
    MacAddress,
    Imei,
    Certificate,
}
