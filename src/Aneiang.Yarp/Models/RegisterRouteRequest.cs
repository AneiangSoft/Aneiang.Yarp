using System.ComponentModel.DataAnnotations;

namespace Aneiang.Yarp.Models
{
    /// <summary>
    /// Route registration request.
    /// </summary>
    public class RegisterRouteRequest
    {
        /// <summary>
        /// Route name (unique identifier), e.g. "my-service-route"
        /// </summary>
        [Required(ErrorMessage = "Route name is required")]
        [StringLength(200, MinimumLength = 1)]
        public string RouteName { get; set; } = string.Empty;

        /// <summary>
        /// Cluster name, e.g. "MyServiceCluster"
        /// </summary>
        [Required(ErrorMessage = "Cluster name is required")]
        [StringLength(200, MinimumLength = 1)]
        public string ClusterName { get; set; } = string.Empty;

        /// <summary>
        /// Match path template, e.g. /api/my-service/{**catchAll}
        /// </summary>
        [Required(ErrorMessage = "Match path is required")]
        public string MatchPath { get; set; } = string.Empty;

        /// <summary>
        /// Destination address, e.g. http://localhost:5000
        /// </summary>
        [Required(ErrorMessage = "Destination address is required")]
        [Url(ErrorMessage = "Destination address must be a valid URL")]
        public string DestinationAddress { get; set; } = string.Empty;

        /// <summary>
        /// Route priority (optional, default 50). Lower values take precedence.
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "Priority must be >= 0")]
        public int? Order { get; set; }
    }
}
