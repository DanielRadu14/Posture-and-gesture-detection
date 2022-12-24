using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;
using System;

public class RecordingManager : MonoBehaviour
{
    public AvatarController userAvatarController;

    public enum GameMode { Default, Countdown, Record, Playback };
    public static GameMode gameModeStat = GameMode.Default;

    public static StreamWriter streamWriter = null;
    private static string root_path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Database" + Path.DirectorySeparatorChar;
    private static string playback_directory = root_path + "Recordings" + Path.DirectorySeparatorChar;
    public static string record_file;

    public struct FrameData
    {
        public Quaternion[] quaternions;
        public string[] jointType;
        public Vector3 rootPosition;
    }
    public const int no_bones = 31;

    public static FrameData[] frames;
    public static int framesCount = 0;
    public static int framesRecorded = 0;
    public static int currentFrame = 0;
    public static int noReps = 0;
    private static int recordingFrameLimit = 500;

    public static List<FrameData> user_frames_list = new List<FrameData>();

    private static float recordingCountdown = 10.0f;

    public UnityEngine.UI.Text infoText;
    public UnityEngine.UI.Text infoDTW;
    private static RecordingManager recordingManagerInstance;

    public bool detectGestures = true;

    // Start is called before the first frame update
    void Start()
    {
        recordingManagerInstance = this;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            if (gameModeStat == GameMode.Default)
            {
                framesCount = 0;

                string recordingPath = EditorUtility.OpenFilePanel("Choose playback file", playback_directory, "");
                frames = loadFrames(recordingPath, ref framesCount);

                gameModeStat = GameMode.Playback;
            }
            else
            {
                frames = null;
                noReps = 0;
                framesCount = 0;
                currentFrame = 0;
                framesRecorded = 0;
                userAvatarController.ResetToInitialPosition();
                gameModeStat = GameMode.Default;
            }

        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (gameModeStat == GameMode.Default)
            {
                gameModeStat = GameMode.Countdown;
                infoText.text = "Recording will start in 10 seconds...";

                int noPlaybackFiles = Directory.GetFiles(playback_directory).Length;
                record_file = playback_directory + "Record" + noPlaybackFiles + ".txt";
                File.Create(record_file);
            }
            else if (gameModeStat == GameMode.Record)
            {
                StopRecording();
            }
        }

