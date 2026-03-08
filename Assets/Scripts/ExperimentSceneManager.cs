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
    private List<HandInteractor> _handInteractors; // 0: Right, 1: Left
    [SerializeField]
    private List<OVRSkeleton> _ovrSkeletons; // 0: Right, 1: Left
    [SerializeField]
    private GameObject _centerEyeAnchor;
    [SerializeField]
    private TextMeshProUGUI _conditionText;
    [SerializeField]
    private TextMeshProUGUI _trialText;
    [SerializeField]
    private ExperimentLogManager _logManager;
    [SerializeField]
    private AudioSource _audioSource;

    [Space]
    [Header("Sound Effects")]
    [SerializeField] private AudioClip _grabSound;
    [SerializeField] private AudioClip _clutchStartSound;
    [SerializeField] private AudioClip _trialEndSound;
    [SerializeField] private AudioClip _timeoutSound;
    [SerializeField] private AudioClip _blockEndSound;

    public enum ExpType { Optimization_Exp1 = 1, Evaluation_Exp2 = 2 }
    public enum GainType { Constant_O = 0, Low_A = 1, Medium_B = 2, High_C = 3 }
    public enum MethodType { Baseline_X = 0, Physics_Y = 1, GeoCtrl_Z = 2 }

    [Space]
    [Header("Exp Information")]
    [SerializeField]
    private int _participantNum;
    [SerializeField]
    private bool _isLeftHanded = false;
    [SerializeField]
    private ExpType _expType = ExpType.Evaluation_Exp2;
    // [SerializeField]
    private GainType _gainType = GainType.Low_A;
    [SerializeField]
    private MethodType _methodType = MethodType.GeoCtrl_Z;
    [SerializeField]
    private bool _isPracticeMode = false;

    private GameObject _die, _target;
    private const float CUBE_SCALE = 0.04f;

    // EXP 1: 3 angles (balanced) * 3 sets * 6 axes (random)
    private Vector3 INIT_POSITION_EXP1 = new Vector3(0.05f, 1f, 0.3f);
    private const int MAX_SET_NUM = 3;
    private List<float> ROTATION_ANGLES = new List<float> { 30f, 120f, 210f };
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
    private Vector3 INIT_POSITION_EXP2 = new Vector3(0.05f, 1f, 0.3f);
    private const float INIT_ROTATION_DEG = 135f;
    private Vector3 _randomAxis;
    private const int MAX_TRIAL_NUM = 3;
    private const int MAX_BLOCK_NUM = 3;
    private int _trialNum = 1, _blockNum = 1; // Num starts with 1, Index starts with 0

    private const float POSITION_THRESHOLD = 0.02f, ROTATION_THRESHOLD_DEG = 10f;
    private const float DWELL_THRESHOLD = 1f, TIMEOUT_THRESHOLD = 10f;
    private float _dwellDuration = 0f, _trialDuration = 0f;
    private bool _isOnTarget = false, _isTimeout = false, _isInTrial = false;

    public event Action OnTrialLoad, OnTrialStart, OnTrialEnd, OnTrialReset, OnTarget, OffTarget, OnTimeout;

    void Awake()
    {
        for (int i = 0; i < _handInteractors.Count; i++)
        {
            _handInteractors[i].SetOVRSkeleton(_ovrSkeletons[i]);
            _handInteractors[i].SetGainCondition((int)_gainType);
        }

        // _latinSequence = GenerateLatinSquareSequence(ROTATION_ANGLES.Count, _participantNum);
        _randomSequence = GenerateRandomSequence(ROTATION_AXES.Count);

        _conditionText.text = (_expType == ExpType.Optimization_Exp1) ? $"{_gainType}".Split('_')[1] : $"{_methodType}".Split('_')[1];
    }


    // Start is called before the first frame update
    void Start()
    {
        OnTrialLoad += LoadNewTrial;
        OnTrialStart += StartTrial;

        OnTrialEnd += EndTrial;
        OnTrialReset += ResetTrial;
        OnTimeout += TimeOut;

        foreach (var h in _handInteractors)
        {
            OnTrialEnd += h.Reset;
            OnTrialReset += h.Reset;
            h.OnGrab += h.GrabObject;
            h.OnGrab += OnGrab;
            h.OnRelease += h.ReleaseObject;
            h.OnClutchEnd += h.EndClutching;
            h.OnClutchStart += h.StartClutching;
            OnTarget += h.OnTarget;
            OffTarget += h.OffTarget;

            h.OnGrab += () => PlaySound(_grabSound);
            h.OnClutchStart += () => PlaySound(_clutchStartSound);
        }

        if (!_isPracticeMode)
        {
            OnTrialLoad += () => _logManager.OnEvent("Trial Load");
            OnTrialStart += () => _logManager.OnEvent("Trial Start");
            OnTrialEnd += () => _logManager.OnEvent("Trial End");
            OnTrialReset += () => _logManager.OnEvent("Trial Reset");
            OnTarget += () => _logManager.OnEvent("On Target");
            OffTarget += () => _logManager.OnEvent("Off Target");
            OnTimeout += () => _logManager.OnEvent("Timeout");

            for (int i = 0; i < _handInteractors.Count; i++)
            {
                int idx = i;
                _handInteractors[idx].OnGrab += () => _logManager.OnEvent("Grab", idx);
                _handInteractors[idx].OnRelease += () => _logManager.OnEvent("Release", idx);
                _handInteractors[idx].OnClutchStart += () => _logManager.OnEvent("Clutch Start", idx);
                _handInteractors[idx].OnClutchEnd += () => _logManager.OnEvent("Clutch End", idx);
            }
        }

        if (!_isPracticeMode) _logManager.Initialize(this);

        OnTrialLoad?.Invoke();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.KeypadEnter))
        {
            if ((_expType == ExpType.Optimization_Exp1) && (_angleIndex < ROTATION_ANGLES.Count) && (_die == null))
            {
                OnTrialLoad?.Invoke();
                return;
            }
            if ((_expType == ExpType.Evaluation_Exp2) && (_blockNum <= MAX_BLOCK_NUM) && (_die == null))
            {
                OnTrialLoad?.Invoke();
                return;
            }
        }
        if (Input.GetKey(KeyCode.Return))
        {
            OnTrialReset?.Invoke();
            return;
        }

        _trialDuration += Time.deltaTime;
        if (_isInTrial && _trialDuration > TIMEOUT_THRESHOLD && !_isPracticeMode)
        {
            OnTimeout?.Invoke();
            CheckNextTrial();
        }

        if (!_isInTrial) return;

        if (!_isPracticeMode) _logManager.WriteStreamRow();

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
                CheckNextTrial();                
            }
        }
    }

    void OnDestroy()
    {
        if (!_isPracticeMode) _logManager.CloseAll();
        DestroyDie();
        DestroyTarget();
    }

    private void LoadNewTrial()
    {
        GenerateDie();
        GenerateTarget();
        _trialText.text = (_expType == ExpType.Optimization_Exp1) ?
            // $"{ROTATION_ANGLES[_latinSequence[_angleIndex]]} deg: Set {_setNum}/{MAX_SET_NUM}" : $"Trial {_trialNum}/{MAX_TRIAL_NUM}";
            $"{ROTATION_ANGLES[_angleIndex]} deg: Set {_setNum}/{MAX_SET_NUM}" : $"Trial {_trialNum}/{MAX_TRIAL_NUM}: Block {_blockNum}/{MAX_BLOCK_NUM}";
    }

    private void StartTrial()
    {
        _isTimeout = false;
        _isInTrial = true;
        _trialDuration = 0f;
    }

    private void EndTrial()
    {
        if (_isTimeout) PlaySound(_timeoutSound);
        else PlaySound(_trialEndSound);
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

    private void CheckNextTrial()
    {
        switch (_expType)
        {
            case ExpType.Optimization_Exp1:
                if (_axisIndex >= ROTATION_AXES.Count)
                {
                    _setNum++;
                    _axisIndex = 0;
                    _randomSequence = GenerateRandomSequence(ROTATION_AXES.Count);
                }
                if (_setNum > MAX_SET_NUM) { _angleIndex++; _setNum = 1; PlaySound(_blockEndSound); }
                else OnTrialLoad?.Invoke();
                if (_angleIndex >= ROTATION_ANGLES.Count) Application.Quit();
                break;
            case ExpType.Evaluation_Exp2:
            default:
                if (_trialNum > MAX_TRIAL_NUM)
                {
                    _blockNum++;
                    if (_blockNum > MAX_BLOCK_NUM) Application.Quit();
                    _trialNum = 1;
                    PlaySound(_blockEndSound);
                }
                else OnTrialLoad?.Invoke();
                break;
        }
    }

    private void TimeOut()
    {
        _isTimeout = true;
        OnTrialEnd?.Invoke();
    }

    private void OnGrab()
    {
        if (!_isInTrial) OnTrialStart?.Invoke();
    }

    private void PlaySound(AudioClip clip)
    {
        if (_audioSource != null && clip != null) _audioSource.PlayOneShot(clip);
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
        if (_isPracticeMode) _angleIndex = 1;
        _target = Instantiate(_targetPrefab);
        // float angle = (_expType == ExpType.Optimization_Exp1) ? ROTATION_ANGLES[_latinSequence[_angleIndex]] : INIT_ROTATION_DEG;
        float angle = (_expType == ExpType.Optimization_Exp1) ? ROTATION_ANGLES[_angleIndex] : INIT_ROTATION_DEG;
        Vector3 axis = (_expType == ExpType.Optimization_Exp1) ? ROTATION_AXES[_randomSequence[_axisIndex]] : UnityEngine.Random.onUnitSphere;
        Vector3 position = (_expType == ExpType.Optimization_Exp1) ? INIT_POSITION_EXP1 : INIT_POSITION_EXP2;
        _target.transform.Rotate(axis.normalized, angle);
        _target.transform.position = !_isLeftHanded ? position : new Vector3(-position.x, position.y, position.z);
        _target.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        if (_expType == ExpType.Optimization_Exp1) DrawAxis(_target, axis); else DisableAxis(_target);
        if (_expType == ExpType.Evaluation_Exp2) _randomAxis = axis;
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

    // Public read-only properties for logging
    public int ParticipantNum => _participantNum;
    public bool IsLeftHanded => _isLeftHanded;
    public ExpType Experiment => _expType;
    public GainType Gain => _gainType;
    public MethodType Method => _methodType;
    public bool IsPracticeMode => _isPracticeMode;
    public bool IsInTrial => _isInTrial;
    public bool IsOnTarget => _isOnTarget;
    public bool IsTimeout => _isTimeout;
    public float TrialDuration => _trialDuration;
    public float DwellDuration => _dwellDuration;
    public int AngleIndex => _angleIndex;
    public int SetNum => _setNum;
    public int AxisIndex => _axisIndex;
    public int TrialNum => _trialNum;
    public int BlockNum => _blockNum;

    public float CurrentAngle => (_expType == ExpType.Optimization_Exp1 && _angleIndex < ROTATION_ANGLES.Count)
        // ? ROTATION_ANGLES[_latinSequence[_angleIndex]] : INIT_ROTATION_DEG;
        ? ROTATION_ANGLES[_angleIndex] : INIT_ROTATION_DEG;
    public Vector3 CurrentAxis => (_expType == ExpType.Optimization_Exp1 && _axisIndex < ROTATION_AXES.Count)
        ? ROTATION_AXES[_randomSequence[_axisIndex]] : _randomAxis;

    public Transform DieTransform => _die != null ? _die.transform : null;
    public Transform TargetTransform => _target != null ? _target.transform : null;
    public Vector3 HeadPosition => _centerEyeAnchor.transform.position;
    public Quaternion HeadRotation => _centerEyeAnchor.transform.rotation;

    public HandInteractor ActiveHand => _handInteractors[_isLeftHanded ? 1 : 0];
    public List<HandInteractor> HandInteractors => _handInteractors;

    public Pose TargetOffset
    {
        get
        {
            if (_die == null || _target == null) return new Pose(Vector3.zero, Quaternion.identity);
            CalculateError(out Pose delta);
            return delta;
        }
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
