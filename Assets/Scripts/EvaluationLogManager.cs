using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

public class EvaluationLogManager : MonoBehaviour
{
    [SerializeField]
    private RotationInteractor _rotationInteractor;
    [SerializeField]
    private EvaluationSceneManager _evaluationSceneManager;

    private int _participantNum { get; set; }
    private int _expCondition { get; set; }

    private string _filePath;
    private const string BASE_DIRECTORY_PATH = "D:/Data/";

    private FileStream _streamFileStream, _eventFileStream, _summaryFileStream;
    private StreamWriter _streamWriter, _eventWriter, _summaryWriter;

    private Dictionary<string, object> _streamData = new Dictionary<string, object>();
    private Dictionary<string, object> _eventData = new Dictionary<string, object>();
    private Dictionary<string, object> _summaryData = new Dictionary<string, object>();

    private long _timeStamp;

    //finger joints in world space
    private Vector3 _wristWorldPosition, _thumbTipWorldPosition, _indexTipWorldPosition, _middleTipWorldPosition, _metacarpalWorldPosition, _modifiedThumbTipWorldPosition;
    private Quaternion _wristWorldRotation, _thumbTipWorldRotation, _indexTipWorldRotation, _middleTipWorldRotation, _metacarpalWorldRotation;

    //finger joints in local space
    private Vector3 _thumbTipLocalPosition, _indexTipLocalPosition, _middleTipLocalPosition, _metacarpalLocalPosition, _modifiedThumbTipLocalPosition;
    private Quaternion _thumbTipLocalRotation, _indexTipLocalRotation, _middleTipLocalRotation, _metacarpalLocalRotation;

    //thumb modification 
    private Quaternion _deltaMetacarpalRotation, _modifiedDeltaMetacarpalRotation;
    private float _deltaMetacarpalAngle, _modifiedDeltaMetacarpalAngle;
    private Vector3 _deltaMetacarpalAxis, _modifiedDeltaMetacarpalAxis;

    //triangle 
    private Vector3 _weightedCentroidWorldPosition, _weightedCentroidLocalPosition;
    private Quaternion _triangleWorldRotation, _triangleLocalRotation;
    private float _thumbWeight, _triangleArea, _triangleP1Angle, _deltaTriangleP1Angle;

    //dice 
    private Vector3 _dieWorldPosition, _dieLocalPosition, _targetOffsetPosition;
    private Quaternion _dieWorldRotation, _dieLocalRotation, _targetOffsetRotation;

    //head
    private Vector3 _headWorldPosition;
    private Quaternion _headWorldRotation;

    //accumulation
    private Vector3 _prevWristWorldPosition, _prevThumbTipLocalPosition, _prevIndexTipLocalPosition, _prevMiddleTipLocalPosition, _prevMetacarpalLocalPosition;
    private Quaternion _prevWristWorldRotation, _prevThumbTipLocalRotation, _prevIndexTipLocalRotation, _prevMiddleTipLocalRotation, _prevMetacarpalLocalRotation;
    private float _totalWristWorldTranslation, _totalThumbTipLocalTranslation, _totalIndexTipLocalTranslation, _totalMiddleTipLocalTranslation, _totalMetacarpalLocalTranslation;
    private float _totalWristWorldRotation, _totalThumbTipLocalRotation, _totalIndexTipLocalRotation, _totalMiddleTipLocalRotation, _totalMetacarpalLocalRotation;
    private Vector3 _prevDieWorldPosition, _prevDieLocalPosition, _prevHeadWorldPosition;
    private Quaternion _prevDieWorldRotation, _prevDieLocalRotation, _prevHeadWorldRotation;
    private float _totalDieWorldTranslation, _totalDieLocalTranslation, _totalHeadWorldTranslation;
    private float _totalDieWorldRotation, _totalDieLocalRotation, _totalHeadWorldRotation;

    //status
    private bool _isGrabbing, _isClutching, _isOverlapped, _isTimeout;
    private int _trialNum;
    private string _eventName;
    private float _taskCompletionTime, _trialDuration;

