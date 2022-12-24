using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;


public class BlobDetector : MonoBehaviour
{
    [Tooltip("Camera used to estimate the overlay positions of 3D-objects over the background. By default it is the main camera.")]
    public Camera foregroundCamera;

    [Tooltip("Blob prefab, used to represent the blob in the 3D space.")]
    public GameObject blobPrefab;

    [Tooltip("The blobs root object.")]
    public GameObject blobsRootObj;

    [Range(0, 500)]
    [Tooltip("Max X and Y distance to blob, in pixels, to consider a pixel part of it.")]
    public int xyDistanceToBlob = 10;

    [Range(0, 500)]
    [Tooltip("Max Z-distance to blob, in mm, to consider a pixel part of it.")]
    public int zDistanceToBlob = 50;

    [Range(0, 500)]
    [Tooltip("Minimum amount of pixels in a blob.")]
    public int minPixelsInBlob = 50;

    [Range(1, 10)]
    [Tooltip("Increment in X & Y directions, when analyzing the raw depth image.")]
    public int xyIncrement = 3;

    [Range(0, 5)]
    [Tooltip("Time between the checks for blobs, in seconds.")]
    public float timeBetweenChecks = 0.1f;

    [Tooltip("UI-Text to display info messages.")]
    public Text infoText;


    [Tooltip("RawImage used to display the depth image.")]
    public RawImage backgroundImage;

    [Tooltip("Camera used to display the background image. Set it, if you'd like to allow background image to resize, to match the depth image's aspect ratio.")]
    public Camera backgroundCamera;

    // min & max distance tracked by the sensor
    [Tooltip("Minimum distance in meters, tracked by the sensor.")]
    [Range(0f, 10f)]
    public float minDistance = 0.5f;

    [Tooltip("Maximum distance in meters, tracked by the sensor.")]
    [Range(0f, 10f)]
    public float maxDistance = 10f;

    [Tooltip("UI-Text to display the maximum distance.")]
    public Text maxDistanceText;


    // reference to KM
    private KinectManager kinectManager = null;

    // depth image resolution
    private int depthImageWidth;
    private int depthImageHeight;

    // depth scale
    private Vector3 depthScale = Vector3.one;

    // screen rectangle taken by the foreground image (in pixels)
    private Rect foregroundImgRect;

    // last depth frame time
    private long lastDepthFrameTime = 0;
    private float lastCheckTime = 0;

    // list of blobs
    private List<Blob> blobs = new List<Blob>();
    // list of cubes
    private List<GameObject> blobObjects = new List<GameObject>();


    // depth material and texture
    private Material depthImageMaterial = null;
    private RenderTexture depthImageTexture = null;
    private ComputeBuffer depthImageBuffer = null;
    private ComputeBuffer depthHistBuffer = null;

    // max depth distance in mm, used for initializing data arrays and compute buffers
    private const int MAX_DEPTH_DISTANCE_MM = 10000;

    // depth image data
    private int[] depthHistBufferData = null;
    private int[] equalHistBufferData = null;
    private int depthHistTotalPoints = 0;


    /// <summary>
    /// Gets the number of detected blobs.
    /// </summary>
    /// <returns>Number of blobs.</returns>
    public int GetBlobsCount()
    {
        return blobs.Count;
    }


    /// <summary>
    /// Gets the blob with the given index.
    /// </summary>
    /// <param name="i">Blob index.</param>
    /// <returns>The blob.</returns>
    public Blob GetBlob(int i)
    {
        if(i >= 0 && i < blobs.Count)
        {
            return blobs[i];
        }

        return null;
    }


    /// <summary>
    /// Gets distance to the blob with the given index.
    /// </summary>
    /// <param name="i">Blob index.</param>
    /// <returns>Distance to the blob.</returns>
    public float GetBlobDistance(int i)
    {
        if (i >= 0 && i < blobs.Count)
        {
            Vector3 blobCenter = blobs[i].GetBlobCenter();
            return blobCenter.z / 1000f;

        }

        return 0f;
    }


    /// <summary>
    /// Gets position on the depth image of the given blob. 
    /// </summary>
    /// <param name="i">Blob index.</param>
    /// <returns>Depth image position of the blob.</returns>
    public Vector2 GetBlobImagePos(int i)
    {
        if (i >= 0 && i < blobs.Count)
        {
            Vector3 blobCenter = blobs[i].GetBlobCenter();
            return (Vector2)blobCenter;

        }

        return Vector2.zero;
    }


