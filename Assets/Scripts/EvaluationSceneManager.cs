using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EvaluationSceneManager : MonoBehaviour
{
    [SerializeField]
    private RotationInteractor _rotationInteractor;
    [SerializeField]
    private GameObject _diePrefab, _targetPrefab;
    private GameObject _die, _target;
    private float _cubeScale = 0.03f;
    private float _initRotationDeg = 120f;
    private Vector3 _initPosition = new Vector3(0.1f, 1f, 0.3f);

    void Awake()
    {
        GenerateTarget();
        GenerateDie();
        _rotationInteractor.SetCube(_die);
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Return))
        {
            DestroyTarget();
            GenerateTarget();
        }
    }

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        _die.transform.position = new Vector3(-_initPosition.x, _initPosition.y, _initPosition.z);
        _die.transform.localScale = new Vector3(_cubeScale, _cubeScale, _cubeScale);
    }

    private void DestroyDie()
    {
        Destroy(_die);
    }

    private void GenerateTarget()
    {
        _target = Instantiate(_targetPrefab);
        Vector3 axis = Random.onUnitSphere;
        _target.transform.Rotate(axis.normalized, _initRotationDeg);
        _target.transform.position = _initPosition;
        _target.transform.localScale = new Vector3(_cubeScale, _cubeScale, _cubeScale);
    }

    private void DestroyTarget()
    {
        Destroy(_target);
    }
}
