using System.Data.SQLite;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using BlueArchive_Assets_Converter.ModuleBox.MediaCatalogAnalysis;
using BlueArchive_Assets_Converter.ModuleBox.Other;
using FileSystem = Microsoft.VisualBasic.FileSystem;

namespace BlueArchive_Assets_Converter.ViewModel;

public class MainViewModel
{
    // Adb.exeパス
    private static readonly string AdbPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "adb-windows", "adb.exe");

    // adbクライアント変数
    private static AdbClient _adbClient;
    private static DeviceData _device;
    private static DeviceClient _deviceClient;

    // adb入出力用変数
    private static ConsoleOutputReceiver _receiver;

    private const string Mobile = "127.0.0.1:7555";
    private static ProcessStartInfo _startInfo;
    private static Process _process;
    private static string _outputFolder = "";
    private static List<string> _diffList = [];
    private static int _mediaType = 1;
    
    public static async Task MainAssetsAnalysis(string outputFolder, string deviceType)
    {
        // 出力先フォルダ
        _outputFolder = outputFolder;
        
        // デバイス種類
        _mediaType = deviceType switch
        {
            "Physical Device (物理デバイス)" => 0,
            "MUMU Player X" => 1,
            _ => _mediaType
        };
        
        //adbサーバ＆クライアントの初期設定
        var result = await ConnectAdb();
        if(!result) { return; }
        
        // 初期設定
        _receiver = new ConsoleOutputReceiver();
        _deviceClient = new DeviceClient(_adbClient, _device);

        await Task.Run(async () =>
        {
            // MediaPatchフォルダをコピー
            await CopyMediaPatchFolder();

            // 整合性チェックを実行(CRC-32)
            // 整合性がなければ以下を実行
            // ・ NoSQLの初期化(データクリア)
            // ・ エミュレータからデータを取得
            // ・ GetCatalog()　CatalogAnalysis() を実行
            await CheckDataIntegrity();

            // // アップデート差分ファイル名を表示
            DifferenceFileDisplay();

            // ファイル名の上書き
            await ChangeFileName();
            
            // MediaPatchフォルダの削除
            Directory.Delete(Property.OutMediaPatchFolder, true);
        });

        Console.WriteLine("処理終了");
    }


    private static async Task<bool> ConnectAdb()
    {
        try
        {
            // Adbサーバを宣言 & 設定
            if (!AdbServer.Instance.GetStatus().IsRunning)
            {
                var server = new AdbServer();
                var result = await server.StartServerAsync(AdbPath);
                if (result != StartServerResult.Started)
                {
                    Console.WriteLine("Can't start abd server");
                }
            }

            // Adbクライアントを設定
            AdbClient.SetEncoding(Encoding.UTF8);
            _adbClient = new AdbClient();
            if (_mediaType == 1)
            {
                await _adbClient.ConnectAsync(Mobile);
            }

            _device = (await _adbClient.GetDevicesAsync()).FirstOrDefault();

            if (!_device.Model.Equals("cannot"))
            {
                Console.WriteLine($"接続端末>> {_device.Model}");
                return true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }

        return false;
    }

    private static async Task CopyMediaPatchFolder()
    {
        try
        {
            
            // MediaPatchフォルダ生成
            if (!Directory.Exists(Property.OutMediaPatchFolder))
            {
                Directory.CreateDirectory(Property.OutMediaPatchFolder);
            }

            // MediaPatchのファイルリストアップ
            await _receiver.FlushAsync();
            await _adbClient.ExecuteRemoteCommandAsync($"ls {Property.AndroidMediaPatchFolder}", _device, _receiver);
            var output = _receiver.ToString();
            var mediaListUp =
                Regex.Replace(output, "\\s+", "\n").Split("\n", StringSplitOptions.RemoveEmptyEntries);
            
            // コピー実行( 3m 45s かかった)
            for (var i = 0; i < mediaListUp.Length; i++)
            {
                Console.WriteLine($"{i} / {mediaListUp.Length}");
                using var service = new SyncService(_device);
                await using var stream = new FileStream(Path.Combine(Property.OutMediaPatchFolder, mediaListUp[i]),
                    FileMode.Create, FileAccess.Write);
                await service.PullAsync(
                    $"{Property.AndroidMediaPatchFolder}/{mediaListUp[i]}",
                    stream);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static async Task GetCatalog()
    {
        try
        {
            // ブルアカを立ち上げる
            await _deviceClient.StartAppAsync(Property.BArchivePackageName);

            // MediaCatalog.bytesをコピーする
            using var service = new SyncService(_device);
            await using var stream = new FileStream(Path.Combine(Property.OutMediaPatchFolder, Property.MediaCatalog),
                FileMode.Append, FileAccess.Write);
            while (true)
            {
                await _adbClient.ExecuteRemoteCommandAsync($"ls {Property.AndroidMediaPatchFolder}", _device,
                    _receiver);
                await _receiver.FlushAsync();
                if (!_receiver.ToString().Contains(Property.MediaCatalog))
                {
                    Console.Write(".");
                    continue;
                }

                Console.WriteLine("!");
                await service.PullAsync(
                    $"{Property.AndroidMediaPatchFolder}/{Property.MediaCatalog}",
                    stream);

                Console.WriteLine("MediaCatalog.bytesを取得");
                // ブルアカを強制的にタスクキルする
                await _deviceClient.StopAppAsync(Property.BArchivePackageName);
                break;
            }

            // MediaCatalog.bytesを削除する
            // (処理順序を一律にするため => MediaCatalog.bytesがあるとMediaCatalog Initializeが飛ばされる)
            await _receiver.FlushAsync();
            await _adbClient.ExecuteRemoteCommandAsync(
                $"rm {Property.AndroidMediaPatchFolder}/{Property.MediaCatalog}",
                _device,
                _receiver);
            if (_receiver.ToString().Equals(""))
            {
                Console.WriteLine(_receiver);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static async Task CheckDataIntegrity()
    {
        while (true)
        {
            // MediaCatalog.bytes取得
            await GetCatalog();

            // MediaCatalogから情報抽出
            // 種類ごとによって処理変更し、SQLite3に保存
            await MediaCatalogAnalysis.OnAssetsAnalysisProcess();

            var result = await MediaCatalogAnalysis.IntegrityCheck();
            if (!result)
            {
                // 出力先MediaPatchフォルダを削除・MediaPatchフォルダをコピー
                Directory.Delete(Property.OutMediaPatchFolder, true);
                await CopyMediaPatchFolder();

                continue;
            }

            break;
        }
    }

    private static async Task ChangeFileName()
    {
        try
        {
            Console.WriteLine("アセットファイルを生成中...");
            await using var db = new SQLiteConnection("Data Source = " + Property.DataBasePath);
            await db.OpenAsync();
            await using var cmd = new SQLiteCommand("SELECT Crc, FullPath, FileName FROM MediaCatalog", db);

            await using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var crcBytes = (byte[])reader["Crc"];
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(crcBytes);
                }
                var crc = BitConverter.ToUInt32(crcBytes, 0);
                // Console.WriteLine(crc);
                var source = Directory
                    .GetFiles(Property.OutMediaPatchFolder).First(f => 
                        Regex.IsMatch(f, "^.+_" + crc + "$"));

                var fullPath = reader["FullPath"].ToString();
                var contentPath = Path.Combine(_outputFolder,
                    Path.GetDirectoryName(fullPath) ?? throw new NullReferenceException());
                var fileName = reader["FileName"].ToString() ?? throw new NullReferenceException();
                var newFile = Path.Combine(contentPath, fileName);

                if (!Path.Exists(contentPath))
                {
                    Directory.CreateDirectory(contentPath);
                }
                
                File.Copy( source, Path.Combine(contentPath, Path.GetFileName(source)));
                if (Path.Exists(newFile))
                {
                    File.Delete(newFile);
                }
                FileSystem.Rename(Path.Combine(contentPath, Path.GetFileName(source)), newFile);
                Console.WriteLine($"生成完了！ : {fileName}");
            }

            db.Close();
        }
        catch (NullReferenceException e)
        {
            Console.WriteLine(e);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static void DifferenceFileDisplay()
    {
        File.WriteAllText(Path.Combine(_outputFolder, "diff.txt"), string.Join("\r\n", _diffList));
    }
}