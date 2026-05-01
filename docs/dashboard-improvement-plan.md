# Aneiang.Yarp.Dashboard 新增集群和路由功能改善方案

## 📋 问题分析

经过深入代码审查，发现 Dashboard 在新增集群和路由方面存在以下核心问题：

### 1. **新增集群功能未实现** ❌
**位置**: `dashboard-clusters.js` 第 262-269 行

```javascript
window.showQuickAddModal(__('modal.addNewCluster'), defaultCluster, async function(newData) {
    if (!newData.clusterId || !newData.destinations || Object.keys(newData.destinations).length === 0) {
        alert(__('modal.clusterIdRequired'));
        return false;
    }
    alert(__('modal.apiNotImplementedCluster'));  // ⚠️ 仅显示提示，未调用 API
    return false;
});
```

**问题**: 
- 表单提交后只弹出 "API not implemented" 提示
- 没有调用后端 API 创建集群
- 用户无法通过 Dashboard 新增集群

### 2. **编辑集群功能未实现** ❌
**位置**: `dashboard-clusters.js` 第 198-228 行

```javascript
window.showJsonEditor(title, clusterJson, async function(newJson) {
    alert(__('modal.apiNotImplemented'));  // ⚠️ 仅显示提示
});
```

**问题**:
- JSON 编辑器保存后没有调用更新 API
- 缺少 `PUT /api/gateway/clusters/{clusterId}` 端点

### 3. **后端缺少独立集群管理 API** ❌
**位置**: `GatewayConfigController.cs`

**当前 API**:
- ✅ `POST /api/gateway/register-route` - 注册路由（同时创建集群）
- ✅ `DELETE /api/gateway/{routeName}` - 删除路由
- ✅ `DELETE /api/gateway/clusters/{clusterId}` - 删除集群
- ❌ **缺少** `POST /api/gateway/clusters` - 新增集群
- ❌ **缺少** `PUT /api/gateway/clusters/{clusterId}` - 更新集群

### 4. **路由和集群耦合度过高** ⚠️
**问题**:
- `TryAddRoute` 方法强制同时创建路由和集群
- 无法单独创建集群（多路由共享场景不支持）
- 无法将路由指向已存在的集群

### 5. **表单体验不佳** ⚠️
**问题**:
- 自动生成的表单字段不友好（如 `destinations` 显示为 JSON 字符串）
- 缺少字段验证和提示
- 复杂配置（如 HealthCheck、Transforms）只能通过 JSON 模式编辑
- 缺少集群选择器（添加路由时需要手动输入 clusterId）

### 6. **数据模型不完整** ⚠️
**问题**:
- `DynamicClusterConfig.HealthCheck` 使用简化模型，与 YARP 实际配置不匹配
- 缺少高级配置支持（SessionAffinity、HttpClient、HttpRequest 等）
- Transforms 编辑不够直观

---

## 🎯 改善目标

1. ✅ 实现完整的集群 CRUD 功能
2. ✅ 解耦路由和集群管理
3. ✅ 提供友好的表单编辑体验
4. ✅ 支持高级配置选项
5. ✅ 完善数据验证和错误处理

---

## 📐 实施方案

### Phase 1: 后端 API 增强

#### 1.1 新增集群管理 API

**文件**: `GatewayConfigController.cs`

```csharp
/// <summary>Create a new cluster.</summary>
[HttpPost("clusters")]
[ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
public IActionResult CreateCluster([FromBody] CreateClusterRequest request)
{
    if (!ModelState.IsValid)
    {
        var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
        return BadRequest(new { code = 400, message = string.Join("; ", errors) });
    }

    var result = _dynamicConfig.TryAddCluster(request);
    return result.Success
        ? Ok(new { code = 200, message = result.Message })
        : BadRequest(new { code = 400, message = result.Message });
}

/// <summary>Update an existing cluster.</summary>
[HttpPut("clusters/{clusterId}")]
[ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
public IActionResult UpdateCluster(string clusterId, [FromBody] UpdateClusterRequest request)
{
    var result = _dynamicConfig.TryUpdateCluster(clusterId, request);
    return result.Success
        ? Ok(new { code = 200, message = result.Message })
        : (result.Message.Contains("not found") 
            ? NotFound(new { code = 404, message = result.Message })
            : BadRequest(new { code = 400, message = result.Message }));
}
```

#### 1.2 新增请求模型

**文件**: `Models/ClusterRequest.cs` (新建)

