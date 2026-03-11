using System;
using UnityEngine;

public class ExperimentLogManager : MonoBehaviour
{
    private ExperimentSceneManager _sm;

    private const string BASE_DIRECTORY_PATH = "D:/Data_Exp2/";

    private CsvLog _streamLog = new CsvLog();
    private CsvLog _eventLog = new CsvLog();
    private CsvLog _summaryLog = new CsvLog();

    private string _basePath;
    private string _conditionsSuffix;
    private long _timestamp;
    private string _eventName;
    private int _eventHandIndex = -1; // -1: N/A, 0: Right, 1: Left
    private float _taskCompletionTime;

    private static readonly string[] HandNames = { "Right", "Left" };

    // Per-hand accumulation fields
    private readonly Vector3[] _prevThumbTipLocal = new Vector3[2];
    private readonly Vector3[] _prevIndexTipLocal = new Vector3[2];
    private readonly Vector3[] _prevMiddleTipLocal = new Vector3[2];
    private readonly Vector3[] _prevWristWorldPos = new Vector3[2];
    private readonly Quaternion[] _prevWristWorldRot = new Quaternion[2];
    private readonly float[] _totalThumbTipTranslation = new float[2];
    private readonly float[] _totalIndexTipTranslation = new float[2];
    private readonly float[] _totalMiddleTipTranslation = new float[2];
    private readonly float[] _totalWristWorldTranslation = new float[2];
    private readonly float[] _totalWristWorldRotation = new float[2];
    private readonly Vector3[] _prevDieLocalPos = new Vector3[2];
    private readonly Quaternion[] _prevDieLocalRot = new Quaternion[2];
    private readonly float[] _totalDieLocalTranslation = new float[2];
    private readonly float[] _totalDieLocalRotation = new float[2];

    // Shared accumulation fields
    private Vector3 _prevDieWorldPos;
    private Quaternion _prevDieWorldRot;
    private float _totalDieWorldTranslation, _totalDieWorldRotation;
    private Vector3 _prevHeadWorldPos;
    private Quaternion _prevHeadWorldRot;
    private float _totalHeadWorldTranslation, _totalHeadWorldRotation;

    public void Initialize(ExperimentSceneManager sm)
    {
        _sm = sm;
        string conditions = (_sm.Experiment == ExperimentSceneManager.ExpType.Optimization_Exp1)
            ? $"_{_sm.Experiment}_P{_sm.ParticipantNum}_{_sm.Gain}"
            : $"_{_sm.Experiment}_P{_sm.ParticipantNum}_{_sm.Method}";
        _basePath = BASE_DIRECTORY_PATH + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + conditions;
        _conditionsSuffix = conditions;

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
        _streamLog.Col("Timestamp", () => _timestamp);
        _streamLog.Col("Trial Duration", () => _sm.TrialDuration);

        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            string h = HandNames[idx];

            _streamLog.ColVector3($"{h} Wrist World Position", () => _sm.HandInteractors[idx].WristWorldPosition);
            _streamLog.ColQuaternion($"{h} Wrist World Rotation", () => _sm.HandInteractors[idx].WristWorldRotation);

            _streamLog.ColVector3($"{h} Thumb Tip World Position", () => _sm.HandInteractors[idx].ThumbTipWorldPosition);
            _streamLog.ColVector3($"{h} Index Tip World Position", () => _sm.HandInteractors[idx].IndexTipWorldPosition);
            _streamLog.ColVector3($"{h} Middle Tip World Position", () => _sm.HandInteractors[idx].MiddleTipWorldPosition);

            _streamLog.ColVector3($"{h} Thumb Tip Local Position", () => _sm.HandInteractors[idx].ThumbTipLocalPosition);
            _streamLog.ColVector3($"{h} Index Tip Local Position", () => _sm.HandInteractors[idx].IndexTipLocalPosition);
            _streamLog.ColVector3($"{h} Middle Tip Local Position", () => _sm.HandInteractors[idx].MiddleTipLocalPosition);

            _streamLog.ColVector3($"{h} Thumb Tip Euro Position", () => _sm.HandInteractors[idx].ThumbTipEuroPosition);
            _streamLog.ColVector3($"{h} Index Tip Euro Position", () => _sm.HandInteractors[idx].IndexTipEuroPosition);
            _streamLog.ColVector3($"{h} Middle Tip Euro Position", () => _sm.HandInteractors[idx].MiddleTipEuroPosition);

            _streamLog.ColVector3($"{h} Triangle Local Position", () => _sm.HandInteractors[idx].TriangleCentroidPosition);
            _streamLog.ColQuaternion($"{h} Triangle Local Rotation", () => _sm.HandInteractors[idx].TriangleRotation);
            _streamLog.Col($"{h} Triangle Area", () => _sm.HandInteractors[idx].TriangleArea);

            _streamLog.ColPose($"{h} Die Local", () => _sm.HandInteractors[idx].ObjectLocalPose);

            _streamLog.Col($"{h} Angle Scale Factor", () => _sm.HandInteractors[idx].AngleScaleFactor);

            _streamLog.Col($"{h} Is Grabbed", () => _sm.HandInteractors[idx].IsGrabbed);
            _streamLog.Col($"{h} Is Rotating", () => _sm.HandInteractors[idx].IsRotating);
            _streamLog.Col($"{h} Is Clutching", () => _sm.HandInteractors[idx].IsClutching);
        }