    /// <summary>
    /// Gets position in the 3d space of the given blob.
    /// </summary>
    /// <param name="i">Blob index.</param>
    /// <returns>Space position of the blob.</returns>
    public Vector3 GetBlobSpacePos(int i)
    {
        if (i >= 0 && i < blobs.Count)
        {
            Vector3 blobCenter = blobs[i].GetBlobCenter();
            Vector3 spacePos = kinectManager.MapDepthPointToSpaceCoords((Vector2)blobCenter, (ushort)blobCenter.z, true);

            return spacePos;

        }

        return Vector3.zero;
    }


    /// <summary>
    /// Sets the minimum distance, in meters.
    /// </summary>
    /// <param name="fMinDist">Min distance.</param>
    public void SetMinDistance(float fMinDist)
    {
        minDistance = fMinDist;
    }


    /// <summary>
    /// Sets the maximum distance, in meters.
    /// </summary>
    /// <param name="fMaxDist">Max distance.</param>
    public void SetMaxDistance(float fMaxDist)
    {
        maxDistance = fMaxDist;
    }


    // internal methods

    void Start()
    {
        kinectManager = KinectManager.Instance;

        if(blobsRootObj == null)
        {
            blobsRootObj = new GameObject("BlobsRoot");
        }

        if (foregroundCamera == null)
        {
            // by default use the main camera
            foregroundCamera = Camera.main;
        }

        // calculate the foreground rectangle
        //foregroundImgRect = kinectManager.GetForegroundRectDepth(sensorIndex, foregroundCamera);

        // create the depth shader
        CreateDepthImageShader(kinectManager.GetSensorData());
    }

    void OnDestroy()
    {
        // release the resources
        DisposeDepthImageShader(); 
    }

    void Update()
    {
        if (kinectManager == null || !kinectManager.IsInitialized())
            return;

        depthImageWidth = kinectManager.GetDepthImageWidth();
        depthImageHeight = kinectManager.GetDepthImageHeight();
        depthScale = kinectManager.GetDepthImageScale();

        // calculate the foreground rectangle
        foregroundImgRect = kinectManager.GetForegroundRectDepth(foregroundCamera);

        // apply the back-image anchor position
        Vector2 anchorPos = backgroundImage ? backgroundImage.rectTransform.anchoredPosition : Vector2.zero;
        foregroundImgRect.position = foregroundImgRect.position + anchorPos;

        if (lastDepthFrameTime != kinectManager.GetDepthFrameTime())
        {
            lastDepthFrameTime = kinectManager.GetDepthFrameTime();

            if ((Time.time - lastCheckTime) >= timeBetweenChecks)
            {
                lastCheckTime = Time.time;

                // detect blobs of pixel in the raw depth image
                DetectBlobsInRawDepth();
            }
        }

        if(blobPrefab)
        {
            // instantiates representative blob objects for each blog
            InstantiateBlobObjects();
        }

        // updates the depth image
        UpdateDepthImage(kinectManager.GetSensorData(), kinectManager);

        // update back image with the depth image
        UpdateBackgroundImage(kinectManager);
    }