```csharp
namespace Aneiang.Yarp.Models;

/// <summary>Create cluster request.</summary>
public class CreateClusterRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string ClusterId { get; set; } = string.Empty;

    [Required]
    public Dictionary<string, string> Destinations { get; set; } = new();

    public string? LoadBalancingPolicy { get; set; } = "RoundRobin";
    
    public ClusterHealthCheckConfig? HealthCheck { get; set; }
}

/// <summary>Update cluster request.</summary>
public class UpdateClusterRequest
{
    public Dictionary<string, string>? Destinations { get; set; }
    public string? LoadBalancingPolicy { get; set; }
    public ClusterHealthCheckConfig? HealthCheck { get; set; }
}

/// <summary>Health check config matching YARP's structure.</summary>
public class ClusterHealthCheckConfig
{
    public ActiveHealthCheckConfig? Active { get; set; }
    public PassiveHealthCheckConfig? Passive { get; set; }
    public string? AvailableDestinationsPolicy { get; set; }
}

public class ActiveHealthCheckConfig
{
    public bool Enabled { get; set; }
    public string? Interval { get; set; }
    public string? Timeout { get; set; }
    public string? Policy { get; set; }
    public string? Path { get; set; }
}

public class PassiveHealthCheckConfig
{
    public bool Enabled { get; set; }
    public string? Policy { get; set; }
    public string? ReactivationPeriod { get; set; }
}
```

#### 1.3 扩展 DynamicYarpConfigService

**文件**: `DynamicYarpConfigService.cs`

```csharp
/// <summary>Add a new cluster independently.</summary>
public RouteOperationResult TryAddCluster(CreateClusterRequest request)
{
    lock (_lock)
    {
        var config = _configProvider.GetConfig();
        var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();
        
        // Check if cluster already exists
        if (clusters.Any(c => string.Equals(c.ClusterId, request.ClusterId, StringComparison.OrdinalIgnoreCase)))
        {
            return new RouteOperationResult(false, $"Cluster '{request.ClusterId}' already exists");
        }

        var clusterConfig = new ClusterConfig
        {
            ClusterId = request.ClusterId,
            Destinations = request.Destinations.ToDictionary(
                d => d.Key,
                d => new DestinationConfig { Address = d.Value }),
            LoadBalancingPolicy = request.LoadBalancingPolicy
        };

        clusters.Add(clusterConfig);
        _configProvider.Update(config.Routes ?? Array.Empty<RouteConfig>(), clusters);

        // Save to dynamic config
        EnsureDynamicConfigInitialized();
        _dynamicConfig!.Clusters.Add(new DynamicClusterConfig
        {
            ClusterId = request.ClusterId,
            Destinations = request.Destinations,
            LoadBalancingPolicy = request.LoadBalancingPolicy,
            HealthCheck = request.HealthCheck != null ? new HealthCheckConfig
            {
                Active = request.HealthCheck.Active?.Enabled ?? false,
                Endpoint = request.HealthCheck.Active?.Path
            } : null,
            Source = "dynamic",
            CreatedAt = DateTime.UtcNow
        });

        SaveDynamicConfig();
        _logger.LogInformation("Cluster '{ClusterId}' created", request.ClusterId);
        return new RouteOperationResult(true, $"Cluster '{request.ClusterId}' created");
    }
}

/// <summary>Update an existing cluster.</summary>
public RouteOperationResult TryUpdateCluster(string clusterId, UpdateClusterRequest request)
{
    lock (_lock)
    {
        var config = _configProvider.GetConfig();
        var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();
        
        var existingIdx = clusters.FindIndex(c => 
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
        
        if (existingIdx < 0)
        {
            return new RouteOperationResult(false, $"Cluster '{clusterId}' not found");
        }

        var existing = clusters[existingIdx];
        var updated = new ClusterConfig
        {
            ClusterId = existing.ClusterId,
            Destinations = request.Destinations?.ToDictionary(
                d => d.Key,
                d => new DestinationConfig { Address = d.Value }) ?? existing.Destinations,
            LoadBalancingPolicy = request.LoadBalancingPolicy ?? existing.LoadBalancingPolicy
        };

        clusters[existingIdx] = updated;
        _configProvider.Update(config.Routes ?? Array.Empty<RouteConfig>(), clusters);

        // Update dynamic config
        EnsureDynamicConfigInitialized();
        var dynCluster = _dynamicConfig!.Clusters.FirstOrDefault(c => 
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
        
        if (dynCluster != null)
        {
            if (request.Destinations != null) dynCluster.Destinations = request.Destinations;
            if (request.LoadBalancingPolicy != null) dynCluster.LoadBalancingPolicy = request.LoadBalancingPolicy;
        }

        SaveDynamicConfig();
        _logger.LogInformation("Cluster '{ClusterId}' updated", clusterId);
        return new RouteOperationResult(true, $"Cluster '{clusterId}' updated");
    }
}
```

