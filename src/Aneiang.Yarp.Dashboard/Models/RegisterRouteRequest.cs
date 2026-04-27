namespace Aneiang.Yarp.Dashboard.Models
{
    /// <summary>
    /// 路由注册请求
    /// </summary>
    public class RegisterRouteRequest
    {
        /// <summary>
        /// 路由名称（唯一标识），例如 "my-service-route"
        /// </summary>
        public string RouteName { get; set; } = string.Empty;

        /// <summary>
        /// 集群名称，例如 "MyServiceCluster"
        /// </summary>
        public string ClusterName { get; set; } = string.Empty;

        /// <summary>
        /// 匹配路径模板，例如 /api/my-service/{**catchAll}
        /// </summary>
        public string MatchPath { get; set; } = string.Empty;

        /// <summary>
        /// 目标地址，例如 http://localhost:5000
        /// </summary>
        public string DestinationAddress { get; set; } = string.Empty;

        /// <summary>
        /// 路由优先级（可选，默认 50），数值越小越优先
        /// </summary>
        public int? Order { get; set; }
    }
}
