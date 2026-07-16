namespace Aneiang.Yarp.Storage.Entities;

public class ProxyLogBodyEntity
{
    public long MetaId { get; set; }

    public string? Message { get; set; }

    public string? RequestBody { get; set; }

    public string? ResponseBody { get; set; }

    public string? RequestHeaders { get; set; }

    public string? ResponseHeaders { get; set; }

    public string? DownstreamBody { get; set; }

    public string? Exception { get; set; }
}