#### 1.4 修改现有路由注册逻辑

**文件**: `DynamicYarpConfigService.cs` - `TryAddRoute` 方法

```csharp
// 修改集群创建逻辑：如果集群已存在，不覆盖
var existingClusterIdx = newClusters.FindIndex(c =>
    string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

if (existingClusterIdx >= 0)
{
    // 集群已存在，可选择追加 destination 或跳过
    _logger.LogInformation("Cluster '{ClusterName}' already exists, reusing", request.ClusterName);
}
else
{
    newClusters.Add(clusterConfig);
}
```

---

### Phase 2: 前端体验优化

#### 2.1 实现新增集群功能

**文件**: `dashboard-clusters.js`

```javascript
window.showAddClusterModal = function() {
    var defaultCluster = {
        clusterId: 'cluster-' + Date.now(),
        destinations: {
            'd1': 'http://localhost:5001'
        },
        loadBalancingPolicy: 'RoundRobin'
    };
    var __ = window.__;
    
    window.showQuickAddModal(__('modal.addNewCluster'), defaultCluster, async function(newData) {
        if (!newData.clusterId) {
            alert(__('modal.clusterIdRequired'));
            return false;
        }
        
        // Parse destinations
        var destinations = {};
        if (typeof newData.destinations === 'string') {
            try {
                destinations = JSON.parse(newData.destinations);
            } catch(e) {
                alert('Invalid destinations JSON');
                return false;
            }
        } else {
            destinations = newData.destinations || {};
        }
        
        if (Object.keys(destinations).length === 0) {
            alert(__('modal.clusterIdRequired'));
            return false;
        }
        
        var d = window.__dashboard;
        var request = {
            clusterId: newData.clusterId,
            destinations: destinations,
            loadBalancingPolicy: newData.loadBalancingPolicy || 'RoundRobin'
        };
        
        var res = await window.authFetch(d.basePath + '/../api/gateway/clusters', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        });
        var json = await res.json();
        
        if (json.code === 200) {
            alert('Cluster created successfully');
            await window.loadClusters();
            return true;
        } else {
            alert('Failed to create cluster: ' + json.message);
            return false;
        }
    });
};
```

#### 2.2 实现编辑集群功能

**文件**: `dashboard-clusters.js`

```javascript
window.editCluster = async function(clusterId) {
    try {
        var d = window.__dashboard;
        var __ = window.__;
        var res = await window.authFetch(d.basePath + '/../api/gateway/dynamic-config');
        var json = await res.json();
        if (json.code !== 200 || !json.data) {
            alert('Failed to load dynamic config');
            return;
        }
        var config = json.data;
        var cluster = config.clusters.find(function(c) { return c.clusterId === clusterId; });
        if (!cluster) {
            alert('Cluster not found in dynamic config');
            return;
        }
        
        var clusterJson = {
            destinations: cluster.destinations || {},
            loadBalancingPolicy: cluster.loadBalancingPolicy || 'RoundRobin'
        };
        
        var title = __('modal.editCluster') + ': ' + clusterId;
        window.showJsonEditor(title, clusterJson, async function(newJson) {
            var updateRes = await window.authFetch(d.basePath + '/../api/gateway/clusters/' + encodeURIComponent(clusterId), {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(newJson)
            });
            var updateJson = await updateRes.json();
            if (updateJson.code === 200) {
                alert('Cluster updated successfully');
                await window.loadClusters();
            } else {
                alert('Failed to update cluster: ' + updateJson.message);
            }
        });
    } catch (e) {
        console.error('Edit cluster failed:', e);
        alert('Error: ' + e.message);
    }
};
```

#### 2.3 改进路由添加表单

**文件**: `dashboard-routes.js`

