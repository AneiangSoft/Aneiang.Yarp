# Aneiang.Yarp.Dashboard 新增功能测试指南

## ✅ 已实现的功能

### 1. 后端 API 增强
- ✅ `POST /api/gateway/clusters` - 创建新集群
- ✅ `PUT /api/gateway/clusters/{clusterId}` - 更新现有集群
- ✅ `DELETE /api/gateway/clusters/{clusterId}` - 删除集群（已存在）
- ✅ 解耦路由和集群创建逻辑

### 2. 前端功能完善
- ✅ 新增集群 - 完整的表单和 JSON 双模式编辑
- ✅ 编辑集群 - JSON 编辑器保存后调用更新 API
- ✅ 优化路由添加 - 自动加载现有集群列表

---

## 🚀 测试步骤

### 准备工作

由于 Visual Studio 锁定了示例项目的 DLL，你需要：

**选项 1：在 Visual Studio 中重新编译**
1. 关闭所有正在运行的 SampleGateway 和 SampleLocalService 实例
2. 在 Visual Studio 中重新生成解决方案
3. 启动 SampleGateway 项目

**选项 2：使用命令行启动（推荐）**
```bash
# 先关闭 Visual Studio 中的调试实例
# 然后运行：
dotnet run --project samples/SampleGateway
```

---

### 测试 1：新增集群功能

#### 通过 Dashboard UI 测试

1. **访问 Dashboard**
   ```
   http://localhost:5000/apigateway
   ```
   - 用户名：`admin`
   - 密码：`demo123`

2. **打开新增集群对话框**
   - 点击"服务集群"标签
   - 点击绿色"新增"按钮

3. **表单模式测试**
   - 使用表单模式填写：
     - ClusterId: `test-cluster-001`
     - Destinations: `{"d1": "http://localhost:8080", "d2": "http://localhost:8081"}`
     - LoadBalancingPolicy: `RoundRobin`
   - 点击"添加"按钮
   - 验证：应该显示"Cluster created successfully"

4. **JSON 模式测试**
   - 再次点击"新增"按钮
   - 切换到"JSON 模式"
   - 编辑 JSON：
     ```json
     {
       "clusterId": "test-cluster-002",
       "destinations": {
         "d1": "http://localhost:9000"
       },
       "loadBalancingPolicy": "LeastRequests"
     }
     ```
   - 点击"添加"按钮
   - 验证：集群应该成功创建

5. **验证集群列表**
   - 刷新页面
   - 确认新集群出现在列表中

#### 通过 API 直接测试

```bash
# 测试 1：创建集群
curl -X POST http://localhost:5000/api/gateway/clusters \
  -H "Content-Type: application/json" \
  -d '{
    "clusterId": "api-test-cluster",
    "destinations": {
      "d1": "http://localhost:7000",
      "d2": "http://localhost:7001"
    },
    "loadBalancingPolicy": "RoundRobin"
  }'

# 预期响应：
# {"code":200,"message":"Cluster 'api-test-cluster' created successfully"}

# 测试 2：尝试创建重复集群（应该失败）
curl -X POST http://localhost:5000/api/gateway/clusters \
  -H "Content-Type: application/json" \
  -d '{
    "clusterId": "api-test-cluster",
    "destinations": {"d1": "http://localhost:7000"}
  }'

# 预期响应：
# {"code":400,"message":"Cluster 'api-test-cluster' already exists. Use update instead."}
```

---

### 测试 2：编辑集群功能

#### 通过 Dashboard UI 测试

1. **编辑现有集群**
   - 在集群列表中找到刚创建的 `test-cluster-001`
   - 点击铅笔图标（编辑按钮）

2. **修改配置**
   - JSON 编辑器会打开
   - 修改负载均衡策略：
     ```json
     {
       "destinations": {
         "d1": "http://localhost:8080",
         "d2": "http://localhost:8081",
         "d3": "http://localhost:8082"
       },
       "loadBalancingPolicy": "LeastRequests"
     }
     ```
   - 点击"保存更改"

3. **验证更新**
   - 应该显示"Cluster updated successfully"
   - 集群列表应该刷新显示新配置

#### 通过 API 直接测试

```bash
# 测试 3：更新集群
curl -X PUT http://localhost:5000/api/gateway/clusters/api-test-cluster \
  -H "Content-Type: application/json" \
  -d '{
    "destinations": {
      "d1": "http://localhost:7000",
      "d2": "http://localhost:7001",
      "d3": "http://localhost:7002"
    },
    "loadBalancingPolicy": "PowerOfTwoChoices"
  }'

# 预期响应：
# {"code":200,"message":"Cluster 'api-test-cluster' updated successfully"}

# 测试 4：更新不存在的集群（应该失败）
curl -X PUT http://localhost:5000/api/gateway/clusters/nonexistent-cluster \
  -H "Content-Type: application/json" \
  -d '{
    "loadBalancingPolicy": "Random"
  }'

# 预期响应：
# {"code":404,"message":"Cluster 'nonexistent-cluster' not found"}
```

