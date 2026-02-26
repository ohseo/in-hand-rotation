using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;

public class ExperimentSceneManager : MonoBehaviour
{
    [Header("Scene Setup")]
    [SerializeField]
    private GameObject _diePrefab;
    [SerializeField]
    private GameObject _targetPrefab;
    [SerializeField]
    private GameObject _arrowPrefab;
    [SerializeField]
    private HandInteractor _handInteractorRight, _handInteractorLeft;
    [SerializeField]
    private OVRSkeleton _ovrSkeletonRight, _ovrSkeletonLeft;
    [SerializeField]
    private GameObject _centerEyeAnchor;
    [SerializeField]
    private TextMeshProUGUI _conditionText;
    [SerializeField]
    private TextMeshProUGUI _trialText;
    // Log Manager

    public enum ExpType { Optimization_Exp1 = 1, Evaluation_Exp2 = 2 }
    public enum GainType { Constant_O = 0, Low_A = 1, Medium_B = 2, High_C = 3 }
    public enum MethodType { Baseline_B = 0, Physics_P = 1, Figeodex_F = 2 }

    [Space]
    [Header("Exp Information")]
    [SerializeField]
    private int _participantNum;
    [SerializeField]
    private bool _isLeftHanded = false;
    [SerializeField]
    private ExpType _expType = ExpType.Optimization_Exp1;
    [SerializeField]
    private GainType _gainType = GainType.Constant_O;
    [SerializeField]
    private MethodType _methodType = MethodType.Figeodex_F;

    private GameObject _die, _target;
    private const float CUBE_SCALE = 0.04f;

    // EXP 1: 3 angles (balanced) * 4 sets * 6 axes (random)
    private Vector3 INIT_POSITION_EXP1 = new Vector3(0.05f, 1f, 0.3f);
    private const int MAX_SET_NUM = 4;
    private List<float> ROTATION_ANGLES = new List<float> { 30f, 90f, 150f };
    private List<Vector3> ROTATION_AXES = new List<Vector3>
    {
        new Vector3(1f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(0f, 0f, 1f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(0f, -1f, 0f),
        new Vector3(0f, 0f, -1f)
    };
    private int[] _latinSequence, _randomSequence;
    private int _angleIndex = 0, _setNum = 1, _axisIndex = 0; // Num starts with 1, Index starts with 0
    private GameObject _arrow1, _arrow2;
    private Vector3 _arrowScale = new Vector3(0.5f, 0.5f, 5f);

    // EXP 2
    private Vector3 INIT_POSITION_EXP2 = new Vector3(0.1f, 1.1f, 0.3f);
    private const float INIT_ROTATION_DEG = 135f;
    private const int MAX_TRIAL_NUM = 3;
    private int _trialNum = 1; // Num starts with 1, Index starts with 0

    private const float POSITION_THRESHOLD = 0.01f, ROTATION_THRESHOLD_DEG = 5f;
    private const float DWELL_THRESHOLD = 1f, TIMEOUT_THRESHOLD = 30f;
    private float _dwellDuration = 0f, _trialDuration = 0f;
    private Pose _targetOffset;
    private bool _isOnTarget = false, _isTimeout = false, _isInTrial = false;

    public event Action OnTrialLoad, OnTrialStart, OnTrialEnd, OnTrialReset, OnTarget, OffTarget;

    void Awake()
    {
        _handInteractorRight.SetOVRSkeleton(_ovrSkeletonRight);
        _handInteractorLeft.SetOVRSkeleton(_ovrSkeletonLeft);
        _handInteractorRight.SetGainCondition((int)_gainType);
        _handInteractorLeft.SetGainCondition((int)_gainType);

        _latinSequence = GenerateLatinSquareSequence(ROTATION_ANGLES.Count, _participantNum);
        _randomSequence = GenerateRandomSequence(ROTATION_AXES.Count);

        _conditionText.text = (_expType == ExpType.Optimization_Exp1) ? $"{_gainType}".Split('_')[1] : $"{_methodType}".Split('_')[1];
    }


    // Start is called before the first frame update
    void Start()
    {
        OnTrialLoad += LoadNewTrial;
        OnTrialStart += StartTrial;

        OnTrialEnd += EndTrial;
        OnTrialEnd += _handInteractorLeft.Reset;
        OnTrialEnd += _handInteractorRight.Reset;

        OnTrialReset += ResetTrial;
        OnTrialReset += _handInteractorLeft.Reset;
        OnTrialReset += _handInteractorRight.Reset;

        _handInteractorLeft.OnGrab += _handInteractorLeft.GrabObject;
        _handInteractorLeft.OnGrab += OnGrab;
        _handInteractorRight.OnGrab += _handInteractorRight.GrabObject;
        _handInteractorRight.OnGrab += OnGrab;

        _handInteractorLeft.OnRelease += _handInteractorLeft.ReleaseObject;
        _handInteractorRight.OnRelease += _handInteractorRight.ReleaseObject;

        _handInteractorLeft.OnClutchEnd += _handInteractorLeft.EndClutching;
        _handInteractorRight.OnClutchEnd += _handInteractorRight.EndClutching;

        _handInteractorLeft.OnClutchStart += _handInteractorLeft.StartClutching;
        _handInteractorRight.OnClutchStart += _handInteractorRight.StartClutching;

        OnTarget += _handInteractorLeft.OnTarget;
        OnTarget += _handInteractorRight.OnTarget;

        OffTarget += _handInteractorLeft.OffTarget;
        OffTarget += _handInteractorRight.OffTarget;

        OnTrialLoad?.Invoke();        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Return))
        {
            OnTrialReset?.Invoke();
            return;
        }

        if (!_isInTrial) return;
        _trialDuration += Time.deltaTime;

        bool isErrorSmall = CalculateError(out Pose offset);
        if (isErrorSmall && !_isOnTarget)
        {
            _dwellDuration = 0f;
            _isOnTarget = true;
            OnTarget?.Invoke();
        }
        else if (!isErrorSmall && _isOnTarget)
        {
            _isOnTarget = false;
            OffTarget?.Invoke();
        }

        if (_isOnTarget)
        {
            _dwellDuration += Time.deltaTime;
            if (_dwellDuration > DWELL_THRESHOLD)
            {
                OnTrialEnd?.Invoke();
                switch (_expType)
                {
                    case ExpType.Optimization_Exp1:
                        if (_axisIndex >= ROTATION_AXES.Count)
                        {
                            _setNum++;
                            _axisIndex = 0;
                            _randomSequence = GenerateRandomSequence(ROTATION_AXES.Count);
                        }
                        if (_setNum > MAX_SET_NUM) { _angleIndex++; _setNum = 1; }
                        if (_angleIndex < ROTATION_ANGLES.Count) OnTrialLoad?.Invoke();
                        break;
                    case ExpType.Evaluation_Exp2:
                    default:
                        if (_trialNum <= MAX_TRIAL_NUM) OnTrialLoad?.Invoke();
                        break;
                }
                
            }
        }
    }

