using CommunityToolkit.Mvvm.ComponentModel;
using Tideward.Features.Codec;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Tideward.Features.Screenshot;

public partial class ScreenshotItem : ObservableObject
{

    public string Name { get; set; }

    public string FilePath { get; set; }

    public string FileName { get; set; }

    public string FileInfo { get; set => SetProperty(ref field, value); }

    public DateTime CreationTime { get; set; }

    public string CreationTimeText { get; set; }

    public string TimeMonthDay { get; set; }

    public ScreenshotItem(string file)
    {
        FilePath = file;
        FileName = Path.GetFileName(file);
        Name = Path.GetFileNameWithoutExtension(file);
        if (TryParseCreationTime(Name, out var time))
        {
            CreationTime = time;
        }
        else
        {
            var info = new FileInfo(file);
            CreationTime = info.CreationTime;
            FileInfo = GetFileInfo(info);
            _fileInfoSet = true;
        }
        CreationTimeText = CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
        TimeMonthDay = CreationTime.ToString("yyyy-MM-dd");
    }

    public static bool TryParseCreationTime(string name, out DateTime time)
    {

        if (DateTime.TryParseExact(TidewardNameRegex().Match(name).Value, "yyyyMMdd_HHmmssff", null, DateTimeStyles.None, out time))
        {
            return true;
        }

        if (DateTime.TryParseExact(NvidiaNameRegex().Match(name).Value, "yyyy.MM.dd - HH.mm.ss.ff", null, DateTimeStyles.None, out time))
        {
            return true;
        }

        if (DateTime.TryParseExact(XBoxNameRegex().Match(name).Value, "yyyy_M_d H_mm_ss", null, DateTimeStyles.None, out time))
        {
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"\d{8}_\d{8}")]
    private static partial Regex TidewardNameRegex();

    [GeneratedRegex(@"\d{4}\.\d{2}\.\d{2} - \d{2}\.\d{2}\.\d{2}\.\d+")]
    private static partial Regex NvidiaNameRegex();

    [GeneratedRegex(@"\d{4}_\d{1,2}_\d{1,2} \d{1,2}_\d{2}_\d{2}")]
    private static partial Regex XBoxNameRegex();

    private static string GetFileInfo(FileInfo info)
    {
        const double KB = 1 << 10, MB = 1 << 20;
        string ext = info.Extension.Replace(".", "").ToUpper();
        string size = info.Length >= MB ? $"{info.Length / MB:F2} MB" : $"{info.Length / KB:F2} KB";
        return $"{ext}  {size}".Trim();
    }

    private bool _fileInfoSet = false;

    private bool _updatedPixelSize = false;

    public async void UpdatePixelSize()
    {
        try
        {
            if (_updatedPixelSize)
            {
                return;
            }
            if (!_fileInfoSet)
            {
                var info = new FileInfo(FilePath);
                FileInfo = GetFileInfo(info);
                _fileInfoSet = true;
            }
            (uint width, uint height) = await ImageLoader.GetImagePixelSizeAsync(FilePath);
            if (width > 0 && height > 0)
            {
                FileInfo = $"{FileInfo}  {width} x {height}";
                _updatedPixelSize = true;
            }
        }
        catch { }
    }

}

public class ScreenshotItemGroup : ObservableCollection<ScreenshotItem>
{

    public string Header { get; set; }

    public ScreenshotItemGroup(string header, IEnumerable<ScreenshotItem> list) : base(list)
    {
        Header = header;
    }

    public ScreenshotItemGroup(string header) : base()
    {
        Header = header;
    }

}