using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Xml;
using Meta.XR.ImmersiveDebugger.UserInterface;
using Unity.VisualScripting;
using UnityEngine;

class LowPassFilter
{
    public float lastValue { get; private set; }
    public bool hasLastValue { get; private set; } = false;

    public LowPassFilter(float initval = 0.0f)
    {
        lastValue = initval;
        hasLastValue = false;
    }

    public float Filter(float x, float a)
    {
        float result;
        if (hasLastValue) result = a * x + (1.0f - a) * lastValue;
        else
        {
            result = x;
            hasLastValue = true;
        }
        lastValue = result;
        return result;
    }
}

public class OneEuroFilter
{
    // using dt instead of freq
    private float _minCutoff, _beta, _dCutoff;
    LowPassFilter xFilter;
    LowPassFilter dxFilter;
    public bool isFreqSet { get; protected set; }

    public OneEuroFilter(float fc = 1.0f, float b = 0.0f, float dc = 1.0f)
    {
        _minCutoff = fc;
        _beta = b;
        _dCutoff = dc;
        isFreqSet = false;

        xFilter = new LowPassFilter();
        dxFilter = new LowPassFilter();
    }

    public float Filter(float x, float dt)
    {
        if (dt <= Mathf.Epsilon) return xFilter.hasLastValue ? xFilter.lastValue : x;
        float dx = xFilter.hasLastValue ? (x - xFilter.lastValue) / dt : 0.0f;
        float edx = dxFilter.Filter(dx, Alpha(_dCutoff, dt));

        float cutoff = _minCutoff + _beta * Mathf.Abs(edx);

        return xFilter.Filter(x, Alpha(cutoff, dt));
    }

    private float Alpha(float cutoff, float dt)
    {
        float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau / dt);
    }
}

public class OneEuroFilter<T> where T : struct
{
    Type _type;
    OneEuroFilter[] _oneEuroFilters;

    public float _minCutoff, _beta, _dCutoff;
    public T _lastValue { get; protected set; }
    public bool _hasLastValue { get; protected set; } = false;

    public OneEuroFilter(float fc = 1.0f, float b = 0.0f, float dc = 1.0f)
    {
        _type = typeof(T);
        _minCutoff = fc;
        _beta = b;
        _dCutoff = dc;

        if (_type == typeof(Vector3))
        {
            _oneEuroFilters = new OneEuroFilter[3];
        }
        else if (_type == typeof(Quaternion))
        {
            _oneEuroFilters = new OneEuroFilter[4];
        }

        for (int i = 0; i < _oneEuroFilters.Length; i++)
        {
            _oneEuroFilters[i] = new OneEuroFilter(_minCutoff, _beta, _dCutoff);
        }
    }

    public T Filter<U>(U value, float dt) where U : struct
    {
        if (typeof(U) == typeof(Vector3))
        {
            Vector3 output = Vector3.zero;
            Vector3 input = (Vector3)Convert.ChangeType(value, typeof(Vector3));

            for (int i = 0; i < _oneEuroFilters.Length; i++)
            {
                output[i] = _oneEuroFilters[i].Filter(input[i], dt);
            }

            _lastValue = (T)Convert.ChangeType(output, typeof(T));
            _hasLastValue = true;
        }
        else if (typeof(U) == typeof(Quaternion))
        {
            Quaternion output = Quaternion.identity;
            Quaternion input = (Quaternion)Convert.ChangeType(value, typeof(Quaternion));

            if (_hasLastValue)
            {
                Quaternion last = (Quaternion)Convert.ChangeType(_lastValue, typeof(Quaternion));
                if (Quaternion.Dot(input, last) < 0.0f)
                {
                    input = new Quaternion(-input.x, -input.y, -input.z, -input.w);
                }
            }

            for (int i = 0; i < _oneEuroFilters.Length; i++)
            {
                output[i] = _oneEuroFilters[i].Filter(input[i], dt);
            }

            _lastValue = (T)Convert.ChangeType(output, typeof(T));
            _hasLastValue = true;
        }

        return _lastValue;
    }
}
