using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using Unity.VisualScripting;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using System.Runtime.CompilerServices;
using Oculus.Interaction;
using System.IO;
using Unity.XR.CoreUtils;

public class BBHandInteractor : MonoBehaviour
{
    // [SerializeField]
    // private TextMeshProUGUI _textbox;
    [SerializeField]
    private bool _isInDebugMode;
    private Dictionary<KeyCode, Action> _keyActions;
    private OVRSkeleton _ovrSkeleton;
    private OVRBone _indexTipBone, _middleTipBone, _thumbTipBone, _wristBone;

    private Pose _wristWorld, _thumbTipWorld, _indexTipWorld, _middleTipWorld;
    private Pose _thumbTip, _indexTip, _middleTip;
    private Pose _prevThumbTip, _prevIndexTip, _prevMiddleTip;
    private Pose _prevObject, _object, _objectWorld, _objectLocal;
    private bool _isOnTarget = false;

    public event Action OnGrab, OnRelease;

    // private const string BASE_DIRECTORY_PATH = "D:/Temp/";
    // private FileStream _fileStream;
    // private StreamWriter _streamWriter;
    // private long _timeStamp;
    // private string _filePath;

    [SerializeField]
    private TextMeshProUGUI _debugText;

    // Start is called before the first frame update
    void Start()
    {
        // for (int i = 0; i < 6; i++)
        // {
        //     GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        //     sphere.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);
        //     sphere.GetComponent<Collider>().isTrigger = true;
        //     _spheres.Add(sphere);
        // }
        _debugText.text = "";

        InitGeometry();
        ResetGeometry();

        // string fileName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv";
        // try
        // {
        //     _fileStream = new FileStream(BASE_DIRECTORY_PATH + fileName, FileMode.Create, FileAccess.Write);
        //     _streamWriter = new StreamWriter(_fileStream, System.Text.Encoding.UTF8);
        //     _streamWriter.WriteLine("Timestamp, Thumb, Index, Middle");
        // }
        // catch (IOException e)
        // {
        //     Debug.LogError("Error: " + e.Message);
        // }
    }

    // Update is called once per frame
    void Update()
    {
        // _debugText.text = $"{_gainCondition}\t{_angleScaleFactor}";

        // should be performed even if no object is grabbed
        // transforms are on the local coordinates based on the wrist if not stated otherwise
        _wristWorld.position = _wristBone.Transform.position;
        _wristWorld.rotation = _wristBone.Transform.rotation;

        _thumbTipWorld.position = _thumbTipBone.Transform.position;
        _indexTipWorld.position = _indexTipBone.Transform.position;
        _middleTipWorld.position = _middleTipBone.Transform.position;

        _thumbTip.position = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _indexTip.position = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _middleTip.position = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        Vector3 thumb, index, middle;
        thumb = _thumbTip.position;
        index = _indexTip.position;
        middle = _middleTip.position;

        _prevThumbTip.position = thumb;
        _prevIndexTip.position = index;
        _prevMiddleTip.position = middle;
    }

    
    public void OnDestroy()
    {
        // _streamWriter.Close();
        // _fileStream.Close();
    }

    private void InitGeometry()
    {
        _indexTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip);
        _middleTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleTip);
        _thumbTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip);
        _wristBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_Wrist);

        _wristWorld.position = _wristBone.Transform.position;
        _wristWorld.rotation = _wristBone.Transform.rotation;

        _prevObject.rotation = Quaternion.identity;
    }

    private void ResetGeometry()
    {
        _prevThumbTip.position = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _prevIndexTip.position = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _prevMiddleTip.position = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _prevObject.rotation = Quaternion.identity;
    }

    public void OnTarget()
    {

    }

    public void OffTarget()
    {

    }

    public void Reset()
    {
        OffTarget();
        InitGeometry();
    }

    public void SetOVRSkeleton(OVRSkeleton s)
    {
        _ovrSkeleton = s;
    }

    // Public read-only properties for logging
    public Vector3 WristWorldPosition => _wristWorld.position;
    public Quaternion WristWorldRotation => _wristWorld.rotation;
    public Vector3 ThumbTipWorldPosition => _thumbTipWorld.position;
    public Vector3 IndexTipWorldPosition => _indexTipWorld.position;
    public Vector3 MiddleTipWorldPosition => _middleTipWorld.position;
    public Vector3 ThumbTipLocalPosition => _thumbTip.position;
    public Vector3 IndexTipLocalPosition => _indexTip.position;
    public Vector3 MiddleTipLocalPosition => _middleTip.position;
    public Pose ObjectWorldPose => _objectWorld;
    public Pose ObjectLocalPose => _objectLocal;
}
