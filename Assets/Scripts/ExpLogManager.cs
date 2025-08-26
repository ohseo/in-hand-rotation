using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

public class ExpLogManager : MonoBehaviour
{
    public int _participantNum { get; set; }

    public int _transferFunction { get; set; }

    private FileStream _summaryFileStream, _eventFileStream, _streamFileStream;
    private StreamWriter _summaryWriter, _eventWriter, _streamWriter;

    private string[] _streamHeader;
    private string[] _worldJointsHeader = new string[12] { "Wrist World Position", "Wrist World Rotation",
                                                            "Thumb World Position", "Thumb World Rotation",
                                                            "Index World Position", "Index World Rotation",
                                                            "Middle World Position", "Middle World Rotation",
                                                            "Metacarpal World Position", "Metacarpal World Rotation",
                                                            "Modified Thumb World Position", "Modified Thumb World Rotation" };
    private string[] _localJointsHeader = new string[10] { "Thumb Local Position", "Thumb Local Rotation",
                                                            "Index Local Position", "Index Local Rotation",
                                                            "Middle Local Position", "Middle Local Rotation",
                                                            "Metacarpal Local Position", "Metacarpal Local Rotation",
                                                            "Modified Thumb Local Position", "Modified Thumb Local Rotation" };

    private string[] _modifiedThumbHeader = new string[3] { "Delta Thumb Angle", "Delta Thumb Axis", "Modified Delta Thumb Angle" };
    private string[] _triangleHeader = new string[11] { "Triangle World Position", "Triangle World Rotation",
                                                        "Triangle Local Position", "Triangle Local Rotation",
                                                        "Weighted Centroid World Position", "Weighted Centroid Local Position",
                                                        "Thumb Weight",
                                                        "Triangle Area", "Triangle P1 Angle",
                                                        "Delta Triangle Rotation","Delta P1 Angle" };

    private string[] _diceHeader = new string[6] { "Dice World Position", "Dice World Rotation",
                                                  "Dice Local Position", "Dice Local Rotation",
                                                  "Grab Offset Position", "Grab Offset Rotation"};
    private string[] _targetOffsetHeader = new string[2] {"Target Offset Position", "Target Offset Rotation"};
    private string[] _accumulationHeader = new string[] {"Total Wrist World Translation", "Total Wrist World Rotation",
    "Total Thumb Local Translation", "Total Thumb Local Rotation",
    "Total Index Local Translation", "Total Index Local Rotation",
    "Total Middle Local Translation", "Total Middle Local Rotation",
    "Total Metacarpal Local Translation", "Total Metacarpal Local Rotation",

    };
    private string[] _statusHeader;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
