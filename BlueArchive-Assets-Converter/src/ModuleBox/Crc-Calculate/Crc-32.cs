using System.Text;

namespace BlueArchive_Assets_Converter.ModuleBox.Crc_Calculate;

public class Crc_32
{
    private static uint[] _table = [];

    public static async Task<ulong> CrcMainProcess(string path)
    {
        try
        {
            var data = await File.ReadAllBytesAsync(path);

            return Calculate(data);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }

        return ulong.MaxValue;
    }

    private static ulong Calculate(byte[] bytes)
    {
        const uint polynomial = 0xEDB88320;
        var crc = 0xFFFFFFFF;
        _table = new uint[256];
        for (uint i = 0; i < 256; i++) {
            var temp = i;
            for (var j = 8; j > 0; j--) {
                if ((temp & 1) == 1) {
                    temp = (temp >> 1) ^ polynomial;
                } else {
                    temp >>= 1;
                }
            }
            _table[i] = temp;
        }
        foreach (var b in bytes) {
            var index = (byte)(((crc) & 0xff) ^ b);
            crc = (crc >> 8) ^ _table[index];
        }
        return crc ^ 0xFFFFFFFF;
    }
}