    // detects blobs of pixel in the raw depth image
    private void DetectBlobsInRawDepth()
    {
        ushort[] rawDepth = kinectManager ? kinectManager.GetRawDepthMap() : null;
        blobs.Clear();

        if (rawDepth == null)
            return;

        //minDistance = kinectManager.GetSensorMinDistance();
        //maxDistance = kinectManager.GetSensorMaxDistance();

        if (maxDistanceText)
        {
            maxDistanceText.text = string.Format("{0:F2} m", maxDistance);
        }

        ushort minDistanceMm = (ushort)(minDistance * 1000f);
        ushort maxDistanceMm = (ushort)(maxDistance * 1000f);

        for (int y = 0, di = 0; y < depthImageHeight; y += xyIncrement)
        {
            di = y * depthImageWidth;

            for (int x = 0; x < depthImageWidth; x += xyIncrement, di += xyIncrement)
            {
                ushort depth = rawDepth[di];
                depth = (depth >= minDistanceMm && depth <= maxDistanceMm) ? depth : (ushort)0;

                if (depth != 0)
                {
                    bool blobFound = false;
                    foreach (var b in blobs)
                    {
                        if (b.IsNearOrInside(x, y, depth, xyDistanceToBlob, zDistanceToBlob))
                        {
                            b.AddDepthPixel(x, y, depth);
                            blobFound = true;
                            break;
                        }
                    }

                    if (!blobFound)
                    {
                        Blob b = new Blob(x, y, depth);
                        blobs.Add(b);
                    }
                }
            }
        }

        // remove inside blobs
        var insideblobs = new List<Blob>();
        foreach (var b in blobs)
            foreach (var b2 in blobs)
                if (b.IsInside(b2) && !insideblobs.Contains(b) && b != b2)
                    insideblobs.Add(b);

        for (int i = 0; i < insideblobs.Count; i++)
            if (blobs.Contains(insideblobs[i]))
                blobs.Remove(insideblobs[i]);

        // remove small blobs
        var smallBlobs = blobs.Where(x => x.pixels < minPixelsInBlob).ToList();
        for (int i = 0; i < smallBlobs.Count; i++)
            if (blobs.Contains(smallBlobs[i]))
                blobs.Remove(smallBlobs[i]);

        if (infoText)
        {
            string sMessage = blobs.Count + " blobs detected.\n";

            for (int i = 0; i < blobs.Count; i++)
            {
                Blob b = blobs[i];
                //sMessage += string.Format("x1: {0}, y1: {1}, x2: {2}, y2: {3}\n", b.minx, b.miny, b.maxx, b.maxy);
                sMessage += string.Format("Blob {0} at {1}\n", i, GetBlobSpacePos(i));
            }

            //Debug.Log(sMessage);
            infoText.text = sMessage;
        }
    }


    // instantiates representative blob objects for each blob
    private void InstantiateBlobObjects()
    {
        int bi = 0;
        foreach (var b in blobs)
        {
            while (bi >= blobObjects.Count)
            {
                var cub = Instantiate(blobPrefab, new Vector3(0, 0, -10), Quaternion.identity);
                //cub.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);  // to match the dimensions of a ball

                blobObjects.Add(cub);
                cub.transform.parent = blobsRootObj.transform;
            }

            Vector3 blobCenter = b.GetBlobCenter();
            Vector3 blobSpacePos = kinectManager.GetPosDepthOverlay((int)blobCenter.x, (int)blobCenter.y, (ushort)blobCenter.z, foregroundCamera, foregroundImgRect);

            blobObjects[bi].transform.position = blobSpacePos;
            blobObjects[bi].name = "Blob" + bi;

            bi++;
        }

        // remove the extra cubes
        for (int i = blobObjects.Count - 1; i >= bi; i--)
        {
            Destroy(blobObjects[i]);
            blobObjects.RemoveAt(i);
        }
    }


    void OnRenderObject()
    {
        int rectX = (int)foregroundImgRect.xMin;
        //int rectY = (int)foregroundImgRect.yMax;
        int rectY = (int)foregroundImgRect.yMin;

        float scaleX = foregroundImgRect.width / depthImageWidth;
        float scaleY = foregroundImgRect.height / depthImageHeight;

        // draw grid
        //DrawGrid();

        // display blob rectangles
        int bi = 0;

        foreach (var b in blobs)
        {
            float x = (depthScale.x >= 0f ? b.minx : depthImageWidth - b.maxx) * scaleX;  // b.minx * scaleX;
            float y = (depthScale.y >= 0f ? b.miny : depthImageHeight - b.maxy) * scaleY;  // b.maxy * scaleY;

            Rect rectBlob = new Rect(rectX + x, rectY + y, (b.maxx - b.minx) * scaleX, (b.maxy - b.miny) * scaleY);
            KinectInterop.DrawRect(rectBlob, 2, Color.white);

            Vector3 blobCenter = b.GetBlobCenter();
            x = (depthScale.x >= 0f ? blobCenter.x : depthImageWidth - blobCenter.x) * scaleX;  // blobCenter.x * scaleX;
            y = (depthScale.y >= 0f ? blobCenter.y : depthImageHeight - blobCenter.y) * scaleY;  // blobCenter.y* scaleY; // 

            Vector3 blobPos = new Vector3(rectX + x, rectY + y, 0);
            KinectInterop.DrawPoint(blobPos, 3, Color.green);

            bi++;
        }
    }