    void OnDestroy()
    {
        DestroyDie();
        DestroyTarget();
    }

    private void LoadNewTrial()
    {
        GenerateDie();
        GenerateTarget();
        _trialText.text = (_expType == ExpType.Optimization_Exp1) ?
            $"{ROTATION_ANGLES[_angleIndex]} deg: Set {_setNum}/{MAX_SET_NUM}" : $"Trial {_trialNum}/{MAX_TRIAL_NUM}";
    }

    private void StartTrial()
    {
        _isTimeout = false;
        _isInTrial = true;
        _trialDuration = 0f;
    }

    private void EndTrial()
    {
        DestroyDie();
        DestroyTarget();
        _isOnTarget = false;
        _isInTrial = false;
        _ = (_expType == ExpType.Optimization_Exp1) ? _axisIndex++ : _trialNum++;
    }

    private void ResetTrial()
    {
        DestroyDie();
        GenerateDie();
        _isOnTarget = false;
    }

    private void OnGrab()
    {
        if (!_isInTrial) OnTrialStart?.Invoke();
    }

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        Vector3 position = (_expType == ExpType.Optimization_Exp1) ? INIT_POSITION_EXP1 : INIT_POSITION_EXP2;
        _die.transform.position = _isLeftHanded ? position : new Vector3(-position.x, position.y, position.z);
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;

