# 私有化生产部署

该拓扑面向单厂试点：Nginx 是唯一对用户开放的入口，只发布 HTTPS 443；API、迁移器和 SQL Server 位于 `internal` 后端网络，不发布宿主机端口。

## 外置文件

在仓库外准备并限制为部署账号和专用容器 secret-reader 组可读：

- API 最小权限连接串；
- Migration 专用连接串；
- MES 首个管理员密码；
- 至少 32 字符的随机 JWT 签名密钥；
- SQL Server SA 初始密码；
- 受信任的 TLS 证书和私钥。

连接串文件使用标准 SQL Server 格式。API 账号不得是 `sa`，也不得属于 `sysadmin`、`db_owner` 或拥有数据库/服务器 CONTROL 权限。Migration 账号只在维护窗口运行，不供 API 使用。

```powershell
$env:NEWMES_IMAGE_TAG = '<不可变版本号>'
$env:NEWMES_SQL_EDITION = 'Express'
$env:NEWMES_SECRET_GID = '<Linux 主机 secret-reader 组的数字 GID>'
$env:NEWMES_APP_CONNECTION_FILE = '<仓库外绝对路径>'
$env:NEWMES_MIGRATION_CONNECTION_FILE = '<仓库外绝对路径>'
$env:NEWMES_INITIAL_ADMIN_PASSWORD_FILE = '<仓库外绝对路径>'
$env:NEWMES_JWT_SIGNING_KEY_FILE = '<仓库外绝对路径>'
$env:NEWMES_SQL_SA_PASSWORD_FILE = '<仓库外绝对路径>'
$env:NEWMES_TLS_CERTIFICATE_FILE = '<仓库外绝对路径>'
$env:NEWMES_TLS_PRIVATE_KEY_FILE = '<仓库外绝对路径>'
```

未设置 `NEWMES_SQL_EDITION` 时默认为可用于生产的 SQL Server Express。Express 受数据库大小、内存和计算资源限制；超出单线试点容量前应购买对应授权并将值改为企业批准的 edition，生产环境禁止使用 Developer。

Linux 主机需要创建无登录权限的专用组，将 secret 文件设置为该组拥有且权限为 `0440`，并把数字 GID 传给 Compose。Compose 的本地 file secret 本质是 bind mount，不能依赖 long syntax 自动改写 `uid/gid/mode`；`group_add` 才能保证非 root 的 API、Migration 和 SQL Server 进程可读各自挂载的文件。该组除部署账号外不得加入宿主登录用户。

```bash
sudo groupadd --gid 20000 newmes-secrets
sudo chgrp 20000 /srv/newmes/secrets/*
sudo chmod 0440 /srv/newmes/secrets/*
export NEWMES_SECRET_GID=20000
```

## 启动顺序

1. 运行 `scripts/test-production-topology.ps1` 验证端口、网络和配置门禁。
2. 启动 SQL Server，并按企业账号规范创建数据库、Migration 账号和 API 最小权限账号。
3. 备份后显式执行 Migration 服务。
4. 全新安装显式建立首个系统管理员；该命令只允许在不存在系统管理员时成功。
5. 启动 API 与 Nginx。
6. 使用受信任证书地址运行 HTTPS 冒烟。

```powershell
.\scripts\test-production-topology.ps1
docker compose -f deploy\compose.production.yml up -d sqlserver
docker compose -f deploy\compose.production.yml --profile operations run --rm migrator
docker compose -f deploy\compose.production.yml --profile operations run --rm migrator bootstrap-admin --username mes.admin --display-name 'MES Administrator'
docker compose -f deploy\compose.production.yml up -d --build api proxy
.\scripts\test-production-deployment.ps1 -BaseUri 'https://mes.factory.example/'
```

生产门禁失败时 `/health/ready` 返回 503 和稳定错误码；响应不返回密钥、连接串、登录名或异常详情。完整异常只进入受保护的结构化日志。

账号授权、密钥轮换、日志保留、备份恢复和故障处置见 [安全与运维手册](../docs/security-operations.md)。
