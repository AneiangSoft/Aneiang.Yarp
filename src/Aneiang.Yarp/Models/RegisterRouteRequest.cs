using System.ComponentModel.DataAnnotations;

namespace Aneiang.Yarp.Models;

/// <summary>Route registration request / 路由注册请求.</summary>
public class RegisterRouteRequest
{
    /// <summary>Route name (unique). 路由名称（唯一）.</summary>
    [Required(ErrorMessage = "Route name is required")]
    [StringLength(200, MinimumLength = 1)]
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Cluster name. 集群名称.</summary>
    [Required(ErrorMessage = "Cluster name is required")]
    [StringLength(200, MinimumLength = 1)]
    public string ClusterName { get; set; } = string.Empty;

    /// <summary>Match path, e.g. /api/service/{**catch-all}. 匹配路径.</summary>
    [Required(ErrorMessage = "Match path is required")]
    public string MatchPath { get; set; } = string.Empty;

    /// <summary>Destination address, e.g. http://localhost:5000. 目标地址.</summary>
    [Required(ErrorMessage = "Destination address is required")]
    [Url(ErrorMessage = "Destination address must be a valid URL")]
    public string DestinationAddress { get; set; } = string.Empty;

    /// <summary>Route priority (default 50). Lower = higher precedence. 路由优先级，越小越优先.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "Priority must be >= 0")]
    public int? Order { get; set; }

    /// <summary>Optional transforms, e.g. [{"PathSet": "/api/backend"}]. 可选路由转换.</summary>
    public List<Dictionary<string, string>>? Transforms { get; set; }
}
