using System.Data.SQLite;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BlueArchive_Assets_Converter.ModuleBox.LiteDataBaseProperty;
using BlueArchive_Assets_Converter.ModuleBox.SqliteDbLibs;
using BlueArchive_Assets_Converter.ViewModel;
using LiteDB;

namespace BlueArchive_Assets_Converter.View;

public partial class MainPage
{
    public MainPage()
    {
        InitializeComponent();
        
        OutPutFolderPath.Text = Preferences.Get("path", "");
        DeviceType.SelectedIndex = 0;
    }

    /// <summary>
    /// MediaCatalogの解析メイン関数
    /// </summary>
    private async void OnAssetsAnalysisProcess(object s, EventArgs e)
    {
        var outputFolder = OutPutFolderPath.Text;
        if (outputFolder == string.Empty)
        {
            return;
        }

        Preferences.Set("path", outputFolder);
        await MainViewModel.MainAssetsAnalysis(outputFolder, DeviceType.SelectedItem.ToString());
    }

}