        // Die (world)
        _streamLog.ColPose("Die World", () => GetDieWorldPose());

        // Target offset
        _streamLog.ColPose("Target Offset", () => _sm.TargetOffset);

        // Head
        _streamLog.ColVector3("Head World Position", () => _sm.HeadPosition);
        _streamLog.ColQuaternion("Head World Rotation", () => _sm.HeadRotation);

        // Status
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
            _eventLog.Col("Angle Index", () => _sm.AngleIndex);
            _eventLog.Col("Axis Index", () => _sm.AxisIndex);
        }
        else
        {
            _eventLog.Col("Block Num", () => _sm.BlockNum);
            _eventLog.Col("Trial Num", () => _sm.TrialNum);
        }

        _eventLog.Col("Current Angle", () => _sm.CurrentAngle);
        _eventLog.ColVector3("Current Axis", () => _sm.CurrentAxis);

        _eventLog.Col("Event Hand", () => _eventHandIndex);
        _eventLog.ColPose("Die World", () => GetDieWorldPose());
        _eventLog.ColPose("Target Offset", () => _sm.TargetOffset);
    }

    private void RegisterSummaryColumns()
    {
        if (_sm.Experiment == ExperimentSceneManager.ExpType.Optimization_Exp1)
        {
            _summaryLog.Col("Set Num", () => _sm.SetNum);
            _summaryLog.Col("Angle Index", () => _sm.AngleIndex);
            _summaryLog.Col("Axis Index", () => _sm.AxisIndex);
        }
        else
        {
            _summaryLog.Col("Block Num", () => _sm.BlockNum);
            _summaryLog.Col("Trial Num", () => _sm.TrialNum);
        }
        _summaryLog.Col("Current Angle", () => _sm.CurrentAngle);
        _summaryLog.ColVector3("Current Axis", () => _sm.CurrentAxis);
        _summaryLog.Col("Task Completion Time", () => _taskCompletionTime);
        _summaryLog.Col("Is Timeout", () => _sm.IsTimeout);

        _summaryLog.ColPose("Target Offset", () => _sm.TargetOffset);

        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            string h = HandNames[idx];
            _summaryLog.Col($"{h} Total Thumb Tip Translation", () => _totalThumbTipTranslation[idx]);
            _summaryLog.Col($"{h} Total Index Tip Translation", () => _totalIndexTipTranslation[idx]);
            _summaryLog.Col($"{h} Total Middle Tip Translation", () => _totalMiddleTipTranslation[idx]);
            _summaryLog.Col($"{h} Total Wrist Translation", () => _totalWristWorldTranslation[idx]);
            _summaryLog.Col($"{h} Total Wrist Rotation", () => _totalWristWorldRotation[idx]);
            _summaryLog.Col($"{h} Total Die Local Translation", () => _totalDieLocalTranslation[idx]);
            _summaryLog.Col($"{h} Total Die Local Rotation", () => _totalDieLocalRotation[idx]);
        }

        _summaryLog.Col("Total Die Translation", () => _totalDieWorldTranslation);
        _summaryLog.Col("Total Die Rotation", () => _totalDieWorldRotation);
        _summaryLog.Col("Total Head Translation", () => _totalHeadWorldTranslation);
        _summaryLog.Col("Total Head Rotation", () => _totalHeadWorldRotation);
    }

    public void OnEvent(string eventName, int handIndex = -1)
    {
        _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _eventName = eventName;
        _eventHandIndex = handIndex;
        _eventLog.WriteRow();

        if (eventName == "Trial Load")
        {
            _streamLog.Close();
            string suffix = (_sm.Experiment == ExperimentSceneManager.ExpType.Optimization_Exp1)
                ? $"_Set{_sm.SetNum}_Angle{_sm.CurrentAngle}_Axis{_sm.AxisIndex}"
                : $"_Block{_sm.BlockNum}_Trial{_sm.TrialNum}";
            string streamPath = BASE_DIRECTORY_PATH + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + _conditionsSuffix + suffix;
            _streamLog.Open(streamPath + "_StreamData.csv");
        }
        else if (eventName == "Trial Start")
        {
            ResetAccumulationData();
        }
        else if (eventName == "Grab")
        {
            ResetPrevPositions(handIndex);
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
        for (int i = 0; i < 2; i++)
        {
            HandInteractor h = _sm.HandInteractors[i];
            if (!h.IsGrabbed) continue;

            Vector3 thumbPos = h.ThumbTipLocalPosition;
            _totalThumbTipTranslation[i] += (thumbPos - _prevThumbTipLocal[i]).magnitude;
            _prevThumbTipLocal[i] = thumbPos;

            Vector3 indexPos = h.IndexTipLocalPosition;
            _totalIndexTipTranslation[i] += (indexPos - _prevIndexTipLocal[i]).magnitude;
            _prevIndexTipLocal[i] = indexPos;

            Vector3 middlePos = h.MiddleTipLocalPosition;
            _totalMiddleTipTranslation[i] += (middlePos - _prevMiddleTipLocal[i]).magnitude;
            _prevMiddleTipLocal[i] = middlePos;

            Vector3 wristPos = h.WristWorldPosition;
            _totalWristWorldTranslation[i] += (wristPos - _prevWristWorldPos[i]).magnitude;
            _prevWristWorldPos[i] = wristPos;

            Quaternion wristRot = h.WristWorldRotation;
            _totalWristWorldRotation[i] += AngleFromDelta(wristRot * Quaternion.Inverse(_prevWristWorldRot[i]));
            _prevWristWorldRot[i] = wristRot;

            Pose dieLocal = h.ObjectLocalPose;
            _totalDieLocalTranslation[i] += (dieLocal.position - _prevDieLocalPos[i]).magnitude;
            _prevDieLocalPos[i] = dieLocal.position;
            _totalDieLocalRotation[i] += AngleFromDelta(dieLocal.rotation * Quaternion.Inverse(_prevDieLocalRot[i]));
            _prevDieLocalRot[i] = dieLocal.rotation;
        }

        Transform dieT = _sm.DieTransform;
        if (dieT != null)
        {
            Vector3 diePos = dieT.position;
            _totalDieWorldTranslation += (diePos - _prevDieWorldPos).magnitude;
            _prevDieWorldPos = diePos;

            Quaternion dieRot = dieT.rotation;
            _totalDieWorldRotation += AngleFromDelta(dieRot * Quaternion.Inverse(_prevDieWorldRot));
            _prevDieWorldRot = dieRot;
        }

        Vector3 headPos = _sm.HeadPosition;
        _totalHeadWorldTranslation += (headPos - _prevHeadWorldPos).magnitude;
        _prevHeadWorldPos = headPos;

        Quaternion headRot = _sm.HeadRotation;
        _totalHeadWorldRotation += AngleFromDelta(headRot * Quaternion.Inverse(_prevHeadWorldRot));
        _prevHeadWorldRot = headRot;
    }

    private void ResetAccumulationData()
    {
        for (int i = 0; i < 2; i++)
        {
            HandInteractor h = _sm.HandInteractors[i];

            _prevThumbTipLocal[i] = h.ThumbTipLocalPosition;
            _prevIndexTipLocal[i] = h.IndexTipLocalPosition;
            _prevMiddleTipLocal[i] = h.MiddleTipLocalPosition;
            _totalThumbTipTranslation[i] = 0f;
            _totalIndexTipTranslation[i] = 0f;
            _totalMiddleTipTranslation[i] = 0f;

            _prevWristWorldPos[i] = h.WristWorldPosition;
            _prevWristWorldRot[i] = h.WristWorldRotation;
            _totalWristWorldTranslation[i] = 0f;
            _totalWristWorldRotation[i] = 0f;

            Pose dieLocal = h.ObjectLocalPose;
            _prevDieLocalPos[i] = dieLocal.position;
            _prevDieLocalRot[i] = dieLocal.rotation;
            _totalDieLocalTranslation[i] = 0f;
            _totalDieLocalRotation[i] = 0f;
        }

        Transform dieT = _sm.DieTransform;
        _prevDieWorldPos = dieT != null ? dieT.position : Vector3.zero;
        _prevDieWorldRot = dieT != null ? dieT.rotation : Quaternion.identity;
        _totalDieWorldTranslation = 0f;
        _totalDieWorldRotation = 0f;

        _prevHeadWorldPos = _sm.HeadPosition;
        _prevHeadWorldRot = _sm.HeadRotation;
        _totalHeadWorldTranslation = 0f;
        _totalHeadWorldRotation = 0f;
    }

    private void ResetPrevPositions(int handIndex)
    {
        if (handIndex < 0 || handIndex >= 2) return;

        HandInteractor h = _sm.HandInteractors[handIndex];

        _prevThumbTipLocal[handIndex] = h.ThumbTipLocalPosition;
        _prevIndexTipLocal[handIndex] = h.IndexTipLocalPosition;
        _prevMiddleTipLocal[handIndex] = h.MiddleTipLocalPosition;

        _prevWristWorldPos[handIndex] = h.WristWorldPosition;
        _prevWristWorldRot[handIndex] = h.WristWorldRotation;

        Pose dieLocal = h.ObjectLocalPose;
        _prevDieLocalPos[handIndex] = dieLocal.position;
        _prevDieLocalRot[handIndex] = dieLocal.rotation;
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
}
