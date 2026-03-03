using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class CsvLog
{
    private readonly List<string> _names = new List<string>();
    private readonly List<Func<string>> _getters = new List<Func<string>>();
    private StreamWriter _writer;
    private FileStream _fileStream;

    public bool IsOpen => _writer != null;

    public void Col(string name, Func<object> getter)
    {
        _names.Add(name);
        _getters.Add(() => getter().ToString());
    }

    public void ColVector3(string prefix, Func<Vector3> getter)
    {
        Col(prefix + " X", () => getter().x);
        Col(prefix + " Y", () => getter().y);
        Col(prefix + " Z", () => getter().z);
        Col(prefix + " Magnitude", () => getter().magnitude);
    }

    public void ColQuaternion(string prefix, Func<Quaternion> getter)
    {
        Col(prefix + " X", () => getter().x);
        Col(prefix + " Y", () => getter().y);
        Col(prefix + " Z", () => getter().z);
        Col(prefix + " W", () => getter().w);
        Col(prefix + " Angle", () => { getter().ToAngleAxis(out float angle, out _); return angle; });
        Col(prefix + " Axis X", () => { getter().ToAngleAxis(out _, out Vector3 axis); return axis.x; });
        Col(prefix + " Axis Y", () => { getter().ToAngleAxis(out _, out Vector3 axis); return axis.y; });
        Col(prefix + " Axis Z", () => { getter().ToAngleAxis(out _, out Vector3 axis); return axis.z; });
    }

    public void ColPose(string prefix, Func<Pose> getter)
    {
        ColVector3(prefix + " Position", () => getter().position);
        ColQuaternion(prefix + " Rotation", () => getter().rotation);
    }

    public bool Open(string filePath)
    {
        try
        {
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            _writer = new StreamWriter(_fileStream, System.Text.Encoding.UTF8);
            _writer.WriteLine(string.Join(",", _names));
            return true;
        }
        catch (IOException e)
        {
            Debug.LogError("CsvLog.Open failed: " + e.Message);
            return false;
        }
    }

    public void WriteRow()
    {
        if (_writer == null) return;
        var values = new string[_getters.Count];
        for (int i = 0; i < _getters.Count; i++)
        {
            values[i] = _getters[i]();
        }
        _writer.WriteLine(string.Join(",", values));
    }

    public void Close()
    {
        _writer?.Close();
        _fileStream?.Close();
        _writer = null;
        _fileStream = null;
    }
}
