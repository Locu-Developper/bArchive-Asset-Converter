namespace BlueArchive_Assets_Converter.ModuleBox.SqliteDbLibs;

public class SqliteRecord
{
    public byte[] Crc {get; set;}
    public byte[] Size {get; set;}
    public int CurrentIdx {get; set;}
    public string FileName {get; set;}
    public string FullPath {get; set;}
    public string Path {get; set;}
}