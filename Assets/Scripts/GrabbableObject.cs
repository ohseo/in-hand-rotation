using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Jobs;

public class GrabbableObject : MonoBehaviour
{
    private Transform _followTarget;
    private Outline _outline;

    void Awake()
    {
        _outline = this.GetComponentInChildren<Outline>();
        if (_outline != null)
        {
            _outline.OutlineColor = Color.blue;
            _outline.OutlineWidth = 3f;
            _outline.enabled = false;
        }
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
        if (_outline != null) _outline.enabled = true;
    }

    public void OnRelease()
    {
        _followTarget = null;
        if (_outline != null) _outline.enabled = false;
    }

    public void SetOutlineWidth(float width)
    {
        if (_outline != null) _outline.OutlineWidth = width;
    }

    public void SetOutlineColor(Color c)
    {
        if (_outline != null) _outline.OutlineColor = c;
    }
}