---

### 测试 3：新增路由功能优化

#### 通过 Dashboard UI 测试

1. **添加新路由**
   - 切换到"路由规则"标签
   - 点击绿色"新增"按钮

2. **验证集群预填充**
   - 表单应该自动选择第一个现有集群
   - 确认 clusterId 字段有默认值

3. **填写路由信息**
   - RouteId: `test-route-001`
   - ClusterId: 选择 `test-cluster-001`（或任意现有集群）
   - MatchPath: `/api/test/{**catch-all}`
   - DestinationAddress: `http://localhost:8080`（如果集群已存在，此值会被忽略）
   - Order: `50`

4. **提交并验证**
   - 点击"添加"
   - 应该显示"Route added successfully"
   - 路由应该出现在列表中

#### 测试路由指向已存在集群

```bash
# 测试 5：添加路由到已存在的集群
curl -X POST http://localhost:5000/api/gateway/register-route \
  -H "Content-Type: application/json" \
  -d '{
    "routeName": "api-test-route",
    "clusterName": "api-test-cluster",
    "matchPath": "/api/test/{**catch-all}",
    "destinationAddress": "http://localhost:7000",
    "order": 50
  }'

# 预期响应：
# {"code":200,"message":"Route 'api-test-route' registered"}
# 注意：不会覆盖已存在的集群配置
```

---

### 测试 4：删除集群功能

```bash
# 测试 6：删除被路由引用的集群（应该失败）
curl -X DELETE http://localhost:5000/api/gateway/clusters/api-test-cluster

# 预期响应：
# {"code":400,"message":"Cluster 'api-test-cluster' is referenced by 1 route(s). Delete routes first."}

# 测试 7：先删除路由
curl -X DELETE http://localhost:5000/api/gateway/api-test-route

# 预期响应：
# {"code":200,"message":"Route 'api-test-route' deleted"}

# 测试 8：现在可以删除集群
curl -X DELETE http://localhost:5000/api/gateway/clusters/api-test-cluster

# 预期响应：
# {"code":200,"message":"Cluster 'api-test-cluster' deleted"}
```

---

### 测试 5：持久化验证

1. **检查动态配置文件**
   ```bash
   # 查看 gateway-dynamic.json
   cat samples/SampleGateway/gateway-dynamic.json
   ```

2. **重启网关**
   ```bash
   # 停止 SampleGateway（Ctrl+C）
   # 重新启动
   dotnet run --project samples/SampleGateway
   ```

3. **验证配置恢复**
   - 访问 Dashboard
   - 确认所有动态创建的集群和路由都存在

---

## 🎯 关键改进验证清单

- [ ] 新增集群功能完整可用（表单和 JSON 模式）
- [ ] 编辑集群功能完整可用
- [ ] 删除集群功能正常
- [ ] 路由可以指向已存在的集群（不会覆盖）
- [ ] 路由添加时自动加载集群列表
- [ ] 配置持久化到 gateway-dynamic.json
- [ ] 重启后配置正确恢复
- [ ] 错误提示友好明确
- [ ] 日志记录完整

---

## 🐛 可能的问题排查

### 问题 1：创建集群时提示 "Cluster already exists"
**原因**：集群 ID 已存在
**解决**：使用不同的 ClusterId 或使用更新 API

### 问题 2：编辑集群后没有变化
**原因**：可能需要刷新页面
**解决**：点击刷新按钮或重新加载页面

### 问题 3：路由添加失败
**原因**：ClusterId 或 MatchPath 为空
**解决**：确保所有必填字段都有值

### 问题 4：JSON 格式错误
**原因**：Destinations 字段格式不正确
**解决**：使用正确的 JSON 格式，例如：
```json
{
  "d1": "http://localhost:8080",
  "d2": "http://localhost:8081"
}
```

---

## 📊 测试数据清理

测试完成后，可以清理测试数据：

```bash
# 方法 1：通过 API 删除
curl -X DELETE http://localhost:5000/api/gateway/test-route-001
curl -X DELETE http://localhost:5000/api/gateway/clusters/test-cluster-001

# 方法 2：手动编辑 gateway-dynamic.json
# 删除测试相关的 routes 和 clusters 条目

# 方法 3：删除整个动态配置文件（会丢失所有动态配置）
rm samples/SampleGateway/gateway-dynamic.json
```

---

## ✨ 预期效果

改善后，你应该能够：
1. ✅ 通过 Dashboard 完整管理集群（增删改查）
2. ✅ 灵活配置路由和集群关系
3. ✅ 使用友好的表单界面进行配置
4. ✅ 实时监控配置变更效果
5. ✅ 享受更流畅的操作体验

祝你测试顺利！如有任何问题，请随时反馈。
