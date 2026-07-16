namespace Aneiang.Yarp.Storage;

public class StorageOptions
{
    public const string SectionName = "Gateway:Storage";
    public SqliteStorageOptions Sqlite { get; set; } = new();
}
