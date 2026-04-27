using System.ComponentModel.DataAnnotations;

namespace Aneiang.Yarp.Models
{
    /// <summary>
    /// 路由注册请求
    /// </summary>
    public class RegisterRouteRequest
    {
        /// <summary>
        /// 路由名称（唯一标识），例如 "my-service-route"
        /// </summary>
        [Required(ErrorMessage = "路由名称不能为空")]
        [StringLength(200, MinimumLength = 1)]
        public string RouteName { get; set; } = string.Empty;

        /// <summary>
        /// 集群名称，例如 "MyServiceCluster"
        /// </summary>
        [Required(ErrorMessage = "集群名称不能为空")]
        [StringLength(200, MinimumLength = 1)]
        public string ClusterName { get; set; } = string.Empty;

        /// <summary>
        /// 匹配路径模板，例如 /api/my-service/{**catchAll}
        /// </summary>
        [Required(ErrorMessage = "匹配路径不能为空")]
        public string MatchPath { get; set; } = string.Empty;

        /// <summary>
        /// 目标地址，例如 http://localhost:5000
        /// </summary>
        [Required(ErrorMessage = "目标地址不能为空")]
        [Url(ErrorMessage = "目标地址必须是有效的 URL")]
        public string DestinationAddress { get; set; } = string.Empty;

        /// <summary>
        /// 路由优先级（可选，默认 50），数值越小越优先
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "优先级必须大于等于 0")]
        public int? Order { get; set; }
    }
}
