using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class Job : ThreadedJob
{
    public string refFile;
    public int resDTW;

    protected override void ThreadFunction()
    {
        DTW dtw = new DTW(refFile);
        resDTW = (int)Math.Round(dtw.getCompResult(), 0);
    }

    protected override void OnFinished()
    {
        RecordingManager.Instance.resDTW = resDTW;
    }
}
