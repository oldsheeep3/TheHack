using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Switcher.App.Logging;

/// <summary>
/// Writes the app's log to a daily file under <see cref="Configuration.AppPaths.LogDirectory"/>.
///
/// Almost every failure path in this application logs a warning and carries on — a HID write that did
/// not land, an output sink that never started, a source whose device is gone. With only
/// <c>AddDebug()</c> configured, all of that goes to a debugger nobody attaches in the field, so a
/// report of "PGM2 is dead" arrives with no evidence at all. This provider is what makes those warnings
/// worth writing.
///
/// Writes are serialized on a background thread so no caller — least of all the libobs graphics thread
/// or the HID read loop — ever blocks on disk. Logging can never throw: a broken log must not be able to
/// take down a live show.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int MaxQueuedEntries = 8192;

    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly LogLevel _minimumLevel;
    private readonly BlockingCollection<string> _queue = new(MaxQueuedEntries);
    private readonly Thread _writerThread;

    private StreamWriter? _writer;
    private DateOnly _writerDate;
    private bool _disposed;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information, int retentionDays = 14)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _retentionDays = retentionDays;

        PruneOldLogs();

        _writerThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = nameof(FileLoggerProvider),
        };
        _writerThread.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();

        // Bounded join: shutdown must not hang on a stuck disk.
        _writerThread.Join(TimeSpan.FromSeconds(2));

        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // Nothing useful to do while tearing down.
        }

        _writer = null;
        _queue.Dispose();
    }

    private bool IsEnabled(LogLevel level) => level >= _minimumLevel && level != LogLevel.None && !_disposed;

    /// <summary>Queues one formatted line. Drops it when the queue is full rather than blocking the
    /// caller — losing a log line is always better than stalling the thread that produced it.</summary>
    private void Enqueue(string line)
    {
        try
        {
            _queue.TryAdd(line);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Provider is shutting down.
        }
    }

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (_writer is null || _writerDate != today)
                {
                    _writer?.Dispose();
                    Directory.CreateDirectory(_directory);
                    _writer = new StreamWriter(
                        Path.Combine(_directory, $"switcher-{today:yyyyMMdd}.log"), append: true, Encoding.UTF8)
                    {
                        AutoFlush = true,
                    };
                    _writerDate = today;
                }

                _writer.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Disk full, folder removed, permissions changed. Drop the line and keep the loop alive so
                // logging resumes by itself once the cause clears.
                try
                {
                    _writer?.Dispose();
                }
                catch (IOException)
                {
                    // Ignored: we are already handling a write failure.
                }

                _writer = null;
            }
        }
    }

    private void PruneOldLogs()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "switcher-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Housekeeping only.
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        private readonly string _shortCategory = category.Split('.')[^1];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);

            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(Level(logLevel)).Append("] ")
                .Append(_shortCategory).Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            provider.Enqueue(builder.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