```javascript
window.showAddRouteModal = async function() {
    // 获取现有集群列表供选择
    var d = window.__dashboard;
    var clusters = [];
    try {
        var res = await window.authFetch(d.basePath + '/clusters');
        var json = await res.json();
        if (json.code === 200) {
            clusters = json.data;
        }
    } catch(e) {
        console.error('Failed to load clusters:', e);
    }
    
    var defaultRoute = {
        routeId: 'route-' + Date.now(),
        clusterId: clusters.length > 0 ? clusters[0].clusterId : '',
        matchPath: '/api/{**catch-all}',
        order: 50,
        destinationAddress: 'http://localhost:5001',
        transforms: []
    };
    
    var __ = window.__;
    window.showQuickAddModal(__('modal.addNewRoute'), defaultRoute, async function(newData) {
        if (!newData.routeId || !newData.clusterId || !newData.matchPath) {
            alert(__('modal.routeRequired'));
            return false;
        }
        
        var request = {
            routeName: newData.routeId,
            clusterName: newData.clusterId,
            matchPath: newData.matchPath,
            destinationAddress: newData.destinationAddress || 'http://localhost:5001',
            order: newData.order || 50,
            transforms: newData.transforms || []
        };
        
        var res = await window.authFetch(d.basePath + '/../api/gateway/register-route', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        });
        var json = await res.json();
        
        if (json.code === 200) {
            alert('Route added successfully');
            await window.loadRoutes();
            await window.loadClusters();
            return true;
        } else {
            alert('Failed to add route: ' + json.message);
            return false;
        }
    });
};
```

---

### Phase 3: 高级功能（可选）

#### 3.1 集群选择器组件

在路由编辑模态框中，将 `clusterId` 输入框改为下拉选择器，从现有集群中选择或创建新集群。

#### 3.2 Transforms 可视化编辑器

提供表单化的 Transforms 编辑界面，支持常见操作：
- PathSet / PathRemovePrefix / PathPrefix
- RequestHeader / ResponseHeader
- Query

#### 3.3 健康检查配置界面

提供 HealthCheck 的配置表单，支持主动/被动健康检查设置。

---

## 📊 实施优先级

| 优先级 | 任务 | 预计工作量 | 影响 |
|--------|------|-----------|------|
| 🔴 P0 | 后端集群 CRUD API | 2 小时 | 核心功能 |
| 🔴 P0 | 前端新增集群功能 | 1 小时 | 核心功能 |
| 🔴 P0 | 前端编辑集群功能 | 1 小时 | 核心功能 |
| 🟡 P1 | 解耦路由和集群创建 | 2 小时 | 用户体验 |
| 🟡 P1 | 改进路由添加表单 | 1.5 小时 | 用户体验 |
| 🟢 P2 | 集群选择器 | 2 小时 | 易用性 |
| 🟢 P2 | Transforms 可视化 | 4 小时 | 高级功能 |

---

## ✅ 测试计划

### 使用 SampleGateway 测试

1. **启动测试环境**
   ```bash
   dotnet run --project samples/SampleGateway
   ```

2. **测试新增集群**
   - 访问 `http://localhost:5000/apigateway`
   - 登录（admin / demo123）
   - 点击集群"新增"按钮
   - 填写集群信息并提交
   - 验证集群列表刷新

3. **测试编辑集群**
   - 点击集群的"编辑"按钮
   - 修改 destinations 或负载均衡策略
   - 保存并验证

4. **测试新增路由**
   - 点击路由"新增"按钮
   - 选择现有集群或创建新集群
   - 配置路由规则并提交

5. **测试 API 直接调用**
   ```bash
   # 创建集群
   curl -X POST http://localhost:5000/api/gateway/clusters \
     -H "Content-Type: application/json" \
     -d '{"clusterId":"test-cluster","destinations":{"d1":"http://localhost:8080"}}'
   
   # 更新集群
   curl -X PUT http://localhost:5000/api/gateway/clusters/test-cluster \
     -H "Content-Type: application/json" \
     -d '{"loadBalancingPolicy":"LeastRequests"}'
   ```

6. **验证持久化**
   - 重启 SampleGateway
   - 确认动态配置从 `gateway-dynamic.json` 加载

---

## 📝 注意事项

1. **向后兼容**: 确保现有 API 不变，只新增功能
2. **数据验证**: 加强输入验证，防止无效配置
3. **错误处理**: 提供友好的错误提示
4. **权限控制**: 集群操作应遵循 `isEditable` 标志
5. **日志记录**: 所有配置变更需记录日志

---

## 🎉 预期效果

改善后，用户将能够：
- ✅ 通过 Dashboard 完整管理集群（增删改查）
- ✅ 灵活配置路由和集群关系
- ✅ 使用友好的表单界面进行配置
- ✅ 实时监控配置变更效果
- ✅ 享受更流畅的操作体验