    private string[] _eventNames = new string[11] { "Scene Loaded", "Trial Start", "Trial End", "Trial Reset",
                                                    "Grab", "Release", "Clutch Start", "Clutch End",
                                                    "On Target", "Off Target", "Timed Out"};


    // Start is called before the first frame update
    void Start()
    {
        UpdateSummaryData();
        UpdateEventData();
        UpdateStreamData();
        _filePath = CreateFilePath();
        CreateNewFile();
    }

    // Update is called once per frame
    void Update()
    {
        DateTimeOffset dt = new DateTimeOffset(DateTime.Now);
        _timeStamp = dt.ToUnixTimeMilliseconds();
        
        _trialNum = _evaluationSceneManager.TrialNum;
        _trialDuration = _evaluationSceneManager.TrialDuration;

        StorePreviousData();
        UpdateFingerJointsData();
        UpdateModificationData();
        UpdateDieData();
        UpdateHeadData();
        UpdateStatusData();
        AccumulateData();

        if (_evaluationSceneManager.IsInTrial)
        {
            UpdateStreamData();
            _streamWriter.WriteLine(GenerateStreamString());//
        }
    }

    public void Init()
    {
    }

    void OnDestroy()
    {
        _eventWriter.Close();
        _eventFileStream.Close();

        _summaryWriter.Close();
        _summaryFileStream.Close();
    }

    public void SetExpConditions(int p, int condition)
    {
        _participantNum = p;
        _expCondition = condition;
    }

    public string CreateFilePath()
    {
        string conditions = $"_P{_participantNum}_Exp1_Condition{_expCondition}";
        string fileName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + conditions;
        return BASE_DIRECTORY_PATH + fileName;
    }

    public bool CreateNewFile()
    {
        string header;
        try
        {
            string eventFilePath = _filePath + "_EventLog.csv";
            _eventFileStream = new FileStream(eventFilePath, FileMode.Create, FileAccess.Write);
            _eventWriter = new StreamWriter(_eventFileStream, System.Text.Encoding.UTF8);
            header = string.Join(",", new List<string>(_eventData.Keys));
            _eventWriter.WriteLine(header);
        }
        catch (System.IO.IOException e)
        {
            Debug.LogError("Failed to create event file. " + e.Message);
            return false;
        }

        try
        {
            string summaryFilePath = _filePath + "_Summary.csv";
            _summaryFileStream = new FileStream(summaryFilePath, FileMode.Create, FileAccess.Write);
            _summaryWriter = new StreamWriter(_summaryFileStream, System.Text.Encoding.UTF8);
            header = string.Join(",", new List<string>(_summaryData.Keys));
            _summaryWriter.WriteLine(header);
        }
        catch (System.IO.IOException e)
        {
            Debug.LogError("Failed to create summary file. " + e.Message);
            return false;
        }

        return true;
    }

    public bool CreateStreamFile()
    {
        Debug.Log("attempt to create stream file");
        try
        {
            string streamFilePath = _filePath + $"_Trial{_trialNum}_StreamData.csv";
            _streamFileStream = new FileStream(streamFilePath, FileMode.Create, FileAccess.Write);
            _streamWriter = new StreamWriter(_streamFileStream, System.Text.Encoding.UTF8);
            string header = string.Join(",", new List<string>(_streamData.Keys));
            _streamWriter.WriteLine(header);
        }
        catch (System.IO.IOException e)
        {
            Debug.LogError("Failed to create stream file. " + e.Message);
            return false;
        }
        return true;
    }

    public void CloseStreamFile()
    {
        _streamWriter.Close();
        _streamFileStream.Close();
    }

    public void OnEvent(string eventName)
    {
        _eventName = eventName;
        UpdateStreamData();
        UpdateEventData();
        _eventWriter.WriteLine(GenerateEventString());

        if (eventName.Equals("Scene Loaded"))
        {
            CreateStreamFile();
        }
        else if (eventName.Equals("Trial End"))
        {
            _taskCompletionTime = _trialDuration;
            UpdateSummaryData();
            CloseStreamFile();
            _summaryWriter.WriteLine(GenerateSummaryString());
        }
    }