        if (gameModeStat == GameMode.Countdown && recordingCountdown > 0)
        {
            recordingCountdown -= Time.deltaTime;
            infoText.text = (recordingCountdown).ToString("0");

            if (infoText.text == "0")
            {
                if (streamWriter != null)
                {
                    streamWriter.Close();
                }
                streamWriter = new StreamWriter(record_file, true);

                frames = null;
                framesRecorded = 0;
                Debug.Log(RecordingManager.record_file + " was created.");

                gameModeStat = GameMode.Record;
                infoText.text = "Started recording!";
            }
        }
    }

    public static void StopRecording()
    {
        recordingCountdown = 10.0f;

        // reset frames data
        frames = null;
        noReps = 0;
        framesCount = 0;
        currentFrame = 0;
       
        recordingManagerInstance.infoText.text = "Recording stopped!";
        recordingManagerInstance.StartCoroutine(SetGameModeToDefault());
    }

    private static IEnumerator SetGameModeToDefault()
    {
        yield return new WaitForSeconds(3.0f);
        if (streamWriter != null)
        {
            streamWriter.Close();
        }
        framesRecorded = 0;
        gameModeStat = GameMode.Default;
    }

    //needs to be in another thread
    public static int detectGesture(string ref_file)
    {
        DTW dtw = new DTW(ref_file);
        double resDTW = dtw.getCompResult();

        return (int)Math.Round(resDTW, 0);
    }

    public static void addUserFrameData(FrameData frameData)
    {
        if (!recordingManagerInstance.detectGestures)
            return;

        user_frames_list.Add(frameData);
        if(user_frames_list.Count == recordingFrameLimit)
        {
            int noPlaybackFiles = Directory.GetFiles(playback_directory).Length;
            for(int i = 0; i < noPlaybackFiles; i++)
            {
                record_file = playback_directory + "Record" + i + ".txt";
                int matchPercent = detectGesture(record_file);
                recordingManagerInstance.infoDTW.text = "DTW = " + matchPercent + "%";

                //TODO
                /*if(matchPercent > threshold)
                {
                    //gesture detected logic
                }*/
            }
            user_frames_list = new List<FrameData>();
        }
    }

    // write frames data to specified recording file
    public static void WriteDataToFile(FrameData frameData)
    {
        //no of recorded frames
        framesRecorded++;

        //frame limit reached, stop recording
        if (framesRecorded >= recordingFrameLimit)
        {
            StopRecording();
            return;
        }

        // write frame number
        streamWriter.WriteLine(framesRecorded);

        // write root position
        string rootPosStr = frameData.rootPosition.x + "," +
                            frameData.rootPosition.y + "," +
                            frameData.rootPosition.z;
        streamWriter.WriteLine(rootPosStr);

        // write quaternions
        for (int q = 0; q < frameData.quaternions.Length; q++)
        {
            if (IsBoneIndexDisabled(q))
            {
                continue;
            }

            string qstr = frameData.quaternions[q].x + "," +
                        frameData.quaternions[q].y + "," +
                        frameData.quaternions[q].z + "," +
                        frameData.quaternions[q].w;

            streamWriter.WriteLine(qstr);
        }
    }

    // initialize data structure with a frame read from stream
    public static FrameData? getFrameData(StreamReader inputStream)
    {
        if (!inputStream.EndOfStream)
        {
            string line;
            float x, y, z, w;

            FrameData frameData = new FrameData();
            Quaternion[] quaternionData = new Quaternion[no_bones];
            Vector3 rootPos = new Vector3();

            try
            {
                // read frame number
                line = inputStream.ReadLine();

                // get root position
                line = inputStream.ReadLine();

                string[] lines = line.Split(',');
                x = float.Parse(lines[0]);
                y = float.Parse(lines[1]);
                z = float.Parse(lines[2]);
                rootPos = new Vector3(x, y, z);

                // read quaternions
                for (int i = 0; i < no_bones; i++)
                {
                    if (IsBoneIndexDisabled(i))
                    {
                        quaternionData[i] = new Quaternion(0, 0, 0, 0);
                        continue;
                    }

                    line = inputStream.ReadLine();

                    string[] comps = line.Split(',');
                    x = float.Parse(comps[0]);
                    y = float.Parse(comps[1]);
                    z = float.Parse(comps[2]);
                    w = float.Parse(comps[3]);
                    quaternionData[i] = new Quaternion(x, y, z, w);
                }
            }
            catch (Exception ex)    // IndexOutOfRange || NullReference
            {
                //Debug.Log(ex.Message);

                // end of the stream
                return null;
            }

            frameData.quaternions = quaternionData;
            frameData.rootPosition = rootPos;

            return frameData;
        }
        // end of the stream
        else
        {
            return null;
        }
    }

    // loads all frames from file and updates the number of frames
    public static FrameData[] loadFrames(string filename, ref int framesCount)
    {
        framesCount = 0;

        List<FrameData> frames = new List<FrameData>();

        // get current file and open a new stream for it
        StreamReader streamReader = new StreamReader(filename, false);

        // get frames
        FrameData? newFrame = getFrameData(streamReader);

        while (newFrame != null)
        {
            frames.Add((FrameData)newFrame);
            framesCount++;

            // next frame
            newFrame = getFrameData(streamReader);
        }

        // close current stream
        if (streamReader != null)
        {
            streamReader.Close();
        }

        // convert list to array
        return frames.ToArray();
    }

    //ignore unimportant bones
    private static bool IsBoneIndexDisabled(int index)
    {
        if (index == 3 || (index >= 8 && index <= 10) ||
            (index >= 14 && index <= 16) || index == 20 || index >= 24)
        {
            return false;
        }
        return false;
    }
}
