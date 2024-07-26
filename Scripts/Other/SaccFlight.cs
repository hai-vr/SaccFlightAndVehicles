
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

#if NOCHAT_ACTIVE
using Input = NochatScript.Core.NochatInput;
#endif

namespace SaccFlightAndVehicles
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SaccFlight : UdonSharpBehaviour
    {
        private VRCPlayerApi localPlayer;
        [SerializeField] private float Thrust_Strength = 29.7f;
        [Tooltip("Strength of extra thrust applied when trying to thrust in direction going against movement")]
        [SerializeField] private float Back_Thrust_Strength = 45f;
        private bool InVR = false;
        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) { gameObject.SetActive(false); }//fixedupdate runs before this happens and causes a crash in the editor until vrc fix it
            else if (localPlayer.IsUserInVR())
            { InVR = true; }
        }
        private void FixedUpdate()
        {
            if (!localPlayer.IsPlayerGrounded())//only does anything if in the air.
            {
                float DeltaTime = Time.fixedDeltaTime;
                float ForwardThrust = Mathf.Max(Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger"), Input.GetKey(KeyCode.F) ? 1 : 0);
                float UpThrust = Mathf.Max(Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger"), Input.GetKey(KeyCode.Space) ? 1 : 0);

                Vector3 PlayerVel = localPlayer.GetVelocity();

                Vector3 NewForwardVec;
                if (InVR)
                {
                    NewForwardVec = (localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation * Quaternion.Euler(0, 60, 0)) * Vector3.forward;
                }
                else//Desktop
                {
                    NewForwardVec = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.forward;
                }
                //get backwards amount
                float BackThrustAmount = -(Vector3.Dot(PlayerVel, NewForwardVec) * Back_Thrust_Strength * DeltaTime);
                NewForwardVec = NewForwardVec * Thrust_Strength * ForwardThrust * DeltaTime * Mathf.Max(1, BackThrustAmount * ForwardThrust);

                Vector3 NewUpVec = Vector3.up * Thrust_Strength * UpThrust * DeltaTime;

#if UNITY_EDITOR
                //SetVelocity overrides all other forces in clientsim so we need to add gravity ourselves
                if (ForwardThrust + UpThrust == 0) { return; }
                else
                { NewForwardVec += -Vector3.up * 9.81f * Time.deltaTime; }
#endif

                localPlayer.SetVelocity(PlayerVel + NewForwardVec + NewUpVec);
            }
        }
        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if (player.isLocal)
            { player.SetVelocity(Vector3.zero); }
        }
    }
}