    public void UpdateFingerJointsData()
    {
        // world
        _rotationInteractor.GetWristWorldTransform(out _wristWorldPosition, out _wristWorldRotation);
        _rotationInteractor.GetThumbTipWorldTransform(out _thumbTipWorldPosition, out _thumbTipWorldRotation);
        _rotationInteractor.GetIndexTipWorldTransform(out _indexTipWorldPosition, out _indexTipWorldRotation);
        _rotationInteractor.GetMiddleTipWorldTransform(out _middleTipWorldPosition, out _middleTipWorldRotation);
        _rotationInteractor.GetMetacarpalWorldTransform(out _metacarpalWorldPosition, out _metacarpalWorldRotation);
        _rotationInteractor.GetModifiedThumbTipWorldPosition(out _modifiedThumbTipWorldPosition);

        // local
        _rotationInteractor.GetThumbTipLocalTransform(out _thumbTipLocalPosition, out _thumbTipLocalRotation);
        _rotationInteractor.GetIndexTipLocalTransform(out _indexTipLocalPosition, out _indexTipLocalRotation);
        _rotationInteractor.GetMiddleTipLocalTransform(out _middleTipLocalPosition, out _middleTipLocalRotation);
        _rotationInteractor.GetMetacarpalLocalTransform(out _metacarpalLocalPosition, out _metacarpalLocalRotation);
        _rotationInteractor.GetModifiedThumbTipLocalPosition(out _modifiedThumbTipLocalPosition);
    }

    public void UpdateModificationData()
    {
        _rotationInteractor.GetDeltaMetacarpalRotation(out _deltaMetacarpalRotation, out _deltaMetacarpalAngle, out _deltaMetacarpalAxis);
        _rotationInteractor.GetModifiedDeltaMetacarpalRotation(out _modifiedDeltaMetacarpalRotation, out _modifiedDeltaMetacarpalAngle, out _modifiedDeltaMetacarpalAxis);
        _rotationInteractor.GetTriangleWorldRotation(out _triangleWorldRotation);
        _rotationInteractor.GetTriangleLocalRotation(out _triangleLocalRotation);
        _rotationInteractor.GetWeightedCentroidWorldPosition(out _weightedCentroidWorldPosition);
        _rotationInteractor.GetWeightedCentroidLocalPosition(out _weightedCentroidLocalPosition);
        _rotationInteractor.GetTriangleProperties(out _thumbWeight, out _triangleArea, out _triangleP1Angle, out _deltaTriangleP1Angle);
    }

    public void UpdateDieData()
    {
        _evaluationSceneManager.GetDieTransform(out _dieWorldPosition, out _dieWorldRotation);
        _rotationInteractor.GetDieLocalTransform(out _dieLocalPosition, out _dieLocalRotation);
        _evaluationSceneManager.GetTargetOffset(out _targetOffsetPosition, out _targetOffsetRotation);
    }

    public void UpdateHeadData()
    {
        _evaluationSceneManager.GetHeadTransform(out _headWorldPosition, out _headWorldRotation);
    }

    public void UpdateStatusData()
    {
        _evaluationSceneManager.GetStatus(out _isGrabbing, out _isClutching, out _isOverlapped, out _isTimeout);
    }

    public void StorePreviousData()
    {
        _prevWristWorldPosition = _wristWorldPosition;
        _prevWristWorldRotation = _wristWorldRotation;

        _prevThumbTipLocalPosition = _thumbTipLocalPosition;
        _prevThumbTipLocalRotation = _thumbTipLocalRotation;

        _prevIndexTipLocalPosition = _indexTipLocalPosition;
        _prevIndexTipLocalRotation = _indexTipLocalRotation;

        _prevMiddleTipLocalPosition = _middleTipLocalPosition;
        _prevMiddleTipLocalRotation = _middleTipLocalRotation;

        _prevMetacarpalLocalPosition = _metacarpalLocalPosition;
        _prevMetacarpalLocalRotation = _metacarpalLocalRotation;

        _prevDieWorldPosition = _dieWorldPosition;
        _prevDieWorldRotation = _dieWorldRotation;

        _prevDieLocalPosition = _dieLocalPosition;
        _prevDieLocalRotation = _dieLocalRotation;

        _prevHeadWorldPosition = _headWorldPosition;
        _prevHeadWorldRotation = _headWorldRotation;
    }

