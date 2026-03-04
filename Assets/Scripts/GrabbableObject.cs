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
    private const float OUTLINE_WIDTH_DEFAULT = 3f;

    void Awake()
    {
        _outline = this.GetComponentInChildren<Outline>();
        if (_outline != null)
        {
            _outline.OutlineColor = Color.blue;
            _outline.OutlineWidth = OUTLINE_WIDTH_DEFAULT;
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
    }

    public void OnRelease()
    {
        _followTarget = null;
    }

    public void SetOutlineWidth(float width)
    {
        if (_outline != null) _outline.OutlineWidth = width;
    }

    public void SetOutlineEnabled(bool enabled)
    {
        if (_outline != null) _outline.enabled = enabled;
    }

    public void SetOutlineColor(Color c)
    {
        if (_outline != null) _outline.OutlineColor = c;
    }
}
