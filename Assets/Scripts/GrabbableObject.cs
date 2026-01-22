using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Jobs;

public class GrabbableObject : MonoBehaviour
{
    private Transform _followTarget;
    private Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (_followTarget != null)
        {
            transform.position = _followTarget.position;
            transform.rotation = _followTarget.rotation;
        }
    }

    public void OnGrab(Transform handTransform)
    {
        _followTarget = handTransform;
    }

    public void OnRelease()
    {
        _followTarget = null;
    }
}