    public void AccumulateData()
    {
        _totalWristWorldTranslation += (_wristWorldPosition - _prevWristWorldPosition).magnitude;
        _totalWristWorldRotation += ReturnAngleFromQuaternion(_wristWorldRotation * Quaternion.Inverse(_prevWristWorldRotation));

        _totalThumbTipLocalTranslation += (_thumbTipLocalPosition - _prevThumbTipLocalPosition).magnitude;
        _totalThumbTipLocalRotation += ReturnAngleFromQuaternion(_thumbTipLocalRotation * Quaternion.Inverse(_prevThumbTipLocalRotation));

        _totalIndexTipLocalTranslation += (_indexTipLocalPosition - _prevIndexTipLocalPosition).magnitude;
        _totalIndexTipLocalRotation += ReturnAngleFromQuaternion(_indexTipLocalRotation * Quaternion.Inverse(_prevIndexTipLocalRotation));

        _totalMiddleTipLocalTranslation += (_middleTipLocalPosition - _prevMiddleTipLocalPosition).magnitude;
        _totalMiddleTipLocalRotation += ReturnAngleFromQuaternion(_middleTipLocalRotation * Quaternion.Inverse(_prevMiddleTipLocalRotation));

        _totalMetacarpalLocalTranslation += (_metacarpalLocalPosition - _prevMetacarpalLocalPosition).magnitude;
        _totalMetacarpalLocalRotation += ReturnAngleFromQuaternion(_metacarpalLocalRotation * Quaternion.Inverse(_prevMetacarpalLocalRotation));

        _totalDieWorldTranslation += (_dieWorldPosition - _prevDieWorldPosition).magnitude;
        _totalDieWorldRotation += ReturnAngleFromQuaternion(_dieWorldRotation * Quaternion.Inverse(_prevDieWorldRotation));

        _totalDieLocalTranslation += (_dieLocalPosition - _prevDieLocalPosition).magnitude;
        _totalDieLocalRotation += ReturnAngleFromQuaternion(_dieLocalRotation * Quaternion.Inverse(_prevDieLocalRotation));

        _totalHeadWorldTranslation += (_headWorldPosition - _prevHeadWorldPosition).magnitude;
        _totalHeadWorldRotation += ReturnAngleFromQuaternion(_headWorldRotation * Quaternion.Inverse(_prevHeadWorldRotation));
    }

    private float ReturnAngleFromQuaternion(Quaternion q)
    {
        q.ToAngleAxis(out float angle, out _);
        if (angle > 180f)
        {
            Debug.Log("Negate angle");
            return (360f - angle);
        }
        else return angle;
    }


    public string GenerateStreamString()
    {
        var values = new List<string>();
        foreach (string key in _streamData.Keys)
        {
            object value = _streamData[key];
            values.Add(value.ToString());
        }
        return string.Join(",", values);
    }

    public string GenerateEventString()
    {
        var values = new List<string>();
        foreach (string key in _eventData.Keys)
        {
            object value = _eventData[key];
            values.Add(value.ToString());
        }

        return string.Join(",", values);
    }

    public string GenerateSummaryString()
    {
        var values = new List<string>();
        foreach (string key in _summaryData.Keys)
        {
            object value = _summaryData[key];
            values.Add(value.ToString());
        }

        return string.Join(",", values);
    }

