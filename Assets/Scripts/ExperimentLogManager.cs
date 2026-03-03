using System;
using UnityEngine;

public class ExperimentLogManager : MonoBehaviour
{
    [SerializeField]
    private ExperimentSceneManager _sm;

    private const string BASE_DIRECTORY_PATH = "D:/Data/";

    private CsvLog _streamLog = new CsvLog();
    private CsvLog _eventLog = new CsvLog();
    private CsvLog _summaryLog = new CsvLog();

    private string _basePath;
    private long _timestamp;
    private string _eventName;
    private float _taskCompletionTime;

    // Accumulation fields for summary
    private Vector3 _prevThumbTipLocal, _prevIndexTipLocal, _prevMiddleTipLocal;
    private Vector3 _prevWristWorldPos, _prevDieWorldPos, _prevHeadWorldPos;
    private Quaternion _prevWristWorldRot, _prevDieWorldRot, _prevHeadWorldRot;
    private float _totalThumbTipTranslation, _totalIndexTipTranslation, _totalMiddleTipTranslation;
    private float _totalWristTranslation, _totalWristRotation;
    private float _totalDieTranslation, _totalDieRotation;
    private float _totalHeadTranslation, _totalHeadRotation;

    public void Initialize()
    {
        string conditions = $"_{_sm.Experiment}_P{_sm.ParticipantNum}_{_sm.Gain}_{_sm.Method}";
        _basePath = BASE_DIRECTORY_PATH + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + conditions;

        RegisterStreamColumns();
        RegisterEventColumns();
        RegisterSummaryColumns();

        _eventLog.Open(_basePath + "_EventLog.csv");
        _summaryLog.Open(_basePath + "_Summary.csv");
    }

