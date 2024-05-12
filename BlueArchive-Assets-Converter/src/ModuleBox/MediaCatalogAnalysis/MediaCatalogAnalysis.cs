using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;
using BlueArchive_Assets_Converter.ModuleBox.Crc_Calculate;
using BlueArchive_Assets_Converter.ModuleBox.Other;
using BlueArchive_Assets_Converter.ModuleBox.SqliteDbLibs;

namespace BlueArchive_Assets_Converter.ModuleBox.MediaCatalogAnalysis;

public static class MediaCatalogAnalysis
{
    private const string DataBasePath = Property.DataBasePath;

    /// <summary>
    /// MediaCatalogの解析メイン関数
    /// </summary>
    public static async Task OnAssetsAnalysisProcess()
    {
        try
        {
            var allBytes =
                (await File.ReadAllBytesAsync($"{Property.OutMediaPatchFolder}/{Property.MediaCatalog}")).Reverse().ToArray();

            Console.WriteLine("解析開始");
            // MediaCatalog.bytesのデバッグ用リバース配列
            // await File.WriteAllBytesAsync("./MediaCatalogReverse.bytes", allBytes);

            if (File.Exists(DataBasePath))
            {
                SQLiteConnection.CreateFile(DataBasePath);
            }

            await using var db = new SQLiteConnection("Data Source = " + DataBasePath);
            await db.OpenAsync();
            await using var cmd = new SQLiteCommand(db);
            cmd.CommandText = Property.MediaCatalogCreate;
            cmd.ExecuteNonQuery();


            // var list = new List<IEnumerable<byte>>();
            var list = new List<SqliteRecord>();
            var currentIdx = 0;
            await Task.Run(() =>
            {
                while (currentIdx + 5 < allBytes.Length)
                {
                    if (allBytes[currentIdx] == 0x00 && CheckPattern(allBytes, currentIdx))
                    {

                        
                        // currentIndex(バイト列の最初)から最後のインデックスを取得
                        var content = GetContent(allBytes, currentIdx);
                        if (content == null)
                        {
                            Console.WriteLine(currentIdx);
                            throw new Exception("不正な値を出力しました。処理が間違っているかもしれません");
                            // continue;
                        }

                        list.Add(content);

                        currentIdx = content.CurrentIdx;
                        Console.WriteLine($"{currentIdx} / {allBytes.Length}");
                    }
                    else
                    {
                        currentIdx++;
                    }
                }

                Console.WriteLine("処理終了");

                list.TrimExcess();

                var tran = db.BeginTransaction();

                foreach (var record in list)
                {
                    cmd.CommandText =
                        "INSERT OR IGNORE INTO MediaCatalog" +
                        "(Crc, Size, FullPath, Path, FileName) " +
                        "VALUES" +
                        "(@Crc, @Size, @Full, @Path, @File)";

                    cmd.Parameters.Add(new SQLiteParameter("@Crc", record.Crc));
                    cmd.Parameters.Add(new SQLiteParameter("@Size", record.Size));
                    cmd.Parameters.Add(new SQLiteParameter("@Full", record.FullPath));
                    cmd.Parameters.Add(new SQLiteParameter("@Path", record.Path));
                    cmd.Parameters.Add(new SQLiteParameter("@File", record.FileName));
                    var tmp = cmd.ExecuteNonQuery();
                    if (tmp >= 0)
                    {
                        continue;
                    }

                    Console.WriteLine("■トランジション失敗...ロールバックします");
                    tran.Rollback();
                    return;
                }
                tran.Commit();
                Console.WriteLine("保存終了");
            });
            
            db.Close();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    /// <summary>
    /// 00 00 00 <メディアタイプ> 00 00 00 00 00 00 とバイト列が一致するか比較する
    /// </summary>
    /// <param name="allBytes"> MediaPatch内の全ファイル情報のバイト配列 </param>
    /// <param name="currentIdx"> ファイル1つ分の最初のインデックス  </param>
    /// <returns>True: パターン一致   False: パターン不一致</returns>
    private static bool CheckPattern(IEnumerable<byte> allBytes, int currentIdx)
    {
        byte?[] pattern = [0x00, 0x00, 0x00, null, 0x00, null, 0x00, 0x00, 0x00, 0x00];
        var checkByteArr = allBytes.Skip(currentIdx).Take(10).ToArray();

        for (var i = 0; i < checkByteArr.Length; i++)
        {
            if (pattern[i] != null)
            {
                if (pattern[i] != checkByteArr[i])
                {
                    return false;
                }
            }
            else
            {
                switch (checkByteArr[i])
                {
                    case 0x01 or 0x02 or 0x03:
                        continue;
                    default:
                        continue;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// ファイル1つ分の最後のインデックスを取得して返す
    /// </summary>
    /// <param name="allBytes"> MediaPatch内の全ファイル情報のバイト配列 </param>
    /// <param name="currentIdx"> ファイル1つ分の最初のインデックス </param>
    /// <returns> int: 最後のインデックスを返す。パターンと一致しなければ intの最大値を返す </returns>
    private static SqliteRecord? GetContent(IReadOnlyList<byte> allBytes, int currentIdx)
    {
        byte[] crc, size;
        string fileName, fullPath, path;
        try
        {
            currentIdx += 10; // 10ビット

            // CRCを取得
            crc = allBytes.Skip(currentIdx).Take(4).ToArray();
            currentIdx += 4; // 4ビット

            // 00 を 5 or 4回カウント
            const int betWeenOfCrcAndSize = 5;
            var maxBitsSize = currentIdx + betWeenOfCrcAndSize;
            var cnt = 0;
            for (var i = currentIdx; i < maxBitsSize; i++)
            {
                if (allBytes[i] != 0x00)
                {
                    break;
                }

                cnt++;
            }
            currentIdx += cnt;
            
            // サイズを取得
            var sizeBit = 8 - cnt;
            size = allBytes.Skip(currentIdx).Take(sizeBit).ToArray();
            currentIdx += sizeBit;
            
            // // 00とサイズを取得
            // const int paddingAndSize = 8;
            // var zeroPadAndSize = string.Join("", allBytes.Skip(currentIdx).Take(paddingAndSize));
            // size = int.Parse(MyRegex().Replace(zeroPadAndSize, ""));
            // currentIdx += paddingAndSize;

            // ファイル名を取得 pattern が出てくるまで繰り返す
            var fileNameObj =
                GetFileOrFullPath(allBytes, currentIdx, new Dictionary<string, object>(), 1);
            fileName = fileNameObj["fileName"].ToString() ?? throw new NullReferenceException("Nullを検出");
            fullPath = fileNameObj["fullPath"].ToString() ?? throw new NullReferenceException("Nullを検出");
            path = fileNameObj["path"].ToString() ?? throw new NullReferenceException("Nullを検出");
            currentIdx = int.Parse(fileNameObj["current"].ToString() ?? throw new NullReferenceException("カレントインデックスが不正です"));
        }
        catch (NullReferenceException e)
        {
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }

        return new SqliteRecord
            { Crc = crc, Size = size, FileName = fileName, FullPath = fullPath, Path = path, CurrentIdx = currentIdx };
    }

    /// <summary>
    /// バイト配列をInt32(int)に変換する
    /// </summary>
    /// <param name="bytes"> バイト配列 </param>
    /// <param name="index"> バイト配列の何番目からか </param>
    /// <returns> int型の数字が返される </returns>
    private static uint ByteArrToInt32(IEnumerable<byte> bytes)
    {
        var tmp = Convert.ToHexString(bytes.ToArray());
        return Convert.ToUInt32(tmp, 16);
    }

    private static IDictionary<string, object> GetFileOrFullPath(
        IReadOnlyList<byte> allBytes, int currentIdx, IDictionary<string, object> fileObjs, int objCount)
    {
        byte?[] pattern = [0x00, 0x00, 0x00, null, 0xFF, 0xFF, 0xFF, null];

        if (objCount >= 4)
        {
            return fileObjs;
        }

        var keyName = objCount switch
        {
            1 => "fileName",
            2 => "fullPath",
            3 => "path",
            _ => throw new Exception("カウントが不正です")
        };

        for (var i = currentIdx; i < allBytes.Count; i++)
        {
            if (allBytes[i] != 0x00)
            {
                continue;
            }

            var checkArr = allBytes.Skip(i).Take(8).ToArray();
            var check = Enumerable.Range(0, pattern.Length)
                .All(j => pattern[j] == checkArr[j] || pattern[j] == null);

            if (!check)
            {
                continue;
            }

            if (objCount == 3)
            {
                currentIdx++;
            }

            // pattern と一致したら
            fileObjs.Add(keyName,
                Encoding.ASCII.GetString(allBytes.Skip(currentIdx).Take(i - currentIdx).Reverse().ToArray()));
            objCount++;
            currentIdx = i + pattern.Length;

            fileObjs["current"] = currentIdx;

            return GetFileOrFullPath(allBytes, currentIdx, fileObjs, objCount);
        }

        return fileObjs;
    }

    public static async Task<bool> IntegrityCheck()
    {
        try
        {
            Console.WriteLine("整合性チェック... 開始(少し時間がかかります)");
            var crcList = new List<ulong>();
        
        
            await using var db = new SQLiteConnection("Data Source = " + DataBasePath);
            await db.OpenAsync();
            await using var cmd = new SQLiteCommand(db);

            cmd.CommandText = "SELECT Crc FROM MediaCatalog";
            cmd.ExecuteNonQuery();

            await using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ulong.TryParse(reader[0] as string, out var crc);
                crcList.Add(crc);
            }

            // file = foreach(crc in crcList) -> foreach(file in Directory.GetFiles(Property.OutMediaPatchFolder))
            // -> if(Regex.IsMatch(Path.GetFileName(file), "^[0-9]{1,}_" + crc + "$")) True => select file
            foreach (var file in from crc in crcList from file in Directory.GetFiles(Property.OutMediaPatchFolder) 
                     where Regex.IsMatch(Path.GetFileName(file), "^[0-9]{1,}_" + crc + "$") select file)
            {
                var result = await Crc_32.CrcMainProcess(file);
                if (result == ulong.MaxValue)
                {
                    throw new Exception("Crc値が一致しませんでした。");
                }
            }
        
            db.Close();
            Console.WriteLine("整合性チェック... 完了！");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return false;
    }
}