    void UpdateStreamData()
    {
        _streamData["Timestamp"] = _timeStamp;
        _streamData["Trial Duration"] = _trialDuration;

        //finger joints in world space
        _streamData["Wrist World Position X"] = _wristWorldPosition.x;
        _streamData["Wrist World Position Y"] = _wristWorldPosition.y;
        _streamData["Wrist World Position Z"] = _wristWorldPosition.z;
        _streamData["Wrist World Rotation X"] = _wristWorldRotation.x;
        _streamData["Wrist World Rotation Y"] = _wristWorldRotation.y;
        _streamData["Wrist World Rotation Z"] = _wristWorldRotation.z;
        _streamData["Wrist World Rotation W"] = _wristWorldRotation.w;

        _streamData["Thumb Tip World Position X"] = _thumbTipWorldPosition.x;
        _streamData["Thumb Tip World Position Y"] = _thumbTipWorldPosition.y;
        _streamData["Thumb Tip World Position Z"] = _thumbTipWorldPosition.z;
        _streamData["Thumb Tip World Rotation X"] = _thumbTipWorldRotation.x;
        _streamData["Thumb Tip World Rotation Y"] = _thumbTipWorldRotation.y;
        _streamData["Thumb Tip World Rotation Z"] = _thumbTipWorldRotation.z;
        _streamData["Thumb Tip World Rotation W"] = _thumbTipWorldRotation.w;

        _streamData["Index Tip World Position X"] = _indexTipWorldPosition.x;
        _streamData["Index Tip World Position Y"] = _indexTipWorldPosition.y;
        _streamData["Index Tip World Position Z"] = _indexTipWorldPosition.z;
        _streamData["Index Tip World Rotation X"] = _indexTipWorldRotation.x;
        _streamData["Index Tip World Rotation Y"] = _indexTipWorldRotation.y;
        _streamData["Index Tip World Rotation Z"] = _indexTipWorldRotation.z;
        _streamData["Index Tip World Rotation W"] = _indexTipWorldRotation.w;

        _streamData["Middle Tip World Position X"] = _middleTipWorldPosition.x;
        _streamData["Middle Tip World Position Y"] = _middleTipWorldPosition.y;
        _streamData["Middle Tip World Position Z"] = _middleTipWorldPosition.z;
        _streamData["Middle Tip World Rotation X"] = _middleTipWorldRotation.x;
        _streamData["Middle Tip World Rotation Y"] = _middleTipWorldRotation.y;
        _streamData["Middle Tip World Rotation Z"] = _middleTipWorldRotation.z;
        _streamData["Middle Tip World Rotation W"] = _middleTipWorldRotation.w;

        _streamData["Metacarpal World Position X"] = _metacarpalWorldPosition.x;
        _streamData["Metacarpal World Position Y"] = _metacarpalWorldPosition.y;
        _streamData["Metacarpal World Position Z"] = _metacarpalWorldPosition.z;
        _streamData["Metacarpal World Rotation X"] = _metacarpalWorldRotation.x;
        _streamData["Metacarpal World Rotation Y"] = _metacarpalWorldRotation.y;
        _streamData["Metacarpal World Rotation Z"] = _metacarpalWorldRotation.z;
        _streamData["Metacarpal World Rotation W"] = _metacarpalWorldRotation.w;

        _streamData["Modified Thumb Tip World Position X"] = _modifiedThumbTipWorldPosition.x;
        _streamData["Modified Thumb Tip World Position Y"] = _modifiedThumbTipWorldPosition.y;
        _streamData["Modified Thumb Tip World Position Z"] = _modifiedThumbTipWorldPosition.z;

        //finger joints in local space
        _streamData["Thumb Tip Local Position X"] = _thumbTipLocalPosition.x;
        _streamData["Thumb Tip Local Position Y"] = _thumbTipLocalPosition.y;
        _streamData["Thumb Tip Local Position Z"] = _thumbTipLocalPosition.z;
        _streamData["Thumb Tip Local Rotation X"] = _thumbTipLocalRotation.x;
        _streamData["Thumb Tip Local Rotation Y"] = _thumbTipLocalRotation.y;
        _streamData["Thumb Tip Local Rotation Z"] = _thumbTipLocalRotation.z;
        _streamData["Thumb Tip Local Rotation W"] = _thumbTipLocalRotation.w;

        _streamData["Index Tip Local Position X"] = _indexTipLocalPosition.x;
        _streamData["Index Tip Local Position Y"] = _indexTipLocalPosition.y;
        _streamData["Index Tip Local Position Z"] = _indexTipLocalPosition.z;
        _streamData["Index Tip Local Rotation X"] = _indexTipLocalRotation.x;
        _streamData["Index Tip Local Rotation Y"] = _indexTipLocalRotation.y;
        _streamData["Index Tip Local Rotation Z"] = _indexTipLocalRotation.z;
        _streamData["Index Tip Local Rotation W"] = _indexTipLocalRotation.w;

        _streamData["Middle Tip Local Position X"] = _middleTipLocalPosition.x;
        _streamData["Middle Tip Local Position Y"] = _middleTipLocalPosition.y;
        _streamData["Middle Tip Local Position Z"] = _middleTipLocalPosition.z;
        _streamData["Middle Tip Local Rotation X"] = _middleTipLocalRotation.x;
        _streamData["Middle Tip Local Rotation Y"] = _middleTipLocalRotation.y;
        _streamData["Middle Tip Local Rotation Z"] = _middleTipLocalRotation.z;
        _streamData["Middle Tip Local Rotation W"] = _middleTipLocalRotation.w;

        _streamData["Metacarpal Local Position X"] = _metacarpalLocalPosition.x;
        _streamData["Metacarpal Local Position Y"] = _metacarpalLocalPosition.y;
        _streamData["Metacarpal Local Position Z"] = _metacarpalLocalPosition.z;
        _streamData["Metacarpal Local Rotation X"] = _metacarpalLocalRotation.x;
        _streamData["Metacarpal Local Rotation Y"] = _metacarpalLocalRotation.y;
        _streamData["Metacarpal Local Rotation Z"] = _metacarpalLocalRotation.z;
        _streamData["Metacarpal Local Rotation W"] = _metacarpalLocalRotation.w;

        _streamData["Modified Thumb Tip Local Position X"] = _modifiedThumbTipLocalPosition.x;
        _streamData["Modified Thumb Tip Local Position Y"] = _modifiedThumbTipLocalPosition.y;
        _streamData["Modified Thumb Tip Local Position Z"] = _modifiedThumbTipLocalPosition.z;

        //thumb modification
        _streamData["Delta Metacarpal Rotation X"] = _deltaMetacarpalRotation.x;
        _streamData["Delta Metacarpal Rotation Y"] = _deltaMetacarpalRotation.y;
        _streamData["Delta Metacarpal Rotation Z"] = _deltaMetacarpalRotation.z;
        _streamData["Delta Metacarpal Rotation W"] = _deltaMetacarpalRotation.w;
        _streamData["Delta Metacarpal Angle"] = _deltaMetacarpalAngle;
        _streamData["Delta Metacarpal Axis X"] = _deltaMetacarpalAxis.x;
        _streamData["Delta Metacarpal Axis Y"] = _deltaMetacarpalAxis.y;
        _streamData["Delta Metacarpal Axis Z"] = _deltaMetacarpalAxis.z;

        _streamData["Modified Delta Metacarpal Rotation X"] = _modifiedDeltaMetacarpalRotation.x;
        _streamData["Modified Delta Metacarpal Rotation Y"] = _modifiedDeltaMetacarpalRotation.y;
        _streamData["Modified Delta Metacarpal Rotation Z"] = _modifiedDeltaMetacarpalRotation.z;
        _streamData["Modified Delta Metacarpal Rotation W"] = _modifiedDeltaMetacarpalRotation.w;
        _streamData["Modified Delta Metacarpal Angle"] = _modifiedDeltaMetacarpalAngle;
        _streamData["Modified Delta Metacarpal Axis X"] = _modifiedDeltaMetacarpalAxis.x;
        _streamData["Modified Delta Metacarpal Axis Y"] = _modifiedDeltaMetacarpalAxis.y;
        _streamData["Modified Delta Metacarpal Axis Z"] = _modifiedDeltaMetacarpalAxis.z;

        //triangle
        _streamData["Triangle World Rotation X"] = _triangleWorldRotation.x;
        _streamData["Triangle World Rotation Y"] = _triangleWorldRotation.y;
        _streamData["Triangle World Rotation Z"] = _triangleWorldRotation.z;
        _streamData["Triangle World Rotation W"] = _triangleWorldRotation.w;

        _streamData["Triangle Local Rotation X"] = _triangleLocalRotation.x;
        _streamData["Triangle Local Rotation Y"] = _triangleLocalRotation.y;
        _streamData["Triangle Local Rotation Z"] = _triangleLocalRotation.z;
        _streamData["Triangle Local Rotation W"] = _triangleLocalRotation.w;

        _streamData["Weighted Centroid World Position X"] = _weightedCentroidWorldPosition.x;
        _streamData["Weighted Centroid World Position Y"] = _weightedCentroidWorldPosition.y;
        _streamData["Weighted Centroid World Position Z"] = _weightedCentroidWorldPosition.z;

        _streamData["Weighted Centroid Local Position X"] = _weightedCentroidLocalPosition.x;
        _streamData["Weighted Centroid Local Position Y"] = _weightedCentroidLocalPosition.y;
        _streamData["Weighted Centroid Local Position Z"] = _weightedCentroidLocalPosition.z;

        _streamData["Thumb Weight"] = _thumbWeight;
        _streamData["Triangle Area"] = _triangleArea;
        _streamData["Triangle P1 Angle"] = _triangleP1Angle;
        _streamData["Delta Triangle P1 Angle"] = _deltaTriangleP1Angle;

        //dice
        _streamData["Die World Position X"] = _dieWorldPosition.x;
        _streamData["Die World Position Y"] = _dieWorldPosition.y;
        _streamData["Die World Position Z"] = _dieWorldPosition.z;
        _streamData["Die World Rotation X"] = _dieWorldRotation.x;
        _streamData["Die World Rotation Y"] = _dieWorldRotation.y;
        _streamData["Die World Rotation Z"] = _dieWorldRotation.z;
        _streamData["Die World Rotation W"] = _dieWorldRotation.w;

        _streamData["Die Local Position X"] = _dieLocalPosition.x;
        _streamData["Die Local Position Y"] = _dieLocalPosition.y;
        _streamData["Die Local Position Z"] = _dieLocalPosition.z;
        _streamData["Die Local Rotation X"] = _dieLocalRotation.x;
        _streamData["Die Local Rotation Y"] = _dieLocalRotation.y;
        _streamData["Die Local Rotation Z"] = _dieLocalRotation.z;
        _streamData["Die Local Rotation W"] = _dieLocalRotation.w;

        _streamData["Target Offset Position X"] = _targetOffsetPosition.x;
        _streamData["Target Offset Position Y"] = _targetOffsetPosition.y;
        _streamData["Target Offset Position Z"] = _targetOffsetPosition.z;
        _streamData["Target Offset Rotation X"] = _targetOffsetRotation.x;
        _streamData["Target Offset Rotation Y"] = _targetOffsetRotation.y;
        _streamData["Target Offset Rotation Z"] = _targetOffsetRotation.z;
        _streamData["Target Offset Rotation W"] = _targetOffsetRotation.w;

        //head
        _streamData["Head World Position X"] = _headWorldPosition.x;
        _streamData["Head World Position Y"] = _headWorldPosition.y;
        _streamData["Head World Position Z"] = _headWorldPosition.z;
        _streamData["Head World Rotation X"] = _headWorldRotation.x;
        _streamData["Head World Rotation Y"] = _headWorldRotation.y;
        _streamData["Head World Rotation Z"] = _headWorldRotation.z;
        _streamData["Head World Rotation W"] = _headWorldRotation.w;

        //status
        _streamData["Is Grabbing"] = _isGrabbing;
        _streamData["Is Clutching"] = _isClutching;
        _streamData["Is Overlapped"] = _isOverlapped;
    }