    public void WriteStreamRow()
    {
        _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_streamLog.IsOpen)
        {
            AccumulateData();
            _streamLog.WriteRow();
        }
    }

    public void CloseAll()
    {
        _streamLog.Close();
        _eventLog.Close();
        _summaryLog.Close();
    }

    private void RegisterStreamColumns()
    {
        HandInteractor h = _sm.ActiveHand;

        _streamLog.Col("Timestamp", () => _timestamp);
        _streamLog.Col("Trial Duration", () => _sm.TrialDuration);

        // Wrist (world)
        _streamLog.ColVector3("Wrist World Position", () => h.WristWorldPosition);
        _streamLog.ColQuaternion("Wrist World Rotation", () => h.WristWorldRotation);

        // Fingertips (world)
        _streamLog.ColVector3("Thumb Tip World Position", () => h.ThumbTipWorldPosition);
        _streamLog.ColVector3("Index Tip World Position", () => h.IndexTipWorldPosition);
        _streamLog.ColVector3("Middle Tip World Position", () => h.MiddleTipWorldPosition);

        // Fingertips (local / wrist-relative)
        _streamLog.ColVector3("Thumb Tip Local Position", () => h.ThumbTipLocalPosition);
        _streamLog.ColVector3("Index Tip Local Position", () => h.IndexTipLocalPosition);
        _streamLog.ColVector3("Middle Tip Local Position", () => h.MiddleTipLocalPosition);

        // Fingertips (euro-filtered)
        _streamLog.ColVector3("Thumb Tip Euro Position", () => h.ThumbTipEuroPosition);
        _streamLog.ColVector3("Index Tip Euro Position", () => h.IndexTipEuroPosition);
        _streamLog.ColVector3("Middle Tip Euro Position", () => h.MiddleTipEuroPosition);

        // Triangle
        _streamLog.ColVector3("Triangle Local Position", () => h.TriangleCentroidPosition);
        _streamLog.ColQuaternion("Triangle Local Rotation", () => h.TriangleRotation);

        // Die (world)
        _streamLog.ColPose("Die World", () => GetDieWorldPose());

        // Die (local / wrist-relative)
        _streamLog.ColPose("Die Local", () => GetDieLocalPose());

        // Target offset
        _streamLog.ColPose("Target Offset", () => _sm.TargetOffset);

        // Head
        _streamLog.ColVector3("Head World Position", () => _sm.HeadPosition);
        _streamLog.ColQuaternion("Head World Rotation", () => _sm.HeadRotation);

        // Gain
        _streamLog.Col("Angle Scale Factor", () => h.AngleScaleFactor);
        _streamLog.Col("Gain Condition", () => h.GainCondition);

        // Status
        _streamLog.Col("Is Grabbed", () => h.IsGrabbed);
        _streamLog.Col("Is Rotating", () => h.IsRotating);
        _streamLog.Col("Is Clutching", () => h.IsClutching);
        _streamLog.Col("Is On Target", () => _sm.IsOnTarget);
    }

    private void RegisterEventColumns()
    {
        _eventLog.Col("Event Name", () => _eventName);
        _eventLog.Col("Timestamp", () => _timestamp);
        _eventLog.Col("Trial Duration", () => _sm.TrialDuration);

        if (_sm.Experiment == ExperimentSceneManager.ExpType.Optimization_Exp1)
        {
            _eventLog.Col("Set Num", () => _sm.SetNum);
        }
        else
        {
            _eventLog.Col("Trial Num", () => _sm.TrialNum);
        }

        _summaryLog.Col("Current Angle", () => _sm.CurrentAngle);
        _summaryLog.ColVector3("Current Axis", () => _sm.CurrentAxis);

        _eventLog.ColPose("Die World", () => GetDieWorldPose());
        _eventLog.ColPose("Die Local", () => GetDieLocalPose());
        _eventLog.ColPose("Target Offset", () => _sm.TargetOffset);
    }

    private void RegisterSummaryColumns()
    {
        if (_sm.Experiment == ExperimentSceneManager.ExpType.Optimization_Exp1)
        {
            _summaryLog.Col("Set Num", () => _sm.SetNum);

        }
        else
        {
            _summaryLog.Col("Trial Num", () => _sm.TrialNum);
        }
        _summaryLog.Col("Current Angle", () => _sm.CurrentAngle);
        _summaryLog.ColVector3("Current Axis", () => _sm.CurrentAxis);
        _summaryLog.Col("Task Completion Time", () => _taskCompletionTime);
        _summaryLog.Col("Is Timeout", () => _sm.IsTimeout);

        _summaryLog.ColPose("Target Offset", () => _sm.TargetOffset);

        // Accumulation
        _summaryLog.Col("Total Thumb Tip Translation", () => _totalThumbTipTranslation);
        _summaryLog.Col("Total Index Tip Translation", () => _totalIndexTipTranslation);
        _summaryLog.Col("Total Middle Tip Translation", () => _totalMiddleTipTranslation);
        _summaryLog.Col("Total Wrist Translation", () => _totalWristTranslation);
        _summaryLog.Col("Total Wrist Rotation", () => _totalWristRotation);
        _summaryLog.Col("Total Die Translation", () => _totalDieTranslation);
        _summaryLog.Col("Total Die Rotation", () => _totalDieRotation);
        _summaryLog.Col("Total Head Translation", () => _totalHeadTranslation);
        _summaryLog.Col("Total Head Rotation", () => _totalHeadRotation);
    }

    public void OnEvent(string eventName)
    {
        _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _eventName = eventName;
        _eventLog.WriteRow();

        if (eventName == "Trial Load")
        {
            _streamLog.Close();
            string suffix = (_sm.Experiment == ExperimentSceneManager.ExpType.Optimization_Exp1)
                ? $"_Set{_sm.SetNum}_Angle{_sm.CurrentAngle}_Axis{_sm.AxisIndex}"
                : $"_Trial{_sm.TrialNum}";
            _streamLog.Open(_basePath + suffix + "_StreamData.csv");
        }
        else if (eventName == "Trial Start")
        {
            ResetAccumulationData();
        }
        else if (eventName == "Trial End")
        {
            _taskCompletionTime = _sm.TrialDuration;
            _summaryLog.WriteRow();
            _streamLog.Close();
        }
    }

    private void AccumulateData()
    {
        HandInteractor h = _sm.ActiveHand;
        if (!h.IsGrabbed) return;

        // Fingertips (local)
        Vector3 thumbPos = h.ThumbTipLocalPosition;
        _totalThumbTipTranslation += (thumbPos - _prevThumbTipLocal).magnitude;
        _prevThumbTipLocal = thumbPos;

        Vector3 indexPos = h.IndexTipLocalPosition;
        _totalIndexTipTranslation += (indexPos - _prevIndexTipLocal).magnitude;
        _prevIndexTipLocal = indexPos;

        Vector3 middlePos = h.MiddleTipLocalPosition;
        _totalMiddleTipTranslation += (middlePos - _prevMiddleTipLocal).magnitude;
        _prevMiddleTipLocal = middlePos;

        // Wrist (world)
        Vector3 wristPos = h.WristWorldPosition;
        _totalWristTranslation += (wristPos - _prevWristWorldPos).magnitude;
        _prevWristWorldPos = wristPos;

        Quaternion wristRot = h.WristWorldRotation;
        _totalWristRotation += AngleFromDelta(wristRot * Quaternion.Inverse(_prevWristWorldRot));
        _prevWristWorldRot = wristRot;

        // Die (world)
        Transform dieT = _sm.DieTransform;
        if (dieT != null)
        {
            Vector3 diePos = dieT.position;
            _totalDieTranslation += (diePos - _prevDieWorldPos).magnitude;
            _prevDieWorldPos = diePos;

            Quaternion dieRot = dieT.rotation;
            _totalDieRotation += AngleFromDelta(dieRot * Quaternion.Inverse(_prevDieWorldRot));
            _prevDieWorldRot = dieRot;
        }

        // Head (world)
        Vector3 headPos = _sm.HeadPosition;
        _totalHeadTranslation += (headPos - _prevHeadWorldPos).magnitude;
        _prevHeadWorldPos = headPos;

        Quaternion headRot = _sm.HeadRotation;
        _totalHeadRotation += AngleFromDelta(headRot * Quaternion.Inverse(_prevHeadWorldRot));
        _prevHeadWorldRot = headRot;
    }

    private void ResetAccumulationData()
    {
        HandInteractor h = _sm.ActiveHand;

        _prevThumbTipLocal = h.ThumbTipLocalPosition;
        _prevIndexTipLocal = h.IndexTipLocalPosition;
        _prevMiddleTipLocal = h.MiddleTipLocalPosition;
        _totalThumbTipTranslation = 0f;
        _totalIndexTipTranslation = 0f;
        _totalMiddleTipTranslation = 0f;

        _prevWristWorldPos = h.WristWorldPosition;
        _prevWristWorldRot = h.WristWorldRotation;
        _totalWristTranslation = 0f;
        _totalWristRotation = 0f;

        Transform dieT = _sm.DieTransform;
        _prevDieWorldPos = dieT != null ? dieT.position : Vector3.zero;
        _prevDieWorldRot = dieT != null ? dieT.rotation : Quaternion.identity;
        _totalDieTranslation = 0f;
        _totalDieRotation = 0f;

        _prevHeadWorldPos = _sm.HeadPosition;
        _prevHeadWorldRot = _sm.HeadRotation;
        _totalHeadTranslation = 0f;
        _totalHeadRotation = 0f;
    }

    private float AngleFromDelta(Quaternion q)
    {
        q.ToAngleAxis(out float angle, out _);
        return angle > 180f ? 360f - angle : angle;
    }

    private Pose GetDieWorldPose()
    {
        Transform t = _sm.DieTransform;
        if (t == null) return new Pose(Vector3.zero, Quaternion.identity);
        return new Pose(t.position, t.rotation);
    }

    private Pose GetDieLocalPose()
    {
        Transform t = _sm.DieTransform;
        if (t == null) return new Pose(Vector3.zero, Quaternion.identity);
        HandInteractor h = _sm.ActiveHand;
        Vector3 localPos = Quaternion.Inverse(h.WristWorldRotation) * (t.position - h.WristWorldPosition);
        Quaternion localRot = Quaternion.Inverse(h.WristWorldRotation) * t.rotation;
        return new Pose(localPos, localRot);
    }
}
