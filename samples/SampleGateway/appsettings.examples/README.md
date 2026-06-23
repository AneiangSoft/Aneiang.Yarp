# 部署场景配置示例

本目录包含 5 种典型部署场景的 appsettings.json 配置文件示例。

## 场景对比

| 文件 | 模式 | 端口 | 说明 |
|------|------|------|------|
| `appsettings.AllInOne.json` | AllInOne | 5200 | 完全兼容现有用户，单端口模式 |
| `appsettings.Split.json` | Split | 80 + 5000 | 单进程双端口，80 代理 + 5000 Dashboard(本机) |
| `appsettings.SplitWithHealth.json` | Split | 80 + 5000 + 5002 | 包含健康检查端点 |
| `appsettings.ProxyOnly.json` | ProxyOnly | 80 + 5001 | 仅代理模式，无 Dashboard UI |
| `appsettings.DashboardOnly.json` | DashboardOnly | 5000 | 仅控制面 Dashboard，无代理转发 |

## 使用方法

### 方式 1：直接覆盖
将示例文件复制为 `appsettings.json` 即可：
```bash
cp appsettings.examples/appsettings.Split.json samples/SampleGateway/appsettings.json
```

### 方式 2：命令行快捷参数
```bash
# Split 模式
dotnet run --project samples/SampleGateway -- \
  --deployment split \
  --proxy-url http://0.0.0.0:80 \
  --dashboard-url http://127.0.0.1:5000

# 含健康检查
dotnet run --project samples/SampleGateway -- \
  --deployment split \
  --proxy-url http://0.0.0.0:80 \
  --dashboard-url http://127.0.0.1:5000 \
  --health-url http://0.0.0.0:5002

# ProxyOnly 模式
dotnet run --project samples/SampleGateway -- \
  --deployment proxyonly \
  --proxy-url http://0.0.0.0:80 \
  --admin-url http://127.0.0.1:5001
```

## Docker / systemd / Windows Service

仓库提供部署模板：

```text
deploy/docker-compose.split.yml
deploy/systemd/aneiang-yarp.service
deploy/windows/Install-AneiangYarpService.ps1
```

Docker Compose 示例：

```bash
docker compose -f deploy/docker-compose.split.yml up -d --build
```

systemd 示例：

```bash
sudo cp deploy/systemd/aneiang-yarp.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now aneiang-yarp
```

Windows Service 示例（管理员 PowerShell）：

```powershell
.\deploy\windows\Install-AneiangYarpService.ps1 -AppDirectory C:\Services\AneiangYarp
```

### 方式 3：环境变量（Docker/K8s）
```bash
export Gateway__Deployment__Mode=Split
export Kestrel__Endpoints__Proxy__Url=http://0.0.0.0:80
export Kestrel__Endpoints__Dashboard__Url=http://127.0.0.1:5000
```

## 健康检查端点

启动后访问：
- `GET /live` - 进程存活（200 always）
- `GET /ready` - 配置已加载就绪
- `GET /health` - 综合健康状态

```bash
curl http://localhost:5002/live
curl http://localhost:5002/ready
curl http://localhost:5002/health
```

## 配置热更新

修改 `appsettings.json` 后，500ms 内自动重新加载：
- 业务配置（路由/集群/策略/限流/告警）→ 自动生效
- Kestrel 端口绑定 → 需要重启进程