    void UpdateEventData()
    {
        _eventData["Trial"] = _trialNum;
        _eventData["Timestamp"] = _timeStamp;
        _eventData["Event Name"] = _eventName;
        _eventData["Trial Duration"] = _trialDuration;

        _eventData["Die World Position X"] = _dieWorldPosition.x;
        _eventData["Die World Position Y"] = _dieWorldPosition.y;
        _eventData["Die World Position Z"] = _dieWorldPosition.z;
        _eventData["Die World Rotation X"] = _dieWorldRotation.x;
        _eventData["Die World Rotation Y"] = _dieWorldRotation.y;
        _eventData["Die World Rotation Z"] = _dieWorldRotation.z;
        _eventData["Die World Rotation W"] = _dieWorldRotation.w;

        _eventData["Die Local Position X"] = _dieLocalPosition.x;
        _eventData["Die Local Position Y"] = _dieLocalPosition.y;
        _eventData["Die Local Position Z"] = _dieLocalPosition.z;
        _eventData["Die Local Rotation X"] = _dieLocalRotation.x;
        _eventData["Die Local Rotation Y"] = _dieLocalRotation.y;
        _eventData["Die Local Rotation Z"] = _dieLocalRotation.z;
        _eventData["Die Local Rotation W"] = _dieLocalRotation.w;

        _eventData["Target Offset Position X"] = _targetOffsetPosition.x;
        _eventData["Target Offset Position Y"] = _targetOffsetPosition.y;
        _eventData["Target Offset Position Z"] = _targetOffsetPosition.z;
        _eventData["Target Offset Rotation X"] = _targetOffsetRotation.x;
        _eventData["Target Offset Rotation Y"] = _targetOffsetRotation.y;
        _eventData["Target Offset Rotation Z"] = _targetOffsetRotation.z;
        _eventData["Target Offset Rotation W"] = _targetOffsetRotation.w;
    }
    
