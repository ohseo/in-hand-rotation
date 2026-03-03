using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Universal CSV-based experiment log manager.
/// - Supports multiple experiments (e.g., Optimization, Evaluation).
/// - For each experiment, supports three logical log types: Stream, Event, Summary.
/// - For Stream logs, creates one CSV file per trial.
/// - For Event and Summary logs, creates one CSV file per experimental run.
///
/// Scene / experiment managers are responsible for:
/// - Defining column order (string[] headers) once.
/// - Providing row values (string[] values) matching that order.
///
/// This class is agnostic to the actual meaning of the columns.
/// </summary>
public class ExperimentLogManager : MonoBehaviour
{
    #region Public API types

    public enum ExperimentType
    {
        Optimization = 0,
        Evaluation = 1
    }

    public enum LogType
    {
        Stream = 0,
        Event = 1,
        Summary = 2
    }

    /// <summary>
    /// Composite key identifying a single CSV "channel":
    /// - Experiment (Optimization / Evaluation, etc.)
    /// - Log type (Stream / Event / Summary)
    /// - Trial index (only used for Stream; null for Event/Summary)
    /// </summary>
    public readonly struct LogKey : IEquatable<LogKey>
    {
        public readonly ExperimentType Experiment;
        public readonly LogType Type;
        public readonly int? Trial;

        public LogKey(ExperimentType experiment, LogType type, int? trial = null)
        {
            Experiment = experiment;
            Type = type;
            Trial = trial;
        }

        public bool Equals(LogKey other)
        {
            return Experiment == other.Experiment &&
                   Type == other.Type &&
                   Trial == other.Trial;
        }

        public override bool Equals(object obj)
        {
            return obj is LogKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Experiment.GetHashCode();
                hash = hash * 23 + Type.GetHashCode();
                hash = hash * 23 + (Trial.HasValue ? Trial.Value.GetHashCode() : 0);
                return hash;
            }
        }
    }

    #endregion

    #region Internal CSV channel

    /// <summary>
    /// Represents one CSV file on disk.
    /// Writes header exactly once, then appends rows.
    /// </summary>
    private sealed class CsvChannel : IDisposable
    {
        private readonly string _path;
        private readonly StreamWriter _writer;

        public CsvChannel(string path, string[] headers)
        {
            _path = path;

            // Ensure directory exists
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create / overwrite file and write header line
            var fileStream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(fileStream, Encoding.UTF8);

            if (headers != null && headers.Length > 0)
            {
                var headerLine = ToCsvLine(headers);
                _writer.WriteLine(headerLine);
                _writer.Flush();
            }
        }

        public void Write(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return;
            }

            var line = ToCsvLine(values);
            _writer.WriteLine(line);
            _writer.Flush();
        }

        public void Dispose()
        {
            try
            {
                _writer?.Flush();
            }
            catch
            {
                // ignore flush errors on dispose
            }

            try
            {
                _writer?.Close();
            }
            catch
            {
                // ignore close errors on dispose
            }
        }
    }

    #endregion

    #region Singleton

    public static ExperimentLogManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    #endregion

    #region Serialized configuration

    [Header("File output")]
    [Tooltip("Base directory where CSV logs will be stored. " +
             "If left empty, Application.persistentDataPath + \"/Logs\" will be used.")]
    [SerializeField]
    private string baseDirectoryPath = "D:/Data";

    [Tooltip("If true, write each row immediately to disk. " +
             "If false, rows are still flushed per-write by default StreamWriter settings.")]
    [SerializeField]
    private bool flushOnEachWrite = true;

    #endregion

    #region State

    // One "run id" identifies a single experimental run (participant + condition, etc.)
    private string _runId;

    // All currently open CSV channels for this run
    private readonly Dictionary<LogKey, CsvChannel> _channels = new Dictionary<LogKey, CsvChannel>();

    #endregion

    #region Public API

    /// <summary>
    /// Begin a new experimental run.
    /// Clears any existing open channels and builds a new run id.
    /// Typical usage: call once when an experiment session starts.
    /// </summary>
    /// <param name="participantNumber">Participant identifier (e.g., 1, 2, ...).</param>
    /// <param name="experimentIndex">Experiment index (e.g., 1 for optimization, 2 for evaluation).</param>
    /// <param name="condition">Experimental condition index.</param>
    public void BeginRun(int participantNumber, int experimentIndex, int condition)
    {
        CloseAllChannels();

        _runId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_P{participantNumber}_Exp{experimentIndex}_Condition{condition}";
    }

    /// <summary>
    /// Log a row to a per-trial Stream CSV file.
    /// A separate file is created for each (Experiment, Trial) pair.
    /// </summary>
    public void LogStreamRow(ExperimentType experiment, int trialIndex, string[] headers, string[] values)
    {
        var key = new LogKey(experiment, LogType.Stream, trialIndex);
        LogRow(key, headers, values);
    }

    /// <summary>
    /// Log a row to an Event CSV file for the given experiment.
    /// One Event file per (Run, Experiment) pair.
    /// </summary>
    public void LogEventRow(ExperimentType experiment, string[] headers, string[] values)
    {
        var key = new LogKey(experiment, LogType.Event);
        LogRow(key, headers, values);
    }

    /// <summary>
    /// Log a row to a Summary CSV file for the given experiment.
    /// One Summary file per (Run, Experiment) pair.
    /// </summary>
    public void LogSummaryRow(ExperimentType experiment, string[] headers, string[] values)
    {
        var key = new LogKey(experiment, LogType.Summary);
        LogRow(key, headers, values);
    }

    /// <summary>
    /// End the current run and close all open CSV files.
    /// </summary>
    public void EndRun()
    {
        CloseAllChannels();
        _runId = null;
    }

    #endregion

    #region Internal helpers

    private void LogRow(LogKey key, string[] headers, string[] values)
    {
        if (values == null)
        {
            Debug.LogWarning("ExperimentLogManager.LogRow called with null values; row skipped.");
            return;
        }

        if (string.IsNullOrEmpty(_runId))
        {
            Debug.LogWarning("ExperimentLogManager.LogRow called before BeginRun; auto-generating run id.");
            _runId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_Run";
        }

        if (!_channels.TryGetValue(key, out var channel))
        {
            // First time we see this (experiment, type, trial) → create file with header
            var path = BuildFilePath(key);
            channel = new CsvChannel(path, headers);
            _channels[key] = channel;
        }

        channel.Write(values);

        if (flushOnEachWrite)
        {
            // CsvChannel.Write already flushes each line; this flag exists in case
            // you later change buffering behaviour.
        }
    }

    private string BuildFilePath(LogKey key)
    {
        string directory = baseDirectoryPath;

        if (string.IsNullOrEmpty(directory))
        {
            directory = Path.Combine(Application.persistentDataPath, "Logs");
        }

        string experimentLabel = key.Experiment.ToString();
        string suffix;

        switch (key.Type)
        {
            case LogType.Stream:
                suffix = $"_{experimentLabel}_Trial{key.Trial}_StreamData.csv";
                break;
            case LogType.Event:
                suffix = $"_{experimentLabel}_EventLog.csv";
                break;
            case LogType.Summary:
                suffix = $"_{experimentLabel}_Summary.csv";
                break;
            default:
                suffix = $"_{experimentLabel}.csv";
                break;
        }

        return Path.Combine(directory, _runId + suffix);
    }

    private void CloseAllChannels()
    {
        foreach (var channel in _channels.Values)
        {
            if (channel != null)
            {
                try
                {
                    channel.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"ExperimentLogManager: error disposing channel: {e.Message}");
                }
            }
        }

        _channels.Clear();
    }

    /// <summary>
    /// Convert an array of fields to a single CSV line with basic escaping.
    /// </summary>
    private static string ToCsvLine(string[] fields)
    {
        if (fields == null || fields.Length == 0)
        {
            return string.Empty;
        }

        var result = new string[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i] ?? string.Empty;
            bool mustQuote = f.Contains(",") || f.Contains("\"") || f.Contains("\n") || f.Contains("\r");
            if (mustQuote)
            {
                f = "\"" + f.Replace("\"", "\"\"") + "\"";
            }
            result[i] = f;
        }

        return string.Join(",", result);
    }

    private void OnDestroy()
    {
        CloseAllChannels();
    }

    private void OnApplicationQuit()
    {
        CloseAllChannels();
    }

    #endregion
}

