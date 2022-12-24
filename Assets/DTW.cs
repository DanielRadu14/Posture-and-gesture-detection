using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static RecordingManager;
using System.IO;
using System.Linq;
using System;

public class DTW : MonoBehaviour
{
    enum Direction
    {
        NULL = -1,
        LEFT = 0,
        DIAGONAL = 1,
        UP = 2,
    };

    // method 1
    private List<Vector2> path_DTW;

    // method 2
    private List<List<Vector2>> paths_DTW;

    private static int noFiles = 0;
    private int noRepetitions = 0;
    private FrameData[] user_frames;
    private FrameData[] ref_frames;
    private int currentRep = 0;

    private double[,] distance;             // cost matrix
    private double[,] results_matrix;       // DTW matrix
    private Direction[,] directions;        // directions

    // errors per Joint
    Dictionary<int, double> errorsForAllJoints;

    // quaternions weights
    private static double[] weights;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public DTW(string ref_file)
    {
        readData(ref_file);
    }

    /// <summary>
    /// Read data from user and reference files and init global structures.
    /// </summary>
    private void readData(string ref_file)
    {
        // load user data
        int user_frames_count = RecordingManager.user_frames_list.Count;
        user_frames = RecordingManager.user_frames_list.ToArray();

        // load ref data
        int ref_frames_count = 0;
        ref_frames = loadFrames(ref_file, ref ref_frames_count);

        //weights = FramesManager.ComputeWeights(ref_file);
    }

    /// <summary>
    /// Eval two gestures using DTW for each gesture file.
    /// </summary>
    public double getCompResult()
    {
        // get the result of the current comparation
        double res = computeDist();

        Debug.Log("DTW : <" + res + "> ");
        double score = (Math.Max(20, res) - res) / Math.Max(18, res) * 100f;

        return score < 100f ? score : 100f;
    }

    /// <summary>
    /// Compute DTW path.
    /// </summary>
    private List<Vector2> computePath()
    {
        List<Vector2> path = new List<Vector2>();
        int i = user_frames.Length;
        int j = ref_frames.Length;

        while (i > 0 && j > 0)
        {
            path.Add(new Vector2(i - 1, j - 1));
            if (directions[i, j] == Direction.LEFT)
            {
                j--;
            }
            else if (directions[i, j] == Direction.UP)
            {
                i--;
            }
            else
            {
                i--;
                j--;
            }
        }

        path.Reverse();

        return path;
    }

    private void initDTW()
    {
        distance = new double[user_frames.Length, ref_frames.Length];
        results_matrix = new double[user_frames.Length + 1, ref_frames.Length + 1];
        directions = new Direction[user_frames.Length + 1, ref_frames.Length + 1];

        // compute initial distance between two gestures
        for (int i = 0; i < user_frames.Length; i++)
        {
            for (int j = 0; j < ref_frames.Length; j++)
            {
                distance[i, j] = CompareFrames(user_frames[i], ref_frames[j]);
            }
        }

        for (int i = 0; i <= user_frames.Length; ++i)
        {
            for (int j = 0; j <= ref_frames.Length; ++j)
            {
                results_matrix[i, j] = -1.0;
            }
        }

        for (int i = 1; i <= user_frames.Length; ++i)
        {
            results_matrix[i, 0] = double.PositiveInfinity;
        }

        for (int j = 1; j <= ref_frames.Length; ++j)
        {
            results_matrix[0, j] = double.PositiveInfinity;
        }

        results_matrix[0, 0] = 0.0;
        directions[0, 0] = Direction.NULL;
    }

    /// <summary>
    /// Compare 2 frames.
    /// </summary>
    public static double CompareFrames(FrameData f1, FrameData f2)
    {
        double sum = 0.0;
        for (int i = 0; i < f1.quaternions.Length; i++)
        {
            sum += CompareQuaternions(f1.quaternions[i], f2.quaternions[i]);
            //weights[i] * 
        }

        return sum / f1.quaternions.Length;
    }

    /// <summary>
    /// Compare 2 quaternions.
    /// </summary>
    public static double CompareQuaternions(Quaternion q1, Quaternion q2)
    {
        float norm1 = (float)(Math.Sqrt(q1.w * q1.w + q1.x * q1.x + q1.y * q1.y + q1.z * q1.z));
        float norm2 = (float)(Math.Sqrt(q2.w * q2.w + q2.x * q2.x + q2.y * q2.y + q2.z * q2.z));

        if (norm1 == 0)
        {
            return norm2;
        }
        else if (norm2 == 0)
        {
            return norm1;
        }

        // unit quaternions
        Quaternion norm_q1 = new Quaternion(q1.x / norm1, q1.y / norm1, q1.z / norm1, q1.w / norm1);
        Quaternion norm_q2 = new Quaternion(q2.x / norm2, q2.y / norm2, q2.z / norm2, q2.w / norm2);

        //  1 - |<u1, u2>|
        return 1 - Math.Abs(norm_q1.x * norm_q2.x + norm_q1.y * norm_q2.y + norm_q1.z * norm_q2.z + norm_q1.w * norm_q2.w);
    }

    /// <summary>
    /// DTW algorithm.
    /// </summary>
    private double computeDist()
    {
        double dist, left, diag, up;

        // init
        initDTW();

        for (int i = 1; i <= user_frames.Length; i++)
        {
            for (int j = 1; j <= ref_frames.Length; j++)
            {
                dist = distance[i - 1, j - 1];

                left = results_matrix[i, j - 1];
                diag = results_matrix[i - 1, j - 1];
                up = results_matrix[i - 1, j];

                if (left <= up && left <= diag)
                {
                    results_matrix[i, j] = left + dist;
                    directions[i, j] = Direction.LEFT;
                }
                else if (up <= left && up <= diag)
                {
                    results_matrix[i, j] = up + dist;
                    directions[i, j] = Direction.UP;
                }
                else
                {
                    results_matrix[i, j] = diag + dist;
                    directions[i, j] = Direction.DIAGONAL;
                }
            }
        }

        return results_matrix[user_frames.Length, ref_frames.Length];
    }
}
