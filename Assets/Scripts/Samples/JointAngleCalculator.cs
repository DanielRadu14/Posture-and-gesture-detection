using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JointAngleCalculator : MonoBehaviour
{
    [Tooltip("Index of the player, tracked by this component. 0 means the 1st player, 1 - the 2nd one, 2 - the 3rd one, etc.")]
    public int playerIndex = 0;

    [Tooltip("Body joint acting as intersection point for the angle.")]
    public KinectInterop.JointType middleJoint = KinectInterop.JointType.AnkleRight;

    [Tooltip("Smoothing factor for the joint angle. The less the smoother. 0 means no smoothing.")]
    [Range(0f, 10f)]
    public float smoothFactor = 5f;

    [Tooltip("UI Text to display the information messages.")]
    public UnityEngine.UI.Text infoText;


    // reference to the KinectManager
    private KinectManager kinectManager;
    private float midJointAngle = 0f;


    void Start()
    {
        kinectManager = KinectManager.Instance;
    }


    void Update()
    {
        if(kinectManager && kinectManager.IsInitialized())
        {
            long userId = kinectManager.GetUserIdByIndex(playerIndex);

            KinectInterop.JointType endJoint1 = KinectInterop.GetParentJoint(middleJoint);
            KinectInterop.JointType endJoint2 = KinectInterop.GetNextJoint(middleJoint);
            //Debug.Log(endJoint1 + " - " + middleJoint + " - " + endJoint2);

            if(userId != 0 && middleJoint != endJoint1 && middleJoint != endJoint2)
            {
                if (kinectManager.IsJointTracked(userId, (int)endJoint1) &&
                    kinectManager.IsJointTracked(userId, (int)middleJoint) &&
                    kinectManager.IsJointTracked(userId, (int)endJoint2))
                {
                    Vector3 posEndJoint1 = kinectManager.GetJointPosition(userId, (int)endJoint1);
                    Vector3 posMiddleJoint = kinectManager.GetJointPosition(userId, (int)middleJoint);
                    Vector3 posEndJoint2 = kinectManager.GetJointPosition(userId, (int)endJoint2);

                    Vector3 dirMidEnd1 = (posEndJoint1 - posMiddleJoint).normalized;
                    Vector3 dirMidEnd2 = (posEndJoint2 - posMiddleJoint).normalized;
                    float newJointAngle = Vector3.Angle(dirMidEnd1, dirMidEnd2);

                    if (midJointAngle != 0f)
                        midJointAngle = smoothFactor > 0f ? Mathf.Lerp(midJointAngle, newJointAngle, smoothFactor * Time.deltaTime) : newJointAngle;
                    else
                        midJointAngle = newJointAngle;

                    if (infoText != null)
                    {
                        infoText.text = string.Format("{0} angle: {1:F0} deg.", middleJoint, midJointAngle);
                    }
                }
            }
            else
            {
                // no user found or end joint selected
                midJointAngle = 0f;
            }

        }
    }

}
