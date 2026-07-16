namespace Aneiang.Yarp.Storage;

public class ProxyLogTrafficBucket
{
    public DateTime TimeBucket { get; set; }
    public int RequestCount { get; set; }
    public int ErrorCount { get; set; }
}
