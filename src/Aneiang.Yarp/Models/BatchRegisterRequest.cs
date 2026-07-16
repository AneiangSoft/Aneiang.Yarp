namespace Aneiang.Yarp.Models;

public class BatchRegisterRequest
{
    public List<RegisterRouteRequest> Routes { get; set; } = new();
    public string? Source { get; set; }
    public string? CreatedBy { get; set; }
}