        if (_expType == ExpType.Optimization_Exp1)
        {
            Vector3 axis = ROTATION_AXES[_randomSequence[_axisIndex]];
            DrawAxis(_die, axis);
            DrawArrow(_die, axis);
        }
        else DisableAxis(_die);
    }

    private void DestroyDie()
    {
        Destroy(_die);
    }

    private void GenerateTarget()
    {
        _target = Instantiate(_targetPrefab);
        float angle = (_expType == ExpType.Optimization_Exp1) ? ROTATION_ANGLES[_latinSequence[_angleIndex]] : INIT_ROTATION_DEG;
        Vector3 axis = (_expType == ExpType.Optimization_Exp1) ? ROTATION_AXES[_randomSequence[_axisIndex]] : UnityEngine.Random.onUnitSphere;
        Vector3 position = (_expType == ExpType.Optimization_Exp1) ? INIT_POSITION_EXP1 : INIT_POSITION_EXP2;
        _target.transform.Rotate(axis.normalized, angle);
        _target.transform.position = !_isLeftHanded ? position : new Vector3(-position.x, position.y, position.z);
        _target.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        if (_expType == ExpType.Optimization_Exp1) DrawAxis(_target, axis); else DisableAxis(_target);
    }

    private void DestroyTarget()
    {
        Destroy(_target);
    }

    private bool CalculateError(out Pose delta)
    {
        delta.position = _target.transform.position - _die.transform.position; //
        delta.rotation = _target.transform.rotation * Quaternion.Inverse(_die.transform.rotation);
        float pError = delta.position.magnitude;
        delta.rotation.ToAngleAxis(out float rError, out Vector3 axis);
        return (pError < POSITION_THRESHOLD) && ((rError < ROTATION_THRESHOLD_DEG) || (rError > 360f - ROTATION_THRESHOLD_DEG));
    }

    private void DrawAxis(GameObject go, Vector3 axis)
    {
        LineRenderer lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.SetPosition(0, axis * -1f);
        lr.SetPosition(1, axis);
    }

    private void DrawArrow(GameObject parent, Vector3 axis)
    {
        _arrow1 = Instantiate(_arrowPrefab, parent.transform, false);
        _arrow2 = Instantiate(_arrowPrefab, parent.transform, false);

        MeshRenderer mr1 = _arrow1.GetComponent<MeshRenderer>();
        MeshRenderer mr2 = _arrow2.GetComponent<MeshRenderer>();
        mr1.material.color = Color.red;
        mr2.material.color = Color.red;

        Vector3 tangent = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.9f
                      ? Vector3.Cross(axis, Vector3.up).normalized
                      : Vector3.Cross(axis, Vector3.forward).normalized;

        _arrow1.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(tangent, axis), axis);
        _arrow1.transform.localPosition = Vector3.Cross(tangent, axis);
        _arrow1.transform.localScale = _arrowScale;

        _arrow2.transform.localRotation = Quaternion.LookRotation(tangent, axis);
        _arrow2.transform.localPosition = tangent;
        _arrow2.transform.localScale = _arrowScale;
    }

    private void DisableAxis(GameObject go)
    {
        LineRenderer lr = go.GetComponent<LineRenderer>();
        lr.enabled = false;
    }

    public static int[] GenerateLatinSquareSequence(int n, int pNum)
    {
        // Since participant num starts with 1
        int participantIndex = pNum--;
        if (participantIndex < 0) participantIndex++;

        int totalRows = (n % 2 == 0) ? n : n * 2;

        // Ensure the index wraps around the available rows
        int effectiveIndex = participantIndex % totalRows;

        // If we are in the second half of an odd-numbered square
        bool isReversedRow = n % 2 != 0 && effectiveIndex >= n;

        // Calculate the base row index (0 to n-1)
        int baseRowIndex = isReversedRow ? effectiveIndex - n : effectiveIndex;

        int[] row = new int[n];
        int h = 0;
        int j = 0;

        // Williams Design Algorithm
        for (int i = 0; i < n; i++)
        {
            int val = 0;
            if (i % 2 == 1)
            {
                val = h + 1;
                h++;
            }
            else
            {
                val = n - j;
                j++;
            }
            row[i] = (val + baseRowIndex) % n;
        }

        // If this is the second half of an odd set, reverse the sequence
        if (isReversedRow)
        {
            System.Array.Reverse(row);
        }

        return row;
    }

    public static int[] GenerateRandomSequence(int n)
    {
        int[] sequence = new int[n];
        
        // Initialize the array with 0 to n-1
        for (int i = 0; i < n; i++)
        {
            sequence[i] = i;
        }

        // Fisher-Yates Shuffle
        for (int i = n - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            int temp = sequence[i];
            sequence[i] = sequence[randomIndex];
            sequence[randomIndex] = temp;
        }

        return sequence;
    }
}