    void UpdateSummaryData()
    {
        _summaryData["Trial"] = _trialNum;
        _summaryData["Task Completion Time"] = _taskCompletionTime;
        _summaryData["Is Timeout"] = _isTimeout;

        //final offset
        _summaryData["Target Offset Position X"] = _targetOffsetPosition.x;
        _summaryData["Target Offset Position Y"] = _targetOffsetPosition.y;
        _summaryData["Target Offset Position Z"] = _targetOffsetPosition.z;
        _summaryData["Target Offset Rotation X"] = _targetOffsetRotation.x;
        _summaryData["Target Offset Rotation Y"] = _targetOffsetRotation.y;
        _summaryData["Target Offset Rotation Z"] = _targetOffsetRotation.z;
        _summaryData["Target Offset Rotation W"] = _targetOffsetRotation.w;

        //accumulation
        _summaryData["Total Wrist World Translation"] = _totalWristWorldTranslation;
        _summaryData["Total Wrist World Rotation"] = _totalWristWorldRotation;
        
        _summaryData["Total Thumb Local Translation"] = _totalThumbTipLocalTranslation;
        _summaryData["Total Thumb Local Rotation"] = _totalThumbTipLocalRotation;

        _summaryData["Total Index Local Translation"] = _totalIndexTipLocalTranslation;
        _summaryData["Total Index Local Rotation"] = _totalIndexTipLocalRotation;

        _summaryData["Total Middle Local Translation"] = _totalMiddleTipLocalTranslation;
        _summaryData["Total Middle Local Rotation"] = _totalMiddleTipLocalRotation;

        _summaryData["Total Metacarpal Local Translation"] = _totalMetacarpalLocalTranslation;
        _summaryData["Total Metacarpal Local Rotation"] = _totalMetacarpalLocalRotation;

        _summaryData["Total Die World Translation"] = _totalDieWorldTranslation;
        _summaryData["Total Die World Rotation"] = _totalDieWorldRotation;

        _summaryData["Total Die Local Translation"] = _totalDieLocalTranslation;
        _summaryData["Total Die Local Rotation"] = _totalDieLocalRotation;

        _summaryData["Total Head World Translation"] = _totalHeadWorldTranslation;
        _summaryData["Total Head World Rotation"] = _totalHeadWorldRotation;
    }
}
