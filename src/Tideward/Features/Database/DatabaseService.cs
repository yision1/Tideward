using Dapper;
using Microsoft.Data.Sqlite;
using SharpSevenZip;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Tideward.Features.Database;

internal static class DatabaseService
{

    private static string _databasePath;

    private static string _connectionString;

    private static Lock _lock = new();

    static DatabaseService()
    {
        SqlMapper.AddTypeHandler(new DapperSqlMapper.DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new DapperSqlMapper.StringListHandler());
        SqlMapper.AddTypeHandler(new DapperSqlMapper.GameBizHandler());
    }

    public static SqliteConnection CreateConnection()
    {
        var con = new SqliteConnection(_connectionString);
        con.Open();
        return con;
    }

    public static void SetDatabase(string folder)
    {
        if (Directory.Exists(folder))
        {
            _databasePath = Path.GetFullPath(Path.Combine(folder, "TidewardDatabase.db"));
            _connectionString = $"DataSource={_databasePath};";
            InitializeDatabase();
        }
    }

    private static void InitializeDatabase()
    {
        lock (_lock)
        {
            using var con = CreateConnection();
            int version = con.QueryFirstOrDefault<int>("PRAGMA USER_VERSION;");
            if (version == 0)
            {
                con.Execute("PRAGMA JOURNAL_MODE = WAL;");
            }
            con.Execute(DatabaseSchema);
            if (version == 0) con.Execute("PRAGMA USER_VERSION = 21;");
        }
    }

    public static void BackupDatabase(string file)
    {
        using var backupCon = new SqliteConnection($"DataSource={file}; Pooling=False;");
        backupCon.Open();
        using var con = CreateConnection();
        con.Execute("VACUUM;", commandType: CommandType.Text);
        con.BackupDatabase(backupCon);
    }

    public static void AutoBackupToAppDataLocal()
    {
        try
        {
#if DEBUG
            return;
#endif
#pragma warning disable CS0162 // 检测到无法访问的代码
            string folder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Tideward\DatabaseBackup");
#pragma warning restore CS0162 // 检测到无法访问的代码
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"TidewardDatabase_AutoBackup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            string archive = Path.ChangeExtension(file, ".7z");
            string archive_tmp = archive + "_tmp";
            string[] files = Directory.GetFiles(folder, "TidewardDatabase_AutoBackup_*.7z");
            if (files.Length == 0)
            {
                BackupDatabase(file);
                new SharpSevenZipCompressor().CompressFiles(archive_tmp, file);
                File.Move(archive_tmp, archive, true);
                File.Delete(file);
            }
            else
            {
                string last = files.OrderByDescending(File.GetLastWriteTime).First();
                if (DateTime.Now - File.GetLastWriteTime(last) > TimeSpan.FromDays(7))
                {
                    BackupDatabase(file);
                    new SharpSevenZipCompressor().CompressFiles(archive_tmp, file);
                    File.Move(archive_tmp, archive, true);
                    File.Delete(file);
                    File.Delete(last);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private class KVT
    {

        public KVT() { }

        public KVT(string key, string value, DateTime time)
        {
            Key = key;
            Value = value;
            Time = time;
        }

        public string Key { get; set; }

        public string Value { get; set; }

        public DateTime Time { get; set; }
    }

    public static T? GetValue<T>(string key, out DateTime time, T? defaultValue = default)
        => TryGetValue(key, out T? result, out time, defaultValue) ? result : defaultValue;

    public static bool TryGetValue<T>(string key, out T? result, out DateTime time, T? defaultValue = default)
    {
        result = defaultValue;
        time = DateTime.MinValue;
        try
        {
            using var con = CreateConnection();
            var kvt = con.QueryFirstOrDefault<KVT>("SELECT * FROM KVT WHERE Key = @key LIMIT 1;", new { key });
            if (kvt != null)
            {
                time = kvt.Time;
                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter == null)
                {
                    return false;
                }
                result = (T?)converter.ConvertFromString(kvt.Value);
                return true;
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public static void SetValue<T>(string key, T value, DateTime? time = null)
    {
        try
        {
            using var con = CreateConnection();
            con.Execute("INSERT OR REPLACE INTO KVT (Key, Value, Time) VALUES (@Key, @Value, @Time);", new KVT(key, value?.ToString() ?? "", time ?? DateTime.Now));

        }
        catch { }
    }

    private const string DatabaseSchema = """
        BEGIN TRANSACTION;
        CREATE TABLE IF NOT EXISTS KVT (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL, Time TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS Setting (Key TEXT NOT NULL PRIMARY KEY, Value TEXT);
        CREATE TABLE IF NOT EXISTS GameAccount
        (
            SHA256 TEXT NOT NULL, GameBiz TEXT NOT NULL, Uid TEXT NOT NULL,
            Name TEXT NOT NULL, Value BLOB NOT NULL, Time TEXT NOT NULL,
            PRIMARY KEY (SHA256, GameBiz)
        );
        CREATE INDEX IF NOT EXISTS IX_GameAccount_GameBiz ON GameAccount (GameBiz);
        CREATE TABLE IF NOT EXISTS GachaLogUrl
        (
            GameBiz TEXT NOT NULL, Uid INTEGER NOT NULL, Url TEXT NOT NULL, Time TEXT NOT NULL,
            PRIMARY KEY (GameBiz, Uid)
        );
        CREATE TABLE IF NOT EXISTS PlayTimeItem
        (
            TimeStamp INTEGER PRIMARY KEY, GameBiz TEXT NOT NULL, Pid INTEGER NOT NULL,
            State INTEGER NOT NULL, CursorPos INTEGER NOT NULL, Message TEXT
        );
        CREATE INDEX IF NOT EXISTS IX_PlayTimeItem_GameBiz ON PlayTimeItem (GameBiz);
        CREATE INDEX IF NOT EXISTS IX_PlayTimeItem_Pid ON PlayTimeItem (Pid);
        CREATE INDEX IF NOT EXISTS IX_PlayTimeItem_State ON PlayTimeItem (State);
        CREATE TABLE IF NOT EXISTS PlayTimeStats
        (
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameBiz TEXT NOT NULL,
            Pid INTEGER NOT NULL, StartTime INTEGER NOT NULL, EndTime INTEGER NOT NULL,
            Interruption INTEGER NOT NULL DEFAULT 0, Type INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_PlayTimeStats_GameBiz_StartTime_Pid ON PlayTimeStats (GameBiz, StartTime, Pid);
        COMMIT TRANSACTION;
        """;

}
