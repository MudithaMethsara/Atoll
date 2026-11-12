using System.IO;
using System.Text;

namespace Atoll.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public sealed class LoggingService : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private long _writtenBytes;
    private bool _disposed;
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;

    public static LoggingService Instance { get; } = new();

    public string LogFilePath => _logFilePath;

    private LoggingService()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atoll",
            "logs");
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, "atoll.log");
        OpenWriter();
    }

    private void OpenWriter()
    {
        try
        {
            var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writtenBytes = stream.Length;
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            _writtenBytes = 0;
            _writer = null;
        }
    }

    public void Log(LogLevel level, string category, string message, Exception? ex = null)
    {
        if (_disposed) return;

        string line = ex is null
            ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {message}"
            : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {message} | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                if (_writtenBytes >= MaxFileSizeBytes)
                {
                    RotateIfNeeded();
                }

                if (_writer is not null)
                {
                    _writer.WriteLine(line);
                    _writtenBytes += Encoding.UTF8.GetByteCount(line) + 2;
                }
            }
            catch
            {
                // Logging must never throw into app execution
            }
        }
    }

    public void Debug(string category, string message)
    {
#if DEBUG
        Log(LogLevel.Debug, category, message);
#endif
    }

    public void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public void Warn(string category, string message, Exception? ex = null) => Log(LogLevel.Warn, category, message, ex);
    public void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logFilePath)) return;

        _writer?.Dispose();
        _writer = null;

        for (int i = 2; i >= 1; i--)
        {
            string src = Path.Combine(_logDirectory, $"atoll.{i}.log");
            string dst = Path.Combine(_logDirectory, $"atoll.{i + 1}.log");
            if (File.Exists(dst)) File.Delete(dst);
            if (File.Exists(src)) File.Move(src, dst);
        }

        string firstArchive = Path.Combine(_logDirectory, "atoll.1.log");
        if (File.Exists(firstArchive)) File.Delete(firstArchive);
        File.Move(_logFilePath, firstArchive);
        OpenWriter();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}