    // draws coordinate grid on screen
    private void DrawGrid()
    {
        int rectX = (int)foregroundImgRect.xMin;
        int rectY = (int)foregroundImgRect.yMin;

        float scaleX = foregroundImgRect.width / depthImageWidth;
        float scaleY = foregroundImgRect.height / depthImageHeight;

        // draw grid
        float c = 0.3f;
        for (int x = 0; x < depthImageWidth; x += 100)
        {
            int sX = (int)(x * scaleX);
            int sMaxY = (int)((depthImageHeight - 1) * scaleY);

            Color clrLine = new Color(c, 0, 0, 1);
            KinectInterop.DrawLine(rectX + sX, rectY, rectX + sX, rectY + sMaxY, 1, clrLine);
            c += 0.1f;
        }

        c = 0.3f;
        for (int y = 0; y < depthImageHeight; y += 100)
        {
            int sY = (int)((depthImageHeight - y) * scaleY);
            int sMaxX = (int)((depthImageWidth - 1) * scaleX);

            Color clrLine = new Color(0, c, 0, 1);
            KinectInterop.DrawLine(rectX, rectY + sY, rectX + sMaxX, rectY + sY, 1, clrLine);
            c += 0.1f;
        }
    }


    // creates the depth image shader and its respective buffers, as needed
    private bool CreateDepthImageShader(KinectInterop.SensorData sensorData)
    {
        Shader depthImageShader = Shader.Find("Kinect/DepthHistImageShader");
        if (depthImageShader != null)
        {
            if (depthImageTexture == null || depthImageTexture.width != sensorData.depthImageWidth || depthImageTexture.height != sensorData.depthImageHeight)
            {
                depthImageTexture = new RenderTexture(sensorData.depthImageWidth, sensorData.depthImageHeight, 0);
                depthImageTexture.wrapMode = TextureWrapMode.Clamp;
                depthImageTexture.filterMode = FilterMode.Point;
                depthImageTexture.enableRandomWrite = true;
            }

            depthImageMaterial = new Material(depthImageShader);

            if (depthImageBuffer == null)
            {
                int depthBufferLength = sensorData.depthImageWidth * sensorData.depthImageHeight / 2;
                depthImageBuffer = new ComputeBuffer(depthBufferLength, sizeof(uint));
            }

            if (depthHistBuffer == null)
            {
                depthHistBuffer = new ComputeBuffer(MAX_DEPTH_DISTANCE_MM + 1, sizeof(uint));
            }

            depthHistBufferData = new int[MAX_DEPTH_DISTANCE_MM + 1];
            equalHistBufferData = new int[MAX_DEPTH_DISTANCE_MM + 1];

            return true;
        }

        return false;
    }


    // disposes the depth-tex shader and its respective buffers
    private void DisposeDepthImageShader()
    {
        if (depthImageTexture != null)
        {
            depthImageTexture.Release();
            depthImageTexture = null;
        }

        if (depthImageBuffer != null)
        {
            depthImageBuffer.Dispose();
            depthImageBuffer = null;
        }

        if (depthHistBuffer != null)
        {
            depthHistBuffer.Dispose();
            depthHistBuffer = null;
        }

        depthImageMaterial = null;
    }

