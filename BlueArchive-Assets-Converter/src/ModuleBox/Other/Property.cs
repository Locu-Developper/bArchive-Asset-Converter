namespace BlueArchive_Assets_Converter.ModuleBox.Other;

public static class Property
{
    public const string BArchivePackageName = "com.YostarJP.BlueArchive";
    public const string BArchiveRun = BArchivePackageName + "/com.yostarjp.bluearchive.MxUnityPlayerActivity";
    public const string AndroidMediaPatchFolder = $"/sdcard/Android/data/{BArchivePackageName}/files/MediaPatch";
    public const string MediaCatalog = "MediaCatalog.bytes";
    public const string DataBasePath = "./MediaCatalog.db";
    public const string OutMediaPatchFolder = "./MediaPatch";

    public const string MediaCatalogCreate =  "CREATE TABLE IF NOT EXISTS MediaCatalog" +
                                              "(id INTEGER PRIMARY KEY, " +
                                              "Crc BLOB, " +
                                              "Size BLOB, " +
                                              "FullPath TEXT UNIQUE, " +
                                              "Path TEXT UNIQUE, " +
                                              "FileName TEXT UNIQUE)";
}