using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tideward;

public static partial class AppConfig
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string TidewardExecutePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Tideward.exe");
}