    // updates sensor depth image
    private void UpdateDepthImage(KinectInterop.SensorData sensorData, KinectManager kinectManager)
    {
        // depth-image hist data
        Array.Clear(depthHistBufferData, 0, depthHistBufferData.Length);
        Array.Clear(equalHistBufferData, 0, equalHistBufferData.Length);
        depthHistTotalPoints = 0;

        //int depthMinDistance = (int)(minDistance * 1000f);
        //int depthMaxDistance = (int)(maxDistance * 1000f);

        int frameLen = sensorData.depthImage.Length;
        for (int i = 0; i < frameLen; i++)
        {
            int depth = sensorData.depthImage[i];
            int limDepth = (depth <= MAX_DEPTH_DISTANCE_MM) ? depth : 0;

            if (limDepth > 0)
            {
                depthHistBufferData[limDepth]++;
                depthHistTotalPoints++;
            }
        }

        equalHistBufferData[0] = depthHistBufferData[0];
        for (int i = 1; i < depthHistBufferData.Length; i++)
        {
            equalHistBufferData[i] = equalHistBufferData[i - 1] + depthHistBufferData[i];
        }

        // make depth 0 equal to the max-depth
        equalHistBufferData[0] = equalHistBufferData[equalHistBufferData.Length - 1];

        if (depthImageBuffer != null && sensorData.depthImage != null)
        {
            int depthBufferLength = sensorData.depthImageWidth * sensorData.depthImageHeight / 2;
            KinectInterop.SetComputeBufferData(depthImageBuffer, sensorData.depthImage, depthBufferLength, sizeof(uint));
            //depthImageBuffer.SetData(sensorData.depthImage);
        }

        if (depthHistBuffer != null)
        {
            //sensorData.depthHistBuffer.SetData(equalHistBufferData);
            KinectInterop.SetComputeBufferData(depthHistBuffer, equalHistBufferData, equalHistBufferData.Length, sizeof(int));
            //depthHistBuffer.SetData(equalHistBufferData);
        }

        depthImageMaterial.SetInt("_TexResX", sensorData.depthImageWidth);
        depthImageMaterial.SetInt("_TexResY", sensorData.depthImageHeight);
        depthImageMaterial.SetInt("_MinDepth", (int)(minDistance * 1000f));
        depthImageMaterial.SetInt("_MaxDepth", (int)(maxDistance * 1000f));
        depthImageMaterial.SetInt("_TotalPoints", depthHistTotalPoints);
        depthImageMaterial.SetBuffer("_DepthMap", depthImageBuffer);
        depthImageMaterial.SetBuffer("_HistMap", depthHistBuffer);

        Graphics.Blit(null, depthImageTexture, depthImageMaterial);
    }


    // last camera rect width & height
    private float lastCamRectW = 0;
    private float lastCamRectH = 0;

    // updates the background image with depth image texture
    private void UpdateBackgroundImage(KinectManager kinectManager)
    {
        if (kinectManager && kinectManager.IsInitialized())
        {
            float cameraWidth = backgroundCamera ? backgroundCamera.pixelRect.width : 0f;
            float cameraHeight = backgroundCamera ? backgroundCamera.pixelRect.height : 0f;

            if (backgroundImage && (backgroundImage.texture == null || lastCamRectW != cameraWidth || lastCamRectH != cameraHeight))
            {
                lastCamRectW = cameraWidth;
                lastCamRectH = cameraHeight;

                backgroundImage.texture = depthImageTexture;
                backgroundImage.rectTransform.localScale = new Vector3(1, -1, 1);
                backgroundImage.color = Color.white;

                if (backgroundCamera != null)
                {
                    // adjust image's size and position to match the stream aspect ratio
                    int depthImageWidth = kinectManager.GetDepthImageWidth();
                    int depthImageHeight = kinectManager.GetDepthImageHeight();

                    RectTransform rectImage = backgroundImage.rectTransform;
                    float rectWidth = (rectImage.anchorMin.x != rectImage.anchorMax.x) ? cameraWidth * (rectImage.anchorMax.x - rectImage.anchorMin.x) : rectImage.sizeDelta.x;
                    float rectHeight = (rectImage.anchorMin.y != rectImage.anchorMax.y) ? cameraHeight * (rectImage.anchorMax.y - rectImage.anchorMin.y) : rectImage.sizeDelta.y;

                    if (depthImageWidth > depthImageHeight)
                        rectWidth = rectHeight * depthImageWidth / depthImageHeight;
                    else
                        rectHeight = rectWidth * depthImageHeight / depthImageWidth;

                    Vector2 pivotOffset = (rectImage.pivot - new Vector2(0.5f, 0.5f)) * 2f;
                    Vector2 imageScale = new Vector2(1, -1);
                    Vector2 anchorPos = rectImage.anchoredPosition + new Vector2(pivotOffset.x * imageScale.x * rectWidth, pivotOffset.y * imageScale.y * rectHeight);

                    if (rectImage.anchorMin.x != rectImage.anchorMax.x)
                    {
                        rectWidth = -(cameraWidth - rectWidth);
                    }

                    if (rectImage.anchorMin.y != rectImage.anchorMax.y)
                    {
                        rectHeight = -(cameraHeight - rectHeight);
                    }

                    rectImage.sizeDelta = new Vector2(rectWidth, rectHeight);
                    rectImage.anchoredPosition = anchorPos;
                }
            }
        }

        //RectTransform rectTransform = backgroundImage.rectTransform;
        //Debug.Log("pivot: " + rectTransform.pivot + ", anchorPos: " + rectTransform.anchoredPosition + ", \nanchorMin: " + rectTransform.anchorMin + ", anchorMax: " + rectTransform.anchorMax);
    }

}

